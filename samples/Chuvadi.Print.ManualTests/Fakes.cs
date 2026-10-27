using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using Chuvadi.Print;

namespace Chuvadi.Print.ManualTests
{
    /// <summary>
    /// A stand-in "document" format for exercising PrintJob without a PDF renderer: a text header
    /// followed by one "width height" line (points) per page.
    /// </summary>
    public static class FakeDocument
    {
        public const string Header = "%CHUVADI-FAKE-DOC";

        public static byte[] Create(params PageDimensions[] pages)
        {
            var sb = new StringBuilder(Header).Append('\n');
            foreach (PageDimensions p in pages)
                sb.Append(p.WidthPoints.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                  .Append(p.HeightPoints.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            return Encoding.ASCII.GetBytes(sb.ToString());
        }

        public static byte[] Uniform(int pageCount, PageDimensions size)
        {
            var pages = new PageDimensions[pageCount];
            for (int i = 0; i < pageCount; i++) pages[i] = size;
            return Create(pages);
        }

        public static PageDimensions[] Parse(ReadOnlySpan<byte> bytes)
        {
            string text = Encoding.ASCII.GetString(bytes);
            string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0 || lines[0] != Header) throw new FormatException("Not a fake document.");
            var pages = new PageDimensions[lines.Length - 1];
            for (int i = 1; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split(' ');
                pages[i - 1] = new PageDimensions(
                    double.Parse(parts[0], CultureInfo.InvariantCulture),
                    double.Parse(parts[1], CultureInfo.InvariantCulture));
            }
            return pages;
        }
    }

    /// <summary>
    /// Renders fake documents procedurally: pastel background, black border, a red marker in the
    /// page's top-left corner (shows rotation), the page number in large digits, and a gray ramp
    /// along the bottom (shows grayscale / dithering). Honours rotation and DPI exactly.
    /// Tracks every page it hands out so tests can prove memory stays bounded.
    /// </summary>
    public sealed class FakeRasterizer : IPageRasterizer
    {
        private readonly List<RasterPage> _issued = new();

        /// <summary>If set, pages are produced in this format regardless of the request.</summary>
        public RasterPixelFormat? ForcedFormat { get; set; }

        /// <summary>If set, rendering this 1-based page throws.</summary>
        public int? FailOnPage { get; set; }

        /// <summary>Called just before each render (for cancellation tests).</summary>
        public Action<int>? BeforeRender { get; set; }

        public List<(int Page, RasterRequest Request)> Renders { get; } = new();
        public int OpenCount { get; private set; }
        public int DocumentsDisposed { get; private set; }

        /// <summary>Pages issued and not yet disposed.</summary>
        public int LivePages
        {
            get
            {
                int live = 0;
                foreach (RasterPage p in _issued) if (!p.IsDisposed) live++;
                return live;
            }
        }

        public IRasterDocument Open(ReadOnlyMemory<byte> document)
        {
            OpenCount++;
            return new Doc(this, FakeDocument.Parse(document.Span));
        }

        private sealed class Doc : IRasterDocument
        {
            private readonly FakeRasterizer _owner;
            private readonly PageDimensions[] _pages;
            private bool _disposed;

            public Doc(FakeRasterizer owner, PageDimensions[] pages)
            {
                _owner = owner;
                _pages = pages;
            }

            public int PageCount => _pages.Length;

            public PageDimensions GetPageSize(int pageNumber) => _pages[pageNumber - 1];

            public RasterPage Render(int pageNumber, RasterRequest request, CancellationToken cancellationToken)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(Doc));
                _owner.BeforeRender?.Invoke(pageNumber);
                cancellationToken.ThrowIfCancellationRequested();
                if (_owner.FailOnPage == pageNumber) throw new InvalidOperationException("Simulated render failure on page " + pageNumber + ".");
                _owner.Renders.Add((pageNumber, request));
                RasterPage page = Draw(pageNumber, _pages[pageNumber - 1], request,
                                       _owner.ForcedFormat ?? (request.PixelFormat == RasterPixelFormat.BlackWhite1 ? RasterPixelFormat.Gray8 : request.PixelFormat));
                _owner._issued.Add(page);
                return page;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _owner.DocumentsDisposed++;
            }
        }

