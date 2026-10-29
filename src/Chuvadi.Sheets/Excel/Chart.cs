using System;
using System.Collections.Generic;

namespace Chuvadi.Sheets.Excel;

/// <summary>The kind of a chart plot (the plot element in &lt;c:plotArea&gt;).</summary>
public enum ChartType
{
    /// <summary>Horizontal bars (&lt;c:barChart&gt; with barDir="bar").</summary>
    Bar = 0,
    /// <summary>Vertical bars (&lt;c:barChart&gt; with barDir="col").</summary>
    Column,
    Line,
    Pie,
    Doughnut,
    /// <summary>Pie of pie or bar of pie (&lt;c:ofPieChart&gt;).</summary>
    OfPie,
    Area,
    Scatter,
    Radar,
    Bubble,
    Stock,
    Surface,
    /// <summary>An Excel 2016 chart (chartex: waterfall, funnel, treemap, sunburst, histogram, box and whisker, map); see <see cref="ChartInfo.ExtendedType"/>.</summary>
    Extended,
    /// <summary>A plot type this reader does not recognise.</summary>
    Other,
}

/// <summary>A data reference of a chart series: the formula and the values cached in the chart part.</summary>
public sealed class ChartDataReference
{
    internal ChartDataReference() { }

    /// <summary>The reference as stored, e.g. "Sheet1!$B$2:$B$13", or null for literal data.</summary>
    public string? Formula { get; internal set; }

    /// <summary>True for numeric data (&lt;c:numRef&gt;/&lt;c:numLit&gt;), false for text (&lt;c:strRef&gt;/&lt;c:strLit&gt;/&lt;c:multiLvlStrRef&gt;).</summary>
    public bool IsNumeric { get; internal set; }

    /// <summary>The number format of the cached values (numeric data), or null.</summary>
    public string? FormatCode { get; internal set; }

    /// <summary>Number of points the cache declares.</summary>
    public int PointCount { get; internal set; }

    /// <summary>
    /// The cached values by point index (length <see cref="PointCount"/>; null where a point has
    /// no cached value), exactly as stored as text. For multi-level categories, the innermost level.
    /// </summary>
    public IReadOnlyList<string?> CachedValues { get; internal set; } = Array.Empty<string?>();

    /// <summary>The cached values as numbers (null where missing or not a number).</summary>
    public IReadOnlyList<double?> CachedNumbers
    {
        get
        {
            var r = new double?[CachedValues.Count];
            for (int i = 0; i < r.Length; i++)
                r[i] = double.TryParse(CachedValues[i], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
            return r;
        }
    }
}

/// <summary>One series of a chart plot (&lt;c:ser&gt;).</summary>
public sealed class ChartSeries
{
    internal ChartSeries() { }

    /// <summary>&lt;c:idx&gt; (identifies the series' formatting).</summary>
    public int Index { get; internal set; }

    /// <summary>&lt;c:order&gt; (drawing order).</summary>
    public int Order { get; internal set; }

    /// <summary>The series name (cached text of the name reference, or the literal name), or null.</summary>
    public string? Name { get; internal set; }

    /// <summary>The name reference, e.g. "Sheet1!$B$1", or null.</summary>
    public string? NameFormula { get; internal set; }

    /// <summary>Category (x-axis labels) data, or null.</summary>
    public ChartDataReference? Categories { get; internal set; }

    /// <summary>Values (y data), or null.</summary>
    public ChartDataReference? Values { get; internal set; }

    /// <summary>For scatter and bubble charts: x values, or null.</summary>
    public ChartDataReference? XValues { get; internal set; }

    /// <summary>For scatter and bubble charts: y values, or null.</summary>
    public ChartDataReference? YValues { get; internal set; }

    /// <summary>For bubble charts: bubble sizes, or null.</summary>
    public ChartDataReference? BubbleSizes { get; internal set; }

    /// <summary>The series' explicit solid fill or line colour, "RRGGBB", or null (theme-cycled).</summary>
    public string? Color { get; internal set; }
}

/// <summary>One plot in a chart's plot area: a chart type with its series (combo charts have several).</summary>
public sealed class ChartPlot
{
    internal ChartPlot() { }

    public ChartType Type { get; internal set; }

    /// <summary>True for the 3-D variants (bar3DChart, line3DChart, pie3DChart, area3DChart, surface3DChart).</summary>
    public bool Is3D { get; internal set; }

    /// <summary>&lt;c:grouping&gt;: "clustered", "stacked", "percentStacked" or "standard", or null.</summary>
    public string? Grouping { get; internal set; }

    /// <summary>For scatter charts: &lt;c:scatterStyle&gt; ("lineMarker", "smoothMarker", "marker", ...), or null.</summary>
    public string? ScatterStyle { get; internal set; }

    public IReadOnlyList<ChartSeries> Series { get; internal set; } = Array.Empty<ChartSeries>();
}

/// <summary>
/// A chart's metadata, read from its chart part: type, title, series with their data
/// references and cached values, axis titles and legend. Metadata only: drawing the chart is
/// the renderer's job (Chuvadi does not render charts).
/// </summary>
public sealed class ChartInfo
{
    internal ChartInfo() { }

    /// <summary>The chart part, e.g. "/xl/charts/chart1.xml".</summary>
    public string PartName { get; internal set; } = string.Empty;

    /// <summary>The main chart type (the first plot's), or <see cref="ChartType.Extended"/> for Excel 2016 charts.</summary>
    public ChartType Type { get; internal set; }

    /// <summary>For <see cref="ChartType.Extended"/> charts: Excel's layout id ("waterfall", "funnel", "treemap", "sunburst", "clusteredColumn" (histogram), "paretoLine", "boxWhisker", "regionMap"), else null.</summary>
    public string? ExtendedType { get; internal set; }

    /// <summary>The chart title as plain text (rich text runs joined, or the cached text of a title reference), or null.</summary>
    public string? Title { get; internal set; }

    /// <summary>The title's cell reference, when the title comes from a cell, or null.</summary>
    public string? TitleFormula { get; internal set; }

    /// <summary>True if the automatic title was deleted (no title shows).</summary>
    public bool AutoTitleDeleted { get; internal set; }

    /// <summary>The plots, in file order (one for ordinary charts, several for combo charts).</summary>
    public IReadOnlyList<ChartPlot> Plots { get; internal set; } = Array.Empty<ChartPlot>();

    /// <summary>All series of all plots, in file order.</summary>
    public IEnumerable<ChartSeries> AllSeries
    {
        get { foreach (var p in Plots) foreach (var s in p.Series) yield return s; }
    }

    /// <summary>Axis titles as plain text, in file order (category axis usually first), empty when none.</summary>
    public IReadOnlyList<string> AxisTitles { get; internal set; } = Array.Empty<string>();

    /// <summary>Legend position ("r", "l", "t", "b", "tr"), or null when there is no legend.</summary>
    public string? LegendPosition { get; internal set; }

    /// <summary>The built-in chart style number (&lt;c:style&gt;), or null.</summary>
    public int? Style { get; internal set; }
}
