using System.Collections.Generic;

namespace Chuvadi.Sheets.Excel;

/// <summary>
/// Options for column auto-fit (<see cref="Sheet.AutoFitColumns"/>,
/// <see cref="SheetWriter.AutoFitColumns"/>, <c>cfg.AutoFitColumns()</c>).
///
/// Auto-fit sizes each column to its widest cell as displayed: values are rendered with their
/// number format (<see cref="NumberFormatter"/>) and measured with the cell's font, size and
/// boldness. Text widths come from embedded font metrics, so widths are estimates (typically
/// within a few percent for Latin text; rougher for complex scripts such as Tamil or
/// Malayalam). Excel never auto-sizes columns when it opens a file, so the computed widths
/// are written into the file.
///
/// Rules: a width set explicitly (<c>Columns[n].Width</c>, <c>SetColumnWidth</c>, a fixed
/// width in the export config) always wins. Wrapped cells, merged cells, rotated text and
/// formulas without a value are not measured. Row heights are left to Excel.
/// </summary>
public sealed class AutoFitOptions
{
    /// <summary>Largest width any column is given, in Excel character units. Default 60.</summary>
    public double MaxWidth { get; init; } = 60;

    /// <summary>
    /// Smallest width any auto-fitted column is given. Default 8.43, Excel's standard width
    /// for the default Calibri 11 font, so short columns keep their normal width.
    /// </summary>
    public double MinWidth { get; init; } = DefaultColumnWidth;

    /// <summary>
    /// Streaming writer only: measure at most this many rows (from the first row written),
    /// trading exactness for speed on very large exports. Null (default) measures every row.
    /// </summary>
    public int? MaxRowsToMeasure { get; init; }

    /// <summary>Extra width added to every auto-fitted column, in character units. Default 0.</summary>
    public double ExtraWidth { get; init; }

    /// <summary>
    /// Adds room for the filter drop-down button on header cells of an autofilter or table
    /// range, so headers are not hidden behind the button. Default true.
    /// </summary>
    public bool AllowForFilterButton { get; init; } = true;

    /// <summary>Only auto-fit these 1-based columns. Null (default) = every column with data.</summary>
    public IReadOnlyCollection<int>? Columns { get; init; }

    /// <summary>Culture and date system used to render values for measuring. Null = invariant / 1900.</summary>
    public NumberFormatOptions? Format { get; init; }

    /// <summary>Excel's standard column width for the default font (Calibri 11).</summary>
    public const double DefaultColumnWidth = 8.43;

    /// <summary>Shared default instance.</summary>
    public static AutoFitOptions Default { get; } = new();
}
