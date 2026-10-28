using System;

namespace Chuvadi.Sheets.Excel;

/// <summary>
/// Sheet-wide row and column defaults, from the worksheet's &lt;sheetFormatPr&gt; element.
/// Values are reported exactly as stored in the file; for a sheet built in memory they hold
/// Excel's defaults. Read-only in this release: the writer does not emit these values.
/// </summary>
public sealed class SheetFormat
{
    internal SheetFormat() { }

    /// <summary>
    /// Default row height in points (&lt;sheetFormatPr defaultRowHeight&gt;). Excel's default is
    /// 15 for an 11pt Calibri workbook. Rows without an explicit height render at this height.
    /// </summary>
    public double DefaultRowHeight { get; internal set; } = 15;

    /// <summary>
    /// Default column width in Excel character units (&lt;sheetFormatPr defaultColWidth&gt;), or
    /// null when the file does not state it (Excel then derives it from
    /// <see cref="BaseColumnWidth"/> and the default font's digit width).
    /// </summary>
    public double? DefaultColumnWidth { get; internal set; }

    /// <summary>Base column width in characters, excluding padding (&lt;sheetFormatPr baseColWidth&gt;). Default 8.</summary>
    public int BaseColumnWidth { get; internal set; } = 8;

    /// <summary>True if rows default to a custom (non-autofit) height.</summary>
    public bool CustomHeight { get; internal set; }

    /// <summary>True if rows are hidden by default (zero height) unless a row says otherwise.</summary>
    public bool ZeroHeight { get; internal set; }

    /// <summary>True if rows have a thick top border by default.</summary>
    public bool ThickTop { get; internal set; }

    /// <summary>True if rows have a thick bottom border by default.</summary>
    public bool ThickBottom { get; internal set; }

    /// <summary>Highest row outline (grouping) level used on the sheet. 0 = no row grouping.</summary>
    public int OutlineLevelRow { get; internal set; }

    /// <summary>Highest column outline (grouping) level used on the sheet. 0 = no column grouping.</summary>
    public int OutlineLevelColumn { get; internal set; }
}

/// <summary>
/// One &lt;col&gt; definition as stored in the file: a span of columns
/// (<see cref="Min"/>..<see cref="Max"/>, 1-based, inclusive) sharing width, visibility,
/// grouping and default style. Excel commonly writes wide spans such as min=5 max=16384, so
/// spans are kept as-is rather than expanded per column.
/// </summary>
public sealed class ColumnRange
{
    internal ColumnRange(int min, int max) { Min = min; Max = max; }

    /// <summary>First column of the span (1-based).</summary>
    public int Min { get; }

    /// <summary>Last column of the span (1-based, inclusive).</summary>
    public int Max { get; }

    /// <summary>Column width in Excel character units, or 0 when the file gives none.</summary>
    public double Width { get; internal set; }

    /// <summary>True if the width was set by the user rather than auto-sized (customWidth).</summary>
    public bool CustomWidth { get; internal set; }

    /// <summary>True if the width was last set by best-fit (auto-fit) (bestFit).</summary>
    public bool BestFit { get; internal set; }

    /// <summary>True if the columns are hidden.</summary>
    public bool Hidden { get; internal set; }

    /// <summary>True if phonetic (furigana) information is shown for these columns.</summary>
    public bool Phonetic { get; internal set; }

    /// <summary>Outline (grouping) level, 0..7. 0 = not grouped.</summary>
    public int OutlineLevel { get; internal set; }

    /// <summary>True if the outline group these columns belong to is collapsed.</summary>
    public bool Collapsed { get; internal set; }

    /// <summary>
    /// Default cell-format (xf) index for empty cells in these columns, or -1 when none is
    /// set. Index into the workbook's cellXfs list.
    /// </summary>
    public int StyleIndex { get; internal set; } = -1;
}

/// <summary>
/// Per-row settings accessor returned by <c>sheet.Rows[n]</c>. Values reflect the file's
/// &lt;row&gt; attributes; a row with no stored attributes reports defaults. Read-only in this
/// release: the writer does not emit row heights, visibility, grouping or row styles.
/// </summary>
public sealed class Row
{
    private readonly RowData? _data;
    private readonly CellStyleTable _styles;

    internal Row(int number, RowData? data, CellStyleTable styles) { Number = number; _data = data; _styles = styles; }

    /// <summary>The 1-based row number.</summary>
    public int Number { get; }

    /// <summary>True if the file stored any attributes for this row.</summary>
    public bool IsDefined => _data is not null;

    /// <summary>
    /// Row height in points as stored (ht), or 0 when the file gives none, in which case the
    /// row renders at <see cref="SheetFormat.DefaultRowHeight"/>. Excel also stores auto-fit
    /// heights here; <see cref="CustomHeight"/> tells the two apart.
    /// </summary>
    public double Height => _data?.Height ?? 0;

    /// <summary>True if the height was set manually rather than auto-fit (customHeight).</summary>
    public bool CustomHeight => _data?.CustomHeight ?? false;

    /// <summary>True if the row is hidden.</summary>
    public bool Hidden => _data?.Hidden ?? false;

    /// <summary>Outline (grouping) level, 0..7. 0 = not grouped.</summary>
    public int OutlineLevel => _data?.OutlineLevel ?? 0;

    /// <summary>True if the outline group this row belongs to is collapsed.</summary>
    public bool Collapsed => _data?.Collapsed ?? false;

    /// <summary>
    /// Row-level cell-format (xf) index, or -1 when none. Excel applies it to the row's empty
    /// cells only when <see cref="CustomFormat"/> is true.
    /// </summary>
    public int StyleIndex => _data?.StyleIndex ?? -1;

    /// <summary>True if <see cref="StyleIndex"/> applies to the row (customFormat).</summary>
    public bool CustomFormat => _data?.CustomFormat ?? false;

    /// <summary>
    /// The resolved row style for the row's empty cells, or null when the row has none or
    /// <see cref="CustomFormat"/> is false (Excel ignores s= on a row without customFormat).
    /// </summary>
    public CellStyle? Style => CustomFormat && StyleIndex >= 0 ? _styles.ResolveCellFormat(StyleIndex) : null;

    /// <summary>True if the row has a thick top border (thickTop).</summary>
    public bool ThickTop => _data?.ThickTop ?? false;

    /// <summary>True if the row has a thick bottom border (thickBot).</summary>
    public bool ThickBottom => _data?.ThickBottom ?? false;

    /// <summary>True if phonetic (furigana) information is shown for the row (ph).</summary>
    public bool Phonetic => _data?.Phonetic ?? false;
}

/// <summary>Indexer for <c>sheet.Rows[N]</c> (1-based).</summary>
public readonly struct RowIndexer
{
    private readonly Sheet _sheet;
    internal RowIndexer(Sheet sheet) { _sheet = sheet; }

    /// <summary>Returns the settings for the given 1-based row number.</summary>
    public Row this[int row1Based]
    {
        get
        {
            if (row1Based < 1) throw new ArgumentOutOfRangeException(nameof(row1Based));
            return _sheet.GetRow(row1Based);
        }
    }
}

/// <summary>Stored &lt;row&gt; attributes. Only rows with at least one attribute get an instance.</summary>
internal sealed class RowData
{
    public double Height;
    public bool CustomHeight;
    public bool Hidden;
    public int OutlineLevel;
    public bool Collapsed;
    public int StyleIndex = -1;
    public bool CustomFormat;
    public bool ThickTop;
    public bool ThickBottom;
    public bool Phonetic;
}
