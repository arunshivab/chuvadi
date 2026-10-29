using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Chuvadi.Docs.Word;
using static Chuvadi.Docs.ManualTests.Program;

namespace Chuvadi.Docs.ManualTests;

/// <summary>
/// Phase 4 — tables: full table/row/cell properties in schema order, vertical merges, nested
/// tables, structured loading with exact round-trip, table styles with conditional formats,
/// and effective cell / run / paragraph formatting inside tables.
/// </summary>
public static class TablesModelTests
{
    private static readonly XNamespace Wn = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static readonly string[] TblPrOrder =
    {
        "tblStyle", "tblpPr", "tblOverlap", "bidiVisual", "tblStyleRowBandSize", "tblStyleColBandSize", "tblW", "jc",
        "tblCellSpacing", "tblInd", "tblBorders", "shd", "tblLayout", "tblCellMar", "tblLook", "tblCaption", "tblDescription",
    };

    private static readonly string[] TcPrOrder =
    {
        "cnfStyle", "tcW", "gridSpan", "hMerge", "vMerge", "tcBorders", "shd", "noWrap", "tcMar", "textDirection",
        "tcFitText", "vAlign", "hideMark",
    };

    public static void Run(string outDir)
    {
        WrittenInSchemaOrder(outDir);
        LoadAndRoundTrip(outDir);
        LegacyOutputUnchanged(outDir);
        TableStyles(outDir);
        EffectiveInTables();
        Hostile(outDir);
        ReviewRegressions(outDir);
    }

    /// <summary>A table exercising every property.</summary>
    private static Document Rich()
    {
        var doc = new Document();
        var t = doc.AddTable(4);
        t.ColumnWidthsPt = new double[] { 120, 80, 80, 100 };
        t.Format.Alignment = TableAlignment.Center;
        t.Format.IndentPt = 6;
        t.Format.CellSpacingPt = 1;
        t.Format.Borders = TableBorders.Horizontal(BorderSpec.Single(0.75, "7F7F7F"));
        t.Format.CellMargins = new CellMargins { LeftPt = 5.4, RightPt = 5.4, TopPt = 2 };
        t.Format.Shading = Shading.Fill("FAFAFA");
        t.Format.Look = TableLook.FirstRow | TableLook.NoVerticalBand;
        t.Format.Caption = "Lab results";
        t.Format.Description = "Haematology panel with reference ranges";
        t.Format.RowBandSize = 1;

        var h = t.HeaderRow("Test", "Result", "Unit", "Reference");
        h.CantSplit = true;
        h.HeightPt = 20;
        h.HeightRule = RowHeightRule.Exact;
        h.Cell(0).TextDirection = CellTextDirection.BottomToTopLeftToRight;

        var r1 = t.AddRow("Haemoglobin", "13.2", "g/dL", "13.0–17.0");
        r1.Cell(0).VerticalMerge = VerticalMerge.Restart;
        r1.Cell(1).Shading = Shading.WithPattern(ShadingPattern.Percent10, "000000", "FFF2CC");
        r1.Cell(2).VerticalAlignment = CellVerticalAlignment.Center;
        r1.Cell(3).Borders = new CellBorders { DiagonalDown = BorderSpec.Single(0.5), Left = BorderSpec.Single(1.5, "C00000") };
        r1.Cell(3).Margins = CellMargins.All(3);
        r1.Cell(3).NoWrap = true;

        var r2 = t.AddRow("", "4.8", "×10⁶/µL", "4.5–5.5");
        r2.Cell(0).VerticalMerge = VerticalMerge.Continue;
        r2.Cell(1).FitText = true;
        r2.Cell(2).HideMark = true;
        r2.Alignment = TableAlignment.Right;

        var r3 = t.AddRow();
        r3.Cell(0).SetText("Remarks (spans three columns)");
        r3.Cell(0).ColumnSpan = 3;
        r3.Cell(3).SetText("ok");
        r3.Hidden = false;

        var r4 = t.AddRow();
        r4.GridBefore = 1;
        r4.Cell(1).SetText("indented row");
        r4.Cell(1).ColumnSpan = 3;

        var nestedHost = t.AddRow();
        nestedHost.Cell(0).SetText("Nested:");
        var nested = nestedHost.Cell(0).AddTable(2);
        nested.AddRow("a", "b");
        nested.AddRow("c", "d");
        nested.Format.Borders = TableBorders.Grid(BorderSpec.Single(0.25));
        nestedHost.Cell(1).SetText("x");
        return doc;
    }

