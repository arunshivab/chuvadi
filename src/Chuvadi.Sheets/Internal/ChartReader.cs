using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.Internal;

/// <summary>
/// Reads chart metadata from a chart part (DrawingML charts, c:) or an Excel 2016 chart part
/// (chartex, cx:). Read limits apply. Values are carried as stored.
/// </summary>
internal static class ChartReader
{
    internal const string CNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    internal const string CxNs = "http://schemas.microsoft.com/office/drawing/2014/chartex";
    private const string ANs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace C = CNs, Cx = CxNs, A = ANs;

    /// <summary>The chart's metadata, or null when the part is missing or unreadable as a chart.</summary>
    public static ChartInfo? Read(XlsxReader book, string partUri)
    {
        if (!book.HasPart(partUri)) return null;
        XDocument doc;
        try
        {
            using var s = book.OpenPartLimited(partUri);
            using var x = XmlReader.Create(s, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, CloseInput = false });
            doc = XDocument.Load(x);
            StrictOoxml.Normalize(doc);
        }
        catch (XmlException) { return null; }
        var root = doc.Root;
        if (root is null) return null;
        if (root.Name == C + "chartSpace") return ReadChart(root, partUri, book.StyleTable);
        if (root.Name == Cx + "chartSpace") return ReadChartEx(root, partUri);
        return null;
    }

    // ---- DrawingML charts -----------------------------------------------------------

    private static ChartInfo ReadChart(XElement space, string partUri, CellStyleTable styles)
    {
        var info = new ChartInfo { PartName = partUri };
        // <c:style> may sit directly under chartSpace or in an mc:AlternateContent fallback.
        info.Style = IntVal(space.Descendants(C + "style").FirstOrDefault());
        var chart = space.Element(C + "chart");
        if (chart is null) return info;
        info.AutoTitleDeleted = BoolVal(chart.Element(C + "autoTitleDeleted")) ?? false;
        if (chart.Element(C + "title") is { } t) (info.Title, info.TitleFormula) = Title(t);

        var plotArea = chart.Element(C + "plotArea");
        var plots = new List<ChartPlot>();
        var axisTitles = new List<string>();
        if (plotArea is not null)
        {
            foreach (var p in plotArea.Elements())
            {
                var plot = Plot(p, styles);
                if (plot is not null) plots.Add(plot);
                else if (p.Name.LocalName is "catAx" or "valAx" or "dateAx" or "serAx" && p.Element(C + "title") is { } at
                         && Title(at).Text is { } atText)
                    axisTitles.Add(atText);
            }
        }
        info.Plots = plots;
        info.AxisTitles = axisTitles;
        info.Type = plots.Count > 0 ? plots[0].Type : ChartType.Other;
        if (chart.Element(C + "legend") is { } lg)
            info.LegendPosition = (string?)lg.Element(C + "legendPos")?.Attribute("val") ?? "r";
        return info;
    }

    private static ChartPlot? Plot(XElement p, CellStyleTable styles)
    {
        if (p.Name.Namespace != C) return null;
        var name = p.Name.LocalName;
        if (!name.EndsWith("Chart", StringComparison.Ordinal)) return null;
        bool is3D = name.Contains("3D", StringComparison.Ordinal);
        ChartType type = name switch
        {
            "barChart" or "bar3DChart" => (string?)p.Element(C + "barDir")?.Attribute("val") == "bar" ? ChartType.Bar : ChartType.Column,
            "lineChart" or "line3DChart" => ChartType.Line,
            "pieChart" or "pie3DChart" => ChartType.Pie,
            "doughnutChart" => ChartType.Doughnut,
            "ofPieChart" => ChartType.OfPie,
            "areaChart" or "area3DChart" => ChartType.Area,
            "scatterChart" => ChartType.Scatter,
            "radarChart" => ChartType.Radar,
            "bubbleChart" => ChartType.Bubble,
            "stockChart" => ChartType.Stock,
            "surfaceChart" or "surface3DChart" => ChartType.Surface,
            _ => ChartType.Other,
        };
        var plot = new ChartPlot
        {
            Type = type,
            Is3D = is3D,
            Grouping = (string?)p.Element(C + "grouping")?.Attribute("val"),
            ScatterStyle = (string?)p.Element(C + "scatterStyle")?.Attribute("val"),
        };
        var series = new List<ChartSeries>();
        foreach (var s in p.Elements(C + "ser"))
        {
            var ser = new ChartSeries
            {
                Index = IntVal(s.Element(C + "idx")) ?? series.Count,
                Order = IntVal(s.Element(C + "order")) ?? series.Count,
                Categories = Data(s.Element(C + "cat")),
                Values = Data(s.Element(C + "val")),
                XValues = Data(s.Element(C + "xVal")),
                YValues = Data(s.Element(C + "yVal")),
                BubbleSizes = Data(s.Element(C + "bubbleSize")),
            };
            if (s.Element(C + "tx") is { } tx)
            {
                if (tx.Element(C + "strRef") is { } sr)
                {
                    ser.NameFormula = sr.Element(C + "f")?.Value;
                    ser.Name = sr.Element(C + "strCache")?.Elements(C + "pt").FirstOrDefault()?.Element(C + "v")?.Value;
                }
                else ser.Name = tx.Element(C + "v")?.Value;
            }
            if (s.Element(C + "spPr") is { } spPr)
            {
                var fill = spPr.Element(A + "solidFill") ?? spPr.Element(A + "ln")?.Element(A + "solidFill");
                if (fill is not null) ser.Color = DrawingReader.Color(fill, styles);
            }
            series.Add(ser);
        }
        plot.Series = series;
        return plot;
    }

    /// <summary>A cat/val/xVal/yVal/bubbleSize element: its reference and cache.</summary>
    private static ChartDataReference? Data(XElement? holder)
    {
        if (holder is null) return null;
        var src = holder.Elements().FirstOrDefault();
        if (src is null) return null;
        var r = new ChartDataReference { IsNumeric = src.Name.LocalName is "numRef" or "numLit" };
        r.Formula = src.Element(C + "f")?.Value;
        XElement? cache = src.Name.LocalName switch
        {
            "numRef" => src.Element(C + "numCache"),
            "strRef" => src.Element(C + "strCache"),
            "multiLvlStrRef" => src.Element(C + "multiLvlStrCache")?.Elements(C + "lvl").FirstOrDefault(),
            "numLit" or "strLit" => src,
            _ => null,
        };
        if (cache is not null)
        {
            r.FormatCode = cache.Element(C + "formatCode")?.Value;
            int count = IntVal(cache.Element(C + "ptCount")) ?? 0;
            if (src.Name.LocalName == "multiLvlStrRef")
                count = IntVal(src.Element(C + "multiLvlStrCache")?.Element(C + "ptCount")) ?? 0;
            var pts = cache.Elements(C + "pt").Select(pt => (Idx: (int?)IntAttr(pt, "idx") ?? -1, V: pt.Element(C + "v")?.Value)).ToList();
            if (count == 0 && pts.Count > 0) count = pts.Max(x => x.Idx) + 1;
            var values = new string?[Math.Max(0, count)];
            foreach (var (idx, v) in pts) if (idx >= 0 && idx < values.Length) values[idx] = v;
            r.PointCount = values.Length;
            r.CachedValues = values;
        }
        return r;
    }

    private static (string? Text, string? Formula) Title(XElement title)
    {
        var tx = title.Element(C + "tx");
        if (tx?.Element(C + "strRef") is { } sr)
            return (sr.Element(C + "strCache")?.Elements(C + "pt").FirstOrDefault()?.Element(C + "v")?.Value, sr.Element(C + "f")?.Value);
        var rich = tx?.Element(C + "rich");
        if (rich is null) return (null, null);
        return (RichText(rich), null);
    }

    private static string? RichText(XElement rich)
    {
        var paras = new List<string>();
        foreach (var p in rich.Elements(A + "p"))
        {
            var sb = new StringBuilder();
            foreach (var n in p.Elements())
            {
                if (n.Name == A + "r" || n.Name == A + "fld") sb.Append(n.Element(A + "t")?.Value);
                else if (n.Name == A + "br") sb.Append('\n');
            }
            paras.Add(sb.ToString());
        }
        var text = string.Join("\n", paras);
        return text.Length == 0 ? null : text;
    }

    // ---- Excel 2016 charts (chartex) --------------------------------------------------

    private static ChartInfo ReadChartEx(XElement space, string partUri)
    {
        var info = new ChartInfo { PartName = partUri, Type = ChartType.Extended };
        var data = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var d in space.Element(Cx + "chartData")?.Elements(Cx + "data") ?? Enumerable.Empty<XElement>())
            if ((string?)d.Attribute("id") is string id) data[id] = d;

        var chart = space.Element(Cx + "chart");
        if (chart?.Element(Cx + "title") is { } t)
        {
            var txData = t.Element(Cx + "tx")?.Element(Cx + "txData");
            if (txData is not null)
            {
                info.TitleFormula = txData.Element(Cx + "f")?.Value;
                info.Title = txData.Element(Cx + "v")?.Value;
            }
            else if (t.Element(Cx + "tx")?.Element(Cx + "rich") is { } rich) info.Title = RichText(rich);
        }
        var series = new List<ChartSeries>();
        foreach (var s in chart?.Element(Cx + "plotArea")?.Element(Cx + "plotAreaRegion")?.Elements(Cx + "series") ?? Enumerable.Empty<XElement>())
        {
            info.ExtendedType ??= (string?)s.Attribute("layoutId");
            var ser = new ChartSeries { Index = series.Count, Order = series.Count };
            var txData = s.Element(Cx + "tx")?.Element(Cx + "txData");
            ser.NameFormula = txData?.Element(Cx + "f")?.Value;
            ser.Name = txData?.Element(Cx + "v")?.Value;
            var dataId = (string?)s.Element(Cx + "dataId")?.Attribute("val");
            if (dataId is not null && data.TryGetValue(dataId, out var d))
            {
                foreach (var dim in d.Elements())
                {
                    var r = new ChartDataReference { IsNumeric = dim.Name.LocalName == "numDim", Formula = dim.Element(Cx + "f")?.Value };
                    var lvl = dim.Elements(Cx + "lvl").FirstOrDefault();
                    if (lvl is not null)
                    {
                        r.FormatCode = (string?)lvl.Attribute("formatCode");
                        int count = IntAttr(lvl, "ptCount") ?? 0;
                        var pts = lvl.Elements(Cx + "pt").Select(pt => (Idx: IntAttr(pt, "idx") ?? -1, V: pt.Value)).ToList();
                        if (count == 0 && pts.Count > 0) count = pts.Max(x => x.Idx) + 1;
                        var values = new string?[Math.Max(0, count)];
                        foreach (var (idx, v) in pts) if (idx >= 0 && idx < values.Length) values[idx] = v;
                        r.PointCount = values.Length;
                        r.CachedValues = values;
                    }
                    var type = (string?)dim.Attribute("type");
                    if (r.IsNumeric && type is "val" or "size" or "y") ser.Values ??= r;
                    else if (r.IsNumeric && type == "x") ser.XValues ??= r;
                    else ser.Categories ??= r;
                }
            }
            series.Add(ser);
        }
        info.Plots = new[] { new ChartPlot { Type = ChartType.Extended, Series = series } };
        if (chart?.Element(Cx + "legend") is { } lg) info.LegendPosition = (string?)lg.Attribute("pos") ?? "r";
        return info;
    }

    // ---- Helpers ---------------------------------------------------------------------

    private static int? IntVal(XElement? e) => IntAttr(e, "val");

    private static int? IntAttr(XElement? e, string name)
        => e is not null && int.TryParse((string?)e.Attribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static bool? BoolVal(XElement? e)
    {
        if (e is null) return null;
        return (string?)e.Attribute("val") is not ("0" or "false");
    }
}
