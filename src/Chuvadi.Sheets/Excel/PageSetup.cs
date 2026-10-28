using System;
using System.Collections.Generic;

namespace Chuvadi.Sheets.Excel;

/// <summary>Page orientation (&lt;pageSetup orientation&gt;).</summary>
public enum PageOrientation
{
    /// <summary>Not stated in the file; Excel uses portrait.</summary>
    Default = 0,
    Portrait,
    Landscape,
}

/// <summary>Order in which pages of a large sheet are numbered and printed.</summary>
public enum PageOrder
{
    /// <summary>Down the rows first, then across (Excel's default).</summary>
    DownThenOver = 0,
    OverThenDown,
}

/// <summary>How cell comments are printed.</summary>
public enum PrintCellComments { None = 0, AsDisplayed, AtEnd }

/// <summary>How error values (#N/A, #DIV/0!, ...) are printed.</summary>
public enum PrintErrors { Displayed = 0, Blank, Dash, NA }

/// <summary>The sheet's view mode in Excel (&lt;sheetView view&gt;).</summary>
public enum SheetViewMode { Normal = 0, PageBreakPreview, PageLayout }

/// <summary>
/// A page break stored in &lt;rowBreaks&gt; / &lt;colBreaks&gt;. For a row break, the new page
/// starts at row <c>Id + 1</c> (the break lies between row Id and row Id + 1, 1-based);
/// likewise for columns. <see cref="Min"/>..<see cref="Max"/> is the span the break line covers.
/// </summary>
/// <param name="Id">Last row (or column) before the break, 1-based.</param>
/// <param name="Min">First column (row) the break line spans; 0 when unspecified.</param>
/// <param name="Max">Last column (row) the break line spans; 0 when unspecified.</param>
/// <param name="Manual">True for a break the user inserted; false for one Excel computed.</param>
public sealed record PageBreak(int Id, int Min, int Max, bool Manual);

/// <summary>A paper size code (ST_PaperSize) with its name and portrait dimensions.</summary>
/// <param name="Code">The &lt;pageSetup paperSize&gt; value.</param>
/// <param name="Name">Readable name, e.g. "A4" or "Letter".</param>
/// <param name="WidthMm">Portrait width in millimetres.</param>
/// <param name="HeightMm">Portrait height in millimetres.</param>
public sealed record PaperSizeInfo(int Code, string Name, double WidthMm, double HeightMm)
{
    private const double In = 25.4;

    private static readonly Dictionary<int, PaperSizeInfo> Known = Build();

    /// <summary>Looks up a paper size code; null for codes not defined by ECMA-376 (printer-specific sizes).</summary>
    public static PaperSizeInfo? FromCode(int code) => Known.TryGetValue(code, out var p) ? p : null;

    /// <summary>All paper sizes defined by ECMA-376 Part 1 §18.18.66 (codes 1–68).</summary>
    public static IReadOnlyCollection<PaperSizeInfo> All => Known.Values;

