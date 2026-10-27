namespace Chuvadi.Print
{
    /// <summary>How margins requested in <see cref="PrintSettings.Margins"/> relate to the device's hard margins.</summary>
    public enum MarginReference
    {
        /// <summary>
        /// Margins are measured from the sheet edge. Each edge uses the larger of the requested margin
        /// and the device's hard (non-printable) margin.
        /// </summary>
        SheetEdge,

        /// <summary>
        /// Margins are measured from the edge of the device's printable area: each edge is the
        /// requested margin plus the device's hard margin.
        /// </summary>
        PrintableArea
    }

    /// <summary>Where and how one document page lands on a sheet. Produced by <see cref="PageLayout.Compute"/>.</summary>
    public sealed class PagePlacement
    {
        internal PagePlacement(PageDimensions pageSize, PageDimensions sheetSize, Margins effectiveMargins,
                               RectanglePoints printableArea, RectanglePoints contentBounds,
                               double scaleFactor, PageRotation rotation, bool isClipped)
        {
            PageSize = pageSize;
            SheetSize = sheetSize;
            EffectiveMargins = effectiveMargins;
            PrintableArea = printableArea;
            ContentBounds = contentBounds;
            ScaleFactor = scaleFactor;
            Rotation = rotation;
            IsClipped = isClipped;
        }

        /// <summary>The document page's natural size, before rotation and scaling.</summary>
        public PageDimensions PageSize { get; }

        /// <summary>The sheet in the orientation it is printed (landscape sheets are wider than tall).</summary>
        public PageDimensions SheetSize { get; }

        /// <summary>The margins actually applied on each edge, after combining with the device's hard margins.</summary>
        public Margins EffectiveMargins { get; }

        /// <summary>The area content may occupy: the sheet minus <see cref="EffectiveMargins"/>.</summary>
        public RectanglePoints PrintableArea { get; }

        /// <summary>Where the (rotated, scaled) page lands on the sheet. May extend beyond <see cref="PrintableArea"/>.</summary>
        public RectanglePoints ContentBounds { get; }

        /// <summary>Uniform scale applied to the page (1.0 = actual size).</summary>
        public double ScaleFactor { get; }

        /// <summary>Clockwise rotation applied to the page content.</summary>
        public PageRotation Rotation { get; }

        /// <summary>True when the content extends beyond the printable area and will be cut off.</summary>
        public bool IsClipped { get; }
    }
}
