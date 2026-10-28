using System;

namespace Chuvadi.Sheets.Excel;

/// <summary>
/// Sheet-wide row and column defaults, from the worksheet's &lt;sheetFormatPr&gt; element, plus
/// the outline summary positions (&lt;outlinePr&gt;). Values are reported exactly as stored in the
/// file; for a sheet built in memory they hold Excel's defaults. Settable since 1.6.0: a value
/// that is set, or a loaded &lt;sheetFormatPr&gt;, is written on save. The outline levels are
/// computed from the rows and columns when saving.
/// </summary>
public sealed class SheetFormat
{
    internal SheetFormat() { }

    private double _defaultRowHeight = 15;
    private double? _defaultColumnWidth;
    private int _baseColumnWidth = 8;
    private bool _customHeight, _zeroHeight, _thickTop, _thickBottom;

    /// <summary>True if the file had &lt;sheetFormatPr&gt;, or a value here was set: only then is it written.</summary>
    public bool IsDefined { get; internal set; }

    /// <summary>
    /// Default row height in points (&lt;sheetFormatPr defaultRowHeight&gt;). Excel's default is
    /// 15 for an 11pt Calibri workbook. Rows without an explicit height render at this height.
    /// </summary>
    public double DefaultRowHeight
    {
        get => _defaultRowHeight;
        set
        {
            if (double.IsNaN(value) || value < 0 || value > 409) throw new ArgumentOutOfRangeException(nameof(DefaultRowHeight), value, "0 to 409 points.");
            _defaultRowHeight = value; IsDefined = true;
        }
    }

    /// <summary>
    /// Default column width in Excel character units (&lt;sheetFormatPr defaultColWidth&gt;), or
    /// null when the file does not state it (Excel then derives it from
    /// <see cref="BaseColumnWidth"/> and the default font's digit width).
    /// </summary>
    public double? DefaultColumnWidth
    {
        get => _defaultColumnWidth;
        set
        {
            if (value is double w && (double.IsNaN(w) || w < 0 || w > 255)) throw new ArgumentOutOfRangeException(nameof(DefaultColumnWidth), w, "0 to 255.");
            _defaultColumnWidth = value; IsDefined = true;
        }
    }

    /// <summary>Base column width in characters, excluding padding (&lt;sheetFormatPr baseColWidth&gt;). Default 8.</summary>
    public int BaseColumnWidth
    {
        get => _baseColumnWidth;
        set
        {
            if (value is < 0 or > 255) throw new ArgumentOutOfRangeException(nameof(BaseColumnWidth), value, "0 to 255.");
            _baseColumnWidth = value; IsDefined = true;
        }
    }

    /// <summary>True if rows default to a custom (non-autofit) height.</summary>
    public bool CustomHeight { get => _customHeight; set { _customHeight = value; IsDefined = true; } }

    /// <summary>True if rows are hidden by default (zero height) unless a row says otherwise.</summary>
    public bool ZeroHeight { get => _zeroHeight; set { _zeroHeight = value; IsDefined = true; } }

    /// <summary>True if rows have a thick top border by default.</summary>
    public bool ThickTop { get => _thickTop; set { _thickTop = value; IsDefined = true; } }

    /// <summary>True if rows have a thick bottom border by default.</summary>
    public bool ThickBottom { get => _thickBottom; set { _thickBottom = value; IsDefined = true; } }

    /// <summary>Highest row outline (grouping) level used on the sheet, as loaded. 0 = no row grouping. Recomputed on save.</summary>
    public int OutlineLevelRow { get; internal set; }

    /// <summary>Highest column outline (grouping) level used on the sheet, as loaded. 0 = no column grouping. Recomputed on save.</summary>
    public int OutlineLevelColumn { get; internal set; }

    /// <summary>Outline summary rows sit below their detail rows (&lt;outlinePr summaryBelow&gt;). Default true.</summary>
    public bool OutlineSummaryBelow { get; set; } = true;

    /// <summary>Outline summary columns sit right of their detail columns (&lt;outlinePr summaryRight&gt;). Default true.</summary>
    public bool OutlineSummaryRight { get; set; } = true;

    /// <summary>Load path: values as stored, without validation.</summary>
    internal void Load(double defaultRowHeight, double? defaultColumnWidth, int baseColumnWidth, bool customHeight,
                       bool zeroHeight, bool thickTop, bool thickBottom, int outlineRow, int outlineColumn)
    {
        _defaultRowHeight = defaultRowHeight; _defaultColumnWidth = defaultColumnWidth; _baseColumnWidth = baseColumnWidth;
        _customHeight = customHeight; _zeroHeight = zeroHeight; _thickTop = thickTop; _thickBottom = thickBottom;
        OutlineLevelRow = outlineRow; OutlineLevelColumn = outlineColumn;
        IsDefined = true;
    }