        public static (int Width, int Height) OutputPixels(PageDimensions page, RasterRequest request)
        {
            PageDimensions rotated = PageLayout.IsQuarterTurn(request.Rotation) ? page.Swap() : page;
            return (Math.Max(1, (int)Math.Round(rotated.WidthPoints / 72.0 * request.DpiX)),
                    Math.Max(1, (int)Math.Round(rotated.HeightPoints / 72.0 * request.DpiY)));
        }

        private static RasterPage Draw(int pageNumber, PageDimensions size, RasterRequest request, RasterPixelFormat format)
        {
            (int w, int h) = OutputPixels(size, request);
            RasterPage page = RasterPage.Rent(w, h, format, request.DpiX, request.DpiY);
            for (int y = 0; y < h; y++)
            {
                Span<byte> row = page.GetRow(y);
                double b = (y + 0.5) / h;
                for (int x = 0; x < w; x++)
                {
                    double a = (x + 0.5) / w;
                    double u, v;
                    switch (request.Rotation)
                    {
                        case PageRotation.Clockwise90: u = b; v = 1 - a; break;
                        case PageRotation.Rotate180: u = 1 - a; v = 1 - b; break;
                        case PageRotation.Clockwise270: u = 1 - b; v = a; break;
                        default: u = a; v = b; break;
                    }
                    Shade(pageNumber, size, u, v, out byte r, out byte g, out byte bl);
                    switch (format)
                    {
                        case RasterPixelFormat.Bgra32:
                            row[x * 4] = bl; row[x * 4 + 1] = g; row[x * 4 + 2] = r; row[x * 4 + 3] = 255; break;
                        case RasterPixelFormat.Rgb24:
                            row[x * 3] = r; row[x * 3 + 1] = g; row[x * 3 + 2] = bl; break;
                        case RasterPixelFormat.Gray8:
                            row[x] = (byte)((r * 299 + g * 587 + bl * 114 + 500) / 1000); break;
                        case RasterPixelFormat.BlackWhite1:
                            if ((r * 299 + g * 587 + bl * 114) / 1000 < 128) row[x >> 3] |= (byte)(0x80 >> (x & 7)); break;
                    }
                }
            }
            return page;
        }

        private static readonly (byte R, byte G, byte B)[] Pastels =
        {
            (214, 234, 248), (213, 245, 227), (252, 243, 207), (250, 219, 216),
            (232, 218, 239), (209, 242, 235), (253, 235, 208), (234, 236, 238)
        };

        private static readonly string[] Glyphs =
        {
            "01110100011001110101110011000101110", "00100011000010000100001000010001110",
            "01110100010000100010001000100011111", "11111000100010000010000011000101110",
            "00010001100101010010111110001000010", "11111100001111000001000011000101110",
            "00110010001000011110100011000101110", "11111000010001000100010000100001000",
            "01110100011000101110100011000101110", "01110100011000101111000010001001100"
        };