    private static void WrittenInSchemaOrder(string outDir)
    {
        var path = Path.Combine(outDir, "tbl-rich.docx");
        Rich().SaveTo(path);
        var body = XElement.Parse(ReadPartXml(path, "word/document.xml"));
        var tbl = body.Element(Wn + "body")!.Element(Wn + "tbl")!;

        Check("tblPr and every tcPr follow the CT_TblPr / CT_TcPr schema order", () =>
        {
            foreach (var tblPr in body.Descendants(Wn + "tblPr")) AssertOrder(tblPr, TblPrOrder);
            foreach (var tcPr in body.Descendants(Wn + "tcPr")) AssertOrder(tcPr, TcPrOrder);
        });

        Check("table properties: style-free tblW sum, jc, spacing, indent, borders, shading, margins, look, caption", () =>
        {
            var p = tbl.Element(Wn + "tblPr")!;
            string A(string el, string attr) => p.Element(Wn + el)?.Attribute(Wn + attr)?.Value ?? "<missing>";
            AssertTrue(A("tblW", "w") == "7600" && A("tblW", "type") == "dxa", "tblW = sum of column widths");
            AssertTrue(A("jc", "val") == "center" && A("tblInd", "w") == "120" && A("tblCellSpacing", "w") == "20", "jc / indent / spacing");
            AssertTrue(p.Element(Wn + "tblBorders")!.Element(Wn + "insideV")!.Attribute(Wn + "val")!.Value == "none", "horizontal-only borders");
            AssertTrue(A("tblLayout", "type") == "fixed" && A("shd", "fill") == "FAFAFA", "layout / shading");
            AssertTrue(p.Element(Wn + "tblCellMar")!.Element(Wn + "left")!.Attribute(Wn + "w")!.Value == "108", "cell margins");
            AssertTrue(A("tblLook", "firstRow") == "1" && A("tblLook", "noVBand") == "1" && A("tblLook", "val") == "0420", "tblLook");
            AssertTrue(A("tblCaption", "val") == "Lab results" && A("tblDescription", "val").StartsWith("Haematology"), "accessibility");
        });

        Check("rows and cells: height/cantSplit/header, vMerge restart/continue, span width = sum, gridBefore, nested table + trailing paragraph", () =>
        {
            var rows = tbl.Elements(Wn + "tr").ToList();
            var trPr = rows[0].Element(Wn + "trPr")!;
            AssertTrue(trPr.Element(Wn + "trHeight")!.Attribute(Wn + "hRule")!.Value == "exact" && trPr.Element(Wn + "cantSplit") is not null
                && trPr.Element(Wn + "tblHeader") is not null, "header row props");
            AssertTrue(rows[1].Elements(Wn + "tc").First().Descendants(Wn + "vMerge").Single().Attribute(Wn + "val")!.Value == "restart", "vMerge restart");
            AssertTrue(rows[2].Elements(Wn + "tc").First().Descendants(Wn + "vMerge").Single().Attribute(Wn + "val") is null, "vMerge continue");
            var span = rows[3].Elements(Wn + "tc").First().Element(Wn + "tcPr")!;
            AssertTrue(span.Element(Wn + "gridSpan")!.Attribute(Wn + "val")!.Value == "3"
                && span.Element(Wn + "tcW")!.Attribute(Wn + "w")!.Value == "5600", "spanned width = 120+80+80 pt");
            AssertTrue(rows[4].Element(Wn + "trPr")!.Element(Wn + "gridBefore")!.Attribute(Wn + "val")!.Value == "1"
                && rows[4].Elements(Wn + "tc").Count() == 1, "gridBefore row has one (spanning) cell");
            var host = rows[5].Elements(Wn + "tc").First();
            var kinds = string.Join(",", host.Elements().Where(e => e.Name != Wn + "tcPr").Select(e => e.Name.LocalName));
            AssertTrue(kinds == "p,tbl,p", $"nested cell content {kinds}");
            var c3 = rows[1].Elements(Wn + "tc").ElementAt(3).Element(Wn + "tcPr")!;
            AssertTrue(c3.Element(Wn + "tcBorders")!.Element(Wn + "tl2br") is not null && c3.Element(Wn + "noWrap") is not null
                && c3.Element(Wn + "tcMar") is not null, "cell borders / noWrap / margins");
            AssertTrue(rows[0].Elements(Wn + "tc").First().Element(Wn + "tcPr")!.Element(Wn + "textDirection")!.Attribute(Wn + "val")!.Value == "btLr", "text direction");
        });
    }

