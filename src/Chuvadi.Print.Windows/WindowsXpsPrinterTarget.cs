using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;

namespace Chuvadi.Print.Windows
{
    /// <summary>How the print system finished an XPS job (PrintDocumentPackageCompletion).</summary>
    public enum XpsJobCompletion
    {
        /// <summary>The job was still in progress at the last status received.</summary>
        InProgress = 0,

        /// <summary>The print system finished the job.</summary>
        Completed = 1,

        /// <summary>The job was cancelled.</summary>
        Canceled = 2,

        /// <summary>The job failed; <see cref="XpsJobStatus.PackageStatus"/> holds the error.</summary>
        Failed = 3
    }

    /// <summary>The last status the print system reported for an XPS job (PrintDocumentPackageStatus).</summary>
    public sealed class XpsJobStatus
    {
        internal XpsJobStatus(int jobId, int currentDocument, int currentPage, int pagesProcessed, XpsJobCompletion completion,
                              int packageStatus, int updates)
        {
            JobId = jobId;
            CurrentDocument = currentDocument;
            CurrentPage = currentPage;
            PagesProcessed = pagesProcessed;
            Completion = completion;
            PackageStatus = packageStatus;
            Updates = updates;
        }

        /// <summary>The spooler job id.</summary>
        public int JobId { get; }

        /// <summary>Zero-based index of the document processed most recently.</summary>
        public int CurrentDocument { get; }

        /// <summary>Zero-based index of the page processed most recently, within <see cref="CurrentDocument"/>.</summary>
        public int CurrentPage { get; }

        /// <summary>Pages the print system has processed in the job so far.</summary>
        public int PagesProcessed { get; }

        public XpsJobCompletion Completion { get; }

        /// <summary>The job's error state (an HRESULT; 0 when there is none).</summary>
        public int PackageStatus { get; }

        /// <summary>How many status updates arrived for the job.</summary>
        public int Updates { get; }

