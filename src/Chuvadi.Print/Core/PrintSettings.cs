namespace Chuvadi.Print
{
    /// <summary>
    /// Device-independent print intent. Every option carries a sensible default and the
    /// full set of choices (see the enums and value types). Object-initializer friendly.
    /// </summary>
    public sealed class PrintSettings
    {
        public PageSelection Pages { get; set; } = PageSelection.All;
        public int Copies { get; set; } = 1;
        public bool Collate { get; set; } = true;
        public Duplex Duplex { get; set; } = Duplex.Simplex;
        public ColorMode Color { get; set; } = ColorMode.Color;
        public PaperSize Paper { get; set; } = PaperSize.A4;
        public PageOrientation Orientation { get; set; } = PageOrientation.Portrait;
        public ScaleMode Scale { get; set; } = ScaleMode.FitToPage;

        /// <summary>Percentage used only when <see cref="Scale"/> is <see cref="ScaleMode.Custom"/>.</summary>
        public double CustomScalePercent { get; set; } = 100.0;

        public ContentAlignment Alignment { get; set; } = ContentAlignment.Center;
        public Margins Margins { get; set; } = Margins.Default;

        /// <summary>When true, print without showing any dialog (unattended/silent printing).</summary>
        public bool Silent { get; set; }

        public PrintSettings Clone() => new()
        {
            Pages = Pages,
            Copies = Copies,
            Collate = Collate,
            Duplex = Duplex,
            Color = Color,
            Paper = Paper,
            Orientation = Orientation,
            Scale = Scale,
            CustomScalePercent = CustomScalePercent,
            Alignment = Alignment,
            Margins = Margins,
            Silent = Silent
        };
    }
}
