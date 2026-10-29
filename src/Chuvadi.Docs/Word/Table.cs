using System;
using System.Collections.Generic;
using System.Text;

namespace Chuvadi.Docs.Word;

/// <summary>
/// A table block. Cells contain paragraphs (so any text formatting works inside cells) and,
/// optionally, nested tables.
///
/// <code>
/// var t = doc.AddTable(columns: 3);
/// t.HeaderRow("Item", "Qty", "Price").Shade("DDE7F5");
/// t.AddRow("Widget", "4", "₹120.00");
/// t.AddRow("Gadget", "2", "₹540.00");
///
/// // Full control:
/// t.Format.Borders = TableBorders.Horizontal(BorderSpec.Single(0.75, "7F7F7F"));
/// t.Format.Alignment = TableAlignment.Center;
/// t.Format.CellMargins = CellMargins.All(4);
/// t.Rows[0].CantSplit = true;
/// t.Rows[1].Cell(0).VerticalMerge = VerticalMerge.Restart;
/// t.Rows[2].Cell(0).VerticalMerge = VerticalMerge.Continue;
/// t.Rows[1].Cell(2).VerticalAlignment = CellVerticalAlignment.Center;
/// </code>
///
/// Borders default to a single thin grid on all edges and inside lines; set
/// <see cref="Borders"/> to false for a borderless layout table, or set
/// <see cref="TableFormat.Borders"/> for full control.
/// </summary>
public sealed class DocTable
{
    private readonly List<TableRow> _rows = new();

    /// <summary>Number of grid columns.</summary>
    public int Columns { get; }

    /// <summary>Per-column preferred widths in points (w:tblGrid). Null = let Word auto-fit.</summary>
    public double[]? ColumnWidthsPt { get; set; }

    /// <summary>
    /// Draw the default single-line grid borders (default true). Ignored when
    /// <see cref="TableFormat.Borders"/> is set.
    /// </summary>
    public bool Borders { get; set; } = true;

    /// <summary>Table-level formatting: style, width, alignment, borders, margins… (w:tblPr).</summary>
    public TableFormat Format { get; } = new();

    public IReadOnlyList<TableRow> Rows => _rows;

    public DocTable(int columns)
    {
        if (columns < 1) throw new ArgumentOutOfRangeException(nameof(columns));
        Columns = columns;
    }

    /// <summary>Adds a row marked as a header (bold by default, repeats on page breaks).</summary>
    public TableRow HeaderRow(params string[] cellTexts)
    {
        var row = AddRowCore(cellTexts, header: true);
        return row;
    }

    /// <summary>Adds a plain data row from cell texts. Missing cells are filled empty.</summary>
    public TableRow AddRow(params string[] cellTexts) => AddRowCore(cellTexts, header: false);

    /// <summary>Adds an empty row to fill cell-by-cell via <see cref="TableRow.Cell(int)"/>.</summary>
    public TableRow AddRow() => AddRowCore(Array.Empty<string>(), header: false);

    private TableRow AddRowCore(string[] cellTexts, bool header)
    {
        var row = new TableRow(Columns, header);
        for (int i = 0; i < Columns; i++)
        {
            var text = i < cellTexts.Length ? cellTexts[i] : string.Empty;
            var p = new Paragraph(text, header ? TextFormat.BoldText : TextFormat.None);
            row.CellAt(i).Paragraphs.Add(p);
        }
        _rows.Add(row);
        return row;
    }

    /// <summary>Adds a fully built row (used by the loader). Its cell count must equal <see cref="Columns"/>.</summary>
    internal void AddLoadedRow(TableRow row)
    {
        if (row.Cells.Count != Columns) throw new ArgumentException("Row cell count must match the table's columns.", nameof(row));
        _rows.Add(row);
    }