    /// <summary>Copies every value of <paramref name="other"/>.</summary>
    public SheetFormat CopyFrom(SheetFormat other)
    {
        if (other is null) throw new ArgumentNullException(nameof(other));
        _defaultRowHeight = other._defaultRowHeight; _defaultColumnWidth = other._defaultColumnWidth;
        _baseColumnWidth = other._baseColumnWidth; _customHeight = other._customHeight; _zeroHeight = other._zeroHeight;
        _thickTop = other._thickTop; _thickBottom = other._thickBottom;
        OutlineLevelRow = other.OutlineLevelRow; OutlineLevelColumn = other.OutlineLevelColumn;
        OutlineSummaryBelow = other.OutlineSummaryBelow; OutlineSummaryRight = other.OutlineSummaryRight;
        IsDefined = other.IsDefined;
        return this;
    }
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
/// Per-row settings accessor returned by <c>sheet.Rows[n]</c> and by the streaming reader's
/// <see cref="RowReader.Attributes"/>. Values reflect the file's &lt;row&gt; attributes; a row
/// with no stored attributes reports defaults. Since 1.6.0 the settings of a Workbook row are
/// settable and written on save; a row from the streaming reader is read-only.
/// </summary>
public sealed class Row
{
    private RowData? _data;
    private readonly CellStyleTable _styles;
    private readonly Sheet? _sheet;

    internal Row(int number, RowData? data, CellStyleTable styles, Sheet? sheet = null)
    {
        Number = number; _data = data; _styles = styles; _sheet = sheet;
    }

    /// <summary>The 1-based row number.</summary>
    public int Number { get; }

    /// <summary>True if the file stored any attributes for this row, or one was set.</summary>
    public bool IsDefined => _data is not null;

    private RowData Edit()
    {
        if (_sheet is null) throw new InvalidOperationException("This row comes from the streaming reader and is read-only.");
        return _data ??= _sheet.GetOrCreateRowData(Number);
    }

    /// <summary>
    /// Row height in points as stored (ht), or 0 when the file gives none, in which case the
    /// row renders at <see cref="SheetFormat.DefaultRowHeight"/>. Excel also stores auto-fit
    /// heights here; <see cref="CustomHeight"/> tells the two apart. Setting a height (0–409)
    /// also sets <see cref="CustomHeight"/>; setting 0 returns the row to automatic height.
    /// </summary>
    public double Height
    {
        get => _data?.Height ?? 0;
        set
        {
            if (double.IsNaN(value) || value < 0 || value > 409) throw new ArgumentOutOfRangeException(nameof(Height), value, "Row height is 0 to 409 points.");
            var d = Edit(); d.Height = value; d.CustomHeight = value > 0;
        }
    }

    /// <summary>True if the height was set manually rather than auto-fit (customHeight).</summary>
    public bool CustomHeight { get => _data?.CustomHeight ?? false; set => Edit().CustomHeight = value; }

    /// <summary>True if the row is hidden.</summary>
    public bool Hidden { get => _data?.Hidden ?? false; set => Edit().Hidden = value; }

    /// <summary>Outline (grouping) level, 0..7. 0 = not grouped.</summary>
    public int OutlineLevel
    {
        get => _data?.OutlineLevel ?? 0;
        set
        {
            if (value is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(OutlineLevel), value, "Outline levels are 0 to 7.");
            Edit().OutlineLevel = value;
        }
    }

    /// <summary>True if the outline group this row belongs to is collapsed.</summary>
    public bool Collapsed { get => _data?.Collapsed ?? false; set => Edit().Collapsed = value; }

    /// <summary>
    /// Row-level cell-format (xf) index as loaded, or -1 when none (or once <see cref="Style"/>
    /// has been set in code). Excel applies it to the row's empty cells only when
    /// <see cref="CustomFormat"/> is true.
    /// </summary>
    public int StyleIndex => _data?.StyleIndex ?? -1;

    /// <summary>True if the row has a row style (customFormat).</summary>
    public bool CustomFormat => _data?.CustomFormat ?? false;

    /// <summary>
    /// The row style for the row's empty cells, or null when the row has none or
    /// <see cref="CustomFormat"/> is false (Excel ignores s= on a row without customFormat).
    /// Setting a style sets customFormat; setting null removes it.
    /// </summary>
    public CellStyle? Style
    {
        get
        {
            if (_data is null) return null;
            if (_data.StyleSet) return _data.Style;
            return CustomFormat && StyleIndex >= 0 ? _styles.ResolveCellFormat(StyleIndex) : null;
        }
        set
        {
            var d = Edit();
            d.Style = value; d.StyleSet = true; d.StyleIndex = -1; d.CustomFormat = value is not null;
        }
    }

    /// <summary>True if the row has a thick top border (thickTop).</summary>
    public bool ThickTop { get => _data?.ThickTop ?? false; set => Edit().ThickTop = value; }

    /// <summary>True if the row has a thick bottom border (thickBot).</summary>
    public bool ThickBottom { get => _data?.ThickBottom ?? false; set => Edit().ThickBottom = value; }

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
    public CellStyle? Style;   // set in code (StyleSet), replacing StyleIndex
    public bool StyleSet;

    /// <summary>True if nothing is set (the row can be written without attributes).</summary>
    public bool IsEmpty => Height == 0 && !CustomHeight && !Hidden && OutlineLevel == 0 && !Collapsed
        && !CustomFormat && !ThickTop && !ThickBottom && !Phonetic;
}