    private static Dictionary<int, PaperSizeInfo> Build()
    {
        var d = new Dictionary<int, PaperSizeInfo>();
        void I(int c, string n, double w, double h) => d[c] = new PaperSizeInfo(c, n, Math.Round(w * In, 1), Math.Round(h * In, 1));
        void M(int c, string n, double w, double h) => d[c] = new PaperSizeInfo(c, n, w, h);
        I(1, "Letter", 8.5, 11); I(2, "Letter Small", 8.5, 11); I(3, "Tabloid", 11, 17); I(4, "Ledger", 17, 11);
        I(5, "Legal", 8.5, 14); I(6, "Statement", 5.5, 8.5); I(7, "Executive", 7.25, 10.5);
        M(8, "A3", 297, 420); M(9, "A4", 210, 297); M(10, "A4 Small", 210, 297); M(11, "A5", 148, 210);
        M(12, "B4 (JIS)", 257, 364); M(13, "B5 (JIS)", 182, 257); I(14, "Folio", 8.5, 13); M(15, "Quarto", 215, 275);
        I(16, "10x14", 10, 14); I(17, "11x17", 11, 17); I(18, "Note", 8.5, 11);
        I(19, "Envelope #9", 3.875, 8.875); I(20, "Envelope #10", 4.125, 9.5); I(21, "Envelope #11", 4.5, 10.375);
        I(22, "Envelope #12", 4.75, 11); I(23, "Envelope #14", 5, 11.5);
        I(24, "C", 17, 22); I(25, "D", 22, 34); I(26, "E", 34, 44);
        M(27, "Envelope DL", 110, 220); M(28, "Envelope C5", 162, 229); M(29, "Envelope C3", 324, 458);
        M(30, "Envelope C4", 229, 324); M(31, "Envelope C6", 114, 162); M(32, "Envelope C65", 114, 229);
        M(33, "Envelope B4", 250, 353); M(34, "Envelope B5", 176, 250); M(35, "Envelope B6", 176, 125);
        M(36, "Envelope Italy", 110, 230); I(37, "Envelope Monarch", 3.875, 7.5); I(38, "Envelope 6 3/4", 3.625, 6.5);
        I(39, "US Std Fanfold", 14.875, 11); I(40, "German Std Fanfold", 8.5, 12); I(41, "German Legal Fanfold", 8.5, 13);
        M(42, "ISO B4", 250, 353); M(43, "Japanese Double Postcard", 200, 148);
        I(44, "Standard 9x11", 9, 11); I(45, "Standard 10x11", 10, 11); I(46, "Standard 15x11", 15, 11);
        M(47, "Envelope Invite", 220, 220);
        I(50, "Letter Extra", 9.275, 12); I(51, "Legal Extra", 9.275, 15); I(52, "Tabloid Extra", 11.69, 18);
        M(53, "A4 Extra", 236, 322); I(54, "Letter Transverse", 8.275, 11); M(55, "A4 Transverse", 210, 297);
        I(56, "Letter Extra Transverse", 9.275, 12); M(57, "SuperA/A4", 227, 356); M(58, "SuperB/A3", 305, 487);
        I(59, "Letter Plus", 8.5, 12.69); M(60, "A4 Plus", 210, 330); M(61, "A5 Transverse", 148, 210);
        M(62, "JIS B5 Transverse", 182, 257); M(63, "A3 Extra", 322, 445); M(64, "A5 Extra", 174, 235);
        M(65, "ISO B5 Extra", 201, 276); M(66, "A2", 420, 594); M(67, "A3 Transverse", 297, 420);
        M(68, "A3 Extra Transverse", 322, 445);
        return d;
    }
}

/// <summary>Page margins in inches (&lt;pageMargins&gt;). Defaults are Excel's "Normal" margins.</summary>
public sealed class PageMargins
{
    internal PageMargins() { }
    public double Left { get; internal set; } = 0.7;
    public double Right { get; internal set; } = 0.7;
    public double Top { get; internal set; } = 0.75;
    public double Bottom { get; internal set; } = 0.75;
    /// <summary>Distance from the page top to the header.</summary>
    public double Header { get; internal set; } = 0.3;
    /// <summary>Distance from the page bottom to the footer.</summary>
    public double Footer { get; internal set; } = 0.3;
    /// <summary>True if the file stated the margins.</summary>
    public bool IsDefined { get; internal set; }
}

/// <summary>
/// Print headers and footers (&lt;headerFooter&gt;), as stored, using Excel's codes
/// (&amp;L/&amp;C/&amp;R sections, &amp;P page, &amp;N pages, &amp;D date, &amp;T time, &amp;F file,
/// &amp;A sheet, &amp;"font,style", &amp;nn size, &amp;K colour, &amp;G picture). Null = none.
/// </summary>
public sealed class HeaderFooterText
{
    internal HeaderFooterText() { }
    public string? OddHeader { get; internal set; }
    public string? OddFooter { get; internal set; }
    /// <summary>Used on even pages when <see cref="DifferentOddEven"/> is true.</summary>
    public string? EvenHeader { get; internal set; }
    public string? EvenFooter { get; internal set; }
    /// <summary>Used on the first page when <see cref="DifferentFirst"/> is true.</summary>
    public string? FirstHeader { get; internal set; }
    public string? FirstFooter { get; internal set; }
    public bool DifferentOddEven { get; internal set; }
    public bool DifferentFirst { get; internal set; }
    /// <summary>Scale header/footer text with the page scaling (default true).</summary>
    public bool ScaleWithDocument { get; internal set; } = true;
    /// <summary>Align header/footer with the page margins (default true).</summary>
    public bool AlignWithMargins { get; internal set; } = true;
}

