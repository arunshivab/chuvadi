using System;
using System.Collections.Generic;

namespace Chuvadi.Docs.Word;

/// <summary>
/// A table block. Cells contain paragraphs (so any text formatting works inside cells).
///
/// <code>
/// var t = doc.AddTable(columns: 3);
/// t.HeaderRow("Item", "Qty", "Price").Shade("DDE7F5");
/// t.AddRow("Widget", "4", "₹120.00");
/// t.AddRow("Gadget", "2", "₹540.00");
/// </code>
///
/// Borders default to a single thin grid on all edges and inside lines; set
/// <see cref="Borders"/> to false for a borderless layout table.
/// </summary>
public sealed class DocTable
{
    private readonly List<TableRow> _rows = new();

    /// <summary>Number of grid columns.</summary>
    public int Columns { get; }

    /// <summary>Per-column preferred widths in points. Null = let Word auto-fit.</summary>
    public double[]? ColumnWidthsPt { get; set; }

    /// <summary>Draw the single-line grid borders (default true).</summary>
    public bool Borders { get; set; } = true;

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
}

/// <summary>One table row.</summary>
public sealed class TableRow
{
    private readonly TableCell[] _cells;

    /// <summary>Header rows repeat at the top of every page the table spans.</summary>
    public bool IsHeader { get; }

    public IReadOnlyList<TableCell> Cells => _cells;

    internal TableRow(int columns, bool isHeader)
    {
        IsHeader = isHeader;
        _cells = new TableCell[columns];
        for (int i = 0; i < columns; i++) _cells[i] = new TableCell();
    }

    /// <summary>The cell at 0-based <paramref name="columnIndex"/>.</summary>
    public TableCell Cell(int columnIndex) => CellAt(columnIndex);

    internal TableCell CellAt(int i)
    {
        if (i < 0 || i >= _cells.Length) throw new ArgumentOutOfRangeException(nameof(i));
        return _cells[i];
    }

    /// <summary>Applies a background shade (6-digit hex, no '#') to every cell in the row.</summary>
    public TableRow Shade(string hexNoHash)
    {
        foreach (var c in _cells) c.ShadeHex = hexNoHash;
        return this;
    }
}

/// <summary>One table cell. Contains paragraphs; supports horizontal span and shading.
/// Cells absorbed by a preceding cell's <see cref="ColumnSpan"/> are skipped on output.</summary>
public sealed class TableCell
{
    public List<Paragraph> Paragraphs { get; } = new();

    /// <summary>How many grid columns this cell spans (default 1).</summary>
    public int ColumnSpan { get; set; } = 1;

    /// <summary>Background fill as 6-digit hex without '#'. Null = none.</summary>
    public string? ShadeHex { get; set; }

    /// <summary>Replaces the cell content with a single plain-text paragraph.</summary>
    public TableCell SetText(string text, TextFormat? format = null)
    {
        Paragraphs.Clear();
        Paragraphs.Add(new Paragraph(text, format));
        return this;
    }

    /// <summary>All cell text (paragraphs joined with newlines).</summary>
    public string GetText()
        => string.Join("\n", Paragraphs.ConvertAll(p => p.GetText()));
}
