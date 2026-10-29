using System;
using System.Collections.Generic;

namespace Chuvadi.Docs.Word;

/// <summary>Cell-level formatting carried by table styles and their conditional formats (w:tcPr subset).</summary>
public sealed class TableCellFormat
{
    public Shading? Shading { get; set; }
    public CellBorders? Borders { get; set; }
    public CellMargins? Margins { get; set; }
    public CellVerticalAlignment? VerticalAlignment { get; set; }
    public CellTextDirection? TextDirection { get; set; }
    public bool? NoWrap { get; set; }
    public bool? FitText { get; set; }
    public bool? HideMark { get; set; }

    /// <summary>Copies every SET value of <paramref name="other"/> onto this format (borders merge per side, margins per side).</summary>
    public TableCellFormat ApplyFrom(TableCellFormat other)
    {
        if (other is null) throw new ArgumentNullException(nameof(other));
        Shading = other.Shading ?? Shading;
        if (other.Borders is { } ob)
        {
            var b = Borders is null ? new CellBorders() : Clone(Borders);
            b.Top = ob.Top ?? b.Top; b.Left = ob.Left ?? b.Left; b.Bottom = ob.Bottom ?? b.Bottom; b.Right = ob.Right ?? b.Right;
            b.InsideHorizontal = ob.InsideHorizontal ?? b.InsideHorizontal; b.InsideVertical = ob.InsideVertical ?? b.InsideVertical;
            b.DiagonalDown = ob.DiagonalDown ?? b.DiagonalDown; b.DiagonalUp = ob.DiagonalUp ?? b.DiagonalUp;
            Borders = b;
        }
        if (other.Margins is { } om)
            Margins = new CellMargins
            {
                TopPt = om.TopPt ?? Margins?.TopPt, LeftPt = om.LeftPt ?? Margins?.LeftPt,
                BottomPt = om.BottomPt ?? Margins?.BottomPt, RightPt = om.RightPt ?? Margins?.RightPt,
            };
        VerticalAlignment = other.VerticalAlignment ?? VerticalAlignment;
        TextDirection = other.TextDirection ?? TextDirection;
        NoWrap = other.NoWrap ?? NoWrap;
        FitText = other.FitText ?? FitText;
        HideMark = other.HideMark ?? HideMark;
        return this;
    }

    /// <summary>The direct formatting of a cell, as a <see cref="TableCellFormat"/>.</summary>
    public static TableCellFormat FromCell(TableCell cell) => new()
    {
        Shading = cell.Shading ?? (cell.ShadeHex is { } h ? Word.Shading.Fill(h) : null),
        Borders = cell.Borders,
        Margins = cell.Margins,
        VerticalAlignment = cell.VerticalAlignment,
        TextDirection = cell.TextDirection,
        NoWrap = cell.NoWrap,
        FitText = cell.FitText,
        HideMark = cell.HideMark,
    };

    internal static CellBorders Clone(CellBorders b) => new()
    {
        Top = b.Top, Left = b.Left, Bottom = b.Bottom, Right = b.Right, InsideHorizontal = b.InsideHorizontal,
        InsideVertical = b.InsideVertical, DiagonalDown = b.DiagonalDown, DiagonalUp = b.DiagonalUp,
    };

    internal bool IsDefault => Shading is null && (Borders is null || Borders.IsEmpty) && (Margins is null || Margins.IsEmpty)
        && VerticalAlignment is null && TextDirection is null && NoWrap is null && FitText is null && HideMark is null;
}

/// <summary>Row-level formatting carried by table styles and their conditional formats (w:trPr subset).</summary>
public sealed class TableRowFormat
{
    public bool? CantSplit { get; set; }
    /// <summary>Repeat as a header row (w:tblHeader).</summary>
    public bool? IsHeader { get; set; }
    public TableAlignment? Alignment { get; set; }
    public double? HeightPt { get; set; }
    public RowHeightRule? HeightRule { get; set; }
    public bool? Hidden { get; set; }

    internal bool IsDefault => CantSplit is null && IsHeader is null && Alignment is null && HeightPt is null && HeightRule is null && Hidden is null;
}

/// <summary>
/// The parts of a table a table style can format differently (ST_TblStyleOverrideType), in the
/// order Word applies them — later regions override earlier ones.
/// </summary>
public enum TableRegion
{
    /// <summary>The whole table (w:wholeTable).</summary>
    WholeTable,
    /// <summary>Odd column bands (w:band1Vert).</summary>
    OddColumnBand,
    /// <summary>Even column bands (w:band2Vert).</summary>
    EvenColumnBand,
    /// <summary>Odd row bands (w:band1Horz).</summary>
    OddRowBand,
    /// <summary>Even row bands (w:band2Horz).</summary>
    EvenRowBand,
    /// <summary>Header row (w:firstRow), when <see cref="TableLook.FirstRow"/> is on.</summary>
    FirstRow,
    /// <summary>Total row (w:lastRow), when <see cref="TableLook.LastRow"/> is on.</summary>
    LastRow,
    /// <summary>First column (w:firstCol), when <see cref="TableLook.FirstColumn"/> is on.</summary>
    FirstColumn,
    /// <summary>Last column (w:lastCol), when <see cref="TableLook.LastColumn"/> is on.</summary>
    LastColumn,
    /// <summary>Top-left cell (w:nwCell).</summary>
    TopLeftCell,
    /// <summary>Top-right cell (w:neCell).</summary>
    TopRightCell,
    /// <summary>Bottom-left cell (w:swCell).</summary>
    BottomLeftCell,
    /// <summary>Bottom-right cell (w:seCell).</summary>
    BottomRightCell,
}

/// <summary>
/// The formatting a table style applies to one <see cref="TableRegion"/> (w:tblStylePr):
/// paragraph and run formatting for the text, table-level borders/margins, and row and cell
/// formatting.
/// </summary>
public sealed class TableConditionalFormat
{
    public ParagraphFormat ParagraphFormat { get; } = new();
    public TextFormat? RunFormat { get; set; }
    /// <summary>Table properties within the region (w:tblPr): borders, margins, shading, alignment…</summary>
    public TableFormat TableFormat { get; } = new();
    /// <summary>Borders applied as table borders within the region — shortcut for <see cref="TableFormat"/>.Borders.</summary>
    public TableBorders? TableBorders { get => TableFormat.Borders; set => TableFormat.Borders = value; }
    /// <summary>Cell margins within the region — shortcut for <see cref="TableFormat"/>.CellMargins.</summary>
    public CellMargins? CellMargins { get => TableFormat.CellMargins; set => TableFormat.CellMargins = value; }
    public TableRowFormat RowFormat { get; } = new();
    public TableCellFormat CellFormat { get; } = new();
}

/// <summary>Identifies one cell of a table for table-aware formatting resolution.</summary>
/// <param name="Table">The innermost table containing the cell.</param>
/// <param name="Row">0-based row index.</param>
/// <param name="Column">0-based grid column of the cell's first column.</param>
public readonly record struct TableCellRef(DocTable Table, int Row, int Column);
