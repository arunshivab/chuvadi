using System.IO;
using System.IO.Packaging;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Xps.Packaging;
using Chuvadi.Print;
using Chuvadi.Print.ManualTests;
using Chuvadi.Print.Windows;

namespace Chuvadi.Print.Xps.Tests
{
    /// <summary>
    /// Opens XPS written by <see cref="XpsWriter"/> with WPF's XPS reader (Microsoft's own XPS consumer) and checks
    /// the rendered pixels of the two-page sample (WINDOWS-VERIFICATION 11f) at points whose colour is known.
    /// Positions are in points, as in the sample; rendering is at 96 dpi.
    /// </summary>
    public sealed class WpfXpsReaderTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "chuvadi-xps-tests-" + Guid.NewGuid().ToString("N"));

        public WpfXpsReaderTests() => Directory.CreateDirectory(_folder);

        public void Dispose()
        {
            try { Directory.Delete(_folder, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>WPF needs a single-threaded apartment.</summary>
        private static void RunSta(Action action)
        {
            ExceptionDispatchInfo? failure = null;
            var thread = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            failure?.Throw();
        }

        private string WriteSample()
        {
            string path = Path.Combine(_folder, "sample.xps");
            IReadOnlyList<VectorPage> pages = Phase4Scenarios.SamplePages(Phase4Scenarios.FindSystemFont());
            Assert.True(XpsWriter.CanWrite(pages, out string? reason), reason);
            XpsWriter.Write(pages, path);
            return path;
        }

        /// <summary>A rendered page: premultiplied BGRA pixels, read as if drawn over white paper.</summary>
        private sealed class Rendered
        {
            private readonly byte[] _pixels;

            public Rendered(int width, int height, byte[] pixels)
            {
                Width = width;
                Height = height;
                _pixels = pixels;
            }

            public int Width { get; }
            public int Height { get; }

            /// <summary>The colour at a point given in points, over white.</summary>
            public (int R, int G, int B) At(double xPt, double yPt)
            {
                int x = (int)Math.Round(xPt * 96 / 72), y = (int)Math.Round(yPt * 96 / 72);
                Assert.InRange(x, 0, Width - 1);
                Assert.InRange(y, 0, Height - 1);
                int i = (y * Width + x) * 4;
                int a = _pixels[i + 3];
                return (_pixels[i + 2] + 255 - a, _pixels[i + 1] + 255 - a, _pixels[i] + 255 - a);
            }

            /// <summary>How many pixels in a rectangle (points) are not near-white.</summary>
            public int Inked(double xPt, double yPt, double wPt, double hPt)
            {
                int n = 0;
                for (double y = yPt; y < yPt + hPt; y += 0.75)
                    for (double x = xPt; x < xPt + wPt; x += 0.75)
                    {
                        (int r, int g, int b) = At(x, y);
                        if (r < 200 || g < 200 || b < 200) n++;
                    }
                return n;
            }
        }

        private static List<Rendered> Render(string path)
        {
            var result = new List<Rendered>();
            using var doc = new XpsDocument(path, FileAccess.Read, CompressionOption.NotCompressed);
            FixedDocumentSequence seq = doc.GetFixedDocumentSequence();
            DocumentPaginator paginator = seq.DocumentPaginator;
            for (int i = 0; i < paginator.PageCount; i++)
            {
                DocumentPage page = paginator.GetPage(i);
                Size size = page.Size;
                if (page.Visual is UIElement element)
                {
                    element.Measure(size);
                    element.Arrange(new Rect(size));
                    element.UpdateLayout();
                }
                int w = (int)Math.Ceiling(size.Width), h = (int)Math.Ceiling(size.Height);
                var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(page.Visual);
                var pixels = new byte[w * h * 4];
                bitmap.CopyPixels(pixels, w * 4, 0);
                result.Add(new Rendered(w, h, pixels));
            }
            return result;
        }

        private static void Near((int R, int G, int B) actual, int r, int g, int b, int tolerance, string what)
        {
            Assert.True(Math.Abs(actual.R - r) <= tolerance && Math.Abs(actual.G - g) <= tolerance && Math.Abs(actual.B - b) <= tolerance,
                what + ": expected about (" + r + "," + g + "," + b + "), got (" + actual.R + "," + actual.G + "," + actual.B + ")");
        }

        // ---------------------------------------------------------------- tests

        [Fact]
        public void WpfOpensTheSampleWithTwoPagesOfTheRightSize()
        {
            string path = WriteSample();
            RunSta(() =>
            {
                using var doc = new XpsDocument(path, FileAccess.Read);
                FixedDocumentSequence seq = doc.GetFixedDocumentSequence();
                Assert.Single(seq.References);
                FixedDocument fixedDoc = seq.References[0].GetDocument(false);
                Assert.Equal(2, fixedDoc.Pages.Count);
                DocumentPaginator paginator = seq.DocumentPaginator;
                Size a4 = paginator.GetPage(0).Size;
                Size a5 = paginator.GetPage(1).Size;
                Assert.Equal(595.28 * 96 / 72, a4.Width, 1);
                Assert.Equal(841.89 * 96 / 72, a4.Height, 1);
                Assert.Equal(595.28 * 96 / 72, a5.Width, 1);
                Assert.Equal(419.53 * 96 / 72, a5.Height, 1);
            });
        }

        [Fact]
        public void PageOneRendersShapesImagesGroupsAndText()
        {
            string path = WriteSample();
            RunSta(() =>
            {
                Rendered page = Render(path)[0];
                Near(page.At(132, 180), 220, 40, 40, 12, "red rectangle");
                Near(page.At(272, 180), 40, 120, 220, 12, "blue rectangle");

                // Group opacity: two black squares at 50 % in one group; the overlap is the same grey as the rest.
                (int R, int G, int B) single = page.At(310, 450);
                (int R, int G, int B) overlap = page.At(360, 500);
                Near(single, 128, 128, 128, 16, "group square");
                Near(overlap, single.R, single.G, single.B, 6, "group overlap (same grey as one square)");

                // 2 x 2 checker image, 60 pt square at (212, 440): quadrant centres.
                Assert.True(page.At(227, 455).R < 80, "checker top-left is black");
                Assert.True(page.At(257, 455).R > 180, "checker top-right is white");

                if (Phase4Scenarios.FindSystemFont() is not null)
                    Assert.True(page.Inked(72, 60, 260, 26) > 200, "the title text is drawn (the embedded font loaded)");
            });
        }

        [Fact]
        public void PageTwoRendersSoftMasksAndTransparentImages()
        {
            string path = WriteSample();
            RunSta(() =>
            {
                Rendered page = Render(path)[1];

                // Bands faded by an alpha mask running 255 -> 0 from x = 72 to 272.
                (int R, int G, int B) start = page.At(76, 110);
                Assert.True(start.R > 180 && start.G < 110, "the red band is strong at its left end: " + start);
                (int R, int G, int B) end = page.At(270, 110);
                Assert.True(end.R > 235 && end.G > 235 && end.B > 235, "the red band has faded to white at its right end: " + end);

                if (Phase4Scenarios.FindSystemFont() is not null)
                {
                    // A gradient rectangle masked by the letters "MASK": its corner, outside the letters, stays white.
                    Near(page.At(322, 102), 255, 255, 255, 12, "masked rectangle outside the letters");
                    Assert.True(page.Inked(320, 100, 200, 90) > 500, "the letters show the gradient");
                }

                // RGBA disc over a black bar: orange at the centre, black bar visible near the disc's edge.
                (int R, int G, int B) centre = page.At(172, 258);
                Assert.True(centre.R > 200 && centre.G > 80 && centre.G < 170 && centre.B < 60, "orange at the disc centre: " + centre);
                Assert.True(page.At(80, 258).R < 40, "the bar is black away from the disc");
                Assert.True(page.At(134, 258).R < 80, "the bar shows through the clear edge of the disc");
            });
        }
    }
}
