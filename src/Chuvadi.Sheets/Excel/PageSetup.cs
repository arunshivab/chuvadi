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

/// <summary>
/// Page margins in inches (&lt;pageMargins&gt;). Defaults are Excel's "Normal" margins. Setting
/// any margin marks the margins as stated (<see cref="IsDefined"/>), so they are written on save.
/// </summary>
public sealed class PageMargins
{
    private readonly PageSetup _owner;
    private double _left = 0.7, _right = 0.7, _top = 0.75, _bottom = 0.75, _header = 0.3, _footer = 0.3;

    internal PageMargins(PageSetup owner) { _owner = owner; }

    public double Left { get => _left; set => _left = Set(value, nameof(Left)); }
    public double Right { get => _right; set => _right = Set(value, nameof(Right)); }
    public double Top { get => _top; set => _top = Set(value, nameof(Top)); }
    public double Bottom { get => _bottom; set => _bottom = Set(value, nameof(Bottom)); }
    /// <summary>Distance from the page top to the header.</summary>
    public double Header { get => _header; set => _header = Set(value, nameof(Header)); }
    /// <summary>Distance from the page bottom to the footer.</summary>
    public double Footer { get => _footer; set => _footer = Set(value, nameof(Footer)); }

    /// <summary>True if the file stated the margins, or a margin was set; only then are they written.</summary>
    public bool IsDefined { get; internal set; }

    /// <summary>Sets all four page margins (inches); header and footer distances stay.</summary>
    public PageMargins Set(double left, double right, double top, double bottom)
    {
        Left = left; Right = right; Top = top; Bottom = bottom;
        return this;
    }

    private double Set(double v, string name)
    {
        if (!_owner.Loading)
        {
            if (double.IsNaN(v) || double.IsInfinity(v) || v < 0 || v >= 49)
                throw new ArgumentOutOfRangeException(name, v, "Margins are in inches, 0 to under 49.");
            IsDefined = true;
        }
        return v;
    }

    internal void CopyFrom(PageMargins o)
    {
        _left = o._left; _right = o._right; _top = o._top; _bottom = o._bottom; _header = o._header; _footer = o._footer;
        IsDefined = o.IsDefined;
    }
}

/// <summary>
/// Print headers and footers (&lt;headerFooter&gt;), as stored, using Excel's codes
/// (&amp;L/&amp;C/&amp;R sections, &amp;P page, &amp;N pages, &amp;D date, &amp;T time, &amp;F file,
/// &amp;A sheet, &amp;"font,style", &amp;nn size, &amp;K colour, &amp;G picture). Null = none.
/// Excel limits each text to 255 characters; longer text is written as given.
/// </summary>
public sealed class HeaderFooterText
{
    internal HeaderFooterText() { }
    public string? OddHeader { get; set; }
    public string? OddFooter { get; set; }
    /// <summary>Used on even pages when <see cref="DifferentOddEven"/> is true.</summary>
    public string? EvenHeader { get; set; }
    public string? EvenFooter { get; set; }
    /// <summary>Used on the first page when <see cref="DifferentFirst"/> is true.</summary>
    public string? FirstHeader { get; set; }
    public string? FirstFooter { get; set; }
    public bool DifferentOddEven { get; set; }
    public bool DifferentFirst { get; set; }
    /// <summary>Scale header/footer text with the page scaling (default true).</summary>
    public bool ScaleWithDocument { get; set; } = true;
    /// <summary>Align header/footer with the page margins (default true).</summary>
    public bool AlignWithMargins { get; set; } = true;

    /// <summary>True if anything differs from "no header/footer": written on save only then.</summary>
    internal bool HasContent =>
        OddHeader is not null || OddFooter is not null || EvenHeader is not null || EvenFooter is not null
        || FirstHeader is not null || FirstFooter is not null || DifferentOddEven || DifferentFirst
        || !ScaleWithDocument || !AlignWithMargins;

    internal void CopyFrom(HeaderFooterText o)
    {
        OddHeader = o.OddHeader; OddFooter = o.OddFooter; EvenHeader = o.EvenHeader; EvenFooter = o.EvenFooter;
        FirstHeader = o.FirstHeader; FirstFooter = o.FirstFooter; DifferentOddEven = o.DifferentOddEven;
        DifferentFirst = o.DifferentFirst; ScaleWithDocument = o.ScaleWithDocument; AlignWithMargins = o.AlignWithMargins;
    }
}

