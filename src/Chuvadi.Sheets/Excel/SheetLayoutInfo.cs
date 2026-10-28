using System;
using System.Collections.Generic;

namespace Chuvadi.Sheets.Excel;

/// <summary>
/// A sheet's layout and print settings for the streaming reader, from
/// <see cref="SheetReader.ReadLayout"/>: everything a renderer or paginator needs before it reads
/// rows. Per-row settings (height, hidden, outline, row style) come with each row through
/// <see cref="RowReader.Attributes"/>. The <see cref="Workbook"/> model exposes the same data on
/// <see cref="Sheet"/>.
/// </summary>
public sealed class SheetLayoutInfo
{
    internal SheetLayoutInfo(PageSetup page, SheetFormat format, IReadOnlyList<ColumnRange> columns,
                             IReadOnlyList<string> merges, string? usedRange, bool showGridLines,
                             bool rightToLeft, SheetViewMode viewMode, int zoomScale, CellStyleTable styles)
    {
        PageSetup = page; Format = format; ColumnRanges = columns; MergeRanges = merges; UsedRange = usedRange;
        ShowGridLines = showGridLines; RightToLeft = rightToLeft; ViewMode = viewMode; ZoomScale = zoomScale;
        _styles = styles;
    }

    private readonly CellStyleTable _styles;

    /// <summary>Print settings: paper, orientation, scaling, margins, header/footer, breaks, print area and titles.</summary>
    public PageSetup PageSetup { get; }

    /// <summary>Sheet-wide defaults (&lt;sheetFormatPr&gt;): default row height and column width, outline levels.</summary>
    public SheetFormat Format { get; }

    /// <summary>The &lt;col&gt; spans in file order, exactly as stored (widths, hidden, outline, column styles).</summary>
    public IReadOnlyList<ColumnRange> ColumnRanges { get; }

    /// <summary>Merged ranges ("A1:C2") in file order. The top-left cell holds the value.</summary>
    public IReadOnlyList<string> MergeRanges { get; }

    /// <summary>The used range the file states in &lt;dimension ref&gt; (e.g. "A1:H200"), or null. Written by the producing tool; not verified.</summary>
    public string? UsedRange { get; }

    /// <summary>Whether gridlines show on screen. Printing uses <see cref="PageSetup.PrintGridlines"/>.</summary>
    public bool ShowGridLines { get; }

    /// <summary>True if the sheet is laid out right-to-left.</summary>
    public bool RightToLeft { get; }

    /// <summary>Excel view mode stored for the sheet.</summary>
    public SheetViewMode ViewMode { get; }

    /// <summary>Zoom percentage of the sheet view. Default 100.</summary>
    public int ZoomScale { get; }

    /// <summary>The span covering a 1-based column (the last one wins if a malformed file overlaps spans), or null.</summary>
    public ColumnRange? FindColumn(int column1Based)
    {
        for (int i = ColumnRanges.Count - 1; i >= 0; i--)
        {
            var r = ColumnRanges[i];
            if (column1Based >= r.Min && column1Based <= r.Max) return r;
        }
        return null;
    }

    /// <summary>
    /// Width of a 1-based column in the file's &lt;col width&gt; units (characters of the default
    /// font's maximum digit width, padding included): the span's width, else
    /// <see cref="SheetFormat.DefaultColumnWidth"/>, else the default ECMA-376 derives from
    /// baseColWidth (base × digit width + 5 px, rounded up to a multiple of 8 px), computed for
    /// Calibri 11 (7 px digits): 9.140625 (64 px, shown by Excel as 8.43) for the default base
    /// of 8. Hidden columns report their width too; check <see cref="IsColumnHidden"/>.
    /// </summary>
    public double ColumnWidth(int column1Based)
    {
        var r = FindColumn(column1Based);
        if (r is not null && r.Width > 0) return r.Width;
        if (Format.DefaultColumnWidth is double d && d > 0) return d;
        const int mdw = 7;
        int px = (Format.BaseColumnWidth * mdw + 5 + 7) / 8 * 8;
        return Math.Truncate((double)px / mdw * 256) / 256;
    }

    /// <summary>True if the 1-based column is hidden.</summary>
    public bool IsColumnHidden(int column1Based) => FindColumn(column1Based)?.Hidden ?? false;

    /// <summary>The column's default cellXf index for cells the file does not store, or 0.</summary>
    public int ColumnStyleIndex(int column1Based)
    {
        var r = FindColumn(column1Based);
        return r is not null && r.StyleIndex >= 0 ? r.StyleIndex : 0;
    }

    /// <summary>The resolved column style, or null when the column has none.</summary>
    public CellStyle? ColumnStyle(int column1Based)
    {
        int idx = ColumnStyleIndex(column1Based);
        return idx > 0 ? _styles.ResolveCellFormat(idx) : null;
    }
}
