using System;

namespace Chuvadi.Docs.Word;

/// <summary>
/// Table-level formatting (w:tblPr): style, width, alignment, indent, layout, borders, default
/// cell margins, cell spacing, shading, banding, right-to-left, and the accessibility caption
/// and description. Every member is optional; null inherits from the table style.
/// </summary>
public sealed class TableFormat
{
    /// <summary>Table style id (w:tblStyle), e.g. a style added to <see cref="Document.Styles"/> with <see cref="StyleType.Table"/>.</summary>
    public string? StyleId { get; set; }

    /// <summary>Which conditional formats of the table style apply (w:tblLook). Null = inherit from the table style; when nothing sets it, Word's default look (<see cref="TableLook.Default"/>, 0x04A0) applies.</summary>
    public TableLook? Look { get; set; }

    /// <summary>Preferred table width (w:tblW). Null = from <see cref="DocTable.ColumnWidthsPt"/> or auto.</summary>
    public TableWidth? Width { get; set; }

    /// <summary>Horizontal alignment of the table between the margins (w:jc).</summary>
    public TableAlignment? Alignment { get; set; }

    /// <summary>Indent of the table's leading edge from the margin, in points (w:tblInd).</summary>
    public double? IndentPt { get; set; }

    /// <summary>Fixed column widths or autofit to contents (w:tblLayout). Null = fixed when column widths are given, else autofit.</summary>
    public TableLayout? Layout { get; set; }

    /// <summary>Table borders including inside lines (w:tblBorders). Null = per <see cref="DocTable.Borders"/>.</summary>
    public TableBorders? Borders { get; set; }

    /// <summary>Default margins inside every cell (w:tblCellMar).</summary>
    public CellMargins? CellMargins { get; set; }

    /// <summary>Space between cells, in points (w:tblCellSpacing). Null = none.</summary>
    public double? CellSpacingPt { get; set; }

    /// <summary>Background shading of the whole table (w:shd).</summary>
    public Shading? Shading { get; set; }

    /// <summary>Lay columns out right-to-left (w:bidiVisual).</summary>
    public bool? RightToLeft { get; set; }

    /// <summary>Rows per horizontal band for banded table styles (w:tblStyleRowBandSize).</summary>
    public int? RowBandSize { get; set; }

    /// <summary>Columns per vertical band for banded table styles (w:tblStyleColBandSize).</summary>
    public int? ColumnBandSize { get; set; }

    /// <summary>Accessibility title of the table (w:tblCaption), read by screen readers.</summary>
    public string? Caption { get; set; }

    /// <summary>Accessibility description of the table (w:tblDescription).</summary>
    public string? Description { get; set; }

    /// <summary>Floating-table positioning (w:tblpPr/w:tblOverlap) carried verbatim from a loaded file.</summary>
    internal System.Collections.Generic.List<string> RawPositioningXml { get; } = new();

    /// <summary>True when nothing is set.</summary>
    internal bool IsDefault =>
        StyleId is null && Look is null && Width is null && Alignment is null && IndentPt is null && Layout is null
        && Borders is null && CellMargins is null && CellSpacingPt is null && Shading is null && RightToLeft is null
        && RowBandSize is null && ColumnBandSize is null && Caption is null && Description is null
        && RawPositioningXml.Count == 0;
}

/// <summary>A table or cell width (ST_TblWidth): auto, points, or a percentage of the available width.</summary>
public readonly record struct TableWidth(TableWidthType Type, double Value)
{
    /// <summary>Let the layout decide.</summary>
    public static TableWidth Auto => new(TableWidthType.Auto, 0);

    /// <summary>A fixed width in points.</summary>
    public static TableWidth Points(double points) => new(TableWidthType.Points, points);

    /// <summary>A percentage of the available width (100 = full width).</summary>
    public static TableWidth Percent(double percent) => new(TableWidthType.Percent, percent);

    /// <summary>Explicitly zero / no width (type="nil").</summary>
    public static TableWidth Nil => new(TableWidthType.Nil, 0);
}

/// <summary>Unit of a <see cref="TableWidth"/>.</summary>
public enum TableWidthType { Auto, Points, Percent, Nil }

/// <summary>Table alignment between the margins.</summary>
public enum TableAlignment { Left, Center, Right }

/// <summary>Column layout algorithm (ST_TblLayoutType).</summary>
public enum TableLayout
{
    /// <summary>Use the column widths as given.</summary>
    Fixed,
    /// <summary>Resize columns to fit the contents.</summary>
    Autofit,
}

/// <summary>Which conditional formats of a table style are applied (w:tblLook).</summary>
[Flags]
public enum TableLook
{
    None = 0,
    FirstRow = 0x0020,
    LastRow = 0x0040,
    FirstColumn = 0x0080,
    LastColumn = 0x0100,
    /// <summary>Do NOT apply horizontal (row) banding.</summary>
    NoHorizontalBand = 0x0200,
    /// <summary>Do NOT apply vertical (column) banding.</summary>
    NoVerticalBand = 0x0400,
    /// <summary>Word's default for new tables: header row, first column, banded rows.</summary>
    Default = FirstRow | FirstColumn | NoVerticalBand,
}