/// <summary>
/// The sheet's print settings. Read from a loaded file (<see cref="Sheet.PageSetup"/>,
/// <see cref="SheetLayoutInfo.PageSetup"/>), and written on save by both the Workbook model and
/// the streaming writer (<see cref="SheetWriter.PageSetup"/>). For a new sheet every value is
/// Excel's default and nothing is written; a value that is set is written. Print areas and
/// print titles are saved as the sheet-scoped defined names _xlnm.Print_Area and
/// _xlnm.Print_Titles.
/// </summary>
public sealed class PageSetup
{
    /// <summary>A page setup with Excel's defaults, e.g. to prepare settings and <see cref="CopyFrom"/> them.</summary>
    public PageSetup() { Margins = new PageMargins(this); }

    /// <summary>Load path: values come from a file, so setters neither validate nor mark <see cref="IsDefined"/>.</summary>
    internal bool Loading { get; set; }

    private int? _paperSize;
    private PageOrientation _orientation;
    private int _scale = 100, _fitToWidth = 1, _fitToHeight = 1, _firstPageNumber = 1, _copies = 1;
    private bool _useFirstPageNumber, _blackAndWhite, _draft;
    private PageOrder _pageOrder;
    private PrintCellComments _cellComments;
    private PrintErrors _errors;
    private int? _hDpi, _vDpi;
    private List<PageBreak> _rowBreaks = new();
    private List<PageBreak> _colBreaks = new();
    private (int First, int Last)? _titleRows, _titleCols;

    /// <summary>
    /// True if the file had a &lt;pageSetup&gt; element, or a &lt;pageSetup&gt; value was set; only
    /// then is the element written. (Fit-to-page lives in &lt;sheetPr&gt; and does not count.)
    /// </summary>
    public bool IsDefined { get; internal set; }

    /// <summary>
    /// Paper size code (ST_PaperSize), or null for the printer default. See
    /// <see cref="PaperSizeInfo.All"/> for the codes ECMA-376 defines (1 = Letter, 9 = A4, 8 = A3,
    /// 5 = Legal, ...); larger codes are printer-specific and kept as given.
    /// </summary>
    public int? PaperSizeCode
    {
        get => _paperSize;
        set { if (value is int v) Check(v >= 1, nameof(PaperSizeCode), v, "Paper size codes start at 1."); _paperSize = value; Mark(); }
    }

    /// <summary>Name and dimensions for <see cref="PaperSizeCode"/>, or null when unknown or unset.</summary>
    public PaperSizeInfo? Paper => PaperSizeCode is int c ? PaperSizeInfo.FromCode(c) : null;

    public PageOrientation Orientation { get => _orientation; set { _orientation = value; Mark(); } }

    /// <summary>Print scale in percent (10–400). Default 100. Ignored when <see cref="FitToPage"/> is true.</summary>
    public int Scale
    {
        get => _scale;
        set { Check(value is >= 10 and <= 400, nameof(Scale), value, "Scale is 10 to 400 percent."); _scale = value; Mark(); }
    }

    /// <summary>
    /// True if the sheet is fitted to <see cref="FitToWidth"/> × <see cref="FitToHeight"/> pages
    /// (&lt;sheetPr&gt;&lt;pageSetUpPr fitToPage&gt;) instead of printed at <see cref="Scale"/>.
    /// </summary>
    public bool FitToPage { get; set; }

    /// <summary>Pages wide to fit to; 0 = automatic (as many as needed). Default 1. Used when <see cref="FitToPage"/> is true.</summary>
    public int FitToWidth
    {
        get => _fitToWidth;
        set { Check(value is >= 0 and <= 32767, nameof(FitToWidth), value, "0 (automatic) to 32767 pages."); _fitToWidth = value; Mark(); }
    }

    /// <summary>Pages tall to fit to; 0 = automatic (as many as needed). Default 1. Used when <see cref="FitToPage"/> is true.</summary>
    public int FitToHeight
    {
        get => _fitToHeight;
        set { Check(value is >= 0 and <= 32767, nameof(FitToHeight), value, "0 (automatic) to 32767 pages."); _fitToHeight = value; Mark(); }
    }

    /// <summary>Fits the sheet to the given pages wide and tall (0 = automatic) and turns <see cref="FitToPage"/> on.</summary>
    public PageSetup FitTo(int pagesWide, int pagesTall)
    {
        FitToWidth = pagesWide;
        FitToHeight = pagesTall;
        FitToPage = true;
        return this;
    }

