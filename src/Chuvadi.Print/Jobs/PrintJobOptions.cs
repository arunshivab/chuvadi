using System;

namespace Chuvadi.Print
{
    /// <summary>Who makes the copies requested in <see cref="PrintSettings.Copies"/>.</summary>
    public enum CopyHandling
    {
        /// <summary>
        /// The target makes copies when its capabilities say it can (and can collate, if collation is
        /// requested); otherwise the job makes them in software.
        /// </summary>
        Auto,
        /// <summary>Always let the target make copies; the job sends each sheet once.</summary>
        Target,
        /// <summary>Always make copies in software by sending sheets repeatedly.</summary>
        Software
    }

    /// <summary>How grayscale is reduced to pure black and white for <see cref="ColorMode.Monochrome"/>.</summary>
    public enum MonochromeConversion
    {
        /// <summary>Each pixel darker than <see cref="PrintJobOptions.MonochromeThreshold"/> becomes black. Crisp text.</summary>
        Threshold,
        /// <summary>8x8 Bayer ordered dither. Even, patterned halftones.</summary>
        OrderedDither,
        /// <summary>Floyd-Steinberg error diffusion. Best for photographs and gradients.</summary>
        ErrorDiffusion
    }

    /// <summary>A snapshot of job progress, reported after each impression.</summary>
    public readonly struct PrintProgress
    {
        public PrintProgress(int impressionsCompleted, int impressionsTotal, int currentPageNumber)
        {
            ImpressionsCompleted = impressionsCompleted;
            ImpressionsTotal = impressionsTotal;
            CurrentPageNumber = currentPageNumber;
        }

        /// <summary>Impressions sent to the target so far.</summary>
        public int ImpressionsCompleted { get; }

        /// <summary>Impressions in the plan.</summary>
        public int ImpressionsTotal { get; }

        /// <summary>1-based document page of the impression just completed; 0 for a blank side.</summary>
        public int CurrentPageNumber { get; }

        /// <summary>Completion fraction in [0, 1].</summary>
        public double Fraction => ImpressionsTotal == 0 ? 1.0 : (double)ImpressionsCompleted / ImpressionsTotal;
    }

    /// <summary>
    /// How a <see cref="PrintJob"/> executes (as opposed to <see cref="PrintSettings"/>, which says what
    /// to print). Options stay with the machine that prints; they are not carried in the spool envelope.
    /// </summary>
    public sealed class PrintJobOptions
    {
        private double? _renderDpiX;
        private double? _renderDpiY;

        /// <summary>Turns document pages into pixels. Required before <see cref="PrintJob.PrintTo"/>.</summary>
        public IPageRasterizer? Rasterizer { get; set; }

        /// <summary>Receives exactly one event per job. Default: discards events.</summary>
        public IPrintAuditSink AuditSink { get; set; } = NullAuditSink.Instance;

        /// <summary>Horizontal rendering resolution. Null (default) uses the target's <see cref="PrintTargetCapabilities.DpiX"/>.</summary>
        public double? RenderDpiX
        {
            get => _renderDpiX;
            set => _renderDpiX = ValidateDpi(value, nameof(RenderDpiX));
        }

        /// <summary>Vertical rendering resolution. Null (default) uses the target's <see cref="PrintTargetCapabilities.DpiY"/>.</summary>
        public double? RenderDpiY
        {
            get => _renderDpiY;
            set => _renderDpiY = ValidateDpi(value, nameof(RenderDpiY));
        }

        /// <summary>Who makes copies. Default <see cref="CopyHandling.Auto"/>.</summary>
        public CopyHandling CopyHandling { get; set; } = CopyHandling.Auto;

        /// <summary>How monochrome output is produced. Default <see cref="MonochromeConversion.Threshold"/>.</summary>
        public MonochromeConversion Monochrome { get; set; } = MonochromeConversion.Threshold;

        /// <summary>Gray level (0-255) below which a pixel is black, for <see cref="MonochromeConversion.Threshold"/>. Default 128.</summary>
        public byte MonochromeThreshold { get; set; } = 128;

        /// <summary>How requested margins combine with the device's hard margins. Default <see cref="MarginReference.SheetEdge"/>.</summary>
        public MarginReference MarginReference { get; set; } = MarginReference.SheetEdge;

        /// <summary>Render annotations. Default true.</summary>
        public bool IncludeAnnotations { get; set; } = true;

        /// <summary>Render form-field appearances. Default true.</summary>
        public bool IncludeFormFields { get; set; } = true;

        /// <summary>Anti-alias text and vector graphics. Default true.</summary>
        public bool AntiAlias { get; set; } = true;

        /// <summary>
        /// When false (default), a failure or cancellation is reported through the returned
        /// <see cref="PrintResult"/> (and the audit trail). When true, the exception is rethrown after
        /// the job has been aborted and audited.
        /// </summary>
        public bool ThrowOnFailure { get; set; }

        /// <summary>Receives a report after each impression. Default none.</summary>
        public IProgress<PrintProgress>? Progress { get; set; }

        public PrintJobOptions Clone() => new()
        {
            Rasterizer = Rasterizer,
            AuditSink = AuditSink,
            RenderDpiX = RenderDpiX,
            RenderDpiY = RenderDpiY,
            CopyHandling = CopyHandling,
            Monochrome = Monochrome,
            MonochromeThreshold = MonochromeThreshold,
            MarginReference = MarginReference,
            IncludeAnnotations = IncludeAnnotations,
            IncludeFormFields = IncludeFormFields,
            AntiAlias = AntiAlias,
            ThrowOnFailure = ThrowOnFailure,
            Progress = Progress
        };

        private static double? ValidateDpi(double? value, string name)
        {
            if (value.HasValue && (!(value.Value > 0) || double.IsInfinity(value.Value)))
                throw new ArgumentOutOfRangeException(name, "DPI must be a positive finite number.");
            return value;
        }
    }
}
