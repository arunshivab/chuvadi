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

    /// <summary>
    /// Settings left to the printer's own default instead of the value in <see cref="PrintSettings"/>
    /// (<see cref="PrintSettings.PrinterDefaults"/>). Combine with <c>|</c>.
    /// </summary>
    [System.Flags]
    public enum PrinterDefault
    {
        /// <summary>Every setting comes from <see cref="PrintSettings"/> (the default).</summary>
        None = 0,

        /// <summary>The printer's default paper size (<see cref="PrintSettings.Paper"/> is not sent).</summary>
        Paper = 1,

        /// <summary>The printer's default one- or two-sided printing (<see cref="PrintSettings.Duplex"/> is not sent).</summary>
        Duplex = 2,

        /// <summary>The printer's default colour mode (<see cref="PrintSettings.Color"/> is not sent).</summary>
        Color = 4,

        /// <summary>
        /// The printer's default number of copies (<see cref="PrintSettings.Copies"/> and
        /// <see cref="PrintSettings.Collate"/> are not sent). One set is produced; where the copies would be made by
        /// Chuvadi.Print rather than the printer, this means one copy.
        /// </summary>
        Copies = 8
    }
}
