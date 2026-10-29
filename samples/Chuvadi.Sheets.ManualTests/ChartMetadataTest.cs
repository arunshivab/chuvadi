using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.ManualTests;

/// <summary>
/// Sheets 1.6.1: chart metadata on sheets (anchor, type, title, series references and cached
/// values, axis titles, legend), for DrawingML charts and Excel 2016 charts (chartex).
/// The fixture's chart parts are shaped like Excel's output.
/// </summary>
internal static class ChartMetadataTest
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string C = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private const string A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string Cx = "http://schemas.microsoft.com/office/drawing/2014/chartex";

    public static void Run(string outputDir)
    {
        Console.WriteLine("[ChartMetadataTest] 1.6.1 chart metadata...");
        Console.WriteLine();
        int pass = 0, fail = 0;

        void Check(string label, Action body)
        {
            try { body(); Console.WriteLine($"    PASS: {label}"); pass++; }
            catch (Exception ex) { Console.WriteLine($"    FAIL: {label}"); Console.WriteLine($"          {ex.GetType().Name}: {ex.Message}"); fail++; }
        }

        static void Eq<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception($"{what}: expected <{expected}> got <{actual}>");
        }

        static void True(bool c, string what) { if (!c) throw new Exception(what); }

        var fx = Path.Combine(outputDir, "f161_charts.xlsx");
        WriteFixture(fx);

        Check("combo chart: type, rich title, plots, series names, references and caches, colour, axis titles, legend, style", () =>
        {
            var d = Workbook.Load(fx)["Data"].Drawings;
            Eq(4, d.Count, "objects");
            var o = d[0];
            True(o.Kind == DrawingKind.Chart && o.ChartPartName == "/xl/charts/chart1.xml", "chart object");
            Eq(new DrawingPoint(5, 0, 2, 0), o.Anchor.From!.Value, "anchor from");
            var c = o.Chart!;
            True(c.Type == ChartType.Column && c.Title == "Monthly  Sales\nFY 2026" && c.TitleFormula is null, "type and rich title (double space kept)");
            Eq(2, c.Plots.Count, "combo: two plots");
            True(c.Plots[0] is { Type: ChartType.Column, Grouping: "clustered", Is3D: false } && c.Plots[1].Type == ChartType.Line, "plot types");
            var s0 = c.Plots[0].Series[0];
            True(s0.Name == "Sales" && s0.NameFormula == "Data!$B$1", "series name");
            True(s0.Categories is { Formula: "Data!$A$2:$A$4", IsNumeric: false, PointCount: 3 } && s0.Categories.CachedValues.SequenceEqual(new[] { "Jan", "Feb", "Mar" }), "categories");
            True(s0.Values is { Formula: "Data!$B$2:$B$4", IsNumeric: true, FormatCode: "General", PointCount: 3 }, "values ref");
            True(s0.Values!.CachedValues.SequenceEqual(new string?[] { "10", null, "15.5" }) && s0.Values.CachedNumbers[2] == 15.5 && s0.Values.CachedNumbers[1] is null, "cached values with a gap");
            Eq("4472C4", s0.Color, "explicit series colour");
            True(c.Plots[1].Series[0] is { Name: "Cost", Index: 1, Order: 1 }, "line series");
            Eq(2, c.AllSeries.Count(), "all series");
            True(c.AxisTitles.SequenceEqual(new[] { "Month", "Amount (₹ lakh)" }), "axis titles");
            True(c.LegendPosition == "b" && c.Style == 2 && !c.AutoTitleDeleted, "legend and style");
        });

        Check("pie with a cell-reference title; 3-D bar; scatter x/y", () =>
        {
            var d = Workbook.Load(fx)["Data"].Drawings;
            var pie = d[1].Chart!;
            True(pie.Type == ChartType.Pie && pie.Title == "Share" && pie.TitleFormula == "Data!$D$1" && pie.LegendPosition is null, "pie");
            var bar = d[2].Chart!;
            True(bar.Type == ChartType.Bar && bar.Plots[0].Is3D && bar.Plots[0].Grouping == "stacked" && bar.AutoTitleDeleted, "3-D horizontal bar");
            var sc = d[2].Chart!.Plots.Count == 2 ? d[2].Chart!.Plots[1] : null;
            True(sc is { Type: ChartType.Scatter, ScatterStyle: "lineMarker" }, "scatter plot");
            True(sc!.Series[0].XValues!.Formula == "Data!$B$2:$B$4" && sc.Series[0].YValues!.CachedValues.SequenceEqual(new[] { "7", "8", "9" }), "x and y");
        });

        Check("Excel 2016 chart (chartex) in mc:AlternateContent: the chart wins over the fallback text box", () =>
        {
            var o = Workbook.Load(fx)["Data"].Drawings[3];
            True(o.Kind == DrawingKind.Chart && o.ChartPartName == "/xl/charts/chartEx1.xml", "chartex frame chosen");
            var c = o.Chart!;
            True(c.Type == ChartType.Extended && c.ExtendedType == "waterfall" && c.Title == "Bridge", "waterfall metadata");
            var s = c.AllSeries.Single();
            True(s.Name == "Change" && s.Values!.Formula == "Data!$B$2:$B$4" && s.Values.CachedValues.SequenceEqual(new[] { "10", "-3", "4" }), "values");
            True(s.Categories!.CachedValues.SequenceEqual(new[] { "Open", "Q1", "Q2" }), "categories");
        });

        Check("streaming ReadDrawings carries the same chart metadata", () =>
        {
            using var r = XlsxReader.Open(fx);
            var c = r.Sheet("Data").ReadDrawings()[0].Chart!;
            True(c.Title == "Monthly  Sales\nFY 2026" && c.AllSeries.Count() == 2, "streaming");
        });

        Check("a chart whose part is missing: object kept, metadata null", () =>
        {
            var p = Path.Combine(outputDir, "f161_chart_missing.xlsx");
            WriteFixture(p, includeChart1: false);
            var o = Workbook.Load(p)["Data"].Drawings[0];
            True(o.Kind == DrawingKind.Chart && o.ChartPartName == "/xl/charts/chart1.xml" && o.Chart is null, "no metadata, no exception");
        });

        Check("1.6.1 helpers for Layout: FindCell does not create cells; Column.EffectiveWidth applies the default", () =>
        {
            var s = Workbook.Load(fx)["Data"];
            int before = s.PopulatedCells.Count();
            True(s.FindCell(1, 1)?.Value as string == "Month" && s.FindCell("Z99") is null, "lookup");
            Eq(before, s.PopulatedCells.Count(), "no cells created");
            Eq(9.140625, s.Columns[3].EffectiveWidth, "default width (Calibri 11, base 8)");
            s.Columns[3].Width = 20;
            Eq(20.0, s.Columns[3].EffectiveWidth, "set width");
            s.Format.DefaultColumnWidth = 12;
            Eq(12.0, s.Columns[4].EffectiveWidth, "stated default");
        });

        Console.WriteLine();
        Console.WriteLine($"  ChartMetadataTest: {pass} passed, {fail} failed.");
        Console.WriteLine();
        if (fail > 0) throw new Exception($"ChartMetadataTest: {fail} check(s) failed.");
    }

    // ---- Fixture -------------------------------------------------------------------------

    private static string Str(string f, params string[] pts)
        => $"<c:strRef><c:f>{f}</c:f><c:strCache><c:ptCount val=\"{pts.Length}\"/>" +
           string.Concat(pts.Select((p, i) => $"<c:pt idx=\"{i}\"><c:v>{p}</c:v></c:pt>")) + "</c:strCache></c:strRef>";

    private static string Num(string f, int count, params (int Idx, string V)[] pts)
        => $"<c:numRef><c:f>{f}</c:f><c:numCache><c:formatCode>General</c:formatCode><c:ptCount val=\"{count}\"/>" +
           string.Concat(pts.Select(p => $"<c:pt idx=\"{p.Idx}\"><c:v>{p.V}</c:v></c:pt>")) + "</c:numCache></c:numRef>";

    private static string ChartPart(string chartBody, string? style = "2")
        => $"<c:chartSpace xmlns:c=\"{C}\" xmlns:a=\"{A}\" xmlns:r=\"{R}\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\">" +
           (style is null ? "" : $"<mc:AlternateContent><mc:Choice Requires=\"c14\" xmlns:c14=\"http://schemas.microsoft.com/office/drawing/2007/8/2/chart\"><c14:style val=\"10{style}\"/></mc:Choice><mc:Fallback><c:style val=\"{style}\"/></mc:Fallback></mc:AlternateContent>") +
           $"<c:chart>{chartBody}</c:chart></c:chartSpace>";

    private static string Rich(params string[] paras)
        => "<c:tx><c:rich><a:bodyPr/><a:p>" + string.Join("</a:p><a:p>", paras.Select(p => $"<a:r><a:t>{p}</a:t></a:r>")) + "</a:p></c:rich></c:tx>";

    private static void WriteFixture(string path, bool includeChart1 = true)
    {
        string chart1 = ChartPart(
            "<c:title>" + Rich("Monthly  Sales", "FY 2026") + "<c:overlay val=\"0\"/></c:title><c:autoTitleDeleted val=\"0\"/>" +
            "<c:plotArea><c:layout/>" +
            "<c:barChart><c:barDir val=\"col\"/><c:grouping val=\"clustered\"/><c:varyColors val=\"0\"/>" +
            "<c:ser><c:idx val=\"0\"/><c:order val=\"0\"/><c:tx>" + Str("Data!$B$1", "Sales") + "</c:tx>" +
            "<c:spPr><a:solidFill><a:schemeClr val=\"accent1\"/></a:solidFill></c:spPr>" +
            "<c:cat>" + Str("Data!$A$2:$A$4", "Jan", "Feb", "Mar") + "</c:cat><c:val>" + Num("Data!$B$2:$B$4", 3, (0, "10"), (2, "15.5")) + "</c:val></c:ser>" +
            "<c:axId val=\"1\"/><c:axId val=\"2\"/></c:barChart>" +
            "<c:lineChart><c:grouping val=\"standard\"/><c:ser><c:idx val=\"1\"/><c:order val=\"1\"/><c:tx>" + Str("Data!$C$1", "Cost") + "</c:tx>" +
            "<c:cat>" + Str("Data!$A$2:$A$4", "Jan", "Feb", "Mar") + "</c:cat><c:val>" + Num("Data!$C$2:$C$4", 3, (0, "7"), (1, "8"), (2, "9")) + "</c:val></c:ser>" +
            "<c:axId val=\"1\"/><c:axId val=\"2\"/></c:lineChart>" +
            "<c:catAx><c:axId val=\"1\"/><c:title>" + Rich("Month") + "</c:title><c:crossAx val=\"2\"/></c:catAx>" +
            "<c:valAx><c:axId val=\"2\"/><c:title>" + Rich("Amount (₹ lakh)") + "</c:title><c:crossAx val=\"1\"/></c:valAx>" +
            "</c:plotArea><c:legend><c:legendPos val=\"b\"/></c:legend><c:plotVisOnly val=\"1\"/>");
        string chart2 = ChartPart(
            "<c:title><c:tx>" + Str("Data!$D$1", "Share") + "</c:tx></c:title><c:autoTitleDeleted val=\"0\"/>" +
            "<c:plotArea><c:pieChart><c:varyColors val=\"1\"/><c:ser><c:idx val=\"0\"/><c:order val=\"0\"/>" +
            "<c:cat>" + Str("Data!$A$2:$A$4", "Jan", "Feb", "Mar") + "</c:cat><c:val>" + Num("Data!$B$2:$B$4", 3, (0, "10"), (1, "12"), (2, "15")) + "</c:val></c:ser></c:pieChart></c:plotArea>", style: null);
        string chart3 = ChartPart(
            "<c:autoTitleDeleted val=\"1\"/><c:plotArea>" +
            "<c:bar3DChart><c:barDir val=\"bar\"/><c:grouping val=\"stacked\"/><c:ser><c:idx val=\"0\"/><c:order val=\"0\"/>" +
            "<c:val>" + Num("Data!$B$2:$B$4", 3, (0, "10"), (1, "12"), (2, "15")) + "</c:val></c:ser></c:bar3DChart>" +
            "<c:scatterChart><c:scatterStyle val=\"lineMarker\"/><c:ser><c:idx val=\"1\"/><c:order val=\"1\"/>" +
            "<c:xVal>" + Num("Data!$B$2:$B$4", 3, (0, "10"), (1, "12"), (2, "15")) + "</c:xVal><c:yVal>" + Num("Data!$C$2:$C$4", 3, (0, "7"), (1, "8"), (2, "9")) + "</c:yVal></c:ser></c:scatterChart>" +
            "</c:plotArea>");
        string chartEx =
            $"<cx:chartSpace xmlns:a=\"{A}\" xmlns:r=\"{R}\" xmlns:cx=\"{Cx}\"><cx:chartData>" +
            "<cx:data id=\"0\"><cx:strDim type=\"cat\"><cx:f>Data!$A$6:$A$8</cx:f><cx:lvl ptCount=\"3\"><cx:pt idx=\"0\">Open</cx:pt><cx:pt idx=\"1\">Q1</cx:pt><cx:pt idx=\"2\">Q2</cx:pt></cx:lvl></cx:strDim>" +
            "<cx:numDim type=\"val\"><cx:f>Data!$B$2:$B$4</cx:f><cx:lvl ptCount=\"3\" formatCode=\"General\"><cx:pt idx=\"0\">10</cx:pt><cx:pt idx=\"1\">-3</cx:pt><cx:pt idx=\"2\">4</cx:pt></cx:lvl></cx:numDim></cx:data></cx:chartData>" +
            "<cx:chart><cx:title pos=\"t\" align=\"ctr\" overlay=\"0\"><cx:tx><cx:txData><cx:v>Bridge</cx:v></cx:txData></cx:tx></cx:title>" +
            "<cx:plotArea><cx:plotAreaRegion><cx:series layoutId=\"waterfall\" uniqueId=\"{11111111-0000-0000-0000-000000000001}\">" +
            "<cx:tx><cx:txData><cx:f>Data!$B$1</cx:f><cx:v>Change</cx:v></cx:txData></cx:tx><cx:dataId val=\"0\"/></cx:series></cx:plotAreaRegion></cx:plotArea>" +
            "<cx:legend pos=\"t\" align=\"ctr\" overlay=\"0\"/></cx:chart></cx:chartSpace>";

        string Frame(int id, string rid, string uri = C, string tag = "c:chart", string ns = C)
            => $"<xdr:graphicFrame macro=\"\"><xdr:nvGraphicFramePr><xdr:cNvPr id=\"{id}\" name=\"Chart {id}\"/><xdr:cNvGraphicFramePr/></xdr:nvGraphicFramePr>" +
               "<xdr:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/></xdr:xfrm>" +
               $"<a:graphic><a:graphicData uri=\"{uri}\"><{tag} xmlns:{tag.Split(':')[0]}=\"{ns}\" r:id=\"{rid}\"/></a:graphicData></a:graphic></xdr:graphicFrame>";
        string Anchor(int col, int row, string body)
            => $"<xdr:twoCellAnchor><xdr:from><xdr:col>{col}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>{row}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>" +
               $"<xdr:to><xdr:col>{col + 6}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>{row + 12}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>{body}<xdr:clientData/></xdr:twoCellAnchor>";
        string drawing =
            "<xdr:wsDr xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\" xmlns:a=\"" + A + "\" xmlns:r=\"" + R + "\" " +
            "xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\">" +
            Anchor(4, 1, Frame(2, "rId1")) + Anchor(4, 15, Frame(3, "rId2")) + Anchor(12, 1, Frame(4, "rId3")) +
            Anchor(12, 15,
                "<mc:AlternateContent><mc:Choice xmlns:cx1=\"http://schemas.microsoft.com/office/drawing/2015/9/8/chartex\" Requires=\"cx1\">" +
                Frame(5, "rId4", "http://schemas.microsoft.com/office/drawing/2014/chartex", "cx:chart", Cx) +
                "</mc:Choice><mc:Fallback><xdr:sp macro=\"\" textlink=\"\"><xdr:nvSpPr><xdr:cNvPr id=\"5\" name=\"Chart 5\"/><xdr:cNvSpPr/></xdr:nvSpPr>" +
                "<xdr:spPr><a:prstGeom prst=\"rect\"/></xdr:spPr><xdr:txBody><a:bodyPr/><a:p><a:r><a:t>This chart isn't available in your version of Excel.</a:t></a:r></a:p></xdr:txBody></xdr:sp>" +
                "</mc:Fallback></mc:AlternateContent>") +
            "</xdr:wsDr>";

        string sheet = "<sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Month</t></is></c></row></sheetData><drawing r:id=\"rId1\"/>";
        byte[] U(string s) => new UTF8Encoding(false).GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" + s);
        string Rels(params (string Id, string Type, string Target)[] rels)
            => "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
               string.Concat(rels.Select(r => $"<Relationship Id=\"{r.Id}\" Type=\"{r.Type}\" Target=\"{r.Target}\"/>")) + "</Relationships>";

        var parts = new List<(string, byte[])>
        {
            ("[Content_Types].xml", U("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                "<Override PartName=\"/xl/drawings/drawing1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawing+xml\"/>" +
                (includeChart1 ? "<Override PartName=\"/xl/charts/chart1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawingml.chart+xml\"/>" : "") +
                "<Override PartName=\"/xl/charts/chart2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawingml.chart+xml\"/>" +
                "<Override PartName=\"/xl/charts/chart3.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawingml.chart+xml\"/>" +
                "<Override PartName=\"/xl/charts/chartEx1.xml\" ContentType=\"application/vnd.ms-office.chartex+xml\"/></Types>")),
            ("_rels/.rels", U(Rels(("rId1", R + "/officeDocument", "xl/workbook.xml")))),
            ("xl/workbook.xml", U($"<workbook xmlns=\"{Main}\" xmlns:r=\"{R}\"><sheets><sheet name=\"Data\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>")),
            ("xl/_rels/workbook.xml.rels", U(Rels(("rId1", R + "/worksheet", "worksheets/sheet1.xml")))),
            ("xl/worksheets/sheet1.xml", U($"<worksheet xmlns=\"{Main}\" xmlns:r=\"{R}\">{sheet}</worksheet>")),
            ("xl/worksheets/_rels/sheet1.xml.rels", U(Rels(("rId1", R + "/drawing", "../drawings/drawing1.xml")))),
            ("xl/drawings/drawing1.xml", U(drawing)),
            ("xl/drawings/_rels/drawing1.xml.rels", U(Rels(("rId1", R + "/chart", "../charts/chart1.xml"), ("rId2", R + "/chart", "../charts/chart2.xml"),
                ("rId3", R + "/chart", "../charts/chart3.xml"), ("rId4", "http://schemas.microsoft.com/office/2014/relationships/chartEx", "../charts/chartEx1.xml")))),
            ("xl/charts/chart2.xml", U(chart2)),
            ("xl/charts/chart3.xml", U(chart3)),
            ("xl/charts/chartEx1.xml", U(chartEx)),
        };
        if (includeChart1) parts.Add(("xl/charts/chart1.xml", U(chart1)));

        if (File.Exists(path)) File.Delete(path);
        using var fs = new FileStream(path, FileMode.CreateNew);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (name, data) in parts)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var s = e.Open();
            s.Write(data, 0, data.Length);
        }
    }
}