    /// <summary>First page number when <see cref="UseFirstPageNumber"/> is true. Default 1.</summary>
    public int FirstPageNumber { get => _firstPageNumber; set { _firstPageNumber = value; Mark(); } }
    public bool UseFirstPageNumber { get => _useFirstPageNumber; set { _useFirstPageNumber = value; Mark(); } }
    public PageOrder PageOrder { get => _pageOrder; set { _pageOrder = value; Mark(); } }
    public bool BlackAndWhite { get => _blackAndWhite; set { _blackAndWhite = value; Mark(); } }
    public bool Draft { get => _draft; set { _draft = value; Mark(); } }
    public PrintCellComments CellComments { get => _cellComments; set { _cellComments = value; Mark(); } }
    public PrintErrors Errors { get => _errors; set { _errors = value; Mark(); } }

    public int? HorizontalDpi
    {
        get => _hDpi;
        set { if (value is int v) Check(v > 0, nameof(HorizontalDpi), v, "DPI must be positive."); _hDpi = value; Mark(); }
    }

    public int? VerticalDpi
    {
        get => _vDpi;
        set { if (value is int v) Check(v > 0, nameof(VerticalDpi), v, "DPI must be positive."); _vDpi = value; Mark(); }
    }

    /// <summary>Number of copies. Default 1.</summary>
    public int Copies
    {
        get => _copies;
        set { Check(value is >= 1 and <= 32767, nameof(Copies), value, "1 to 32767 copies."); _copies = value; Mark(); }
    }

    public PageMargins Margins { get; }
    public HeaderFooterText HeaderFooter { get; } = new();

    /// <summary>Print cell gridlines (&lt;printOptions gridLines&gt;).</summary>
    public bool PrintGridlines { get; set; }
    /// <summary>Print row and column headings (&lt;printOptions headings&gt;).</summary>
    public bool PrintHeadings { get; set; }
    public bool CenterHorizontally { get; set; }
    public bool CenterVertically { get; set; }

    internal bool HasPrintOptions => PrintGridlines || PrintHeadings || CenterHorizontally || CenterVertically;

    // ---- Page breaks --------------------------------------------------------------------

    /// <summary>Row page breaks (manual and stored automatic ones), in file or insertion order.</summary>
    public IReadOnlyList<PageBreak> RowBreaks => _rowBreaks;
    /// <summary>Column page breaks, in file or insertion order.</summary>
    public IReadOnlyList<PageBreak> ColumnBreaks => _colBreaks;

    /// <summary>
    /// Adds a manual page break after the given 1-based row (the next page starts at
    /// <paramref name="afterRow"/> + 1), across all columns. A break already at that row is replaced.
    /// </summary>
    public PageSetup AddRowBreak(int afterRow)
    {
        Check(afterRow is >= 1 and < 1048576, nameof(afterRow), afterRow, "1 to 1048575.");
        _rowBreaks.RemoveAll(b => b.Id == afterRow);
        _rowBreaks.Add(new PageBreak(afterRow, 0, 16383, true));
        return this;
    }

    /// <summary>Adds a manual page break after the given 1-based column, across all rows.</summary>
    public PageSetup AddColumnBreak(int afterColumn)
    {
        Check(afterColumn is >= 1 and < 16384, nameof(afterColumn), afterColumn, "1 to 16383.");
        _colBreaks.RemoveAll(b => b.Id == afterColumn);
        _colBreaks.Add(new PageBreak(afterColumn, 0, 1048575, true));
        return this;
    }

    /// <summary>Removes the row break after the given row; false if there was none.</summary>
    public bool RemoveRowBreak(int afterRow) => _rowBreaks.RemoveAll(b => b.Id == afterRow) > 0;

    /// <summary>Removes the column break after the given column; false if there was none.</summary>
    public bool RemoveColumnBreak(int afterColumn) => _colBreaks.RemoveAll(b => b.Id == afterColumn) > 0;

    public PageSetup ClearRowBreaks() { _rowBreaks.Clear(); return this; }
    public PageSetup ClearColumnBreaks() { _colBreaks.Clear(); return this; }

    internal void SetLoadedBreaks(List<PageBreak> rows, List<PageBreak> cols)
    {
        _rowBreaks = new List<PageBreak>(rows);
        _colBreaks = new List<PageBreak>(cols);
    }

    // ---- Print area and titles ------------------------------------------------------------

    /// <summary>
    /// The _xlnm.Print_Area formula as loaded (e.g. "Sheet1!$A$1:$D$20"), or null. Cleared by
    /// <see cref="SetPrintArea"/> and <see cref="ClearPrintArea"/>. Saving writes the name from
    /// <see cref="PrintAreas"/>; only when a loaded formula could not be read into areas is it
    /// written back unchanged.
    /// </summary>
    public string? PrintAreaFormula { get; internal set; }

