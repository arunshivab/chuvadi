using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Chuvadi.Print.Ipp
{
    /// <summary>
    /// Prints rendered pages to an IPP Everywhere printer as PWG Raster (driverless, any OS). Each sheet is
    /// composed at its planned position, turned to the printer's portrait feed orientation, transformed for
    /// duplex back sides as the printer requests, and spooled to a temporary file (one sheet in memory at a
    /// time) before a single Print-Job. Use with <see cref="PrintJob.PrintTo"/>.
    /// </summary>
    public sealed class IppPrinterTarget : IPrintTarget, IDisposable
    {
        public const string PwgRasterFormat = "image/pwg-raster";

        private readonly IppClient _client;
        private readonly bool _ownsClient;
        private readonly List<string> _approximations = new();
        private double? _maxRasterDpi = 300;

        private IppPrinterCapabilities? _caps;
        private PrintJobContext? _context;
        private FileStream? _spool;
        private PwgRasterWriter? _writer;
        private IppAttributeGroup? _ticket;
        private PwgColorSpace _colorSpace;
        private IppResolution _resolution;
        private PwgSheetBack _sheetBack;
        private string _mediaName = string.Empty;

        public IppPrinterTarget(string printerUri, IppClientOptions? clientOptions = null)
        {
            _client = new IppClient(printerUri, clientOptions);
            _ownsClient = true;
        }

        public IppPrinterTarget(IppClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public IppClient Client => _client;

        /// <inheritdoc />
        public string Name => _client.PrinterUri;

        /// <summary>Tray, media type, quality, strictness and extra attributes.</summary>
        public IppJobOptions JobOptions { get; set; } = new();

        /// <summary>
        /// Highest raster resolution to use; the printer's best supported PWG resolution at or below it is chosen
        /// (or its lowest, if all are higher). Null = the printer's highest. Default 300.
        /// </summary>
        public double? MaxRasterDpi
        {
            get => _maxRasterDpi;
            set
            {
                if (value.HasValue && (!(value.Value > 0) || double.IsInfinity(value.Value)))
                    throw new ArgumentOutOfRangeException(nameof(MaxRasterDpi));
                _maxRasterDpi = value;
            }
        }

        /// <summary>Overrides the printer's pwg-raster-document-sheet-back. Null (default) = use the printer's value.</summary>
        public PwgSheetBack? SheetBackOverride { get; set; }

        /// <summary>Folder for the temporary spool file. Null = the system temp folder.</summary>
        public string? TemporaryFolder { get; set; }

        /// <summary>When set, waits up to this long for the printer to finish the job (see <see cref="IppDocumentTarget.WaitForCompletion"/>).</summary>
        public TimeSpan? WaitForCompletion { get; set; }

        public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

        /// <summary>Printer job id of the most recent job (0 before the first).</summary>
        public int LastJobId { get; private set; }

        /// <summary>Settings that could not be expressed exactly in the most recent job.</summary>
        public IReadOnlyList<string> LastApproximations => _approximations;

        /// <summary>Printer capabilities read for the most recent job.</summary>
        public IppPrinterCapabilities? LastCapabilities => _caps;

        public IppPrinterTarget WithJobOptions(Action<IppJobOptions> configure)
        {
            (configure ?? throw new ArgumentNullException(nameof(configure)))(JobOptions);
            return this;
        }

        public IppPrinterTarget WithMaxRasterDpi(double? dpi) { MaxRasterDpi = dpi; return this; }
        public IppPrinterTarget WithSheetBack(PwgSheetBack? sheetBack) { SheetBackOverride = sheetBack; return this; }
        public IppPrinterTarget WithWaitForCompletion(TimeSpan? timeout) { WaitForCompletion = timeout; return this; }

        // ------------------------------------------------------------------ IPrintTarget

        /// <inheritdoc />
        public PrintTargetCapabilities GetCapabilities(PrintSettings settings)
        {
            if (settings is null) throw new ArgumentNullException(nameof(settings));
            IppPrinterCapabilities caps = new(_client.GetPrinterAttributes().Group(IppTag.PrinterAttributes)
                                              ?? throw new IppException("The printer returned no printer attributes."));
            _caps = caps;
            if (!caps.SupportsFormat(PwgRasterFormat))
                throw new NotSupportedException("The printer does not accept PWG Raster (" + PwgRasterFormat
                                                + "). Send the PDF with IppDocumentTarget and PrintJob.SubmitTo instead.");
            IppResolution resolution = ChooseResolution(caps.PwgRasterResolutions, MaxRasterDpi);
            bool color = caps.SupportsColor && IppPrinterCapabilities.Contains(caps.PwgRasterTypes, "srgb_8");
            return new PrintTargetCapabilities
            {
                DpiX = resolution.CrossFeedDpi,
                DpiY = resolution.FeedDpi,
                HardMargins = caps.MinimumMargins,
                SupportsCopies = caps.MaxCopies > 1,
                SupportsCollate = caps.SupportsCollatedCopies,
                SupportsDuplex = caps.SupportsDuplex,
                SupportsColor = color,
                PreferredPixelFormat = RasterPixelFormat.Rgb24
            };
        }

        /// <inheritdoc />
        public void StartJob(PrintJobContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            IppPrinterCapabilities caps = _caps ?? throw new InvalidOperationException("GetCapabilities was not called.");
            if (!caps.IsAcceptingJobs) throw new IppException("The printer is not accepting jobs.");

            _approximations.Clear();
            _resolution = ChooseResolution(caps.PwgRasterResolutions, MaxRasterDpi);
            _colorSpace = context.OutputPixelFormat switch
            {
                RasterPixelFormat.BlackWhite1 when IppPrinterCapabilities.Contains(caps.PwgRasterTypes, "black_1") => PwgColorSpace.Black1,
                RasterPixelFormat.Bgra32 or RasterPixelFormat.Rgb24 when IppPrinterCapabilities.Contains(caps.PwgRasterTypes, "srgb_8") => PwgColorSpace.SRgb8,
                _ => PwgColorSpace.SGray8
            };
            if (caps.PwgRasterTypes.Count > 0 && !IppPrinterCapabilities.Contains(caps.PwgRasterTypes, TypeName(_colorSpace)))
                throw new NotSupportedException("The printer supports none of the PWG Raster types this job can produce.");
            _sheetBack = SheetBackOverride ?? caps.PwgRasterSheetBack;
            _mediaName = PwgMediaNames.NameFor(context.Settings.Paper, caps.Media);
            _ticket = IppJobTicket.Build(context.Settings, caps, JobOptions, documentMode: false,
                                         printerMakesCopies: context.TargetHandlesCopies, null, _resolution, _approximations);

            string folder = TemporaryFolder ?? Path.GetTempPath();
            _spool = new FileStream(Path.Combine(folder, "chuvadi-ipp-" + Guid.NewGuid().ToString("N") + ".pwg"),
                                    FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose);
            _writer = new PwgRasterWriter(_spool);
        }

        /// <inheritdoc />
        public void PrintPage(PrintedPage page)
        {
            if (page is null) throw new ArgumentNullException(nameof(page));
            PrintJobContext context = _context ?? throw new InvalidOperationException("StartJob was not called.");
            PwgRasterWriter writer = _writer ?? throw new InvalidOperationException("StartJob was not called.");

            PageDimensions sheet = page.Placement?.SheetSize ?? PageLayout.SheetFor(context.Settings.Paper, context.Settings.Orientation);
            bool landscape = sheet.IsLandscape;
            double dpiX = _resolution.CrossFeedDpi, dpiY = _resolution.FeedDpi;

            // The composed sheet, in the orientation it was laid out (landscape sheets are wider than tall).
            int sheetW = Math.Max(1, (int)Math.Round(sheet.WidthPoints / 72.0 * (landscape ? dpiY : dpiX)));
            int sheetH = Math.Max(1, (int)Math.Round(sheet.HeightPoints / 72.0 * (landscape ? dpiX : dpiY)));
            int channels = _colorSpace == PwgColorSpace.SRgb8 ? 3 : 1;
            long canvasSize = (long)sheetW * sheetH * channels;
            if (canvasSize > Array.MaxLength) throw new InvalidOperationException("Sheet too large at this resolution.");
            byte[] canvas = ArrayPool<byte>.Shared.Rent((int)canvasSize);
            try
            {
                canvas.AsSpan(0, (int)canvasSize).Fill(255);
                if (page.Raster is not null && page.Placement is not null)
                    Paste(canvas, sheetW, sheetH, channels, page.Raster, page.Placement.ContentBounds,
                          landscape ? dpiY : dpiX, landscape ? dpiX : dpiY);

                // Media is fed portrait: a landscape sheet is turned 90 degrees counter-clockwise.
                int mediaW = landscape ? sheetH : sheetW;
                int mediaH = landscape ? sheetW : sheetH;
                (int crossFeed, int feed) = page.IsBackSide ? BackTransform(context.Settings.Duplex, _sheetBack) : (1, 1);

                var header = new PwgPageHeader
                {
                    ColorSpace = _colorSpace,
                    Width = mediaW,
                    Height = mediaH,
                    DpiX = (int)Math.Round(dpiX),
                    DpiY = (int)Math.Round(dpiY),
                    PageWidthPoints = (int)Math.Round(Math.Min(sheet.WidthPoints, sheet.HeightPoints)),
                    PageHeightPoints = (int)Math.Round(Math.Max(sheet.WidthPoints, sheet.HeightPoints)),
                    PageSizeName = _mediaName,
                    Duplex = context.Settings.Duplex != Duplex.Simplex,
                    Tumble = context.Settings.Duplex == Duplex.Horizontal,
                    TotalPageCount = context.Plan.Impressions.Count,
                    CrossFeedTransform = crossFeed,
                    FeedTransform = feed,
                    PrintQuality = JobOptions.Quality.HasValue ? (int)JobOptions.Quality.Value : 0,
                    MediaType = JobOptions.MediaType ?? string.Empty
                };

                writer.BeginPage(header);
                byte[] line = ArrayPool<byte>.Shared.Rent(header.BytesPerLine);
                try
                {
                    Span<byte> lineSpan = line.AsSpan(0, header.BytesPerLine);
                    for (int y = 0; y < mediaH; y++)
                    {
                        int my = feed < 0 ? mediaH - 1 - y : y;
                        FillMediaLine(canvas, sheetW, sheetH, channels, landscape, mediaW, my, crossFeed < 0, lineSpan);
                        writer.WriteLine(lineSpan);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(line);
                }
                writer.EndPage();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(canvas);
            }
        }

        /// <inheritdoc />
        public void EndJob()
        {
            FileStream spool = _spool ?? throw new InvalidOperationException("StartJob was not called.");
            try
            {
                spool.Flush();
                spool.Position = 0;
                LastJobId = _client.PrintJob(spool, PwgRasterFormat, _ticket, _context?.DocumentTitle ?? "Chuvadi.Print job");
                if (WaitForCompletion.HasValue)
                    IppJobWaiter.Wait(_client, LastJobId, WaitForCompletion.Value, PollInterval, CancellationToken.None);
            }
            finally
            {
                Reset();
            }
        }

        /// <inheritdoc />
        public void AbortJob(Exception? reason) => Reset();

        public void Dispose()
        {
            Reset();
            if (_ownsClient) _client.Dispose();
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Back-side transform for PWG Raster (as CUPS' cupsRasterInitPWGHeader applies it): returns the
        /// cross-feed and feed transforms (1 = as is, -1 = mirrored) for a back side.
        /// </summary>
        internal static (int CrossFeed, int Feed) BackTransform(Duplex duplex, PwgSheetBack sheetBack)
        {
            bool tumble = duplex == Duplex.Horizontal;
            return sheetBack switch
            {
                PwgSheetBack.Flipped => tumble ? (-1, 1) : (1, -1),
                PwgSheetBack.ManualTumble => tumble ? (-1, -1) : (1, 1),
                PwgSheetBack.Rotated => tumble ? (1, 1) : (-1, -1),
                _ => (1, 1)
            };
        }

        internal static IppResolution ChooseResolution(IReadOnlyList<IppResolution> supported, double? maxDpi)
        {
            if (supported.Count == 0) return new IppResolution(300, 300);
            IppResolution? best = null, lowest = null, highest = null;
            foreach (IppResolution r in supported)
            {
                double dpi = Math.Max(r.CrossFeedDpi, r.FeedDpi);
                if (lowest is null || dpi < Math.Max(lowest.Value.CrossFeedDpi, lowest.Value.FeedDpi)) lowest = r;
                if (highest is null || dpi > Math.Max(highest.Value.CrossFeedDpi, highest.Value.FeedDpi)) highest = r;
                if (maxDpi.HasValue && dpi <= maxDpi.Value
                    && (best is null || dpi > Math.Max(best.Value.CrossFeedDpi, best.Value.FeedDpi)))
                    best = r;
            }
            return maxDpi.HasValue ? best ?? lowest!.Value : highest!.Value;
        }

        internal static string TypeName(PwgColorSpace space) => space switch
        {
            PwgColorSpace.Black1 => "black_1",
            PwgColorSpace.SGray8 => "sgray_8",
            _ => "srgb_8"
        };

        /// <summary>Nearest-neighbour paste of the raster into the sheet canvas (gray or RGB, white paper).</summary>
        private static void Paste(byte[] canvas, int width, int height, int channels, RasterPage raster,
                                  RectanglePoints bounds, double dpiX, double dpiY)
        {
            double left = bounds.X / 72.0 * dpiX, top = bounds.Y / 72.0 * dpiY;
            double w = bounds.Width / 72.0 * dpiX, h = bounds.Height / 72.0 * dpiY;
            if (w <= 0 || h <= 0) return;
            int x0 = Math.Max(0, (int)Math.Floor(left)), y0 = Math.Max(0, (int)Math.Floor(top));
            int x1 = Math.Min(width, (int)Math.Ceiling(left + w)), y1 = Math.Min(height, (int)Math.Ceiling(top + h));
            double sx = raster.PixelWidth / w, sy = raster.PixelHeight / h;
            for (int cy = y0; cy < y1; cy++)
            {
                int ry = (int)((cy + 0.5 - top) * sy);
                if (ry < 0 || ry >= raster.PixelHeight) continue;
                ReadOnlySpan<byte> src = raster.GetRow(ry);
                int rowBase = cy * width * channels;
                for (int cx = x0; cx < x1; cx++)
                {
                    int rx = (int)((cx + 0.5 - left) * sx);
                    if (rx < 0 || rx >= raster.PixelWidth) continue;
                    byte r, g, b;
                    switch (raster.PixelFormat)
                    {
                        case RasterPixelFormat.Bgra32:
                        {
                            int i = rx * 4, a = src[i + 3];
                            b = Over(src[i], a); g = Over(src[i + 1], a); r = Over(src[i + 2], a);
                            break;
                        }
                        case RasterPixelFormat.Rgb24:
                            r = src[rx * 3]; g = src[rx * 3 + 1]; b = src[rx * 3 + 2];
                            break;
                        case RasterPixelFormat.Gray8:
                            r = g = b = src[rx];
                            break;
                        default:
                            r = g = b = (src[rx >> 3] & (0x80 >> (rx & 7))) != 0 ? (byte)0 : (byte)255;
                            break;
                    }
                    int o = rowBase + cx * channels;
                    if (channels == 3) { canvas[o] = r; canvas[o + 1] = g; canvas[o + 2] = b; }
                    else canvas[o] = (byte)((r * 299 + g * 587 + b * 114 + 500) / 1000);
                }
            }
        }

        /// <summary>Produces one media line (portrait feed orientation) from the sheet canvas.</summary>
        private void FillMediaLine(byte[] canvas, int sheetW, int sheetH, int channels, bool landscape,
                                   int mediaW, int mediaY, bool mirrorX, Span<byte> line)
        {
            if (_colorSpace == PwgColorSpace.Black1) line.Clear();
            for (int mx = 0; mx < mediaW; mx++)
            {
                int x = mirrorX ? mediaW - 1 - mx : mx;
                // Portrait media (x, y) <- landscape sheet rotated 90 degrees counter-clockwise:
                // media column x is sheet row x, media row y is sheet column (sheetW - 1 - y).
                int sx = landscape ? sheetW - 1 - mediaY : x;
                int sy = landscape ? x : mediaY;
                int o = (sy * sheetW + sx) * channels;
                switch (_colorSpace)
                {
                    case PwgColorSpace.SRgb8:
                        line[mx * 3] = canvas[o]; line[mx * 3 + 1] = canvas[o + 1]; line[mx * 3 + 2] = canvas[o + 2];
                        break;
                    case PwgColorSpace.SGray8:
                        line[mx] = canvas[o];
                        break;
                    default:
                        if (canvas[o] < 128) line[mx >> 3] |= (byte)(0x80 >> (mx & 7));
                        break;
                }
            }
        }

        private static byte Over(int channel, int alpha) => (byte)((channel * alpha + 255 * (255 - alpha) + 127) / 255);

        private void Reset()
        {
            _writer = null;
            _ticket = null;
            _context = null;
            FileStream? spool = _spool;
            _spool = null;
            spool?.Dispose();
        }
    }
}
