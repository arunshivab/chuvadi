namespace Chuvadi.Sheets.Excel;

/// <summary>How a cell's formula is stored (&lt;f t="..."&gt;).</summary>
public enum FormulaKind
{
    /// <summary>An ordinary formula in one cell.</summary>
    Normal = 0,

    /// <summary>
    /// A shared formula: one cell stores the text and a range (<see cref="CellFormula.Range"/>);
    /// the other cells of the range reuse it with their references shifted (Excel's fill-down).
    /// </summary>
    Shared,

    /// <summary>An array (Ctrl+Shift+Enter) formula; the top-left cell stores the text and the range it fills.</summary>
    Array,

    /// <summary>A what-if data table (TABLE()); Excel computes these, the file holds only results.</summary>
    DataTable,
}

/// <summary>A cell's formula as read from the file.</summary>
/// <param name="Text">The formula without the leading '=' (empty for a data table). For a shared
/// formula's child cell, the child's own text with references shifted.</param>
/// <param name="Kind">How the formula is stored.</param>
/// <param name="Range">For the cell that stores a shared or array formula, the range it covers
/// (e.g. "C2:C100"); for a data table, its range; otherwise null.</param>
public sealed record CellFormula(string Text, FormulaKind Kind, string? Range);
