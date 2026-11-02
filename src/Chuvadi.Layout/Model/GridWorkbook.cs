using System;
using System.Collections.Generic;
using System.Globalization;
using Chuvadi.Layout.Drawing;

namespace Chuvadi.Layout.Model
{
    /// <summary>
    /// The grid IR: what a workbook looks like for printing. Cells carry their displayed text (formatted by the
    /// importer with the cell's number format; Layout never recalculates or formats values) and their effective
    /// style. Row and column indices are zero-based.
    /// </summary>
    public sealed class GridWorkbook
    {
        public IList<GridSheet> Sheets { get; } = new List<GridSheet>();

        public string? Title { get; set; }
    }

    /// <summary>A zero-based, inclusive cell range.</summary>
    public readonly record struct CellRange(int FirstRow, int FirstColumn, int LastRow, int LastColumn)
    {
        public bool Contains(int row, int column) => row >= FirstRow && row <= LastRow && column >= FirstColumn && column <= LastColumn;

        public int RowCount => LastRow - FirstRow + 1;

        public int ColumnCount => LastColumn - FirstColumn + 1;

        /// <summary>Parses "A1", "A1:C10", "$A$1:$C$10" (absolute markers ignored).</summary>
        /// <exception cref="FormatException">Not an A1 reference.</exception>
        public static CellRange Parse(string reference)
        {
            ArgumentNullException.ThrowIfNull(reference);
            string[] parts = reference.Replace("$", string.Empty, StringComparison.Ordinal).Split(':');
            if (parts.Length is < 1 or > 2)
            {
                throw new FormatException($"'{reference}' is not an A1 range.");
            }
            (int r1, int c1) = CellAddress.Parse(parts[0]);
            (int r2, int c2) = parts.Length == 2 ? CellAddress.Parse(parts[1]) : (r1, c1);
            return new CellRange(Math.Min(r1, r2), Math.Min(c1, c2), Math.Max(r1, r2), Math.Max(c1, c2));
        }

        public override string ToString() => FirstRow == LastRow && FirstColumn == LastColumn
            ? CellAddress.Format(FirstRow, FirstColumn)
            : CellAddress.Format(FirstRow, FirstColumn) + ":" + CellAddress.Format(LastRow, LastColumn);
    }

    /// <summary>A1 addresses.</summary>
    public static class CellAddress
    {
        /// <summary>Column letters for a zero-based index (0 → "A", 26 → "AA").</summary>
        public static string ColumnName(int column)
        {
            if (column < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(column));
            }
            Span<char> buf = stackalloc char[8];
            int i = buf.Length;
            int n = column + 1;
            while (n > 0)
            {
                int rem = (n - 1) % 26;
                buf[--i] = (char)('A' + rem);
                n = (n - 1) / 26;
            }
            return new string(buf[i..]);
        }

        public static string Format(int row, int column) => ColumnName(column) + (row + 1).ToString(CultureInfo.InvariantCulture);