/// <summary>Borders of a table: the outer edges plus the inside horizontal and vertical lines (w:tblBorders).</summary>
public sealed class TableBorders
{
    public BorderSpec? Top { get; set; }
    public BorderSpec? Left { get; set; }
    public BorderSpec? Bottom { get; set; }
    public BorderSpec? Right { get; set; }
    /// <summary>Lines between rows.</summary>
    public BorderSpec? InsideHorizontal { get; set; }
    /// <summary>Lines between columns.</summary>
    public BorderSpec? InsideVertical { get; set; }

    /// <summary>The same line on every edge and inside line — a full grid.</summary>
    public static TableBorders Grid(BorderSpec line)
        => new() { Top = line, Left = line, Bottom = line, Right = line, InsideHorizontal = line, InsideVertical = line };

    /// <summary>Only the horizontal lines (top, bottom, between rows) — a common report look.</summary>
    public static TableBorders Horizontal(BorderSpec line)
        => new() { Top = line, Bottom = line, InsideHorizontal = line, Left = BorderSpec.None, Right = BorderSpec.None, InsideVertical = BorderSpec.None };

    /// <summary>Explicitly no borders anywhere.</summary>
    public static TableBorders NoBorders() => Grid(BorderSpec.None);

    internal bool IsEmpty => Top is null && Left is null && Bottom is null && Right is null && InsideHorizontal is null && InsideVertical is null;
}

/// <summary>Borders of one cell (w:tcBorders), overriding the table borders for that cell.</summary>
public sealed class CellBorders
{
    public BorderSpec? Top { get; set; }
    public BorderSpec? Left { get; set; }
    public BorderSpec? Bottom { get; set; }
    public BorderSpec? Right { get; set; }
    /// <summary>Inside horizontal line (applies when the cell spans rows of a merged region).</summary>
    public BorderSpec? InsideHorizontal { get; set; }
    /// <summary>Inside vertical line.</summary>
    public BorderSpec? InsideVertical { get; set; }
    /// <summary>Diagonal from top-left to bottom-right (w:tl2br).</summary>
    public BorderSpec? DiagonalDown { get; set; }
    /// <summary>Diagonal from top-right to bottom-left (w:tr2bl).</summary>
    public BorderSpec? DiagonalUp { get; set; }

    /// <summary>Same border on the four outer sides of the cell.</summary>
    public static CellBorders Box(BorderSpec line) => new() { Top = line, Left = line, Bottom = line, Right = line };

    internal bool IsEmpty => Top is null && Left is null && Bottom is null && Right is null
        && InsideHorizontal is null && InsideVertical is null && DiagonalDown is null && DiagonalUp is null;
}

/// <summary>Margins between a cell's border and its content, in points (w:tblCellMar / w:tcMar).</summary>
public sealed record CellMargins
{
    public double? TopPt { get; init; }
    public double? LeftPt { get; init; }
    public double? BottomPt { get; init; }
    public double? RightPt { get; init; }

    /// <summary>The same margin on all four sides.</summary>
    public static CellMargins All(double points) => new() { TopPt = points, LeftPt = points, BottomPt = points, RightPt = points };

    internal bool IsEmpty => TopPt is null && LeftPt is null && BottomPt is null && RightPt is null;
}

/// <summary>How a row's height is interpreted (ST_HeightRule).</summary>
public enum RowHeightRule
{
    /// <summary>Height from content; <see cref="TableRow.HeightPt"/> ignored.</summary>
    Auto,
    /// <summary>At least <see cref="TableRow.HeightPt"/>; grows with content.</summary>
    AtLeast,
    /// <summary>Exactly <see cref="TableRow.HeightPt"/>; content is clipped.</summary>
    Exact,
}

/// <summary>Vertical merge state of a cell (w:vMerge).</summary>
public enum VerticalMerge
{
    /// <summary>First cell of a vertically merged region.</summary>
    Restart,
    /// <summary>Continues the merged region of the cell above (its own content is ignored by Word).</summary>
    Continue,
}

/// <summary>Legacy horizontal merge state of a cell (w:hMerge).</summary>
public enum HorizontalMerge
{
    /// <summary>First cell of a horizontally merged region.</summary>
    Restart,
    /// <summary>Continues the merged region of the cell to the left.</summary>
    Continue,
}

/// <summary>Vertical alignment of content inside a cell (w:vAlign).</summary>
public enum CellVerticalAlignment { Top, Center, Bottom }

/// <summary>Text flow direction inside a cell (ST_TextDirection).</summary>
public enum CellTextDirection
{
    /// <summary>Left to right, top to bottom (normal).</summary>
    LeftToRightTopToBottom,
    /// <summary>Top to bottom, right to left (text rotated 90° clockwise).</summary>
    TopToBottomRightToLeft,
    /// <summary>Bottom to top, left to right (text rotated 90° counter-clockwise).</summary>
    BottomToTopLeftToRight,
    /// <summary>Left to right, top to bottom, East Asian characters rotated.</summary>
    LeftToRightTopToBottomRotated,
    /// <summary>Top to bottom, left to right, East Asian characters rotated.</summary>
    TopToBottomLeftToRightRotated,
    /// <summary>Top to bottom, right to left, East Asian characters rotated.</summary>
    TopToBottomRightToLeftRotated,
}