    private static void LoadAndRoundTrip(string outDir)
    {
        var a = Path.Combine(outDir, "tbl-rt-a.docx");
        Rich().SaveTo(a);

        Check("Document.Load builds the structured table (properties, merges, spans, nested tables)", () =>
        {
            var t = (DocTable)Document.Load(a).Blocks[0];
            AssertTrue(t.Columns == 4 && t.Rows.Count == 6 && t.ColumnWidthsPt![0] == 120, "shape");
            AssertTrue(t.Format.Alignment == TableAlignment.Center && t.Format.Caption == "Lab results"
                && t.Format.Look == (TableLook.FirstRow | TableLook.NoVerticalBand) && t.Format.CellMargins!.LeftPt == 5.4, "table format");
            AssertTrue(t.Rows[0].IsHeader && t.Rows[0].HeightPt == 20 && t.Rows[0].HeightRule == RowHeightRule.Exact && t.Rows[0].CantSplit == true, "row props");
            AssertTrue(t.Rows[1].Cells[0].VerticalMerge == VerticalMerge.Restart && t.Rows[2].Cells[0].VerticalMerge == VerticalMerge.Continue, "vMerge");
            AssertTrue(t.Rows[1].Cells[1].Shading!.Pattern == ShadingPattern.Percent10 && t.Rows[1].Cells[2].VerticalAlignment == CellVerticalAlignment.Center, "cell props");
            AssertTrue(t.Rows[3].Cells[0].ColumnSpan == 3 && t.Rows[3].Cells[3].GetText() == "ok", "span");
            AssertTrue(t.Rows[4].GridBefore == 1 && t.Rows[4].Cells[1].ColumnSpan == 3 && t.Rows[4].Cells[1].GetText() == "indented row", "gridBefore");
            var nested = t.Rows[5].Cells[0].NestedTables.Single();
            AssertTrue(nested.Rows.Count == 2 && nested.Rows[1].Cells[1].GetText() == "d", "nested table loaded");
            AssertTrue(t.Rows[5].Cells[0].GetText().StartsWith("Nested:\na\tb", StringComparison.Ordinal), "nested text in cell text");
        });
        Check("load → save reproduces document.xml exactly (tables, merges, nested tables)", () =>
        {
            var b = Path.Combine(outDir, "tbl-rt-b.docx");
            Document.Load(a).SaveTo(b);
            AssertTrue(ReadPartXml(a, "word/document.xml") == ReadPartXml(b, "word/document.xml"), "document.xml differs");
        });
    }

    private static void LegacyOutputUnchanged(string outDir)
    {
        var path = Path.Combine(outDir, "tbl-legacy.docx");
        var doc = new Document();
        var t1 = doc.AddTable(2);
        t1.AddRow("a", "b");
        var t2 = doc.AddTable(2);
        t2.Borders = false;
        t2.ColumnWidthsPt = new double[] { 100, 50 };
        t2.AddRow("c", "d");
        doc.SaveTo(path);

        Check("simple tables keep the 1.2 output: default single grid + autofit; widths → fixed; Borders=false → none", () =>
        {
            var tbls = XElement.Parse(ReadPartXml(path, "word/document.xml")).Descendants(Wn + "tbl").ToList();
            var p1 = tbls[0].Element(Wn + "tblPr")!;
            AssertTrue(p1.Element(Wn + "tblW")!.Attribute(Wn + "type")!.Value == "auto" && p1.Element(Wn + "tblLayout")!.Attribute(Wn + "type")!.Value == "autofit", "auto");
            AssertTrue(p1.Element(Wn + "tblBorders")!.Elements().All(e => e.Attribute(Wn + "val")!.Value == "single" && e.Attribute(Wn + "sz")!.Value == "4"), "grid");
            var p2 = tbls[1].Element(Wn + "tblPr")!;
            AssertTrue(p2.Element(Wn + "tblBorders") is null && p2.Element(Wn + "tblLayout")!.Attribute(Wn + "type")!.Value == "fixed", "no borders, fixed");
        });
    }

