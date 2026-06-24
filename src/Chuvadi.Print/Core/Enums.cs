namespace Chuvadi.Print
{
    /// <summary>Two-sided (duplex) printing mode. All printer-supported modes are represented.</summary>
    public enum Duplex
    {
        /// <summary>Single-sided.</summary>
        Simplex,
        /// <summary>Two-sided, flipped along the long edge (book binding).</summary>
        Vertical,
        /// <summary>Two-sided, flipped along the short edge (tablet binding).</summary>
        Horizontal
    }

    /// <summary>Colour rendering mode.</summary>
    public enum ColorMode
    {
        Color,
        Grayscale,
        Monochrome
    }

    /// <summary>Page orientation. All four rotations are represented.</summary>
    public enum PageOrientation
    {
        Portrait,
        Landscape,
        ReversePortrait,
        ReverseLandscape
    }

    /// <summary>How page content is scaled onto the sheet.</summary>
    public enum ScaleMode
    {
        /// <summary>No scaling; render at the document's natural size.</summary>
        ActualSize,
        /// <summary>Scale up or down so the whole page fits the printable area.</summary>
        FitToPage,
        /// <summary>Only scale down if too large; never enlarge.</summary>
        ShrinkToFit,
        /// <summary>Scale so the page width fits the printable width.</summary>
        FitToWidth,
        /// <summary>Scale so the page height fits the printable height.</summary>
        FitToHeight,
        /// <summary>Scale by an explicit percentage (see CustomScalePercent).</summary>
        Custom
    }

    /// <summary>Horizontal placement of content within the printable area.</summary>
    public enum HorizontalAlignment { Left, Center, Right }

    /// <summary>Vertical placement of content within the printable area.</summary>
    public enum VerticalAlignment { Top, Center, Bottom }

    /// <summary>Lifecycle status of a print job.</summary>
    public enum PrintJobStatus { Created, Queued, Spooling, Printed, Failed, Cancelled }
}