    /// <summary>The print areas as A1 ranges ("A1:D20", "A:C", "1:5"), in order; empty when the whole used range prints.</summary>
    public IReadOnlyList<string> PrintAreas { get; internal set; } = Array.Empty<string>();

    /// <summary>
    /// Sets the print area: one or more A1 ranges on this sheet ("A1:D20"; whole columns "A:C";
    /// whole rows "1:5"; a single cell "B2"). Several ranges print on separate pages.
    /// '$' signs are optional. Replaces any previous print area.
    /// </summary>
    public PageSetup SetPrintArea(params string[] ranges)
    {
        if (ranges is null || ranges.Length == 0) throw new ArgumentException("At least one range is required.", nameof(ranges));
        var list = new List<string>(ranges.Length);
        foreach (var r in ranges)
        {
            var norm = Chuvadi.Sheets.Internal.PrintNames.NormalizeArea(r)
                ?? throw new ArgumentException($"'{r}' is not an A1 range on this sheet (e.g. \"A1:D20\", \"A:C\", \"1:5\").", nameof(ranges));
            list.Add(norm);
        }
        PrintAreas = list;
        PrintAreaFormula = null;
        return this;
    }

    /// <summary>Removes the print area: the whole used range prints.</summary>
    public PageSetup ClearPrintArea()
    {
        PrintAreas = Array.Empty<string>();
        PrintAreaFormula = null;
        return this;
    }

    /// <summary>The _xlnm.Print_Titles formula as loaded, or null. Cleared when titles are set.</summary>
    public string? PrintTitlesFormula { get; internal set; }

    /// <summary>Rows repeated at the top of every page (1-based, inclusive), or null for none.</summary>
    public (int First, int Last)? PrintTitleRows
    {
        get => _titleRows;
        set
        {
            if (value is (int f, int l))
                Check(f >= 1 && l >= f && l <= 1048576, nameof(PrintTitleRows), value, "Rows 1..1048576 with First <= Last.");
            _titleRows = value;
            if (!Loading) PrintTitlesFormula = null;
        }
    }

    /// <summary>Columns repeated at the left of every page (1-based, inclusive), or null for none.</summary>
    public (int First, int Last)? PrintTitleColumns
    {
        get => _titleCols;
        set
        {
            if (value is (int f, int l))
                Check(f >= 1 && l >= f && l <= 16384, nameof(PrintTitleColumns), value, "Columns 1..16384 with First <= Last.");
            _titleCols = value;
            if (!Loading) PrintTitlesFormula = null;
        }
    }

    // ---- Copy -----------------------------------------------------------------------------

    /// <summary>Copies every setting of <paramref name="other"/> (including margins, header/footer, breaks, print area and titles).</summary>
    public PageSetup CopyFrom(PageSetup other)
    {
        if (other is null) throw new ArgumentNullException(nameof(other));
        if (ReferenceEquals(other, this)) return this;
        IsDefined = other.IsDefined;
        _paperSize = other._paperSize; _orientation = other._orientation; _scale = other._scale;
        FitToPage = other.FitToPage; _fitToWidth = other._fitToWidth; _fitToHeight = other._fitToHeight;
        _firstPageNumber = other._firstPageNumber; _useFirstPageNumber = other._useFirstPageNumber;
        _pageOrder = other._pageOrder; _blackAndWhite = other._blackAndWhite; _draft = other._draft;
        _cellComments = other._cellComments; _errors = other._errors; _hDpi = other._hDpi; _vDpi = other._vDpi;
        _copies = other._copies;
        Margins.CopyFrom(other.Margins);
        HeaderFooter.CopyFrom(other.HeaderFooter);
        PrintGridlines = other.PrintGridlines; PrintHeadings = other.PrintHeadings;
        CenterHorizontally = other.CenterHorizontally; CenterVertically = other.CenterVertically;
        _rowBreaks = new List<PageBreak>(other._rowBreaks);
        _colBreaks = new List<PageBreak>(other._colBreaks);
        PrintAreas = new List<string>(other.PrintAreas);
        PrintAreaFormula = other.PrintAreaFormula;
        _titleRows = other._titleRows; _titleCols = other._titleCols;
        PrintTitlesFormula = other.PrintTitlesFormula;
        return this;
    }

    private void Mark() { if (!Loading) IsDefined = true; }

    private void Check(bool ok, string name, object? value, string message)
    {
        if (!ok && !Loading) throw new ArgumentOutOfRangeException(name, value, message);
    }
}
