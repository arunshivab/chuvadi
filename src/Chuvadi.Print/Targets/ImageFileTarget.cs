using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Chuvadi.Print
{
    /// <summary>
    /// A portable print target that writes each impression as a PNG image of the whole sheet:
    /// white paper with the page placed exactly where a printer would put it. Useful for print
    /// previews, archiving what was printed, and visually verifying margins, alignment and scaling.
    /// </summary>
    /// <remarks>
    /// Colour jobs produce 8-bit RGB PNGs; grayscale and monochrome jobs produce 8-bit grayscale PNGs.
    /// One sheet image is held in memory at a time.
    /// </remarks>
    public sealed class ImageFileTarget : IPrintTarget
    {
        private readonly List<string> _written = new();
        private PrintJobContext? _context;
        private double _dpiX = 96;
        private double _dpiY = 96;
        private string _fileNamePattern = "impression-{0:D4}.png";

        /// <param name="folderPath">Folder to write into. Created when the job starts if it does not exist.</param>
        public ImageFileTarget(string folderPath)
        {
            if (folderPath is null) throw new ArgumentNullException(nameof(folderPath));
            if (folderPath.Length == 0) throw new ArgumentException("Folder path cannot be empty.", nameof(folderPath));
            FolderPath = folderPath;
        }

        public string FolderPath { get; }

        /// <inheritdoc />
        public string Name => "Image files: " + FolderPath;

        /// <summary>Horizontal resolution of the sheet images, in pixels per inch. Default 96.</summary>
        public double DpiX
        {
            get => _dpiX;
            set => _dpiX = value > 0 && !double.IsInfinity(value) ? value : throw new ArgumentOutOfRangeException(nameof(DpiX));
        }

        /// <summary>Vertical resolution of the sheet images, in pixels per inch. Default 96.</summary>
        public double DpiY
        {
            get => _dpiY;
            set => _dpiY = value > 0 && !double.IsInfinity(value) ? value : throw new ArgumentOutOfRangeException(nameof(DpiY));
        }

        /// <summary>Simulated non-printable edges (to preview a specific printer). Default none.</summary>
        public Margins HardMargins { get; set; } = Margins.None;

        /// <summary>When true, outlines the printable area (blue) and the content bounds (red). Default false.</summary>
        public bool ShowGuides { get; set; }

        /// <summary>
        /// Composite-format file name. Placeholders: {0} impression number (1-based), {1} document page
        /// (0 for a blank side), {2} copy number, {3} sheet number (1-based), {4} "front" or "back".
        /// Default "impression-{0:D4}.png".
        /// </summary>
        public string FileNamePattern
        {
            get => _fileNamePattern;
            set
            {
                if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("File name pattern cannot be empty.", nameof(value));
                _fileNamePattern = value;
            }
        }

        /// <summary>Full paths of the files written by the most recent job, in order.</summary>
        public IReadOnlyList<string> WrittenFiles => _written;

        /// <summary>Sets both resolutions. Returns this target.</summary>
        public ImageFileTarget WithDpi(double dpi)
        {
            DpiX = dpi;
            DpiY = dpi;
            return this;
        }

        public ImageFileTarget WithDpi(double dpiX, double dpiY)
        {
            DpiX = dpiX;
            DpiY = dpiY;
            return this;
        }

        public ImageFileTarget WithHardMargins(Margins hardMargins)
        {
            HardMargins = hardMargins;
            return this;
        }

        public ImageFileTarget WithGuides(bool showGuides = true)
        {
            ShowGuides = showGuides;
            return this;
        }

        public ImageFileTarget WithFileNamePattern(string pattern)
        {
            FileNamePattern = pattern;
            return this;
        }

        /// <inheritdoc />
        public PrintTargetCapabilities GetCapabilities(PrintSettings settings) => new()
        {
            DpiX = DpiX,
            DpiY = DpiY,
            HardMargins = HardMargins,
            SupportsCopies = false,
            SupportsCollate = false,
            SupportsDuplex = true,
            SupportsColor = true,
            PreferredPixelFormat = RasterPixelFormat.Rgb24
        };

        /// <inheritdoc />
        public void StartJob(PrintJobContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _written.Clear();
            Directory.CreateDirectory(FolderPath);
        }

        /// <inheritdoc />
        public void PrintPage(PrintedPage page)
        {
            if (page is null) throw new ArgumentNullException(nameof(page));
            PrintJobContext context = _context ?? throw new InvalidOperationException("StartJob was not called.");

            PageDimensions sheet = page.Placement?.SheetSize
                ?? PageLayout.SheetFor(context.Settings.Paper, context.Settings.Orientation);
            int width = Math.Max(1, (int)Math.Round(sheet.WidthPoints / 72.0 * DpiX));
            int height = Math.Max(1, (int)Math.Round(sheet.HeightPoints / 72.0 * DpiY));
            bool color = context.OutputPixelFormat is RasterPixelFormat.Bgra32 or RasterPixelFormat.Rgb24;
            int channels = color ? 3 : 1;
            int rowBytes = checked(width * channels);
            long size = (long)rowBytes * height;
            if (size > Array.MaxLength) throw new InvalidOperationException("Sheet image is too large at this resolution.");

            byte[] canvas = ArrayPool<byte>.Shared.Rent((int)size);
            try
            {
                canvas.AsSpan(0, (int)size).Fill(255);
                if (page.Raster is not null && page.Placement is not null)
                    Paste(canvas, width, height, channels, page.Raster, page.Placement.ContentBounds);
                if (ShowGuides && page.Placement is not null)
                {
                    DrawRect(canvas, width, height, channels, page.Placement.PrintableArea, 0, 120, 255);
                    DrawRect(canvas, width, height, channels, page.Placement.ContentBounds, 230, 0, 0);
                }

                string path = Path.Combine(FolderPath, FileNameFor(page));
                var encoder = new PngEncoder(width, height, color ? PngEncoder.ColorTypeRgb : PngEncoder.ColorTypeGray, DpiX, DpiY);
                using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    encoder.Write(stream, (y, row) => canvas.AsSpan(y * rowBytes, rowBytes).CopyTo(row));
                }
                _written.Add(Path.GetFullPath(path));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(canvas);
            }
        }

        /// <inheritdoc />
        public void EndJob() => _context = null;

        /// <inheritdoc />
        public void AbortJob(Exception? reason) => _context = null;

        private string FileNameFor(PrintedPage page)
            => string.Format(CultureInfo.InvariantCulture, FileNamePattern,
                             page.ImpressionIndex + 1, page.SourcePageNumber, page.CopyNumber,
                             page.SheetIndex + 1, page.IsBackSide ? "back" : "front");

        /// <summary>Nearest-neighbour maps the raster onto its content bounds, clipped to the sheet.</summary>
        private void Paste(byte[] canvas, int width, int height, int channels, RasterPage raster, RectanglePoints bounds)
        {
            double left = bounds.X / 72.0 * DpiX;
            double top = bounds.Y / 72.0 * DpiY;
            double boundsWidth = bounds.Width / 72.0 * DpiX;
            double boundsHeight = bounds.Height / 72.0 * DpiY;
            if (boundsWidth <= 0 || boundsHeight <= 0) return;

            int x0 = Math.Max(0, (int)Math.Floor(left));
            int y0 = Math.Max(0, (int)Math.Floor(top));
            int x1 = Math.Min(width, (int)Math.Ceiling(left + boundsWidth));
            int y1 = Math.Min(height, (int)Math.Ceiling(top + boundsHeight));
            double sx = raster.PixelWidth / boundsWidth;
            double sy = raster.PixelHeight / boundsHeight;
            int rowBytes = width * channels;

            for (int cy = y0; cy < y1; cy++)
            {
                int ry = (int)((cy + 0.5 - top) * sy);
                if (ry < 0 || ry >= raster.PixelHeight) continue;
                ReadOnlySpan<byte> src = raster.GetRow(ry);
                int rowBase = cy * rowBytes;
                for (int cx = x0; cx < x1; cx++)
                {
                    int rx = (int)((cx + 0.5 - left) * sx);
                    if (rx < 0 || rx >= raster.PixelWidth) continue;
                    ReadPixel(src, raster.PixelFormat, rx, out byte r, out byte g, out byte b);
                    int i = rowBase + cx * channels;
                    if (channels == 3)
                    {
                        canvas[i] = r; canvas[i + 1] = g; canvas[i + 2] = b;
                    }
                    else
                    {
                        canvas[i] = (byte)((r * 299 + g * 587 + b * 114 + 500) / 1000);
                    }
                }
            }
        }

        private static void ReadPixel(ReadOnlySpan<byte> row, RasterPixelFormat format, int x, out byte r, out byte g, out byte b)
        {
            switch (format)
            {
                case RasterPixelFormat.Bgra32:
                {
                    int i = x * 4, a = row[i + 3];
                    b = Over(row[i], a); g = Over(row[i + 1], a); r = Over(row[i + 2], a);
                    return;
                }
                case RasterPixelFormat.Rgb24:
                    r = row[x * 3]; g = row[x * 3 + 1]; b = row[x * 3 + 2];
                    return;
                case RasterPixelFormat.Gray8:
                    r = g = b = row[x];
                    return;
                case RasterPixelFormat.BlackWhite1:
                    r = g = b = (row[x >> 3] & (0x80 >> (x & 7))) != 0 ? (byte)0 : (byte)255;
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(format));
            }
        }

        private static byte Over(int channel, int alpha) => (byte)((channel * alpha + 255 * (255 - alpha) + 127) / 255);

        private void DrawRect(byte[] canvas, int width, int height, int channels, RectanglePoints rect, byte r, byte g, byte b)
        {
            int x0 = (int)Math.Round(rect.X / 72.0 * DpiX);
            int y0 = (int)Math.Round(rect.Y / 72.0 * DpiY);
            int x1 = (int)Math.Round(rect.Right / 72.0 * DpiX) - 1;
            int y1 = (int)Math.Round(rect.Bottom / 72.0 * DpiY) - 1;
            for (int x = x0; x <= x1; x++) { Plot(canvas, width, height, channels, x, y0, r, g, b); Plot(canvas, width, height, channels, x, y1, r, g, b); }
            for (int y = y0; y <= y1; y++) { Plot(canvas, width, height, channels, x0, y, r, g, b); Plot(canvas, width, height, channels, x1, y, r, g, b); }
        }

        private static void Plot(byte[] canvas, int width, int height, int channels, int x, int y, byte r, byte g, byte b)
        {
            if ((uint)x >= (uint)width || (uint)y >= (uint)height) return;
            int i = (y * width + x) * channels;
            if (channels == 3) { canvas[i] = r; canvas[i + 1] = g; canvas[i + 2] = b; }
            else canvas[i] = (byte)((r * 299 + g * 587 + b * 114 + 500) / 1000);
        }
    }
}
