using System;
using System.Collections.Generic;
using Chuvadi.Sheets.Internal;

namespace Chuvadi.Sheets.Excel;

/// <summary>
/// A chart sheet (xl/chartsheets): a tab holding one chart that prints on its own page, since
/// 1.7.0. Read-only. <see cref="Workbook.SaveTo(string)"/> does not write chart sheets.
/// </summary>
public sealed class ChartSheet
{
    internal ChartSheet(SheetInfo info, SheetLayoutInfo layout, IReadOnlyList<DrawingObject> drawings)
    {
        Name = info.Name;
        Visibility = info.Visibility;
        WorkbookIndex = info.WorkbookIndex;
        PageSetup = layout.PageSetup;
        View = layout.View;
        TabColor = layout.TabColor;
        Drawings = drawings;
        foreach (var d in drawings)
            if (d.Chart is not null) { Chart = d.Chart; break; }
    }

    public string Name { get; }

    public SheetVisibility Visibility { get; }

    /// <summary>0-based position among all sheets in tab order (worksheets and chart sheets), as <see cref="SheetInfo.WorkbookIndex"/>.</summary>
    public int WorkbookIndex { get; }

    /// <summary>Margins, orientation, paper, header/footer and the other print settings of the chart sheet.</summary>
    public PageSetup PageSetup { get; }

    /// <summary>The sheet view (zoom, zoom-to-fit is not modelled).</summary>
    public SheetView View { get; }

    /// <summary>Tab colour as RGB hex, or null.</summary>
    public string? TabColor { get; }

    /// <summary>The drawing objects on the sheet (normally one chart, absolutely anchored).</summary>
    public IReadOnlyList<DrawingObject> Drawings { get; }

    /// <summary>The (first) chart's metadata, or null when its part is missing.</summary>
    public ChartInfo? Chart { get; }

    public override string ToString() => $"ChartSheet '{Name}'";
}
