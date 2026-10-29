using System;
using System.Collections.Generic;

namespace Chuvadi.Sheets.Excel;

/// <summary>How a sheet window is divided (&lt;pane state&gt;).</summary>
public enum PaneState
{
    /// <summary>Rows and/or columns are frozen; <see cref="SheetPane.XSplit"/>/<see cref="SheetPane.YSplit"/> count cells.</summary>
    Frozen = 0,
    /// <summary>The window is split into movable panes; the splits are in twips (1/20 point).</summary>
    Split,
    /// <summary>Frozen, created from a split (Excel keeps the split position when unfreezing).</summary>
    FrozenSplit,
}

/// <summary>A pane of a divided sheet window (&lt;pane activePane&gt;, &lt;selection pane&gt;).</summary>
public enum PaneKind { TopLeft = 0, TopRight, BottomLeft, BottomRight }

/// <summary>Frozen or split panes of the sheet window (&lt;pane&gt;).</summary>
public sealed class SheetPane
{
    /// <summary>Frozen, split, or frozen-from-split.</summary>
    public PaneState State { get; set; }

    /// <summary>Horizontal split: columns frozen (Frozen states) or the split position in twips (Split). 0 = none.</summary>
    public double XSplit { get; set; }

    /// <summary>Vertical split: rows frozen (Frozen states) or the split position in twips (Split). 0 = none.</summary>
    public double YSplit { get; set; }

    /// <summary>Top-left visible cell of the bottom-right pane (e.g. "B2"), or null.</summary>
    public string? TopLeftCell { get; set; }

    /// <summary>The pane that has the focus.</summary>
    public PaneKind ActivePane { get; set; }

    internal SheetPane Clone() => new() { State = State, XSplit = XSplit, YSplit = YSplit, TopLeftCell = TopLeftCell, ActivePane = ActivePane };
}

/// <summary>The selection in one pane (&lt;selection&gt;).</summary>
/// <param name="Pane">The pane the selection belongs to.</param>
/// <param name="ActiveCell">The active cell, e.g. "C5", or null.</param>
/// <param name="Ranges">The selected ranges, space-separated as stored ("C5" or "A1:B3 D4"), or null.</param>
public sealed record SheetSelection(PaneKind Pane, string? ActiveCell, string? Ranges);

/// <summary>
/// How the sheet is shown in Excel's window (&lt;sheetView&gt;, the first view): gridlines,
/// headings, zeros, zoom, view mode, frozen or split panes, the selection. Read from a loaded
/// file and written on save (<see cref="Sheet.View"/>, <see cref="SheetWriter.View"/>). Values
/// left at Excel's defaults are not written; a sheet that changes nothing writes no view.
/// </summary>
public sealed class SheetView
{
    private int _zoom = 100;
    private int? _zoomNormal, _zoomPageLayout, _zoomPageBreak;
    private readonly List<SheetSelection> _selections = new();

    /// <summary>Show cell gridlines on screen. Default true. Printing uses <see cref="PageSetup.PrintGridlines"/>.</summary>
    public bool ShowGridLines { get; set; } = true;

    /// <summary>Show row and column headings (1, 2, 3 / A, B, C). Default true.</summary>
    public bool ShowHeadings { get; set; } = true;

    /// <summary>Show zero values (false shows them as blank). Default true.</summary>
    public bool ShowZeros { get; set; } = true;

    /// <summary>Show the ruler in Page Layout view. Default true.</summary>
    public bool ShowRuler { get; set; } = true;

    /// <summary>Show formulas instead of their results.</summary>
    public bool ShowFormulas { get; set; }

    /// <summary>Show outline (grouping) symbols. Default true.</summary>
    public bool ShowOutlineSymbols { get; set; } = true;

    /// <summary>Show white space around pages in Page Layout view. Default true.</summary>
    public bool ShowWhiteSpace { get; set; } = true;

    /// <summary>Lay the sheet out right-to-left (column A on the right).</summary>
    public bool RightToLeft { get; set; }

    /// <summary>True if this sheet's tab is selected (part of the selected tab group).</summary>
    public bool TabSelected { get; set; }

    /// <summary>Normal, Page Break Preview or Page Layout.</summary>
    public SheetViewMode ViewMode { get; set; }

    /// <summary>Top-left visible cell (e.g. "A1"), or null for the default.</summary>
    public string? TopLeftCell { get; set; }

    /// <summary>Current zoom in percent (10–400). Default 100.</summary>
    public int ZoomScale { get => _zoom; set => _zoom = CheckZoom(value, nameof(ZoomScale)); }