        /// <summary>Parses "B12" to (11, 1).</summary>
        public static (int Row, int Column) Parse(string a1)
        {
            ArgumentNullException.ThrowIfNull(a1);
            int i = 0;
            int col = 0;
            while (i < a1.Length && char.IsAsciiLetter(a1[i]))
            {
                col = (col * 26) + (char.ToUpperInvariant(a1[i]) - 'A' + 1);
                i++;
                if (col > 16384)
                {
                    throw new FormatException($"'{a1}' column is out of range.");
                }
            }
            if (i == 0 || i == a1.Length || !int.TryParse(a1.AsSpan(i), NumberStyles.None, CultureInfo.InvariantCulture, out int row) || row < 1 || row > 1048576)
            {
                throw new FormatException($"'{a1}' is not an A1 cell reference.");
            }
            return (row - 1, col - 1);
        }
    }

    /// <summary>Horizontal alignment of cell content (OOXML ST_HorizontalAlignment).</summary>
    public enum CellHorizontalAlignment
    {
        /// <summary>Numbers and dates right, text left, booleans and errors centred.</summary>
        General,
        Left,
        Center,
        Right,

        /// <summary>Repeats the content to fill the cell.</summary>
        Fill,
        Justify,

        /// <summary>Centred across the selection of empty cells to the right.</summary>
        CenterContinuous,
        Distributed,
    }

    /// <summary>Vertical alignment of cell content.</summary>
    public enum CellVerticalAlignmentKind
    {
        Top,
        Center,
        Bottom,
        Justify,
        Distributed,
    }

    /// <summary>What a cell's value is: it decides General alignment and "####" for numbers that do not fit.</summary>
    public enum CellValueKind
    {
        Empty,
        Text,
        Number,
        Boolean,
        Error,
    }

    /// <summary>Effective cell style.</summary>
    public sealed record GridCellStyle
    {
        public CharacterFormat Font { get; init; } = CharacterFormat.Default;

        public LayoutColor? Fill { get; init; }

        public BorderLine BorderTop { get; init; } = BorderLine.None;
        public BorderLine BorderBottom { get; init; } = BorderLine.None;
        public BorderLine BorderLeft { get; init; } = BorderLine.None;
        public BorderLine BorderRight { get; init; } = BorderLine.None;

        /// <summary>Diagonal from the bottom-left to the top-right corner (border/@diagonalUp).</summary>
        public BorderLine BorderDiagonalUp { get; init; } = BorderLine.None;

        /// <summary>Diagonal from the top-left to the bottom-right corner (border/@diagonalDown).</summary>
        public BorderLine BorderDiagonalDown { get; init; } = BorderLine.None;

        public CellHorizontalAlignment HorizontalAlignment { get; init; }

        public CellVerticalAlignmentKind VerticalAlignment { get; init; } = CellVerticalAlignmentKind.Bottom;

        public bool WrapText { get; init; }

        public bool ShrinkToFit { get; init; }

        /// <summary>Indent level (each level is three max-digit widths).</summary>
        public int Indent { get; init; }

        /// <summary>Text rotation in degrees (0..180, or 255 for vertical stacked). Non-zero rotation is drawn horizontally with a warning.</summary>
        public int TextRotation { get; init; }

        public static GridCellStyle Default { get; } = new GridCellStyle();
    }

    /// <summary>A cell: displayed text (as Excel shows it), value kind and style. Rich text uses <see cref="Runs"/>.</summary>
    public sealed class GridCell
    {
        public GridCell(string displayText, CellValueKind kind = CellValueKind.Text, GridCellStyle? style = null)
        {
            DisplayText = displayText ?? throw new ArgumentNullException(nameof(displayText));
            Kind = kind;
            Style = style ?? GridCellStyle.Default;
        }

        public string DisplayText { get; }

        public CellValueKind Kind { get; }

        public GridCellStyle Style { get; }

        /// <summary>Rich-text runs; when set, they replace <see cref="DisplayText"/> for drawing.</summary>
        public IReadOnlyList<(string Text, CharacterFormat Format)>? Runs { get; init; }

        /// <summary>Hyperlink target (an external URI).</summary>
        public string? HyperlinkUri { get; init; }

        /// <summary>A link to a place in the workbook ("'Sheet 2'!A1" or a defined name); carried as the link's anchor.</summary>
        public string? HyperlinkLocation { get; init; }

        /// <summary>
        /// The number format's repeat-fill character ('*x'): <see cref="DisplayText"/> up to <see cref="FillIndex"/> stays at
        /// the cell's left edge, the rest at its right edge, and this character fills the space between.
        /// </summary>
        public char? FillCharacter { get; init; }

        /// <summary>Where in <see cref="DisplayText"/> the fill goes (see <see cref="FillCharacter"/>).</summary>
        public int? FillIndex { get; init; }

        /// <summary>A conditional-format data bar drawn behind the text.</summary>
        public GridDataBar? DataBar { get; init; }

        /// <summary>A conditional-format icon drawn at the cell's left edge.</summary>
        public GridIcon? Icon { get; init; }
    }

    /// <summary>
    /// A conditional-format icon: Excel's icon set name (for example "3TrafficLights1", "5Arrows", "4Rating") and the
    /// 0-based icon index in Excel's order (0 = the icon for the lowest values) out of <see cref="Count"/>.
    /// Drawn as simplified vector icons (warning IconSetApproximated).
    /// </summary>
    public sealed record GridIcon(string IconSet, int Index, int Count);

    /// <summary>
    /// A data bar (conditional formatting): a bar from the cell's left edge (or, for negative values, from the
    /// right edge leftwards) whose length is <see cref="Length"/> of the cell width, as Excel computes it from
    /// the rule's minimum and maximum lengths.
    /// </summary>
    public sealed record GridDataBar(double Length, LayoutColor Color, bool Negative = false, bool ShowValue = true)
    {
        /// <summary>Outline colour (Excel 2010 bars), or null for none.</summary>
        public LayoutColor? BorderColor { get; init; }

        /// <summary>Excel fades gradient bars to white; drawn solid in this version.</summary>
        public bool Gradient { get; init; }
    }

    /// <summary>What a <see cref="GridDrawing"/> is.</summary>
    public enum GridDrawingKind
    {
        /// <summary>A picture (<see cref="GridDrawing.Image"/>).</summary>
        Picture,

        /// <summary>A shape or text box: fill, outline and plain text.</summary>
        Shape,

        /// <summary>A chart. Charts are not rendered in this version; a labelled placeholder is drawn.</summary>
        Chart,

        /// <summary>A group; its <see cref="GridDrawing.Children"/> are placed in the group's rectangle.</summary>
        Group,
    }

    /// <summary>A cell corner plus an offset in points (xdr:from / xdr:to). Row and column are zero-based.</summary>
    public readonly record struct GridAnchorPoint(int Row, int Column, double OffsetX = 0, double OffsetY = 0);

    /// <summary>
    /// A picture, shape or chart on a sheet. Placed by <see cref="From"/> and <see cref="To"/> (two-cell anchor),
    /// by <see cref="From"/> and a size (one-cell anchor), or by <see cref="X"/>/<see cref="Y"/> and a size in points
    /// from the sheet's top-left corner (absolute anchor). Drawings print over the cells and are clipped to each page.
    /// </summary>
    public sealed class GridDrawing
    {
        public GridDrawing(GridDrawingKind kind)
        {
            Kind = kind;
        }

        public GridDrawingKind Kind { get; }

        public GridAnchorPoint? From { get; init; }

        public GridAnchorPoint? To { get; init; }

        public double? X { get; init; }

        public double? Y { get; init; }

        public double? Width { get; init; }

        public double? Height { get; init; }

        /// <summary>For a group's child: its rectangle as fractions (0–1) of the group's rectangle.</summary>
        public LayoutRect? RelativeBounds { get; init; }

        public string? Name { get; init; }

        /// <summary>Alternative text.</summary>
        public string? Description { get; init; }

        /// <summary>Rotation in degrees, clockwise. Not applied in this version (a warning says so).</summary>
        public double Rotation { get; init; }

        /// <summary>Mirrored left to right (a:xfrm/@flipH). Applied to lines; pictures are drawn unflipped.</summary>
        public bool FlipHorizontal { get; init; }

        /// <summary>Mirrored top to bottom (a:xfrm/@flipV). Applied to lines; pictures are drawn unflipped.</summary>
        public bool FlipVertical { get; init; }

        public LayoutImage? Image { get; init; }

        /// <summary>Picture crop as fractions of the image cut from each edge (a:srcRect).</summary>
        public (double Left, double Top, double Right, double Bottom)? Crop { get; init; }

        /// <summary>Preset geometry of a shape (prstGeom: "rect", "roundRect", "ellipse", "line", ...).</summary>
        public string? Geometry { get; init; }

        public LayoutColor? Fill { get; init; }

        public LayoutColor? Line { get; init; }

        /// <summary>Outline width in points.</summary>
        public double LineWidth { get; init; } = 0.75;

        /// <summary>Plain text of a shape or text box, or a chart's title.</summary>
        public string? Text { get; init; }

        public IList<GridDrawing> Children { get; } = new List<GridDrawing>();
    }

    /// <summary>Column properties. Width is in Excel character units as stored in the file (the "width" attribute).</summary>
    public readonly record struct GridColumn(double Width, bool Hidden = false);

    /// <summary>Row properties. Height in points (the "ht" attribute); null = automatic.</summary>
    public readonly record struct GridRow(double? Height, bool Hidden = false);

    /// <summary>Page order when a print range spans pages in both directions.</summary>
    public enum GridPageOrder
    {
        DownThenOver,
        OverThenDown,
    }

    /// <summary>Excel page setup (worksheet pageSetup, pageMargins, printOptions, sheetPr/pageSetUpPr). Lengths in points.</summary>
    public sealed record GridPageSetup
    {
        /// <summary>Paper size before orientation (A4 portrait by default).</summary>
        public double PaperWidth { get; init; } = 595.3;

        public double PaperHeight { get; init; } = 841.9;

        public bool Landscape { get; init; }

        /// <summary>Excel's default margins: left/right 0.7 in, top/bottom 0.75 in, header/footer 0.3 in.</summary>
        public double MarginLeft { get; init; } = 50.4;
        public double MarginRight { get; init; } = 50.4;
        public double MarginTop { get; init; } = 54;
        public double MarginBottom { get; init; } = 54;
        public double HeaderMargin { get; init; } = 21.6;
        public double FooterMargin { get; init; } = 21.6;

        /// <summary>Scale in percent (10..400) when <see cref="FitToPage"/> is false.</summary>
        public int Scale { get; init; } = 100;

        /// <summary>Fit-to-page scaling (sheetPr/pageSetUpPr/@fitToPage).</summary>
        public bool FitToPage { get; init; }

        /// <summary>Pages wide (0 = automatic, no width constraint).</summary>
        public int FitToWidth { get; init; } = 1;

        /// <summary>Pages tall (0 = automatic, no height constraint).</summary>
        public int FitToHeight { get; init; } = 1;

        public bool CenterHorizontally { get; init; }
        public bool CenterVertically { get; init; }
        public bool PrintGridlines { get; init; }
        public bool PrintHeadings { get; init; }
        public GridPageOrder PageOrder { get; init; } = GridPageOrder.DownThenOver;

        /// <summary>First page number (pageSetup/@firstPageNumber with useFirstPageNumber); null = continue.</summary>
        public int? FirstPageNumber { get; init; }

        /// <summary>
        /// Header and footer pictures (the &amp;G code), keyed by Excel's VML shape id: "LH", "CH", "RH", "LF", "CF", "RF", with
        /// "EVEN" or "FIRST" appended for those pages. Null or a missing key: the &amp;G is not drawn (with a warning).
        /// </summary>
        public IReadOnlyDictionary<string, GridHeaderFooterPicture>? Pictures { get; init; }

        /// <summary>Header/footer definitions with Excel codes (&amp;L &amp;C &amp;R &amp;P &amp;N &amp;D &amp;T &amp;F &amp;A …).</summary>
        public string? OddHeader { get; init; }
        public string? OddFooter { get; init; }
        public string? EvenHeader { get; init; }
        public string? EvenFooter { get; init; }
        public string? FirstHeader { get; init; }
        public string? FirstFooter { get; init; }
        public bool DifferentOddEven { get; init; }
        public bool DifferentFirst { get; init; }

        /// <summary>Headers and footers align with the page margins (headerFooter/@alignWithMargins, default true).</summary>
        public bool HeaderFooterAlignWithMargins { get; init; } = true;

        /// <summary>Headers and footers scale with the sheet (headerFooter/@scaleWithDoc, default true).</summary>
        public bool HeaderFooterScaleWithDocument { get; init; } = true;

        public double PageWidth => Landscape ? Math.Max(PaperWidth, PaperHeight) : Math.Min(PaperWidth, PaperHeight);

        public double PageHeight => Landscape ? Math.Min(PaperWidth, PaperHeight) : Math.Max(PaperWidth, PaperHeight);
    }

    /// <summary>A header or footer picture: the image and its size in points (null: from its pixels at 96 dpi).</summary>
    public sealed record GridHeaderFooterPicture(LayoutImage Image, double? Width = null, double? Height = null);

    /// <summary>A worksheet for printing.</summary>
    public sealed class GridSheet
    {
        private readonly Dictionary<long, GridCell> _cells = new Dictionary<long, GridCell>();

        public GridSheet(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        public string Name { get; }

        /// <summary>Hidden sheets are not printed.</summary>
        public bool Hidden { get; set; }

        /// <summary>Right-to-left sheet (sheetView/@rightToLeft): column A is at the right and the columns run leftwards.</summary>
        public bool RightToLeft { get; set; }

        /// <summary>
        /// Rows are hidden unless <see cref="Rows"/> says otherwise (sheetFormatPr/@zeroHeight). A row listed in
        /// <see cref="Rows"/> with Hidden = false is shown.
        /// </summary>
        public bool RowsHiddenByDefault { get; set; }

        /// <summary>Font of the Normal style: defines Excel's max digit width for column widths and the default row height.</summary>
        public CharacterFormat NormalFont { get; set; } = new CharacterFormat { FontFamily = "Calibri", FontSize = 11 };

        /// <summary>Default column width in character units (sheetFormatPr/@defaultColWidth); null = derived from <see cref="BaseColumnWidth"/>.</summary>
        public double? DefaultColumnWidth { get; set; }

        /// <summary>sheetFormatPr/@baseColWidth: the default column width in characters before padding (Excel: 8).</summary>
        public int BaseColumnWidth { get; set; } = 8;

        /// <summary>Pictures, shapes and charts, in drawing order.</summary>
        public IList<GridDrawing> Drawings { get; } = new List<GridDrawing>();

        /// <summary>Default row height in points (sheetFormatPr/@defaultRowHeight). Excel writes 15 for Calibri 11.</summary>
        public double DefaultRowHeight { get; set; } = 15;

        public IDictionary<int, GridColumn> Columns { get; } = new Dictionary<int, GridColumn>();

        public IDictionary<int, GridRow> Rows { get; } = new Dictionary<int, GridRow>();

        public IList<CellRange> MergedRanges { get; } = new List<CellRange>();

        public GridPageSetup PageSetup { get; set; } = new GridPageSetup();

        /// <summary>Print areas (_xlnm.Print_Area); each prints separately. Empty = the used range.</summary>
        public IList<CellRange> PrintAreas { get; } = new List<CellRange>();

        /// <summary>Rows repeated at the top of each page (_xlnm.Print_Titles), inclusive.</summary>
        public (int First, int Last)? PrintTitleRows { get; set; }

        /// <summary>Columns repeated at the left of each page (_xlnm.Print_Titles), inclusive.</summary>
        public (int First, int Last)? PrintTitleColumns { get; set; }

        /// <summary>Manual page breaks: a new page starts at each of these row indices.</summary>
        public ISet<int> RowBreaks { get; } = new SortedSet<int>();

        /// <summary>Manual page breaks: a new page starts at each of these column indices.</summary>
        public ISet<int> ColumnBreaks { get; } = new SortedSet<int>();

        public int CellCount => _cells.Count;

        /// <summary>Sets a cell (null removes it).</summary>
        public void SetCell(int row, int column, GridCell? cell)
        {
            if (row < 0 || row >= 1048576 || column < 0 || column >= 16384)
            {
                throw new ArgumentOutOfRangeException(nameof(row), "Cell is outside the Excel grid.");
            }
            long key = Key(row, column);
            if (cell is null)
            {
                _cells.Remove(key);
            }
            else
            {
                _cells[key] = cell;
            }
        }

        /// <summary>Convenience: sets displayed text with a value kind and style.</summary>
        public GridCell Set(int row, int column, string displayText, CellValueKind kind = CellValueKind.Text, GridCellStyle? style = null)
        {
            GridCell c = new GridCell(displayText, kind, style);
            SetCell(row, column, c);
            return c;
        }

        public GridCell? GetCell(int row, int column) => _cells.TryGetValue(Key(row, column), out GridCell? c) ? c : null;

        /// <summary>Every populated cell.</summary>
        public IEnumerable<(int Row, int Column, GridCell Cell)> Cells()
        {
            foreach (KeyValuePair<long, GridCell> kv in _cells)
            {
                yield return ((int)(kv.Key >> 14), (int)(kv.Key & 0x3FFF), kv.Value);
            }
        }

        /// <summary>The bounding range of populated cells and merges, or null for an empty sheet.</summary>
        public CellRange? UsedRange()
        {
            int r1 = int.MaxValue, c1 = int.MaxValue, r2 = -1, c2 = -1;
            foreach (long key in _cells.Keys)
            {
                int r = (int)(key >> 14);
                int c = (int)(key & 0x3FFF);
                r1 = Math.Min(r1, r);
                c1 = Math.Min(c1, c);
                r2 = Math.Max(r2, r);
                c2 = Math.Max(c2, c);
            }
            foreach (CellRange m in MergedRanges)
            {
                r1 = Math.Min(r1, m.FirstRow);
                c1 = Math.Min(c1, m.FirstColumn);
                r2 = Math.Max(r2, m.LastRow);
                c2 = Math.Max(c2, m.LastColumn);
            }
            return r2 < 0 ? null : new CellRange(r1, c1, r2, c2);
        }

        private static long Key(int row, int column) => ((long)row << 14) | (uint)column;
    }
}
