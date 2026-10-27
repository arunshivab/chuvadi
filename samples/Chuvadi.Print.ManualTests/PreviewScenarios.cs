using System;
using System.IO;
using Chuvadi.Print;

namespace Chuvadi.Print.ManualTests
{
    /// <summary>
    /// Writes PNG sheet images through <see cref="ImageFileTarget"/> for visual verification.
    /// Guides are on: blue = printable area, red = content bounds. The fake page shows a red square
    /// in its own top-left corner (reveals rotation), its page number, and a gray ramp near the bottom.
    /// </summary>
    public static class PreviewScenarios
    {
        private const double Dpi = 72;

        public static int WriteAll(string root)
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            int files = 0;
            files += WriteAlignment(Path.Combine(root, "01-alignment"));
            files += WriteScale(Path.Combine(root, "02-scale"));
            files += WriteOrientation(Path.Combine(root, "03-orientation"));
            files += WriteColour(Path.Combine(root, "04-colour"));
            files += WriteDuplex(Path.Combine(root, "05-duplex-copies"));
            files += WriteMargins(Path.Combine(root, "06-margins"));
            return files;
        }

        private static int Emit(PrintJob job, string folder, string fileName)
        {
            var target = new ImageFileTarget(folder).WithDpi(Dpi).WithGuides().WithFileNamePattern(fileName);
            PrintResult result = job.WithRasterizer(new FakeRasterizer()).PrintTo(target);
            if (result.Status != PrintJobStatus.Printed)
                throw new InvalidOperationException("Preview job failed (" + fileName + "): " + result.Error);
            return target.WrittenFiles.Count;
        }

        // A6 page at actual size on A4 with 0.5in margins, in each of the nine positions.
        private static int WriteAlignment(string folder)
        {
            var positions = new (string Name, ContentAlignment Value)[]
            {
                ("1-TopLeft", ContentAlignment.TopLeft), ("2-TopCenter", ContentAlignment.TopCenter), ("3-TopRight", ContentAlignment.TopRight),
                ("4-CenterLeft", ContentAlignment.CenterLeft), ("5-Center", ContentAlignment.Center), ("6-CenterRight", ContentAlignment.CenterRight),
                ("7-BottomLeft", ContentAlignment.BottomLeft), ("8-BottomCenter", ContentAlignment.BottomCenter), ("9-BottomRight", ContentAlignment.BottomRight)
            };
            int n = 0;
            foreach (var p in positions)
            {
                var job = new PrintJob(FakeDocument.Create(PageDimensions.FromPaper(PaperSize.A6)))
                    .WithScale(ScaleMode.ActualSize).WithAlignment(p.Value);
                n += Emit(job, folder, p.Name + ".png");
            }
            return n;
        }

        // A Letter page and a small 200x120pt page under every scale mode, on A4.
        private static int WriteScale(string folder)
        {
            var modes = new (string Name, ScaleMode Mode)[]
            {
                ("ActualSize", ScaleMode.ActualSize), ("FitToPage", ScaleMode.FitToPage), ("ShrinkToFit", ScaleMode.ShrinkToFit),
                ("FitToWidth", ScaleMode.FitToWidth), ("FitToHeight", ScaleMode.FitToHeight), ("Custom-60pct", ScaleMode.Custom)
            };
            int n = 0;
            foreach (var m in modes)
            {
                foreach (var (label, page) in new[] { ("letter", PageDimensions.FromPaper(PaperSize.Letter)), ("small", new PageDimensions(200, 120)) })
                {
                    var job = new PrintJob(FakeDocument.Create(page)).WithScale(m.Mode);
                    if (m.Mode == ScaleMode.Custom) job.WithCustomScale(60);
                    n += Emit(job, folder, m.Name + "-" + label + ".png");
                }
            }
            return n;
        }

        // A portrait A5 page under each orientation, then a landscape page with and without auto-rotate.
        private static int WriteOrientation(string folder)
        {
            int n = 0;
            foreach (PageOrientation o in new[] { PageOrientation.Portrait, PageOrientation.Landscape, PageOrientation.ReversePortrait, PageOrientation.ReverseLandscape })
            {
                var job = new PrintJob(FakeDocument.Create(PageDimensions.FromPaper(PaperSize.A5))).WithOrientation(o);
                n += Emit(job, folder, o + ".png");
            }
            var landscape = PageDimensions.FromPaper(PaperSize.A4).Swap();
            n += Emit(new PrintJob(FakeDocument.Create(landscape)), folder, "LandscapePage-AutoRotateOff.png");
            n += Emit(new PrintJob(FakeDocument.Create(landscape)).WithAutoRotate(), folder, "LandscapePage-AutoRotateOn.png");
            return n;
        }

        private static int WriteColour(string folder)
        {
            byte[] doc = FakeDocument.Create(PageDimensions.FromPaper(PaperSize.A4));
            int n = 0;
            n += Emit(new PrintJob(doc).WithColor(ColorMode.Color), folder, "1-Color.png");
            n += Emit(new PrintJob(doc).WithColor(ColorMode.Grayscale), folder, "2-Grayscale.png");
            n += Emit(new PrintJob(doc).WithColor(ColorMode.Monochrome).WithMonochrome(MonochromeConversion.Threshold), folder, "3-Mono-Threshold.png");
            n += Emit(new PrintJob(doc).WithColor(ColorMode.Monochrome).WithMonochrome(MonochromeConversion.OrderedDither), folder, "4-Mono-OrderedDither.png");
            n += Emit(new PrintJob(doc).WithColor(ColorMode.Monochrome).WithMonochrome(MonochromeConversion.ErrorDiffusion), folder, "5-Mono-ErrorDiffusion.png");
            return n;
        }

        // 3 pages, 2 collated copies, duplex: page 3's sheet gets a blank back so copy 2 starts on a new sheet.
        private static int WriteDuplex(string folder)
        {
            var job = new PrintJob(FakeDocument.Uniform(3, PageDimensions.FromPaper(PaperSize.A4)))
                .WithDuplex(Chuvadi.Print.Duplex.Vertical).WithCopies(2, collate: true);
            return Emit(job, folder, "{0:D2}-sheet{3}-{4}-page{1}-copy{2}.png");
        }

        // Hard margins 10/20/30/40pt with 25pt requested margins, under both margin references.
        private static int WriteMargins(string folder)
        {
            int n = 0;
            foreach (MarginReference reference in new[] { MarginReference.SheetEdge, MarginReference.PrintableArea })
            {
                var job = new PrintJob(FakeDocument.Create(PageDimensions.FromPaper(PaperSize.A4)))
                    .WithMargins(Chuvadi.Print.Margins.Uniform(25)).WithMarginReference(reference);
                var target = new ImageFileTarget(folder).WithDpi(Dpi).WithGuides()
                    .WithHardMargins(new Chuvadi.Print.Margins(10, 20, 30, 40)).WithFileNamePattern(reference + ".png");
                PrintResult result = job.WithRasterizer(new FakeRasterizer()).PrintTo(target);
                if (result.Status != PrintJobStatus.Printed) throw new InvalidOperationException("Preview margins failed: " + result.Error);
                n += target.WrittenFiles.Count;
            }
            return n;
        }
    }
}