        public override string ToString()
            => "job " + JobId + ": " + Completion + ", " + PagesProcessed + " pages processed, status 0x"
               + PackageStatus.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) + " (" + Updates + " updates)";
    }

    /// <summary>
    /// Prints vector pages to an installed Windows printer as XPS, through the Print Document Package API (Windows 8 and
    /// later): the driver draws the pages as vectors, so output is sharp at any resolution and small to spool (D-96
    /// position 4, <see cref="PrintRouteKind.WindowsQueue"/>). Pages come from an <see cref="IVectorPageSource"/> (a PDF
    /// converter, or the application's own pages through <see cref="PrintPages"/>).
    /// </summary>
    /// <remarks>
    /// <para>Pages are placed on the sheet with the same plan and layout maths as <see cref="WindowsPrinterTarget"/> (page
    /// selection, scale mode, alignment, margins, orientation, auto-rotate). The print settings become the same DEVMODE
    /// <see cref="WindowsPrinterTarget"/> uses (paper, orientation, duplex, colour, copies, collation, tray, media,
    /// resolution), which the operating system converts to the job's PrintTicket (PTConvertDevModeToPrintTicket), so
    /// settings behave alike on both targets.</para>
    /// <para>Grey and black-and-white output come from the driver (the PrintTicket asks for monochrome); the
    /// <see cref="PrintJobOptions.Monochrome"/> conversions apply only to rendered pages.</para>
    /// <para>The XPS is made by <see cref="XpsWriter"/>, then loaded into the operating system's XPS Object Model and
    /// written to the queue page by page; the Object Model re-serialises page markup on the way (Microsoft documents this
    /// round trip), which does not change what is drawn.</para>
    /// <para>Never shows UI itself. Some drivers show their own (Microsoft Print to PDF and Microsoft XPS Document Writer
    /// ask where to save).</para>
    /// </remarks>
    [SupportedOSPlatform("windows")]
    public sealed class WindowsXpsPrinterTarget : IDocumentTarget
    {
        /// <summary>The default for <see cref="StatusTimeout"/>: two minutes.</summary>
        public static readonly TimeSpan DefaultStatusTimeout = TimeSpan.FromMinutes(2);

        private TimeSpan? _statusTimeout = DefaultStatusTimeout;

        /// <param name="printerName">Queue name as listed by <see cref="WindowsPrinters.List"/>.</param>
        /// <param name="source">Turns the job's document into vector pages. Null when only <see cref="PrintPages"/> is used.</param>
        public WindowsXpsPrinterTarget(string printerName, IVectorPageSource? source = null)
        {
            if (printerName is null) throw new ArgumentNullException(nameof(printerName));
            if (printerName.Length == 0) throw new ArgumentException("Printer name cannot be empty.", nameof(printerName));
            PrinterName = printerName;
            Source = source;
        }

        public string PrinterName { get; }

        /// <inheritdoc />
        public string Name => PrinterName;

        /// <summary>Turns a job's document into vector pages for <see cref="Submit"/>. Null = only <see cref="PrintPages"/> can print.</summary>
        public IVectorPageSource? Source { get; set; }

        /// <summary>Paper source (tray). Null = driver default.</summary>
        public PrinterBin? Bin { get; set; }

        /// <summary>Media type. Null = driver default.</summary>
        public PrinterMediaType? MediaType { get; set; }

        /// <summary>Explicit device resolution; takes precedence over <see cref="Quality"/>. Null = driver default.</summary>
        public PrinterResolution? Resolution { get; set; }

        /// <summary>Driver quality preset, used when <see cref="Resolution"/> is null. Null = driver default.</summary>
        public PrintQuality? Quality { get; set; }

        /// <summary>How the requested paper is matched to the driver's papers. Default <see cref="PaperMatching.ExactOrCustom"/>.</summary>
        public PaperMatching PaperMatching { get; set; } = PaperMatching.ExactOrCustom;

        /// <summary>How requested margins combine with the queue's hard margins. Default <see cref="Chuvadi.Print.MarginReference.SheetEdge"/>.</summary>
        public MarginReference MarginReference { get; set; } = MarginReference.SheetEdge;

        /// <summary>
        /// Who makes copies. Default <see cref="Chuvadi.Print.CopyHandling.Auto"/>: the PrintTicket asks the driver for them
        /// when the driver says it can make them (and collate them, when asked); otherwise the sheets are repeated in the XPS.
        /// </summary>
        public CopyHandling CopyHandling { get; set; } = CopyHandling.Auto;

        /// <summary>
        /// When true, the job fails if the driver cannot honour the settings (it ignored the paper size, or duplex was
        /// requested on a simplex printer). When false (default) it prints anyway: without duplex support, blank back
        /// sides are not sent.
        /// </summary>
        public bool StrictCapabilities { get; set; }

        /// <summary>
        /// How long to wait, after the last page is written, for the print system to report the job finished. A job it
        /// reports failed or cancelled fails; no report in time fails too. Null = do not wait: the job counts as printed once
        /// the queue has taken every page. <see cref="Timeout.InfiniteTimeSpan"/> = wait without limit. Default
        /// <see cref="DefaultStatusTimeout"/> (two minutes).
        /// </summary>
        public TimeSpan? StatusTimeout
        {
            get => _statusTimeout;
            set
            {
                if (value is TimeSpan t && t != Timeout.InfiniteTimeSpan && (t < TimeSpan.Zero || t.TotalMilliseconds > int.MaxValue))
                    throw new ArgumentOutOfRangeException(nameof(StatusTimeout));
                _statusTimeout = value;
            }
        }

        /// <summary>Spooler job id of the most recent job (0 before the first job, or when the print system reported none).</summary>
        public int LastSpoolJobId { get; private set; }

        /// <summary>The last status the print system reported for the most recent job; null when it reported none.</summary>
        public XpsJobStatus? LastJobStatus { get; private set; }

        /// <summary>
        /// False when the print system did not accept the status sink for the most recent job; the job then counts as printed
        /// once the queue has taken every page, whatever <see cref="StatusTimeout"/> says.
        /// </summary>
        public bool LastStatusEventsConnected { get; private set; }

        /// <summary>The job PrintTicket (XML, UTF-8 as the operating system wrote it) sent with the most recent job.</summary>
        public byte[]? LastPrintTicket { get; private set; }

        /// <summary>Sheets (sides) in the most recent job's XPS.</summary>
        public int LastSheetCount { get; private set; }

        // ------------------------------------------------------------------ fluent

        public WindowsXpsPrinterTarget WithSource(IVectorPageSource? source) { Source = source; return this; }
        public WindowsXpsPrinterTarget WithBin(PrinterBin? bin) { Bin = bin; return this; }
        public WindowsXpsPrinterTarget WithMediaType(PrinterMediaType? mediaType) { MediaType = mediaType; return this; }
        public WindowsXpsPrinterTarget WithResolution(PrinterResolution? resolution) { Resolution = resolution; return this; }
        public WindowsXpsPrinterTarget WithQuality(PrintQuality? quality) { Quality = quality; return this; }
        public WindowsXpsPrinterTarget WithPaperMatching(PaperMatching matching) { PaperMatching = matching; return this; }
        public WindowsXpsPrinterTarget WithMarginReference(MarginReference reference) { MarginReference = reference; return this; }
        public WindowsXpsPrinterTarget WithCopyHandling(CopyHandling handling) { CopyHandling = handling; return this; }
        public WindowsXpsPrinterTarget WithStrictCapabilities(bool strict = true) { StrictCapabilities = strict; return this; }
        public WindowsXpsPrinterTarget WithStatusTimeout(TimeSpan? timeout) { StatusTimeout = timeout; return this; }

        // ------------------------------------------------------------------ capabilities and checks

        /// <summary>The full driver capabilities for these settings and this target's tray, media and resolution choices.</summary>
        public WindowsPrinterCapabilities ReadCapabilities(PrintSettings settings)
        {
            if (settings is null) throw new ArgumentNullException(nameof(settings));
            return WindowsPrinters.GetCapabilities(PrinterName, settings, PaperMatching, Bin, MediaType, Resolution, Quality);
        }

        /// <summary>
        /// Whether every page the settings select can be printed as XPS, without printing anything. Opens the document with
        /// <see cref="Source"/> and checks each selected page with <see cref="XpsWriter.CanWrite(VectorPage, out string?)"/>.
        /// </summary>
        /// <param name="document">The job's document, as <see cref="Source"/> reads it.</param>
        /// <param name="settings">Selects the pages.</param>
        /// <param name="reason">Why not (no source, no pages, or the first page and item XPS cannot express); null when it can.</param>
        /// <param name="cancellationToken">Checked before each page.</param>
        public bool CanPrint(ReadOnlyMemory<byte> document, PrintSettings settings, out string? reason, CancellationToken cancellationToken = default)
        {
            if (settings is null) throw new ArgumentNullException(nameof(settings));
            if (Source is null)
            {
                reason = "no vector page source is set";
                return false;
            }
            using IVectorDocument opened = Source.Open(document) ?? throw new InvalidOperationException("The vector source returned no document.");
            return CanPrint(opened, settings, out reason, cancellationToken);
        }

        /// <summary>Whether <see cref="PrintPages"/> could print these pages with these settings.</summary>
        public bool CanPrint(IReadOnlyList<VectorPage> pages, PrintSettings settings, out string? reason, CancellationToken cancellationToken = default)
        {
            if (pages is null) throw new ArgumentNullException(nameof(pages));
            if (settings is null) throw new ArgumentNullException(nameof(settings));
            return CanPrint(new VectorPageList(pages), settings, out reason, cancellationToken);
        }

        private static bool CanPrint(IVectorDocument document, PrintSettings settings, out string? reason, CancellationToken cancellationToken)
        {
            if (document.PageCount < 1)
            {
                reason = "the document has no pages";
                return false;
            }
            IReadOnlyList<int> selected = settings.Pages.Resolve(document.PageCount);
            if (selected.Count == 0)
            {
                reason = "the page selection picks no pages";
                return false;
            }
            var seen = new HashSet<int>();
            foreach (int number in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!seen.Add(number)) continue;
                VectorPage page = document.GetPage(number, cancellationToken)
                    ?? throw new InvalidOperationException("The vector source returned no page " + number + ".");
                if (!XpsWriter.CanWrite(page, out string? why))
                {
                    reason = "page " + number + ": " + why;
                    return false;
                }
            }
            reason = null;
            return true;
        }

        // ------------------------------------------------------------------ printing

        /// <inheritdoc />
        /// <exception cref="InvalidOperationException">No <see cref="Source"/> is set, or the job has no pages.</exception>
        /// <exception cref="NotSupportedException">A page holds content XPS cannot express, or <see cref="StrictCapabilities"/> refused the settings.</exception>
        /// <exception cref="PrinterNotFoundException">The queue does not exist or cannot be opened.</exception>
        /// <exception cref="WindowsPrintException">The print system refused or failed the job, or reported nothing within <see cref="StatusTimeout"/>.</exception>
        public void Submit(DocumentSubmission submission, CancellationToken cancellationToken)
        {
            if (submission is null) throw new ArgumentNullException(nameof(submission));
            WindowsPrinters.EnsureWindows();
            IVectorPageSource source = Source ?? throw new InvalidOperationException(
                "WindowsXpsPrinterTarget needs a vector page source (Source) to print a document.");
            using IVectorDocument document = source.Open(submission.Document)
                ?? throw new InvalidOperationException("The vector source returned no document.");
            Print(document, submission.Settings, submission.DocumentTitle, cancellationToken);
        }

        /// <summary>
        /// Prints pages the application drew itself. They are laid out on the sheets exactly as a document's pages would be
        /// (<paramref name="settings"/> selects pages, copies, duplex and the rest).
        /// </summary>
        /// <exception cref="NotSupportedException">A page holds content XPS cannot express, or <see cref="StrictCapabilities"/> refused the settings.</exception>
        /// <exception cref="PrinterNotFoundException">The queue does not exist or cannot be opened.</exception>
        /// <exception cref="WindowsPrintException">The print system refused or failed the job, or reported nothing within <see cref="StatusTimeout"/>.</exception>
        public void PrintPages(IReadOnlyList<VectorPage> pages, PrintSettings settings, string? documentTitle = null,
                               CancellationToken cancellationToken = default)
        {
            if (pages is null) throw new ArgumentNullException(nameof(pages));
            if (settings is null) throw new ArgumentNullException(nameof(settings));
            WindowsPrinters.EnsureWindows();
            Print(new VectorPageList(pages), settings, documentTitle, cancellationToken);
        }

        /// <summary>
        /// Writes the XPS package this target would send for a document, without printing: the same sheets, placement and
        /// copies. Reads the queue's capabilities (hard margins, copies, duplex), so the queue must exist.
        /// </summary>
        public void SaveXps(ReadOnlyMemory<byte> document, PrintSettings settings, Stream output, CancellationToken cancellationToken = default)
        {
            if (settings is null) throw new ArgumentNullException(nameof(settings));
            if (output is null) throw new ArgumentNullException(nameof(output));
            WindowsPrinters.EnsureWindows();
            IVectorPageSource source = Source ?? throw new InvalidOperationException("No vector page source (Source) is set.");
            using IVectorDocument opened = source.Open(document) ?? throw new InvalidOperationException("The vector source returned no document.");
            Prepared prepared = Prepare(opened, settings, cancellationToken);
            XpsWriter.Write(prepared.Sheets, output);
        }

        /// <summary>
        /// The job PrintTicket (XML) the operating system makes for these settings on this queue, as <see cref="Submit"/>
        /// would send it. Prints nothing.
        /// </summary>
        public byte[] CreatePrintTicket(PrintSettings settings)
        {
            if (settings is null) throw new ArgumentNullException(nameof(settings));
            WindowsPrinters.EnsureWindows();
            PrintTargetCapabilities capabilities = ReadCapabilities(settings).ToTargetCapabilities(null);
            settings = settings.ResolvePrinterDefaults(capabilities);
            bool targetCopies = PrintJob.TargetMakesCopies(settings, capabilities, CopyHandling);
            return XpsPackageSubmitter.CreatePrintTicket(PrinterName, BuildDevMode(settings, targetCopies));
        }

        private void Print(IVectorDocument document, PrintSettings settings, string? documentTitle, CancellationToken cancellationToken)
        {
            Prepared prepared = Prepare(document, settings, cancellationToken);

            byte[] xps;
            using (var buffer = new MemoryStream())
            {
                XpsWriter.Write(prepared.Sheets, buffer);
                xps = buffer.ToArray();
            }
            byte[] devMode = BuildDevMode(prepared.Settings, prepared.TargetMakesCopies);
            // A private job never names its title to the spooler, even when called directly with one.
            string jobName = settings.Private ? PrintSettings.PrivateJobName
                : string.IsNullOrEmpty(documentTitle) ? "Chuvadi.Print job" : documentTitle!;

            LastSpoolJobId = 0;
            LastJobStatus = null;
            LastPrintTicket = null;
            LastStatusEventsConnected = false;
            LastSheetCount = prepared.Sheets.Count;

            XpsSubmissionResult result = XpsPackageSubmitter.Submit(
                new XpsSubmissionRequest(PrinterName, jobName, xps, devMode, StatusTimeout), cancellationToken);
            Record(result);
        }

        private void Record(XpsSubmissionResult result)
        {
            LastPrintTicket = result.PrintTicket;
            LastStatusEventsConnected = result.StatusEventsConnected;
            if (result.Status is PrintDocumentPackageStatus s)
            {
                LastSpoolJobId = (int)s.JobId;
                LastJobStatus = new XpsJobStatus((int)s.JobId, s.CurrentDocument, s.CurrentPage, s.CurrentPageTotal,
                                                 (XpsJobCompletion)s.Completion, s.PackageStatus, result.StatusUpdates);
            }
        }

        private sealed class Prepared
        {
            public Prepared(IReadOnlyList<VectorPage> sheets, bool targetMakesCopies, PrintSettings settings)
            {
                Sheets = sheets;
                TargetMakesCopies = targetMakesCopies;
                Settings = settings;
            }

            public IReadOnlyList<VectorPage> Sheets { get; }
            public bool TargetMakesCopies { get; }

            /// <summary>The settings the sheets were laid out with (printer defaults resolved).</summary>
            public PrintSettings Settings { get; }
        }

        private Prepared Prepare(IVectorDocument document, PrintSettings settings, CancellationToken cancellationToken)
        {
            if (settings.Copies < 1) throw new InvalidOperationException("PrintSettings.Copies must be at least 1.");
            WindowsPrinterCapabilities driver = ReadCapabilities(settings);
            PrintTargetCapabilities capabilities = driver.ToTargetCapabilities(null);
            settings = settings.ResolvePrinterDefaults(capabilities);

            bool duplexRequested = settings.Duplex != Duplex.Simplex;
            if (duplexRequested && !capabilities.SupportsDuplex && StrictCapabilities)
                throw new NotSupportedException("Printer '" + PrinterName + "' does not support two-sided printing.");
            PageDimensions planned = PageLayout.SheetFor(settings.Paper, settings.Orientation);
            if (StrictCapabilities && !driver.Geometry.SheetMatches(planned))
                throw new NotSupportedException("Printer '" + PrinterName + "' would print on " + driver.DeviceSheet
                                                + " instead of the requested " + planned + ".");

            bool targetCopies = PrintJob.TargetMakesCopies(settings, capabilities, CopyHandling);
            IReadOnlyList<VectorPage> sheets = XpsSheetBuilder.Build(document, settings, capabilities, MarginReference, targetCopies,
                                                                    skipBlankSides: duplexRequested && !capabilities.SupportsDuplex,
                                                                    cancellationToken);
            if (!XpsWriter.CanWrite(sheets, out string? reason)) throw new NotSupportedException(reason);
            return new Prepared(sheets, targetCopies, settings);
        }

        /// <summary>The driver-merged DEVMODE for these settings, as <see cref="WindowsPrinterTarget"/> makes it.</summary>
        private byte[] BuildDevMode(PrintSettings settings, bool targetHandlesCopies)
        {
            using PrinterSession session = PrinterSession.Open(PrinterName);
            DevModeRequest request = DevModeMapper.Map(settings, session.ReadPapers(), PaperMatching, targetHandlesCopies,
                                                       Bin, MediaType, Resolution, Quality);
            DevModeBuffer devMode = session.GetDefaultDevMode();
            DevModeMapper.Apply(request, devMode);
            return session.Merge(devMode).Bytes;
        }
    }
}