    /// <summary>Zoom used in Normal view, or null when not stored.</summary>
    public int? ZoomScaleNormal { get => _zoomNormal; set => _zoomNormal = value is int v ? CheckZoom(v, nameof(ZoomScaleNormal)) : null; }

    /// <summary>Zoom used in Page Layout view, or null when not stored.</summary>
    public int? ZoomScalePageLayout { get => _zoomPageLayout; set => _zoomPageLayout = value is int v ? CheckZoom(v, nameof(ZoomScalePageLayout)) : null; }

    /// <summary>Zoom used in Page Break Preview, or null when not stored.</summary>
    public int? ZoomScalePageBreakPreview { get => _zoomPageBreak; set => _zoomPageBreak = value is int v ? CheckZoom(v, nameof(ZoomScalePageBreakPreview)) : null; }

    /// <summary>Frozen or split panes, or null when the window is not divided.</summary>
    public SheetPane? Pane { get; set; }

    /// <summary>The selection per pane, as stored (Excel writes one per visible pane).</summary>
    public IList<SheetSelection> Selections => _selections;

    /// <summary>
    /// Freezes the first <paramref name="rows"/> rows and <paramref name="columns"/> columns
    /// (0 = none); both 0 removes the panes.
    /// </summary>
    public SheetView Freeze(int rows, int columns)
    {
        if (rows < 0) throw new ArgumentOutOfRangeException(nameof(rows));
        if (columns < 0) throw new ArgumentOutOfRangeException(nameof(columns));
        if (rows == 0 && columns == 0) { Pane = null; return this; }
        Pane = new SheetPane
        {
            State = PaneState.Frozen,
            XSplit = columns,
            YSplit = rows,
            TopLeftCell = Internal.CellAddress.ToA1(rows + 1, columns + 1),
            ActivePane = rows > 0 && columns > 0 ? PaneKind.BottomRight : rows > 0 ? PaneKind.BottomLeft : PaneKind.TopRight,
        };
        return this;
    }

    /// <summary>
    /// Splits the window into movable panes at the given positions in twips (1/20 point; 0 = no
    /// split in that direction). <paramref name="topLeftCell"/> is the first cell of the lower-right pane.
    /// </summary>
    public SheetView Split(double xTwips, double yTwips, string? topLeftCell = null)
    {
        if (xTwips < 0) throw new ArgumentOutOfRangeException(nameof(xTwips));
        if (yTwips < 0) throw new ArgumentOutOfRangeException(nameof(yTwips));
        if (xTwips == 0 && yTwips == 0) { Pane = null; return this; }
        Pane = new SheetPane
        {
            State = PaneState.Split,
            XSplit = xTwips,
            YSplit = yTwips,
            TopLeftCell = topLeftCell,
            ActivePane = xTwips > 0 && yTwips > 0 ? PaneKind.BottomRight : yTwips > 0 ? PaneKind.BottomLeft : PaneKind.TopRight,
        };
        return this;
    }

    /// <summary>Rows frozen at the top (0 when the panes are not frozen).</summary>
    public int FrozenRows => Pane is { State: not PaneState.Split } p ? (int)p.YSplit : 0;

    /// <summary>Columns frozen at the left (0 when the panes are not frozen).</summary>
    public int FrozenColumns => Pane is { State: not PaneState.Split } p ? (int)p.XSplit : 0;

    /// <summary>Selects one cell (the active cell) in the active pane, replacing any selection.</summary>
    public SheetView Select(string activeCell, string? ranges = null)
    {
        if (string.IsNullOrEmpty(activeCell)) throw new ArgumentException("Cell required.", nameof(activeCell));
        _selections.Clear();
        _selections.Add(new SheetSelection(Pane?.ActivePane ?? PaneKind.TopLeft, activeCell, ranges ?? activeCell));
        return this;
    }

    /// <summary>Copies every setting of <paramref name="other"/>.</summary>
    public SheetView CopyFrom(SheetView other)
    {
        if (other is null) throw new ArgumentNullException(nameof(other));
        if (ReferenceEquals(other, this)) return this;
        ShowGridLines = other.ShowGridLines; ShowHeadings = other.ShowHeadings; ShowZeros = other.ShowZeros;
        ShowRuler = other.ShowRuler; ShowFormulas = other.ShowFormulas; ShowOutlineSymbols = other.ShowOutlineSymbols;
        ShowWhiteSpace = other.ShowWhiteSpace; RightToLeft = other.RightToLeft; TabSelected = other.TabSelected;
        ViewMode = other.ViewMode; TopLeftCell = other.TopLeftCell;
        _zoom = other._zoom; _zoomNormal = other._zoomNormal; _zoomPageLayout = other._zoomPageLayout; _zoomPageBreak = other._zoomPageBreak;
        Pane = other.Pane?.Clone();
        _selections.Clear();
        _selections.AddRange(other._selections);
        return this;
    }