    private static Document StyledDoc(out DocTable t)
    {
        var doc = new Document();
        var st = doc.Styles.AddTableStyle("LabGrid", "Lab Grid");
        st.TableFormat.Borders = TableBorders.Horizontal(BorderSpec.Single(0.5, "BFBFBF"));
        st.TableFormat.CellMargins = new CellMargins { LeftPt = 4, RightPt = 4 };
        st.CellFormat.VerticalAlignment = CellVerticalAlignment.Center;
        var head = st.Conditional(TableRegion.FirstRow);
        head.RunFormat = new TextFormat { Bold = true, ColorHex = "FFFFFF" };
        head.CellFormat.Shading = Shading.Fill("1F3864");
        head.TableBorders = new TableBorders { Bottom = BorderSpec.Single(1.5, "000000") };
        st.Conditional(TableRegion.OddRowBand).CellFormat.Shading = Shading.Fill("DEEAF6");
        var total = st.Conditional(TableRegion.LastRow);
        total.TableBorders = new TableBorders { Top = new BorderSpec { Style = BorderStyle.Double, WidthPt = 1.5 } };
        total.RunFormat = new TextFormat { Bold = true };
        st.Conditional(TableRegion.FirstColumn).ParagraphFormat.Alignment = ParagraphAlignment.Left;

        t = doc.AddTable(3);
        t.Borders = false;
        t.Format.StyleId = "LabGrid";
        t.Format.Look = TableLook.FirstRow | TableLook.LastRow | TableLook.NoVerticalBand;
        t.HeaderRow("Test", "Result", "Unit");
        t.AddRow("Hb", "13.2", "g/dL");
        t.AddRow("RBC", "4.8", "M/µL");
        t.AddRow("WBC", "7.1", "K/µL");
        t.AddRow("Total", "—", "");
        return doc;
    }

    private static void TableStyles(string outDir)
    {
        var a = Path.Combine(outDir, "tbl-style-a.docx");
        StyledDoc(out _).SaveTo(a);

        Check("table style written: tblPr / tcPr / tblStylePr (firstRow, band1Horz, lastRow, firstCol) in schema order", () =>
        {
            var styles = XElement.Parse(ReadPartXml(a, "word/styles.xml"));
            var st = styles.Elements(Wn + "style").Single(s => s.Attribute(Wn + "styleId")!.Value == "LabGrid");
            AssertTrue(st.Attribute(Wn + "type")!.Value == "table", "type");
            var tail = st.Elements().Select(e => e.Name.LocalName).SkipWhile(n => n != "tblPr").ToList();
            AssertTrue(tail.First() == "tblPr" && tail.Contains("tcPr") && tail.IndexOf("tcPr") < tail.IndexOf("tblStylePr"), $"order: {string.Join(",", tail)}");
            var types = st.Elements(Wn + "tblStylePr").Select(e => e.Attribute(Wn + "type")!.Value).ToList();
            AssertTrue(string.Join(",", types) == "firstRow,lastRow,firstCol,band1Horz", $"regions in ST_TblStyleOverrideType order: {string.Join(",", types)}");
            var fr = st.Elements(Wn + "tblStylePr").Single(e => e.Attribute(Wn + "type")!.Value == "firstRow");
            AssertTrue(fr.Element(Wn + "tcPr")!.Element(Wn + "shd")!.Attribute(Wn + "fill")!.Value == "1F3864", "firstRow shading");
        });
        Check("table styles round-trip: load → save reproduces styles.xml and document.xml exactly", () =>
        {
            var loaded = Document.Load(a);
            var st = loaded.Styles["LabGrid"];
            AssertTrue(st.Type == StyleType.Table && st.ConditionalFormats.Count == 4
                && st.Conditional(TableRegion.FirstRow).CellFormat.Shading!.FillHex == "1F3864", "model loaded");
            var b = Path.Combine(outDir, "tbl-style-b.docx");
            loaded.SaveTo(b);
            AssertTrue(ReadPartXml(a, "word/styles.xml") == ReadPartXml(b, "word/styles.xml"), "styles.xml differs");
            AssertTrue(ReadPartXml(a, "word/document.xml") == ReadPartXml(b, "word/document.xml"), "document.xml differs");
        });
    }