/// <summary>
/// The sheet's print settings, read from a loaded file. For a sheet built in memory every
/// value is Excel's default. Read-only in this release: the writer emits only the page
/// header/footer (<see cref="Sheet.SetHeaderFooter"/>).
/// </summary>
public sealed class PageSetup
{
    internal PageSetup() { }

    /// <summary>True if the file had a &lt;pageSetup&gt; element.</summary>
    public bool IsDefined { get; internal set; }

    /// <summary>Paper size code as stored, or null (Excel then uses the printer default; 1 = Letter per the spec).</summary>
    public int? PaperSizeCode { get; internal set; }

    /// <summary>Name and dimensions for <see cref="PaperSizeCode"/>, or null when unknown or unset.</summary>
    public PaperSizeInfo? Paper => PaperSizeCode is int c ? PaperSizeInfo.FromCode(c) : null;

    public PageOrientation Orientation { get; internal set; }

    /// <summary>Print scale in percent (10–400). Default 100. Ignored when <see cref="FitToPage"/> is true.</summary>
    public int Scale { get; internal set; } = 100;

    /// <summary>True if the sheet is set to fit to <see cref="FitToWidth"/> × <see cref="FitToHeight"/> pages (&lt;sheetPr&gt;&lt;pageSetUpPr fitToPage&gt;).</summary>
    public bool FitToPage { get; internal set; }

    /// <summary>Pages wide to fit to; 0 = automatic (as many as needed). Default 1.</summary>
    public int FitToWidth { get; internal set; } = 1;

    /// <summary>Pages tall to fit to; 0 = automatic (as many as needed). Default 1.</summary>
    public int FitToHeight { get; internal set; } = 1;

    /// <summary>First page number when <see cref="UseFirstPageNumber"/> is true. Default 1.</summary>
    public int FirstPageNumber { get; internal set; } = 1;
    public bool UseFirstPageNumber { get; internal set; }
    public PageOrder PageOrder { get; internal set; }
    public bool BlackAndWhite { get; internal set; }
    public bool Draft { get; internal set; }
    public PrintCellComments CellComments { get; internal set; }
    public PrintErrors Errors { get; internal set; }
    public int? HorizontalDpi { get; internal set; }
    public int? VerticalDpi { get; internal set; }
    /// <summary>Number of copies. Default 1.</summary>
    public int Copies { get; internal set; } = 1;

    public PageMargins Margins { get; } = new();
    public HeaderFooterText HeaderFooter { get; } = new();

    /// <summary>Print cell gridlines (&lt;printOptions gridLines&gt;).</summary>
    public bool PrintGridlines { get; internal set; }
    /// <summary>Print row and column headings (&lt;printOptions headings&gt;).</summary>
    public bool PrintHeadings { get; internal set; }
    public bool CenterHorizontally { get; internal set; }
    public bool CenterVertically { get; internal set; }

    /// <summary>Row page breaks (manual and stored automatic ones), in file order.</summary>
    public IReadOnlyList<PageBreak> RowBreaks { get; internal set; } = Array.Empty<PageBreak>();
    /// <summary>Column page breaks, in file order.</summary>
    public IReadOnlyList<PageBreak> ColumnBreaks { get; internal set; } = Array.Empty<PageBreak>();

    /// <summary>The _xlnm.Print_Area defined name's formula as stored (e.g. "Sheet1!$A$1:$D$20"), or null.</summary>
    public string? PrintAreaFormula { get; internal set; }

    /// <summary>The print areas as plain A1 ranges ("A1:D20"), in order; empty when the whole used range prints.</summary>
    public IReadOnlyList<string> PrintAreas { get; internal set; } = Array.Empty<string>();

    /// <summary>The _xlnm.Print_Titles formula as stored, or null.</summary>
    public string? PrintTitlesFormula { get; internal set; }

    /// <summary>Rows repeated at the top of every page (1-based, inclusive), or null.</summary>
    public (int First, int Last)? PrintTitleRows { get; internal set; }

    /// <summary>Columns repeated at the left of every page (1-based, inclusive), or null.</summary>
    public (int First, int Last)? PrintTitleColumns { get; internal set; }
}
