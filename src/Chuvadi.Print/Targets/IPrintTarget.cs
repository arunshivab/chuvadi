using System;

namespace Chuvadi.Print
{
    /// <summary>
    /// Where a print job's pages go: a physical printer (Chuvadi.Print.Windows), image files
    /// (<see cref="ImageFileTarget"/>), or anything the host implements.
    /// </summary>
    /// <remarks>
    /// Call order for one job: <see cref="GetCapabilities"/>, <see cref="StartJob"/>, then
    /// <see cref="PrintPage"/> once per impression, then <see cref="EndJob"/>; or
    /// <see cref="AbortJob"/> after <see cref="StartJob"/> if the job fails or is cancelled.
    /// </remarks>
    public interface IPrintTarget
    {
        /// <summary>Display name of the destination (printer name, folder, ...). Recorded in the audit trail.</summary>
        string Name { get; }

        /// <summary>Describes the device for these settings (resolution, hard margins, what it can do itself).</summary>
        PrintTargetCapabilities GetCapabilities(PrintSettings settings);

        /// <summary>Begins a job.</summary>
        void StartJob(PrintJobContext context);

        /// <summary>
        /// Emits one side of one sheet. <see cref="PrintedPage.Raster"/> is only valid for the duration
        /// of this call; copy anything that must outlive it.
        /// </summary>
        void PrintPage(PrintedPage page);

        /// <summary>Completes the job.</summary>
        void EndJob();

        /// <summary>Abandons the job. <paramref name="reason"/> is null when the job was cancelled.</summary>
        void AbortJob(Exception? reason);
    }

    /// <summary>What a print target can do for a given set of settings.</summary>
    public sealed class PrintTargetCapabilities
    {
        private double _dpiX = 300;
        private double _dpiY = 300;

        /// <summary>Horizontal device resolution in pixels per inch.</summary>
        public double DpiX
        {
            get => _dpiX;
            init => _dpiX = value > 0 && !double.IsInfinity(value) ? value : throw new ArgumentOutOfRangeException(nameof(DpiX));
        }

        /// <summary>Vertical device resolution in pixels per inch.</summary>
        public double DpiY
        {
            get => _dpiY;
            init => _dpiY = value > 0 && !double.IsInfinity(value) ? value : throw new ArgumentOutOfRangeException(nameof(DpiY));
        }

        /// <summary>The device's non-printable edges, in the sheet's printed orientation.</summary>
        public Margins HardMargins { get; init; } = Margins.None;

        /// <summary>The device makes multiple copies itself.</summary>
        public bool SupportsCopies { get; init; }

        /// <summary>The device collates the copies it makes.</summary>
        public bool SupportsCollate { get; init; }

        /// <summary>The device prints both sides.</summary>
        public bool SupportsDuplex { get; init; }

        /// <summary>The device prints colour.</summary>
        public bool SupportsColor { get; init; } = true;

        /// <summary>
        /// The pixel format the device wants for colour output (<see cref="RasterPixelFormat.Bgra32"/> or
        /// <see cref="RasterPixelFormat.Rgb24"/>). Grayscale output is always Gray8 and monochrome
        /// output is always BlackWhite1.
        /// </summary>
        public RasterPixelFormat PreferredPixelFormat { get; init; } = RasterPixelFormat.Bgra32;

        /// <summary>Capabilities of an idealised 300 dpi device that does nothing itself.</summary>
        public static PrintTargetCapabilities Default { get; } = new();
    }

    /// <summary>Everything a target needs to know when a job starts.</summary>
    public sealed class PrintJobContext
    {
        internal PrintJobContext(Guid jobId, string? documentTitle, PrintSettings settings, PrintPlan plan,
                                 PrintTargetCapabilities capabilities, RasterPixelFormat outputPixelFormat)
        {
            JobId = jobId;
            DocumentTitle = documentTitle;
            Settings = settings;
            Plan = plan;
            Capabilities = capabilities;
            OutputPixelFormat = outputPixelFormat;
        }

        public Guid JobId { get; }
        public string? DocumentTitle { get; }

        /// <summary>A snapshot of the job's settings. Targets must not modify it.</summary>
        public PrintSettings Settings { get; }

        public PrintPlan Plan { get; }

        /// <summary>The capabilities the job was planned against.</summary>
        public PrintTargetCapabilities Capabilities { get; }

        /// <summary>When true, the target must make <see cref="PrintSettings.Copies"/> copies itself.</summary>
        public bool TargetHandlesCopies => Plan.TargetHandlesCopies;

        /// <summary>The pixel format every non-blank <see cref="PrintedPage.Raster"/> will have.</summary>
        public RasterPixelFormat OutputPixelFormat { get; }
    }

    /// <summary>One side of one sheet, ready for the device.</summary>
    public sealed class PrintedPage
    {
        internal PrintedPage(PlannedImpression impression, int impressionIndex, PagePlacement? placement, RasterPage? raster)
        {
            Impression = impression;
            ImpressionIndex = impressionIndex;
            Placement = placement;
            Raster = raster;
        }

        /// <summary>The planned impression this page fulfils.</summary>
        public PlannedImpression Impression { get; }

        /// <summary>0-based position in the job's output stream.</summary>
        public int ImpressionIndex { get; }

        /// <summary>1-based document page; 0 for a blank side.</summary>
        public int SourcePageNumber => Impression.SourcePageNumber;

        /// <summary>1-based copy number.</summary>
        public int CopyNumber => Impression.CopyNumber;

        /// <summary>0-based sheet index.</summary>
        public int SheetIndex => Impression.SheetIndex;

        public bool IsBackSide => Impression.IsBackSide;

        /// <summary>True for an inserted blank side; <see cref="Placement"/> and <see cref="Raster"/> are then null.</summary>
        public bool IsBlank => Impression.IsBlank;

        /// <summary>Where the raster goes on the sheet. Null for a blank side.</summary>
        public PagePlacement? Placement { get; }

        /// <summary>
        /// The rendered page, already rotated and scaled for the device: it covers
        /// <see cref="PagePlacement.ContentBounds"/> at the device resolution. Null for a blank side.
        /// Valid only during <see cref="IPrintTarget.PrintPage"/>.
        /// </summary>
        public RasterPage? Raster { get; }
    }
}