    private static void EffectiveInTables()
    {
        var doc = StyledDoc(out var t);
        TableCellFormat Cell(int r, int c) => doc.GetEffectiveCellFormat(new TableCellRef(t, r, c));

        Check("effective cell: header shading, odd-band shading alternates, last row double top border, style vAlign", () =>
        {
            AssertTrue(Cell(0, 1).Shading!.FillHex == "1F3864", "header shading");
            AssertTrue(Cell(1, 1).Shading?.FillHex == "DEEAF6" && Cell(2, 1).Shading is null && Cell(3, 1).Shading?.FillHex == "DEEAF6", "row banding (header excluded)");
            AssertTrue(Cell(4, 1).Shading is null, "total row not banded");
            AssertTrue(Cell(4, 0).Borders!.Top!.Style == BorderStyle.Double, "lastRow region top border");
            AssertTrue(Cell(0, 0).Borders!.Bottom!.WidthPt == 1.5 && Cell(0, 0).Borders!.Bottom!.ColorHex == "000000", "firstRow bottom border");
            AssertTrue(Cell(2, 1).Borders!.Top!.ColorHex == "BFBFBF" && Cell(2, 1).Borders!.Left!.Style == BorderStyle.None, "inside lines from the style");
            AssertTrue(Cell(2, 2).VerticalAlignment == CellVerticalAlignment.Center && Cell(2, 2).Margins!.LeftPt == 4, "style cell format + margins");
        });
        Check("effective cell: direct cell formatting overrides the table style", () =>
        {
            t.Rows[1].Cells[1].ShadeHex = "FFC7CE";
            t.Rows[1].Cells[1].Borders = new CellBorders { Top = BorderSpec.None };
            AssertTrue(Cell(1, 1).Shading!.FillHex == "FFC7CE" && Cell(1, 1).Borders!.Top!.Style == BorderStyle.None, "direct wins");
        });
        Check("effective run in a table: header region bold/white; table style is the lowest style level and joins the toggle XOR", () =>
        {
            var hp = t.Rows[0].Cells[0].Paragraphs[0];
            var f = doc.GetEffectiveRunFormat(hp, hp.Runs[0], new TableCellRef(t, 0, 0));
            AssertTrue(f.ColorHex == "FFFFFF", "header colour from the firstRow region");
            // HeaderRow writes direct bold, so it stays bold; a plain data cell in the total row gets style bold:
            var tp = t.Rows[4].Cells[1].Paragraphs[0];
            AssertTrue(doc.GetEffectiveRunFormat(tp, tp.Runs[0], new TableCellRef(t, 4, 1)).Bold, "lastRow region bold");
            var emph = doc.Styles.AddCharacterStyle("Emph");
            emph.RunFormat = new TextFormat { Bold = true };
            var run = new Run("x", TextFormat.None) { CharacterStyleId = "Emph" };
            AssertTrue(!doc.GetEffectiveRunFormat(tp, run, new TableCellRef(t, 4, 1)).Bold, "table-style bold XOR character-style bold = not bold");
            var dp = t.Rows[2].Cells[1].Paragraphs[0];
            AssertTrue(!doc.GetEffectiveRunFormat(dp, dp.Runs[0], new TableCellRef(t, 2, 1)).Bold, "data cell not bold");
        });
        Check("effective table format merges the style chain with direct formatting", () =>
        {
            var tf = doc.GetEffectiveTableFormat(t);
            AssertTrue(tf.Borders!.Top!.ColorHex == "BFBFBF" && tf.CellMargins!.RightPt == 4 && tf.Look!.Value.HasFlag(TableLook.LastRow), "merged");
        });
    }