    /// <summary>All table text: rows newline-separated, cells tab-separated (nested tables included).</summary>
    public string GetText()
    {
        var sb = new StringBuilder();
        foreach (var row in _rows)
        {
            for (int i = 0; i < row.Cells.Count; i++)
            {
                if (i > 0) sb.Append('\t');
                sb.Append(row.Cells[i].GetText());
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }
}

/// <summary>One table row.</summary>
public sealed class TableRow
{
    private readonly TableCell?[] _cells;
    private readonly LazyCells _view;

    /// <summary>Header rows repeat at the top of every page the table spans (w:tblHeader).</summary>
    public bool IsHeader { get; set; }

    /// <summary>Row height in points (w:trHeight), interpreted per <see cref="HeightRule"/>. Null = auto.</summary>
    public double? HeightPt { get; set; }

    /// <summary>How <see cref="HeightPt"/> applies. Default AtLeast (Word's default when a height is given).</summary>
    public RowHeightRule HeightRule { get; set; } = RowHeightRule.AtLeast;

    /// <summary>Keep the row on one page — don't let it break across pages (w:cantSplit).</summary>
    public bool? CantSplit { get; set; }

    /// <summary>Horizontal alignment of this row (w:jc in trPr).</summary>
    public TableAlignment? Alignment { get; set; }

    /// <summary>Hide the row (w:hidden in trPr).</summary>
    public bool? Hidden { get; set; }

    /// <summary>Grid columns skipped before the first cell (w:gridBefore) — for ragged tables.</summary>
    public int GridBefore { get; set; }

    /// <summary>
    /// Grid columns left empty after the last cell (w:gridAfter). Note: cells are indexed by GRID
    /// column, so with <see cref="GridBefore"/> = n the first written cell is <c>Cell(n)</c>.
    /// </summary>
    public int GridAfter { get; set; }

    /// <summary>Preferred width of the skipped grid columns before the first cell (w:wBefore).</summary>
    public TableWidth? WidthBefore { get; set; }

    /// <summary>Preferred width of the empty grid columns after the last cell (w:wAfter).</summary>
    public TableWidth? WidthAfter { get; set; }

    /// <summary>Cell spacing for this row only, in points (w:tblCellSpacing in trPr).</summary>
    public double? CellSpacingPt { get; set; }

    public IReadOnlyList<TableCell> Cells => _view;

    /// <summary>
    /// Trailing grid columns that a loaded file left without cells (a ragged row with no
    /// gridAfter). Untouched placeholder cells there are not written back, so the row keeps
    /// its shape; give one content or formatting and it is written.
    /// </summary>
    internal int OmittedTrailingCells { get; set; }

    /// <summary>A row over pre-built cells (used by the loader); null entries are created on first access.</summary>
    internal TableRow(TableCell?[] cells, bool isHeader)
    {
        IsHeader = isHeader;
        _cells = cells;
        _view = new LazyCells(this);
    }

    internal TableRow(int columns, bool isHeader)
    {
        IsHeader = isHeader;
        _cells = new TableCell?[columns];
        for (int i = 0; i < columns; i++) _cells[i] = new TableCell();
        _view = new LazyCells(this);
    }

    /// <summary>Read-only view that materialises placeholder cells on access (keeps sparse loaded tables cheap).</summary>
    private sealed class LazyCells : IReadOnlyList<TableCell>
    {
        private readonly TableRow _row;
        public LazyCells(TableRow row) => _row = row;
        public TableCell this[int index] => _row.CellAt(index);
        public int Count => _row._cells.Length;
        public IEnumerator<TableCell> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>The cell at 0-based <paramref name="columnIndex"/>.</summary>
    public TableCell Cell(int columnIndex) => CellAt(columnIndex);

    internal TableCell CellAt(int i)
    {
        if (i < 0 || i >= _cells.Length) throw new ArgumentOutOfRangeException(nameof(i));
        return _cells[i] ??= new TableCell();
    }

    /// <summary>Applies a background shade (6-digit hex, no '#') to every cell in the row.</summary>
    public TableRow Shade(string hexNoHash)
    {
        foreach (var c in Cells) c.ShadeHex = hexNoHash;
        return this;
    }
}

/// <summary>
/// One table cell. Contains paragraphs and optional nested tables; supports horizontal span
/// (<see cref="ColumnSpan"/>), vertical merge (<see cref="VerticalMerge"/>), width, borders,
/// shading, margins, vertical alignment and text direction. Cells absorbed by a preceding
/// cell's <see cref="ColumnSpan"/> are skipped on output.
/// </summary>
public sealed class TableCell
{
    // Each nested table is anchored to the paragraph it follows (null = before all paragraphs),
    // so later edits to Paragraphs never move it relative to its neighbour.
    private readonly List<(Paragraph? After, DocTable Table)> _nested = new();

    public List<Paragraph> Paragraphs { get; } = new();

    /// <summary>How many grid columns this cell spans (default 1).</summary>
    public int ColumnSpan { get; set; } = 1;

    /// <summary>Background fill as 6-digit hex without '#' — shortcut for a plain <see cref="Shading"/>. Null = none.</summary>
    public string? ShadeHex { get; set; }

    /// <summary>Full cell shading (pattern, colors). Takes precedence over <see cref="ShadeHex"/>.</summary>
    public Shading? Shading { get; set; }

    /// <summary>Vertical merge with the cells above/below (w:vMerge). Null = not merged.</summary>
    public VerticalMerge? VerticalMerge { get; set; }

    /// <summary>
    /// Legacy horizontal merge (w:hMerge), as written by older Word versions; new documents use
    /// <see cref="ColumnSpan"/>. Null = not merged.
    /// </summary>
    public HorizontalMerge? HorizontalMerge { get; set; }

    /// <summary>Preferred cell width (w:tcW). Null = from the table's column widths, or auto.</summary>
    public TableWidth? Width { get; set; }

    /// <summary>Cell borders, overriding the table borders for this cell (w:tcBorders).</summary>
    public CellBorders? Borders { get; set; }

    /// <summary>Cell margins, overriding the table's default cell margins (w:tcMar).</summary>
    public CellMargins? Margins { get; set; }

    /// <summary>Vertical alignment of the cell content (w:vAlign).</summary>
    public CellVerticalAlignment? VerticalAlignment { get; set; }

    /// <summary>Text flow direction (w:textDirection), e.g. rotated column headers.</summary>
    public CellTextDirection? TextDirection { get; set; }

    /// <summary>Don't wrap text in this cell (w:noWrap).</summary>
    public bool? NoWrap { get; set; }

    /// <summary>Shrink/expand text to fit the cell width exactly (w:tcFitText).</summary>
    public bool? FitText { get; set; }

    /// <summary>Ignore the cell's end-of-cell mark when computing row height (w:hideMark).</summary>
    public bool? HideMark { get; set; }

    /// <summary>Tables nested in this cell, in content order.</summary>
    public IReadOnlyList<DocTable> NestedTables
    {
        get
        {
            var list = new List<DocTable>();
            foreach (var b in Blocks) if (b is DocTable t) list.Add(t);
            return list;
        }
    }

    /// <summary>
    /// The cell content in order: <see cref="Paragraph"/>s and nested <see cref="DocTable"/>s.
    /// A nested table follows the paragraph that was last in the cell when it was added (or
    /// comes first if the cell was empty); if that paragraph is later removed, the table moves
    /// to the end.
    /// </summary>
    public IReadOnlyList<object> Blocks
    {
        get
        {
            var list = new List<object>(Paragraphs.Count + _nested.Count);
            var present = new HashSet<Paragraph>(ReferenceEqualityComparer.Instance);
            foreach (var p in Paragraphs) present.Add(p);
            foreach (var (after, t) in _nested) if (after is null) list.Add(t);
            var placed = new HashSet<Paragraph>(ReferenceEqualityComparer.Instance);
            foreach (var p in Paragraphs)
            {
                list.Add(p);
                if (!placed.Add(p)) continue; // the same paragraph object twice: anchor at its first occurrence
                foreach (var (after, t) in _nested) if (ReferenceEquals(after, p)) list.Add(t);
            }
            foreach (var (after, t) in _nested) if (after is not null && !present.Contains(after)) list.Add(t);
            return list;
        }
    }

    /// <summary>
    /// Adds a table nested inside this cell, after the cell's current last paragraph. Word requires
    /// a paragraph after a nested table at the end of a cell; one is written automatically.
    /// </summary>
    public DocTable AddTable(int columns) => AddTable(new DocTable(columns));

    /// <summary>
    /// Adds an existing table nested after the cell's current last paragraph. Throws when the
    /// table contains this cell (a table cannot be nested inside itself).
    /// </summary>
    public DocTable AddTable(DocTable table)
    {
        if (table is null) throw new ArgumentNullException(nameof(table));
        if (Contains(table, this, new HashSet<DocTable>(ReferenceEqualityComparer.Instance)))
            throw new ArgumentException("A table cannot be nested inside one of its own cells.", nameof(table));
        _nested.Add((Paragraphs.Count > 0 ? Paragraphs[^1] : null, table));
        return table;
    }

    /// <summary>Loader: appends a nested table after the paragraphs read so far.</summary>
    internal void AppendNestedTable(DocTable table)
        => _nested.Add((Paragraphs.Count > 0 ? Paragraphs[^1] : null, table));

    private static bool Contains(DocTable table, TableCell cell, HashSet<DocTable> seen)
    {
        if (!seen.Add(table)) return false;
        foreach (var row in table.Rows)
            foreach (var c in row.Cells)
            {
                if (ReferenceEquals(c, cell)) return true;
                foreach (var (_, inner) in c._nested)
                    if (Contains(inner, cell, seen)) return true;
            }
        return false;
    }

    /// <summary>True when the cell has no content and no formatting (a placeholder the loader created).</summary>
    internal bool IsPristine =>
        Paragraphs.Count == 0 && _nested.Count == 0 && ColumnSpan == 1 && ShadeHex is null && Shading is null
        && VerticalMerge is null && HorizontalMerge is null && Width is null && Borders is null && Margins is null
        && VerticalAlignment is null && TextDirection is null && NoWrap is null && FitText is null && HideMark is null;

    /// <summary>Replaces the cell content with a single plain-text paragraph (nested tables are removed).</summary>
    public TableCell SetText(string text, TextFormat? format = null)
    {
        Paragraphs.Clear();
        _nested.Clear();
        Paragraphs.Add(new Paragraph(text, format));
        return this;
    }

    /// <summary>Replaces the cell content with a single image paragraph.</summary>
    public TableCell SetImage(ImageSpec image)
    {
        if (image is null) throw new ArgumentNullException(nameof(image));
        Paragraphs.Clear();
        _nested.Clear();
        Paragraphs.Add(new Paragraph().Image(image));
        return this;
    }

    /// <summary>Adds an image paragraph to the cell, keeping any existing content.</summary>
    public TableCell AddImage(ImageSpec image)
    {
        if (image is null) throw new ArgumentNullException(nameof(image));
        Paragraphs.Add(new Paragraph().Image(image));
        return this;
    }

    /// <summary>All cell text (paragraphs joined with newlines; nested tables included in place).</summary>
    public string GetText()
    {
        var parts = new List<string>();
        foreach (var b in Blocks)
        {
            if (b is Paragraph p) parts.Add(p.GetText());
            else if (b is DocTable t) parts.Add(t.GetText().TrimEnd('\n'));
        }
        return string.Join("\n", parts);
    }
}