        /// <summary>Colour of the fake page at normalised position (u, v), u rightwards, v downwards.</summary>
        public static void Shade(int pageNumber, PageDimensions size, double u, double v, out byte r, out byte g, out byte b)
        {
            double xPt = u * size.WidthPoints, yPt = v * size.HeightPoints;
            double borderPt = Math.Max(2.0, Math.Min(size.WidthPoints, size.HeightPoints) * 0.012);

            if (xPt < borderPt || yPt < borderPt || xPt > size.WidthPoints - borderPt || yPt > size.HeightPoints - borderPt)
            {
                r = g = b = 0;
                return;
            }

            double marker = Math.Min(size.WidthPoints, size.HeightPoints) * 0.12;
            if (xPt < borderPt + marker && yPt < borderPt + marker)
            {
                r = 220; g = 30; b = 30;
                return;
            }

            if (v > 0.84 && v < 0.94 && u > 0.08 && u < 0.92)
            {
                byte level = (byte)Math.Round((u - 0.08) / 0.84 * 255);
                r = g = b = level;
                return;
            }

            string digits = pageNumber.ToString(CultureInfo.InvariantCulture);
            double glyphH = size.HeightPoints * 0.28;
            double cell = glyphH / 7.0;
            double glyphW = cell * 5.0, gap = cell;
            double totalW = digits.Length * glyphW + (digits.Length - 1) * gap;
            double left = (size.WidthPoints - totalW) / 2.0, top = (size.HeightPoints - glyphH) / 2.0;
            if (xPt >= left && xPt < left + totalW && yPt >= top && yPt < top + glyphH)
            {
                double local = xPt - left;
                int index = (int)(local / (glyphW + gap));
                double within = local - index * (glyphW + gap);
                if (index < digits.Length && within < glyphW)
                {
                    int col = Math.Min(4, (int)(within / cell));
                    int rowIdx = Math.Min(6, (int)((yPt - top) / cell));
                    if (Glyphs[digits[index] - '0'][rowIdx * 5 + col] == '1')
                    {
                        r = 20; g = 40; b = 90;
                        return;
                    }
                }
            }

            var pastel = Pastels[(pageNumber - 1) % Pastels.Length];
            r = pastel.R; g = pastel.G; b = pastel.B;
        }
    }

    /// <summary>A target that records everything it is given.</summary>
    public sealed class RecordingTarget : IPrintTarget
    {
        public sealed class Record
        {
            public PlannedImpression Impression { get; init; }
            public int ImpressionIndex { get; init; }
            public PagePlacement? Placement { get; init; }
            public RasterPixelFormat? Format { get; init; }
            public int PixelWidth { get; init; }
            public int PixelHeight { get; init; }
            public long BlackPixels { get; init; }
            public int LiveRasterizerPages { get; init; }
        }

        public string Name { get; set; } = "Recording target";
        public PrintTargetCapabilities Capabilities { get; set; } = new() { DpiX = 72, DpiY = 72 };
        public int? FailOnImpression { get; set; }
        public Func<int>? LivePageProbe { get; set; }
        public Action<int>? AfterImpression { get; set; }

        public List<string> Events { get; } = new();
        public List<Record> Pages { get; } = new();
        public PrintJobContext? Context { get; private set; }
        public Exception? AbortReason { get; private set; }

        public PrintTargetCapabilities GetCapabilities(PrintSettings settings)
        {
            Events.Add("caps");
            return Capabilities;
        }

        public void StartJob(PrintJobContext context)
        {
            Context = context;
            Events.Add("start");
        }

        public void PrintPage(PrintedPage page)
        {
            if (FailOnImpression == page.ImpressionIndex) throw new InvalidOperationException("Simulated device failure.");
            RasterPage? raster = page.Raster;
            Pages.Add(new Record
            {
                Impression = page.Impression,
                ImpressionIndex = page.ImpressionIndex,
                Placement = page.Placement,
                Format = raster?.PixelFormat,
                PixelWidth = raster?.PixelWidth ?? 0,
                PixelHeight = raster?.PixelHeight ?? 0,
                BlackPixels = raster is null ? 0 : CountBlack(raster),
                LiveRasterizerPages = LivePageProbe?.Invoke() ?? -1
            });
            Events.Add(page.IsBlank ? "blank" : "page" + page.SourcePageNumber);
            AfterImpression?.Invoke(page.ImpressionIndex);
        }

        public void EndJob() => Events.Add("end");

        public void AbortJob(Exception? reason)
        {
            AbortReason = reason;
            Events.Add("abort");
        }

        public static long CountBlack(RasterPage raster)
        {
            long count = 0;
            for (int y = 0; y < raster.PixelHeight; y++)
            {
                ReadOnlySpan<byte> row = raster.GetRow(y);
                for (int x = 0; x < raster.PixelWidth; x++)
                {
                    bool black = raster.PixelFormat switch
                    {
                        RasterPixelFormat.BlackWhite1 => (row[x >> 3] & (0x80 >> (x & 7))) != 0,
                        RasterPixelFormat.Gray8 => row[x] < 16,
                        RasterPixelFormat.Rgb24 => row[x * 3] < 16 && row[x * 3 + 1] < 16 && row[x * 3 + 2] < 16,
                        RasterPixelFormat.Bgra32 => row[x * 4] < 16 && row[x * 4 + 1] < 16 && row[x * 4 + 2] < 16,
                        _ => false
                    };
                    if (black) count++;
                }
            }
            return count;
        }
    }

    /// <summary>Keeps audit events in memory.</summary>
    public sealed class MemoryAuditSink : IPrintAuditSink
    {
        public List<PrintAuditEvent> Events { get; } = new();
        public void Record(PrintAuditEvent auditEvent) => Events.Add(auditEvent);
    }

    /// <summary>Collects progress reports synchronously (System.Progress posts asynchronously).</summary>
    public sealed class ListProgress : IProgress<PrintProgress>
    {
        public List<PrintProgress> Reports { get; } = new();
        public void Report(PrintProgress value) => Reports.Add(value);
    }
}