    private static void Hostile(string outDir)
    {
        var path = Path.Combine(outDir, "tbl-hostile.docx");
        var d = new Document();
        d.AddParagraph("base");
        d.SaveTo(path);
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Update))
        {
            var e = zip.GetEntry("word/document.xml")!;
            string xml;
            using (var r = new StreamReader(e.Open())) xml = r.ReadToEnd();
            e.Delete();
            xml = xml.Replace("<w:body>", "<w:body>" +
                "<w:tbl><w:tblGrid><w:gridCol w:w=\"1000\"/></w:tblGrid>" +
                "<w:tr><w:trPr><w:gridBefore w:val=\"999999\"/></w:trPr><w:tc><w:tcPr><w:gridSpan w:val=\"2147483647\"/><w:tcW w:w=\"1e309\" w:type=\"dxa\"/></w:tcPr><w:p><w:r><w:t>wide</w:t></w:r></w:p></w:tc></w:tr>" +
                "<w:tr><w:tc><w:p/></w:tc><w:tc><w:p><w:r><w:t>extra</w:t></w:r></w:p></w:tc><w:tc><w:tcPr><w:tcW w:w=\"50%\" w:type=\"pct\"/></w:tcPr><w:p/></w:tc></w:tr>" +
                "</w:tbl>");
            using var w = new StreamWriter(zip.CreateEntry("word/document.xml").Open(), new UTF8Encoding(false));
            w.Write(xml);
        }
        Check("hostile tables: huge gridSpan/gridBefore clamp, rows wider than the grid widen it, odd widths ignored; no throw", () =>
        {
            var loaded = Document.Load(path);
            var t = (DocTable)loaded.Blocks[0];
            AssertTrue(t.Columns <= 63, $"columns {t.Columns}");
            AssertTrue(t.GetText().Contains("wide") && t.GetText().Contains("extra"), "content kept");
            AssertTrue(t.Rows[1].Cells[2].Width == TableWidth.Percent(50), "50% width read");
            loaded.SaveTo(new MemoryStream());
        });
    }

    /// <summary>Writes a one-paragraph docx and replaces its body start with <paramref name="bodyXml"/>.</summary>
    private static string Inject(string outDir, string name, string bodyXml)
    {
        var path = Path.Combine(outDir, name);
        var d = new Document();
        d.AddParagraph("end");
        d.SaveTo(path);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Update);
        var e = zip.GetEntry("word/document.xml")!;
        string xml;
        using (var r = new StreamReader(e.Open())) xml = r.ReadToEnd();
        e.Delete();
        xml = xml.Replace("<w:body>", "<w:body>" + bodyXml);
        using var w = new StreamWriter(zip.CreateEntry("word/document.xml").Open(), new UTF8Encoding(false));
        w.Write(xml);
        return path;
    }

    private static string Tc(string text, string tcPr = "") => $"<w:tc>{tcPr}<w:p><w:r><w:t>{text}</w:t></w:r></w:p></w:tc>";

    private static void ReviewRegressions(string outDir)
    {
        Check("rows and cells wrapped in w:sdt / w:customXml are read (content kept)", () =>
        {
            var path = Inject(outDir, "tbl-sdt.docx",
                "<w:tbl><w:tblGrid><w:gridCol w:w=\"2000\"/><w:gridCol w:w=\"2000\"/></w:tblGrid>" +
                "<w:sdt><w:sdtPr><w:alias w:val=\"rows\"/></w:sdtPr><w:sdtContent><w:tr>" + Tc("r1a") + Tc("r1b") + "</w:tr>" +
                "<w:customXml w:element=\"x\"><w:tr>" + Tc("r2a") + Tc("r2b") + "</w:tr></w:customXml></w:sdtContent></w:sdt>" +
                "<w:tr>" + Tc("r3a") + "<w:sdt><w:sdtPr/><w:sdtContent>" + Tc("r3b") + "</w:sdtContent></w:sdt></w:tr></w:tbl>");
            var t = (DocTable)Document.Load(path).Blocks[0];
            AssertTrue(t.Rows.Count == 3 && t.Columns == 2, $"rows {t.Rows.Count} cols {t.Columns}");
            AssertTrue(t.Rows[1].Cells[1].GetText() == "r2b" && t.Rows[2].Cells[1].GetText() == "r3b", "wrapped content");
        });
        Check("column cap: a 5000-column grid loads as 63 columns, 20000 sparse rows stay cheap", () =>
        {
            var sb = new StringBuilder("<w:tbl><w:tblGrid>");
            for (int i = 0; i < 5000; i++) sb.Append("<w:gridCol w:w=\"10\"/>");
            sb.Append("</w:tblGrid>");
            for (int i = 0; i < 20000; i++) sb.Append("<w:tr><w:tc><w:p/></w:tc></w:tr>");
            sb.Append("</w:tbl>");
            var path = Inject(outDir, "tbl-wide.docx", sb.ToString());
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var doc = Document.Load(path);
            var t = (DocTable)doc.Blocks[0];
            AssertTrue(t.Columns == 63 && t.Rows.Count == 20000, $"cols {t.Columns}");
            var ms = new MemoryStream();
            doc.SaveTo(ms);
            AssertTrue(sw.Elapsed.TotalSeconds < 20, $"took {sw.Elapsed}");
            var t2 = (DocTable)Document.Load(new MemoryStream(ms.ToArray())).Blocks[0];
            AssertTrue(t2.Rows[5].Cells.Count == 63, "row width");
        });
        Check("huge gridSpan cannot widen the declared grid; ragged rows stay ragged on save", () =>
        {
            var path = Inject(outDir, "tbl-ragged.docx",
                "<w:tbl><w:tblGrid><w:gridCol w:w=\"1000\"/><w:gridCol w:w=\"1000\"/><w:gridCol w:w=\"1000\"/></w:tblGrid>" +
                "<w:tr>" + Tc("a", "<w:tcPr><w:gridSpan w:val=\"999\"/></w:tcPr>") + "</w:tr>" +
                "<w:tr>" + Tc("b") + "</w:tr>" +
                "<w:tr>" + Tc("c") + Tc("d") + Tc("e") + "</w:tr></w:tbl>");
            var doc = Document.Load(path);
            var t = (DocTable)doc.Blocks[0];
            AssertTrue(t.Columns == 3 && t.Rows[0].Cells[0].ColumnSpan == 3, $"cols {t.Columns} span {t.Rows[0].Cells[0].ColumnSpan}");
            var outPath = Path.Combine(outDir, "tbl-ragged-out.docx");
            doc.SaveTo(outPath);
            var tbl = XElement.Parse(ReadPartXml(outPath, "word/document.xml")).Descendants(Wn + "tbl").First();
            var counts = tbl.Elements(Wn + "tr").Select(r => r.Elements(Wn + "tc").Count()).ToList();
            AssertTrue(string.Join(",", counts) == "1,1,3", $"tc per row {string.Join(",", counts)}");
            // Editing a placeholder cell makes it real.
            t.Rows[1].Cells[2].SetText("filled");
            doc.SaveTo(outPath);
            tbl = XElement.Parse(ReadPartXml(outPath, "word/document.xml")).Descendants(Wn + "tbl").First();
            AssertTrue(tbl.Elements(Wn + "tr").ElementAt(1).Elements(Wn + "tc").Count() == 3, "filled placeholder written");
        });
        Check("hMerge, wBefore/wAfter and row tblCellSpacing round-trip exactly", () =>
        {
            var path = Inject(outDir, "tbl-legacy-merge.docx",
                "<w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"1000\"/><w:gridCol w:w=\"1000\"/><w:gridCol w:w=\"1000\"/></w:tblGrid>" +
                "<w:tr><w:trPr><w:gridBefore w:val=\"1\"/><w:wBefore w:w=\"1000\" w:type=\"dxa\"/><w:tblCellSpacing w:w=\"20\" w:type=\"dxa\"/></w:trPr>" +
                Tc("m", "<w:tcPr><w:hMerge w:val=\"restart\"/></w:tcPr>") + Tc("", "<w:tcPr><w:hMerge/></w:tcPr>") + "</w:tr>" +
                "<w:tr><w:trPr><w:gridAfter w:val=\"1\"/><w:wAfter w:w=\"50\" w:type=\"pct\"/></w:trPr>" + Tc("x") + Tc("y") + "</w:tr></w:tbl>");
            var a = Document.Load(path);
            var t = (DocTable)a.Blocks[0];
            AssertTrue(t.Rows[0].Cells[1].HorizontalMerge == HorizontalMerge.Restart && t.Rows[0].Cells[2].HorizontalMerge == HorizontalMerge.Continue, "hMerge");
            AssertTrue(t.Rows[0].WidthBefore == TableWidth.Points(50) && t.Rows[0].CellSpacingPt == 1 && t.Rows[1].WidthAfter == TableWidth.Percent(1), "row props");
            var p1 = Path.Combine(outDir, "tbl-legacy-merge-1.docx");
            var p2 = Path.Combine(outDir, "tbl-legacy-merge-2.docx");
            a.SaveTo(p1);
            Document.Load(p1).SaveTo(p2);
            AssertTrue(ReadPartXml(p1, "word/document.xml") == ReadPartXml(p2, "word/document.xml"), "stable round-trip");
            var tr = XElement.Parse(ReadPartXml(p1, "word/document.xml")).Descendants(Wn + "trPr").First();
            AssertTrue(string.Join(",", tr.Elements().Select(e => e.Name.LocalName)) == "gridBefore,wBefore,tblCellSpacing", "trPr order");
        });
        Check("a table cannot be nested inside itself (directly or through another table)", () =>
        {
            var t1 = new DocTable(1);
            var c1 = t1.AddRow("a").Cells[0];
            AssertThrows<ArgumentException>(() => c1.AddTable(t1), "direct");
            var t2 = new DocTable(1);
            var c2 = t2.AddRow("b").Cells[0];
            c2.AddTable(t1);
            AssertThrows<ArgumentException>(() => c1.AddTable(t2), "through t2");
        });
        Check("nested table stays after its paragraph when paragraphs are inserted before it", () =>
        {
            var t = new DocTable(1);
            var cell = t.AddRow("first").Cells[0];
            var inner = cell.AddTable(1);
            cell.Paragraphs.Add(new Paragraph().Text("after"));
            cell.Paragraphs.Insert(0, new Paragraph().Text("zero"));
            var order = cell.Blocks.Select(b => b is DocTable ? "T" : ((Paragraph)b).GetText()).ToList();
            AssertTrue(string.Join("|", order) == "zero|first|T|after", string.Join("|", order));
        });
        Check("no tblLook anywhere: Word's default look (header row, first column, row bands) applies", () =>
        {
            var doc = StyledDoc(out var t);
            t.Format.Look = null;
            var c = doc.GetEffectiveCellFormat(new TableCellRef(t, 0, 1));
            AssertTrue(c.Shading?.FillHex == "1F3864", "header region on by default");
            AssertTrue(doc.GetEffectiveCellFormat(new TableCellRef(t, 4, 1)).Borders!.Top!.Style != BorderStyle.Double, "lastRow off by default");
        });
        Check("conditional order: first column beats header row; the nwCell corner beats both", () =>
        {
            var doc = new Document();
            var st = doc.Styles.AddTableStyle("Ord", "Ord");
            st.Conditional(TableRegion.FirstRow).CellFormat.Shading = Shading.Fill("AAAAAA");
            st.Conditional(TableRegion.FirstColumn).CellFormat.Shading = Shading.Fill("BBBBBB");
            var t = doc.AddTable(2);
            t.Format.StyleId = "Ord";
            t.Format.Look = TableLook.FirstRow | TableLook.FirstColumn;
            t.AddRow("a", "b"); t.AddRow("c", "d");
            AssertTrue(doc.GetEffectiveCellFormat(new TableCellRef(t, 0, 0)).Shading!.FillHex == "BBBBBB", "firstCol after firstRow");
            st.Conditional(TableRegion.TopLeftCell).CellFormat.Shading = Shading.Fill("CCCCCC");
            AssertTrue(doc.GetEffectiveCellFormat(new TableCellRef(t, 0, 0)).Shading!.FillHex == "CCCCCC", "corner last");
            AssertTrue(doc.GetEffectiveCellFormat(new TableCellRef(t, 0, 1)).Shading!.FillHex == "AAAAAA", "header elsewhere");
        });
        Check("borders apply level by level: a region's table border beats the whole-style cell border", () =>
        {
            var doc = new Document();
            var st = doc.Styles.AddTableStyle("Lv", "Lv");
            st.CellFormat.Borders = new CellBorders { Top = BorderSpec.Single(0.5, "111111") };
            st.Conditional(TableRegion.FirstRow).TableBorders = new TableBorders { Top = BorderSpec.Single(2, "222222") };
            var t = doc.AddTable(2);
            t.Borders = false;
            t.Format.StyleId = "Lv";
            t.Format.Look = TableLook.FirstRow;
            t.AddRow("a", "b"); t.AddRow("c", "d");
            AssertTrue(doc.GetEffectiveCellFormat(new TableCellRef(t, 0, 1)).Borders!.Top!.ColorHex == "222222", "region later");
            AssertTrue(doc.GetEffectiveCellFormat(new TableCellRef(t, 1, 1)).Borders!.Top!.ColorHex == "111111", "style cell border elsewhere");
        });
        Check("conditional table properties (shading, alignment, indent, spacing) and style tblLook/tblW round-trip", () =>
        {
            var doc = new Document();
            var st = doc.Styles.AddTableStyle("Full", "Full");
            st.TableFormat.Look = TableLook.FirstRow;
            st.TableFormat.Width = TableWidth.Percent(100);
            var cf = st.Conditional(TableRegion.FirstRow);
            cf.TableFormat.Shading = Shading.Fill("FFEEDD");
            cf.TableFormat.Alignment = TableAlignment.Center;
            cf.TableFormat.IndentPt = 3;
            cf.TableFormat.CellSpacingPt = 1;
            doc.AddTable(1).AddRow("x");
            var a = Path.Combine(outDir, "tbl-style-full.docx");
            doc.SaveTo(a);
            var back = Document.Load(a).Styles["Full"];
            var bcf = back.Conditional(TableRegion.FirstRow).TableFormat;
            AssertTrue(bcf.Shading!.FillHex == "FFEEDD" && bcf.Alignment == TableAlignment.Center && bcf.IndentPt == 3 && bcf.CellSpacingPt == 1, "conditional tblPr");
            AssertTrue(back.TableFormat.Look == TableLook.FirstRow && back.TableFormat.Width == TableWidth.Percent(100), "style tblLook/tblW");
        });
        Check("TableCellRef out of range throws ArgumentOutOfRangeException", () =>
        {
            var doc = StyledDoc(out var t);
            var p = t.Rows[0].Cells[0].Paragraphs[0];
            AssertThrows<ArgumentOutOfRangeException>(() => doc.GetEffectiveRunFormat(p, p.Runs[0], new TableCellRef(t, 99, 0)), "row");
            AssertThrows<ArgumentOutOfRangeException>(() => doc.GetEffectiveParagraphFormat(p, new TableCellRef(t, 0, -1)), "col");
            AssertThrows<ArgumentOutOfRangeException>(() => doc.GetEffectiveCellFormat(new TableCellRef(t, 0, 3)), "cell");
        });
    }

    private static void AssertOrder(XElement parent, string[] order)
    {
        var names = parent.Elements().Select(e => e.Name.LocalName).ToList();
        var idx = names.Select(n => Array.IndexOf(order, n)).ToList();
        AssertTrue(idx.All(i => i >= 0), $"unknown child in {parent.Name.LocalName}: [{string.Join(",", names)}]");
        for (int i = 1; i < idx.Count; i++)
            AssertTrue(idx[i] > idx[i - 1], $"{parent.Name.LocalName} out of order: [{string.Join(",", names)}]");
    }
}