    /// <summary>True if anything differs from Excel's defaults (only then is a view written).</summary>
    internal bool HasContent =>
        !ShowGridLines || !ShowHeadings || !ShowZeros || !ShowRuler || ShowFormulas || !ShowOutlineSymbols
        || !ShowWhiteSpace || RightToLeft || TabSelected || ViewMode != SheetViewMode.Normal || TopLeftCell is not null
        || _zoom != 100 || _zoomNormal is not null || _zoomPageLayout is not null || _zoomPageBreak is not null
        || Pane is not null || _selections.Count > 0;

    private static int CheckZoom(int v, string name)
    {
        if (v < 10 || v > 400) throw new ArgumentOutOfRangeException(name, v, "Zoom is 10 to 400 percent.");
        return v;
    }

    /// <summary>Load path: zoom values as stored, without validation.</summary>
    internal void SetLoadedZoom(int zoom, int? normal, int? pageLayout, int? pageBreak)
    {
        _zoom = zoom; _zoomNormal = normal; _zoomPageLayout = pageLayout; _zoomPageBreak = pageBreak;
    }
}

/// <summary>
/// A row's settings for the streaming writer (<see cref="SheetWriter.WriteRow(RowFormat, object?[])"/>):
/// height, visibility, outline grouping, a row style and thick borders. Defaults write nothing.
/// </summary>
public sealed class RowFormat
{
    private double? _height;
    private int _outline;

    /// <summary>Row height in points (0–409), or null for Excel's automatic height.</summary>
    public double? Height
    {
        get => _height;
        set
        {
            if (value is double h && (double.IsNaN(h) || h < 0 || h > 409))
                throw new ArgumentOutOfRangeException(nameof(Height), h, "Row height is 0 to 409 points.");
            _height = value;
        }
    }

    /// <summary>
    /// True if <see cref="Height"/> was set by hand (customHeight); default true whenever a height
    /// is given. Set false to store an auto-fitted height as such.
    /// </summary>
    public bool CustomHeight { get; set; } = true;

    /// <summary>Hide the row.</summary>
    public bool Hidden { get; set; }

    /// <summary>Outline (grouping) level, 0–7.</summary>
    public int OutlineLevel
    {
        get => _outline;
        set
        {
            if (value is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(OutlineLevel), value, "Outline levels are 0 to 7.");
            _outline = value;
        }
    }

    /// <summary>The outline group this row ends is collapsed (set on the summary row).</summary>
    public bool Collapsed { get; set; }

    /// <summary>Style for the row's empty cells (Excel's row format, customFormat), or null.</summary>
    public CellStyle? Style { get; set; }

    /// <summary>Thick top border on the row (thickTop).</summary>
    public bool ThickTop { get; set; }

    /// <summary>Thick bottom border on the row (thickBot).</summary>
    public bool ThickBottom { get; set; }

    internal bool HasContent => _height is not null || Hidden || _outline > 0 || Collapsed || Style is not null || ThickTop || ThickBottom;
}

/// <summary>
/// Settings for a span of columns on the streaming writer
/// (<see cref="SheetWriter.SetColumns"/>): width, visibility, outline grouping, a column style.
/// </summary>
public sealed class ColumnFormat
{
    private double? _width;
    private int _outline;

    /// <summary>Width in Excel's &lt;col width&gt; units (0–255), or null for the default width.</summary>
    public double? Width
    {
        get => _width;
        set
        {
            if (value is double w && (double.IsNaN(w) || w < 0 || w > 255))
                throw new ArgumentOutOfRangeException(nameof(Width), w, "Column width is 0 to 255.");
            _width = value;
        }
    }

    /// <summary>Hide the columns.</summary>
    public bool Hidden { get; set; }

    /// <summary>Outline (grouping) level, 0–7.</summary>
    public int OutlineLevel
    {
        get => _outline;
        set
        {
            if (value is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(OutlineLevel), value, "Outline levels are 0 to 7.");
            _outline = value;
        }
    }

    /// <summary>The outline group these columns end is collapsed.</summary>
    public bool Collapsed { get; set; }

    /// <summary>The width was set by best-fit (auto-fit) rather than by hand.</summary>
    public bool BestFit { get; set; }

    /// <summary>Default style for the columns' empty cells, or null.</summary>
    public CellStyle? Style { get; set; }
}
