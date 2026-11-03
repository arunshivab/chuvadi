using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Chuvadi.Print.Windows
{
    /// <summary>
    /// Prints to an installed Windows printer through the GDI spooler. Print settings become the driver's
    /// DEVMODE (paper, orientation, duplex, colour, copies, collation, tray, media, resolution); each page
    /// is drawn at its planned position with StretchDIBits, in bands so memory stays bounded.
    /// Never shows UI itself (some drivers, such as "Microsoft Print to PDF" without
    /// <see cref="OutputFileName"/>, may show their own).
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class WindowsPrinterTarget : IPrintTarget
    {
        private double? _maxRasterDpi = 300;
        private IntPtr _hdc;
        private DeviceGeometry _geometry;
        private bool _skipBlankSides;
        private bool _docStarted;

        /// <param name="printerName">Queue name as listed by <see cref="WindowsPrinters.List"/>.</param>
        public WindowsPrinterTarget(string printerName)
        {
            if (printerName is null) throw new ArgumentNullException(nameof(printerName));
            if (printerName.Length == 0) throw new ArgumentException("Printer name cannot be empty.", nameof(printerName));
            PrinterName = printerName;
        }

        /// <summary>A target for the current user's default printer.</summary>
        /// <exception cref="WindowsPrintException">No default printer is set.</exception>
        public static WindowsPrinterTarget Default()
            => new(WindowsPrinters.DefaultPrinterName ?? throw new WindowsPrintException("No default printer is set."));

        public string PrinterName { get; }

        /// <inheritdoc />
        public string Name => PrinterName;

        /// <summary>Paper source (tray). Null = driver default.</summary>
        public PrinterBin? Bin { get; set; }

        /// <summary>Media type. Null = driver default.</summary>
        public PrinterMediaType? MediaType { get; set; }

        /// <summary>Explicit device resolution; takes precedence over <see cref="Quality"/>. Null = driver default.</summary>
        public PrinterResolution? Resolution { get; set; }

        /// <summary>Driver quality preset, used when <see cref="Resolution"/> is null. Null = driver default.</summary>
        public PrintQuality? Quality { get; set; }

        /// <summary>
        /// Sends the driver's output to this file instead of the device (DOCINFO lpszOutput). With
        /// "Microsoft Print to PDF" this writes a PDF without a save dialog.
        /// </summary>
        public string? OutputFileName { get; set; }

        /// <summary>How the requested paper is matched to the driver's papers. Default <see cref="PaperMatching.ExactOrCustom"/>.</summary>
        public PaperMatching PaperMatching { get; set; } = PaperMatching.ExactOrCustom;

        /// <summary>
        /// Upper bound on the rendering resolution, so a 1200 dpi device does not produce huge page bitmaps;
        /// GDI scales the bitmap to the device. Null = render at full device resolution. Default 300.
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

        /// <summary>
        /// When true, the job fails if the driver cannot honour the settings (it ignored the paper size, or
        /// duplex was requested on a simplex printer). When false (default) it prints anyway: the sheet the
        /// driver chose is used and, without duplex support, blank back sides are not sent.
        /// </summary>
        public bool StrictCapabilities { get; set; }

        /// <summary>Spooler job id of the most recent job (0 before the first job).</summary>
        public int LastSpoolJobId { get; private set; }

        /// <summary>The device sheet the most recent job printed on, in points.</summary>
        public PageDimensions? LastDeviceSheet { get; private set; }

        // ------------------------------------------------------------------ fluent

        public WindowsPrinterTarget WithBin(PrinterBin? bin) { Bin = bin; return this; }
        public WindowsPrinterTarget WithMediaType(PrinterMediaType? mediaType) { MediaType = mediaType; return this; }
        public WindowsPrinterTarget WithResolution(PrinterResolution? resolution) { Resolution = resolution; return this; }
        public WindowsPrinterTarget WithQuality(PrintQuality? quality) { Quality = quality; return this; }
        public WindowsPrinterTarget WithOutputFile(string? outputFileName) { OutputFileName = outputFileName; return this; }
        public WindowsPrinterTarget WithPaperMatching(PaperMatching matching) { PaperMatching = matching; return this; }
        public WindowsPrinterTarget WithMaxRasterDpi(double? dpi) { MaxRasterDpi = dpi; return this; }
        public WindowsPrinterTarget WithStrictCapabilities(bool strict = true) { StrictCapabilities = strict; return this; }

        // ------------------------------------------------------------------ IPrintTarget

        /// <inheritdoc />
        public PrintTargetCapabilities GetCapabilities(PrintSettings settings)
        {
            if (settings is null) throw new ArgumentNullException(nameof(settings));
            return ReadCapabilities(settings).ToTargetCapabilities(MaxRasterDpi);
        }

        /// <summary>The full driver capabilities for these settings and this target's tray/media/resolution choices.</summary>
        public WindowsPrinterCapabilities ReadCapabilities(PrintSettings settings)
            => WindowsPrinters.GetCapabilities(PrinterName, settings, PaperMatching, Bin, MediaType, Resolution, Quality);

        /// <inheritdoc />
        public void StartJob(PrintJobContext context)
        {
            if (context is null) throw new ArgumentNullException(nameof(context));
            if (_hdc != IntPtr.Zero) throw new InvalidOperationException("A job is already in progress on this target.");
            PrintSettings settings = context.Settings;

            using PrinterSession session = PrinterSession.Open(PrinterName);
            DevModeRequest request = DevModeMapper.Map(settings, session.ReadPapers(), PaperMatching,
                                                       context.TargetHandlesCopies, Bin, MediaType, Resolution, Quality);
            DevModeBuffer devMode = session.GetDefaultDevMode();
            DevModeMapper.Apply(request, devMode);
            devMode = session.Merge(devMode);

            bool duplexRequested = settings.Duplex != Duplex.Simplex;
            bool duplexSupported = session.Capability(NativeMethods.DC_DUPLEX) == 1;
            if (duplexRequested && !duplexSupported && StrictCapabilities)
                throw new NotSupportedException("Printer '" + PrinterName + "' does not support two-sided printing.");
            _skipBlankSides = duplexRequested && !duplexSupported;

            IntPtr hdc = session.CreateContext(devMode, informationOnly: false);
            try
            {
                _geometry = PrinterSession.ReadGeometry(hdc);
                LastDeviceSheet = _geometry.SheetSize;
                PageDimensions planned = PageLayout.SheetFor(settings.Paper, settings.Orientation);
                if (StrictCapabilities && !_geometry.SheetMatches(planned))
                    throw new NotSupportedException("Printer '" + PrinterName + "' printed on " + _geometry.SheetSize
                                                    + " instead of the requested " + planned + ".");

                LastSpoolJobId = StartDocument(hdc, context.DocumentTitle);
                _hdc = hdc;
                _docStarted = true;
            }
            catch
            {
                NativeMethods.DeleteDC(hdc);
                throw;
            }
        }

        /// <inheritdoc />
        public void PrintPage(PrintedPage page)
        {
            if (page is null) throw new ArgumentNullException(nameof(page));
            if (_hdc == IntPtr.Zero) throw new InvalidOperationException("StartJob was not called.");
            if (page.IsBlank && _skipBlankSides) return;

            if (NativeMethods.StartPage(_hdc) <= 0)
                throw new WindowsPrintException("StartPage failed", Marshal.GetLastWin32Error());
            if (page.Raster is not null && page.Placement is not null)
                Draw(page.Raster, page.Placement);
            if (NativeMethods.EndPage(_hdc) <= 0)
                throw new WindowsPrintException("EndPage failed", Marshal.GetLastWin32Error());
        }

        /// <inheritdoc />
        public void EndJob()
        {
            IntPtr hdc = _hdc;
            if (hdc == IntPtr.Zero) return;
            try
            {
                if (_docStarted && NativeMethods.EndDoc(hdc) <= 0)
                    throw new WindowsPrintException("EndDoc failed", Marshal.GetLastWin32Error());
            }
            finally
            {
                Release();
            }
        }

        /// <inheritdoc />
        public void AbortJob(Exception? reason)
        {
            IntPtr hdc = _hdc;
            if (hdc == IntPtr.Zero) return;
            try
            {
                if (_docStarted) NativeMethods.AbortDoc(hdc);
            }
            finally
            {
                Release();
            }
        }

        // ------------------------------------------------------------------ drawing

        private void Draw(RasterPage raster, PagePlacement placement)
        {
            (int x, int y, int width, int height) = _geometry.ToDevice(placement.ContentBounds);
            if (width <= 0 || height <= 0) return;

            bool bilevel = raster.PixelFormat == RasterPixelFormat.BlackWhite1;
            NativeMethods.SetStretchBltMode(_hdc, bilevel ? NativeMethods.COLORONCOLOR : NativeMethods.HALFTONE);
            if (!bilevel) NativeMethods.SetBrushOrgEx(_hdc, 0, 0, IntPtr.Zero);

            var dib = new DibBand(raster);
            byte[] bits = ArrayPool<byte>.Shared.Rent(dib.Stride * dib.RowsPerBand);
            byte[] fullBandInfo = dib.BuildBitmapInfo(dib.RowsPerBand);
            try
            {
                for (int first = 0; first < raster.PixelHeight; first += dib.RowsPerBand)
                {
                    int rows = Math.Min(dib.RowsPerBand, raster.PixelHeight - first);
                    (int bandY, int bandHeight) = dib.DestinationRows(first, rows, y, height);
                    if (bandHeight <= 0) continue;
                    dib.FillBand(first, rows, bits);
                    byte[] info = rows == dib.RowsPerBand ? fullBandInfo : dib.BuildBitmapInfo(rows);
                    Blit(bits, info, x, bandY, width, bandHeight, raster.PixelWidth, rows);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bits);
            }
        }

        private void Blit(byte[] bits, byte[] info, int x, int y, int width, int height, int sourceWidth, int sourceRows)
        {
            var pinBits = GCHandle.Alloc(bits, GCHandleType.Pinned);
            var pinInfo = GCHandle.Alloc(info, GCHandleType.Pinned);
            try
            {
                int lines = NativeMethods.StretchDIBits(_hdc, x, y, width, height, 0, 0, sourceWidth, sourceRows,
                                                        pinBits.AddrOfPinnedObject(), pinInfo.AddrOfPinnedObject(),
                                                        NativeMethods.DIB_RGB_COLORS, NativeMethods.SRCCOPY);
                if (lines == 0 || lines == -1)
                    throw new WindowsPrintException("StretchDIBits failed", Marshal.GetLastWin32Error());
            }
            finally
            {
                pinBits.Free();
                pinInfo.Free();
            }
        }

        private int StartDocument(IntPtr hdc, string? title)
        {
            IntPtr docName = Marshal.StringToHGlobalUni(string.IsNullOrEmpty(title) ? "Chuvadi.Print job" : title);
            IntPtr output = OutputFileName is null ? IntPtr.Zero : Marshal.StringToHGlobalUni(OutputFileName);
            try
            {
                var info = new NativeMethods.DOCINFO
                {
                    cbSize = Marshal.SizeOf<NativeMethods.DOCINFO>(),
                    lpszDocName = docName,
                    lpszOutput = output
                };
                int jobId = NativeMethods.StartDocW(hdc, ref info);
                if (jobId <= 0)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw error == NativeMethods.ERROR_CANCELLED
                        ? new OperationCanceledException("The print job was cancelled in the driver's dialog.")
                        : new WindowsPrintException("StartDoc failed on '" + PrinterName + "'", error);
                }
                return jobId;
            }
            finally
            {
                Marshal.FreeHGlobal(docName);
                if (output != IntPtr.Zero) Marshal.FreeHGlobal(output);
            }
        }

        private void Release()
        {
            IntPtr hdc = _hdc;
            _hdc = IntPtr.Zero;
            _docStarted = false;
            _skipBlankSides = false;
            if (hdc != IntPtr.Zero) NativeMethods.DeleteDC(hdc);
        }
    }
}
