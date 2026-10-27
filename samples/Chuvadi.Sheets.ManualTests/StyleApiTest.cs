using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.ManualTests;

/// <summary>
/// Read-side style API (Sheets 1.2.0): merged ranges, row/column dimensions, the workbook
/// style table and resolved cell styles. Fixtures are assembled from raw SpreadsheetML parts
/// shaped like Excel's own output, so the reader is tested against files Chuvadi did not write.
/// </summary>
internal static class StyleApiTest
{
    public static void Run(string outputDir)
    {
        Console.WriteLine("[StyleApiTest] Read-side style API...");
        Console.WriteLine();

        var pass = 0;
        var fail = 0;

        void Check(string label, Action body)
        {
            try
            {
                body();
                Console.WriteLine($"    PASS: {label}");
                pass++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    FAIL: {label}");
                Console.WriteLine($"          {ex.GetType().Name}: {ex.Message}");
                fail++;
            }
        }

        // ---- SHEETS-03: merged ranges --------------------------------------------------

        var mergeFixture = Path.Combine(outputDir, "styleapi_merges.xlsx");
        Fixture.Write(mergeFixture, sheetXml:
            "<sheetData>" +
            "<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Title</t></is></c></row>" +
            "<row r=\"3\"><c r=\"B3\"><v>7</v></c></row>" +
            "</sheetData>" +
            "<mergeCells count=\"3\">" +
            "<mergeCell ref=\"A1:D1\"/><mergeCell ref=\"B3:C5\"/><mergeCell ref=\"AA10:AB20\"/>" +
            "</mergeCells>");

        Check("merges: loaded in file order as A1 range strings", () =>
        {
            var wb = Workbook.Load(mergeFixture);
            var m = wb.Sheets[0].MergeRanges;
            Expect.Seq(new[] { "A1:D1", "B3:C5", "AA10:AB20" }, m, "MergeRanges");
        });

        Check("merges: cell values unaffected by merge collection", () =>
        {
            var wb = Workbook.Load(mergeFixture);
            var s = wb.Sheets[0];
            Expect.Eq("Title", s.Cell("A1").Value, "A1");
            Expect.Eq(7.0, s.Cell("B3").Value, "B3");
        });

        Check("merges: sheet with no mergeCells has empty list", () =>
        {
            var p = Path.Combine(outputDir, "styleapi_nomerge.xlsx");
            Fixture.Write(p, sheetXml: "<sheetData><row r=\"1\"><c r=\"A1\"><v>1</v></c></row></sheetData>");
            Expect.Eq(0, Workbook.Load(p).Sheets[0].MergeRanges.Count, "count");
        });

        Check("merges: Chuvadi-written workbook round-trips its merges", () =>
        {
            var p = Path.Combine(outputDir, "styleapi_merge_roundtrip.xlsx");
            var wb = new Workbook();
            var s = wb.AddSheet("M");
            s.Cell("A1").Value = "x";
            s.MergeCells("A1:B2");
            s.Range("D4:F4").Merge();
            wb.SaveTo(p);
            var back = Workbook.Load(p).Sheets[0].MergeRanges;
            Expect.Seq(new[] { "A1:B2", "D4:F4" }, back, "round-trip");
        });

        Check("merges: streaming reader still yields the same rows", () =>
        {
            using var r = XlsxReader.Open(mergeFixture, new XlsxReaderOptions { TreatFirstRowAsHeaders = false });
            var rows = r.Sheet(1).Rows.Select(x => x.RowNumber).ToArray();
            Expect.Seq(new[] { 1, 3 }, rows, "row numbers");
        });

        // ---- SHEETS-02: sheet format, column spans, row attributes ------------------------

        var dimFixture = Path.Combine(outputDir, "styleapi_dimensions.xlsx");
        Fixture.Write(dimFixture, sheetXml:
            "<sheetFormatPr baseColWidth=\"10\" defaultColWidth=\"11.5\" defaultRowHeight=\"16\" customHeight=\"1\" " +
            "outlineLevelRow=\"1\" outlineLevelCol=\"2\"/>" +
            "<cols>" +
            "<col min=\"1\" max=\"1\" width=\"25.5\" customWidth=\"1\"/>" +
            "<col min=\"2\" max=\"3\" width=\"9.1640625\" hidden=\"1\" customWidth=\"1\" outlineLevel=\"2\" collapsed=\"1\"/>" +
            "<col min=\"5\" max=\"16384\" width=\"12\" style=\"3\" bestFit=\"1\"/>" +
            "</cols>" +
            "<sheetData>" +
            "<row r=\"1\" ht=\"30\" customHeight=\"1\"><c r=\"A1\"><v>1</v></c></row>" +
            "<row r=\"2\" hidden=\"1\" outlineLevel=\"1\"><c r=\"A2\"><v>2</v></c></row>" +
            "<row r=\"3\" spans=\"1:1\" xmlns:x14ac=\"http://schemas.microsoft.com/office/spreadsheetml/2009/9/ac\" x14ac:dyDescent=\"0.2\"><c r=\"A3\"><v>3</v></c></row>" +
            "<row r=\"4\" s=\"2\" customFormat=\"1\" thickBot=\"1\" ph=\"1\"/>" +
            "</sheetData>");

        Check("dimensions: sheetFormatPr values", () =>
        {
            var f = Workbook.Load(dimFixture).Sheets[0].Format;
            Expect.Eq(16.0, f.DefaultRowHeight, "DefaultRowHeight");
            Expect.Eq(11.5, f.DefaultColumnWidth, "DefaultColumnWidth");
            Expect.Eq(10, f.BaseColumnWidth, "BaseColumnWidth");
            Expect.True(f.CustomHeight, "CustomHeight");
            Expect.Eq(1, f.OutlineLevelRow, "OutlineLevelRow");
            Expect.Eq(2, f.OutlineLevelColumn, "OutlineLevelColumn");
            Expect.True(!f.ZeroHeight && !f.ThickTop && !f.ThickBottom, "unset flags false");
        });

        Check("dimensions: column spans kept as stored, not expanded", () =>
        {
            var s = Workbook.Load(dimFixture).Sheets[0];
            Expect.Eq(3, s.ColumnRanges.Count, "span count");
            Expect.Eq(5, s.ColumnRanges[2].Min, "span3.Min");
            Expect.Eq(16384, s.ColumnRanges[2].Max, "span3.Max");
        });

        Check("dimensions: per-column lookup through spans", () =>
        {
            var s = Workbook.Load(dimFixture).Sheets[0];
            Expect.Eq(25.5, s.Columns[1].Width, "A width");
            Expect.True(s.Columns[1].CustomWidth, "A customWidth");
            Expect.True(s.Columns[3].Hidden, "C hidden (span 2..3)");
            Expect.Eq(2, s.Columns[3].OutlineLevel, "C outline");
            Expect.True(s.Columns[3].Collapsed, "C collapsed");
            Expect.Eq(0.0, s.Columns[4].Width, "D undefined width");
            Expect.True(s.Columns[4].Range is null, "D has no span");
            Expect.Eq(-1, s.Columns[4].StyleIndex, "D style");
            Expect.Eq(12.0, s.Columns[100].Width, "CV width (wide span)");
            Expect.Eq(3, s.Columns[100].StyleIndex, "CV style");
            Expect.True(s.Columns[100].BestFit, "CV bestFit");
            Expect.True(!s.Columns[100].CustomWidth, "CV not custom");
        });

        Check("dimensions: row attributes", () =>
        {
            var s = Workbook.Load(dimFixture).Sheets[0];
            Expect.Eq(30.0, s.Rows[1].Height, "row1 height");
            Expect.True(s.Rows[1].CustomHeight, "row1 customHeight");
            Expect.True(s.Rows[2].Hidden, "row2 hidden");
            Expect.Eq(1, s.Rows[2].OutlineLevel, "row2 outline");
            Expect.True(!s.Rows[3].IsDefined, "row3 has only r/spans/dyDescent");
            Expect.Eq(2, s.Rows[4].StyleIndex, "row4 style");
            Expect.True(s.Rows[4].CustomFormat, "row4 customFormat");
            Expect.True(s.Rows[4].ThickBottom, "row4 thickBot");
            Expect.True(s.Rows[4].Phonetic, "row4 ph");
            Expect.Eq(0.0, s.Rows[99].Height, "row99 default");
            Expect.Seq(new[] { 1, 2, 4 }, s.DefinedRows.Select(r => r.Number), "DefinedRows");
        });

        Check("dimensions: in-memory sheet reports Excel defaults", () =>
        {
            var s = new Workbook().AddSheet("N");
            Expect.Eq(15.0, s.Format.DefaultRowHeight, "DefaultRowHeight");
            Expect.True(s.Format.DefaultColumnWidth is null, "DefaultColumnWidth null");
            Expect.Eq(8, s.Format.BaseColumnWidth, "BaseColumnWidth");
            Expect.Eq(0, s.ColumnRanges.Count, "no spans");
            Expect.True(!s.Rows[1].IsDefined, "no rows");
        });

        Check("dimensions: re-save keeps spans and splits around a set width", () =>
        {
            var p = Path.Combine(outputDir, "styleapi_dimensions_resave.xlsx");
            var wb = Workbook.Load(dimFixture);
            wb.Sheets[0].Columns[6].Width = 30;   // inside the 5..16384 span
            wb.SaveTo(p);
            var back = Workbook.Load(p).Sheets[0];
            var spans = back.ColumnRanges.Select(r => $"{r.Min}-{r.Max}:{r.Width}").ToArray();
            Expect.Seq(new[] { "1-1:25.5", "2-3:9.1640625", "5-5:12", "6-6:30", "7-16384:12" }, spans, "written spans");
        });

        Check("dimensions: model-built column widths behave as before", () =>
        {
            var p = Path.Combine(outputDir, "styleapi_dimensions_model.xlsx");
            var wb = new Workbook();
            var s = wb.AddSheet("W");
            s.Cell("A1").Value = "x";
            s.Columns[3].Width = 20;
            s.Columns[1].Width = 8;
            wb.SaveTo(p);
            var back = Workbook.Load(p).Sheets[0];
            Expect.Eq(20.0, back.Columns[3].Width, "C");
            Expect.Eq(8.0, back.Columns[1].Width, "A");
            Expect.Seq(new[] { 1, 3 }, back.ColumnRanges.Select(r => r.Min), "sorted spans");
        });

        // ---- Style table: styles.xml + theme, raw ------------------------------------------

        var styleFixture = Path.Combine(outputDir, "styleapi_styletable.xlsx");
        Fixture.Write(styleFixture, sheetXml: SampleSheetXml, stylesXml: SampleStylesXml, themeXml: OfficeThemeXml);

        Check("styletable: list counts", () =>
        {
            var t = Workbook.Load(styleFixture).StyleTable;
            Expect.Eq(3, t.Fonts.Count, "fonts");
            Expect.Eq(4, t.Fills.Count, "fills");
            Expect.Eq(2, t.Borders.Count, "borders");
            Expect.Eq(2, t.CellStyleFormats.Count, "cellStyleXfs");
            Expect.Eq(4, t.CellFormats.Count, "cellXfs");
            Expect.Eq(3, t.NamedStyles.Count, "cellStyles");
            Expect.Eq(2, t.DifferentialFormats.Count, "dxfs");
        });

        Check("styletable: fonts as stored", () =>
        {
            var t = Workbook.Load(styleFixture).StyleTable;
            var f0 = t.Fonts[0];
            Expect.Eq("Calibri", f0.Name, "f0 name");
            Expect.Eq(11.0, f0.Size, "f0 size");
            Expect.Eq(FontScheme.Minor, f0.Scheme, "f0 scheme");
            Expect.Eq(2, f0.Family, "f0 family");
            Expect.Eq(ColorKind.Theme, f0.Color!.Kind, "f0 color kind");
            Expect.Eq(1, f0.Color.Theme, "f0 theme");
            var f1 = t.Fonts[1];
            Expect.True(f1.Bold && !f1.Italic && f1.Strikethrough, "f1 b, i val=0, strike");
            Expect.Eq(UnderlineStyle.Double, f1.Underline, "f1 underline");
            Expect.Eq(FontVerticalAlign.Superscript, f1.VerticalAlign, "f1 vertAlign");
            Expect.Eq("Nirmala UI", f1.Name, "f1 name");
            Expect.Eq("FF1F4E79", f1.Color!.Rgb, "f1 rgb raw");
            Expect.Eq(1, f1.Charset, "f1 charset");
            Expect.Eq(UnderlineStyle.Single, t.Fonts[2].Underline, "f2 <u/> = single");
        });

        Check("styletable: pattern and gradient fills", () =>
        {
            var t = Workbook.Load(styleFixture).StyleTable;
            Expect.Eq(FillPattern.None, t.Fills[0].Pattern, "fill0");
            Expect.Eq(FillPattern.Gray125, t.Fills[1].Pattern, "fill1");
            Expect.Eq(FillPattern.Solid, t.Fills[2].Pattern, "fill2");
            Expect.Eq(4, t.Fills[2].ForegroundColor!.Theme, "fill2 fg theme");
            Expect.True(Math.Abs(t.Fills[2].ForegroundColor!.Tint - 0.79998168889431442) < 1e-12, "fill2 tint");
            Expect.Eq(64, t.Fills[2].BackgroundColor!.Indexed, "fill2 bg indexed");
            var g = t.Fills[3].Gradient!;
            Expect.True(t.Fills[3].Pattern is null, "gradient fill has no pattern");
            Expect.Eq(GradientType.Linear, g.Type, "gradient type");
            Expect.Eq(90.0, g.Degree, "gradient degree");
            Expect.Seq(new[] { 0.0, 1.0 }, g.Stops.Select(s => s.Position), "stop positions");
        });

        Check("styletable: borders incl. new styles and diagonal", () =>
        {
            var b = Workbook.Load(styleFixture).StyleTable.Borders[1];
            Expect.Eq(BorderStyle.Thin, b.Left.Style, "left");
            Expect.Eq(64, b.Left.Color!.Indexed, "left color");
            Expect.Eq(BorderStyle.MediumDashDot, b.Right.Style, "right");
            Expect.Eq(ColorKind.Auto, b.Right.Color!.Kind, "right auto");
            Expect.Eq(BorderStyle.Double, b.Top.Style, "top");
            Expect.Eq(BorderStyle.SlantDashDot, b.Bottom.Style, "bottom");
            Expect.True(b.Bottom.Color is null, "bottom no color");
            Expect.Eq(BorderStyle.Hair, b.Diagonal.Style, "diagonal");
            Expect.True(b.DiagonalUp && !b.DiagonalDown && b.Outline, "diagonal flags, outline default");
            Expect.Eq(BorderStyle.None, Workbook.Load(styleFixture).StyleTable.Borders[0].Left.Style, "empty <left/>");
        });

        Check("styletable: cell formats, apply flags, alignment, protection", () =>
        {
            var t = Workbook.Load(styleFixture).StyleTable;
            var x0 = t.CellFormats[0];
            Expect.True(x0.ApplyFont is null && x0.Alignment is null && x0.Protection is null, "x0 bare");
            Expect.Eq(0, x0.ParentFormatIndex, "x0 xfId");
            var x1 = t.CellFormats[1];
            Expect.Eq(164, x1.NumberFormatId, "x1 numFmt");
            Expect.Eq(1, x1.FontId, "x1 font");
            Expect.Eq(true, x1.ApplyFont, "x1 applyFont");
            Expect.True(x1.QuotePrefix, "x1 quotePrefix");
            var a = x1.Alignment!;
            Expect.Eq(HorizontalAlign.CenterContinuous, a.Horizontal, "h");
            Expect.Eq(VerticalAlign.Top, a.Vertical, "v");
            Expect.True(a.WrapText && a.ShrinkToFit, "wrap, shrink");
            Expect.Eq(2, a.Indent, "indent");
            Expect.Eq(45, a.TextRotation, "rotation");
            Expect.Eq(ReadingOrder.RightToLeft, a.ReadingOrder, "readingOrder");
            Expect.True(!x1.Protection!.Locked && x1.Protection.Hidden, "protection");
            Expect.Eq(1, t.CellFormats[2].ParentFormatIndex, "x2 xfId");
            Expect.Eq(-1, t.CellStyleFormats[1].ParentFormatIndex, "style xf has no parent");
            Expect.Eq(false, t.CellStyleFormats[1].ApplyFill, "style xf applyFill=0");
        });

        Check("styletable: named styles and dxfs", () =>
        {
            var t = Workbook.Load(styleFixture).StyleTable;
            Expect.Eq("Heading 1", t.NamedStyles[1].Name, "name");
            Expect.Eq(1, t.NamedStyles[1].FormatIndex, "xfId");
            Expect.Eq(16, t.NamedStyles[1].BuiltinId, "builtinId");
            Expect.True(t.NamedStyles[2].BuiltinId is null, "custom style has no builtinId");
            var d0 = t.DifferentialFormats[0];
            Expect.True(d0.Font!.Bold, "dxf0 bold");
            Expect.True(d0.Fill!.Pattern is null, "dxf0 patternType absent");
            Expect.Eq("FFFFC7CE", d0.Fill.BackgroundColor!.Rgb, "dxf0 bg");
            Expect.True(d0.Border is null && d0.NumberFormatId is null, "dxf0 has only font+fill");
            var d1 = t.DifferentialFormats[1];
            Expect.Eq(166, d1.NumberFormatId, "dxf1 numFmtId");
            Expect.Eq("0.0%", d1.NumberFormatCode, "dxf1 code");
            Expect.Eq(BorderStyle.Thin, d1.Border!.Left.Style, "dxf1 border");
        });

        Check("styletable: number formats (custom, ECMA built-in, locale-dependent)", () =>
        {
            var t = Workbook.Load(styleFixture).StyleTable;
            Expect.Eq("dd/mm/yyyy;@", t.GetNumberFormatCode(164), "164 custom");
            Expect.Eq("\"₹\"#,##0.00", t.GetNumberFormatCode(165), "165 rupee (non-ASCII)");
            Expect.Eq("mm-dd-yy", t.GetNumberFormatCode(14), "14 built-in");
            Expect.Eq("#,##0 ;(#,##0)", t.GetNumberFormatCode(37), "37 per ECMA");
            Expect.True(t.GetNumberFormatCode(44) is null, "44 has no fixed code");
            Expect.True(t.IsLocaleDependentNumberFormat(44), "44 locale-dependent");
            Expect.True(!t.IsLocaleDependentNumberFormat(14) && !t.IsLocaleDependentNumberFormat(164), "14/164 not");
        });

        Check("styletable: theme colours and fonts", () =>
        {
            var th = Workbook.Load(styleFixture).StyleTable.Theme;
            Expect.True(th.IsPresent, "present");
            Expect.Eq("Office Theme", th.Name, "name");
            Expect.Eq("000000", th.SchemeColors[0], "dk1 from sysClr lastClr");
            Expect.Eq("FFFFFF", th.SchemeColors[1], "lt1");
            Expect.Eq("4472C4", th.SchemeColors[4], "accent1");
            Expect.Eq("954F72", th.SchemeColors[11], "folHlink");
            Expect.Eq("Calibri Light", th.MajorFont.Latin, "major");
            Expect.Eq("Calibri", th.MinorFont.Latin, "minor");
            Expect.Eq("Latha", th.MinorFont.Scripts["Taml"], "Tamil script font");
            Expect.Eq("Kartika", th.MinorFont.Scripts["Mlym"], "Malayalam script font");
        });

        Check("styletable: colour resolution (theme swap, tint, indexed, rgb, auto)", () =>
        {
            var t = Workbook.Load(styleFixture).StyleTable;
            Expect.Eq("000000", t.ResolveColor(t.Fonts[0].Color), "theme 1 = dk1 (text)");
            // Excel's palette swatches come from DrawingML lumMod/lumOff with Excel's own rounding;
            // the spec's tint formula lands within a couple of units per channel of them.
            Expect.Near("D9E1F2", t.ResolveColor(t.Fills[2].ForegroundColor), 2, "accent1 lighter 80% (Excel palette value)");
            Expect.Near("C65911", t.ResolveColor(t.Borders[1].Top.Color), 2, "accent2 darker 25% (Excel palette value)");
            Expect.Eq("FFFFFF", t.ResolveColor(t.Fills[3].Gradient!.Stops[0].Color), "theme 0 = lt1 (background)");
            Expect.Eq("000000", t.ResolveColor(t.Fills[2].BackgroundColor), "indexed 64 = system fg");
            Expect.Eq("FF0000", t.ResolveColor(t.Fonts[2].Color), "indexed 10 = default palette red");
            Expect.Eq("1F4E79", t.ResolveColor(t.Fonts[1].Color), "rgb ARGB → RGB");
            Expect.True(t.ResolveColor(t.Borders[1].Right.Color) is null, "auto → null");
            Expect.True(t.ResolveColor(null) is null, "null → null");
        });

        Check("styletable: custom indexed palette overrides default", () =>
        {
            var p = Path.Combine(outputDir, "styleapi_indexed.xlsx");
            Fixture.Write(p, sheetXml: SampleSheetXml, stylesXml:
                "<fonts count=\"1\"><font><color indexed=\"2\"/></font></fonts>" +
                "<cellXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellXfs>" +
                "<colors><indexedColors>" + string.Concat(Enumerable.Range(0, 64).Select(i =>
                    $"<rgbColor rgb=\"FF{(i == 2 ? "123456" : "000000")}\"/>")) +
                "</indexedColors></colors>");
            var t = Workbook.Load(p).StyleTable;
            Expect.True(t.HasCustomIndexedColors, "custom flag");
            Expect.Eq("123456", t.ResolveColor(t.Fonts[0].Color), "custom indexed 2");
            Expect.True(!t.Theme.IsPresent, "no theme part");
        });

        Check("styletable: streaming reader exposes the same table; in-memory workbook is empty", () =>
        {
            using var r = XlsxReader.Open(styleFixture);
            Expect.Eq(4, r.StyleTable.CellFormats.Count, "reader cellXfs");
            Expect.True(ReferenceEquals(CellStyleTable.Empty, new Workbook().StyleTable), "Empty for new workbook");
            Expect.Eq(0, CellStyleTable.Empty.CellFormats.Count, "Empty has no formats");
        });

        Check("styletable: date detection still driven by cellXfs", () =>
        {
            using var r = XlsxReader.Open(styleFixture, new XlsxReaderOptions { TreatFirstRowAsHeaders = false });
            var row = r.Sheet(1).Rows.First();
            Expect.True(row[0] is DateTime, "A1 (xf1, custom dd/mm/yyyy) is a date");
            Expect.True(row[1] is DateTime, "B1 (xf2, built-in 14) is a date");
            Expect.True(row[2] is double, "C1 (xf3, numFmt 44) is a number");
        });

        Check("styletable: malformed theme ignored unless StrictXml", () =>
        {
            var p = Path.Combine(outputDir, "styleapi_badtheme.xlsx");
            Fixture.Write(p, sheetXml: SampleSheetXml, stylesXml: SampleStylesXml, themeXml: "<a:theme xmlns:a=\"x\"><broken>");
            Expect.True(!Workbook.Load(p).StyleTable.Theme.IsPresent, "lenient: no theme");
            var threw = false;
            try { using var r = XlsxReader.Open(p, new XlsxReaderOptions { StrictXml = true }); }
            catch (System.Xml.XmlException) { threw = true; }
            Expect.True(threw, "strict: throws");
        });

        // ---- SHEETS-01: resolved Cell.Style + StyleIndex ------------------------------------

        Check("resolve: full-featured cellXf flattens every component", () =>
        {
            var c = Workbook.Load(styleFixture).Sheets[0].Cell("A1");
            Expect.Eq(1, c.StyleIndex, "StyleIndex");
            var s = c.Style!.Value;
            Expect.Eq("Nirmala UI", s.FontName, "font");
            Expect.Eq(14.0, s.FontSize, "size");
            Expect.True(s.Bold && !s.Italic && s.Strikethrough, "b / i / strike");
            Expect.True(s.Underline, "Underline flag");
            Expect.Eq(UnderlineStyle.Double, s.UnderlineKind, "underline kind");
            Expect.Eq(FontVerticalAlign.Superscript, s.FontVerticalPosition, "superscript");
            Expect.Eq("1F4E79", s.FontColor, "font colour");
            Expect.Eq(FillPattern.Solid, s.FillPattern, "fill pattern");
            Expect.Near("D9E1F2", s.FillColor, 2, "solid fill = tinted accent1");
            Expect.Eq(s.FillColor, s.FillForegroundColor, "fg = solid colour");
            Expect.Eq("000000", s.FillBackgroundColor, "bg indexed 64");
            Expect.Eq(new Border(BorderStyle.Thin, "000000"), s.BorderLeft, "left");
            Expect.Eq(new Border(BorderStyle.MediumDashDot, null), s.BorderRight, "right (auto colour)");
            Expect.Eq(BorderStyle.Double, s.BorderTop.Style, "top");
            Expect.Near("C65911", s.BorderTop.Color, 2, "top tinted accent2");
            Expect.Eq(new Border(BorderStyle.SlantDashDot, null), s.BorderBottom, "bottom");
            Expect.Eq(BorderStyle.Hair, s.BorderDiagonal.Style, "diagonal");
            Expect.True(s.DiagonalUp && !s.DiagonalDown, "diagonal direction");
            Expect.Eq("dd/mm/yyyy;@", s.NumberFormat, "number format");
            Expect.Eq(164, s.NumberFormatId, "number format id");
            Expect.Eq(HorizontalAlign.CenterContinuous, s.HAlign, "h");
            Expect.Eq(VerticalAlign.Top, s.VAlign, "v");
            Expect.True(s.WrapText && s.ShrinkToFit, "wrap/shrink");
            Expect.Eq(2, s.Indent, "indent");
            Expect.Eq(45, s.TextRotation, "rotation");
            Expect.Eq(ReadingOrder.RightToLeft, s.ReadingOrder, "readingOrder");
            Expect.Eq(false, s.Locked, "locked");
            Expect.True(s.FormulaHidden && s.QuotePrefix, "hidden / quotePrefix");
            Expect.Eq("Normal", s.StyleName, "named style");
        });

        Check("resolve: gradient fill, built-in date format, named-style parent", () =>
        {
            var s = Workbook.Load(styleFixture).Sheets[0].Cell("B1").Style!.Value;
            Expect.True(s.FillColor is null && s.FillPattern is null, "gradient: no solid colour");
            Expect.Eq(2, s.FillGradient!.Stops.Count, "gradient stops");
            Expect.Eq("mm-dd-yy", s.NumberFormat, "built-in 14");
            Expect.Eq("Arial", s.FontName, "font");
            Expect.Eq("FF0000", s.FontColor, "indexed 10");
            Expect.True(s.Underline && s.UnderlineKind == UnderlineStyle.Single, "<u/> single");
            Expect.Eq("Heading 1", s.StyleName, "xfId 1 → Heading 1");
        });

        Check("resolve: locale-dependent format keeps id, defaults fill in", () =>
        {
            var s = Workbook.Load(styleFixture).Sheets[0].Cell("C1").Style!.Value;
            Expect.True(s.NumberFormat is null, "44: no code");
            Expect.Eq(44, s.NumberFormatId, "44: id kept");
            Expect.Eq("Calibri", s.FontName, "font");
            Expect.Eq("000000", s.FontColor, "theme 1 text colour");
            Expect.Eq(VerticalAlign.Bottom, s.VAlign, "Excel default vertical = bottom");
            Expect.Eq(true, s.Locked, "default locked");
            Expect.Eq(FillPattern.None, s.FillPattern, "no fill");
        });

        var inheritFixture = Path.Combine(outputDir, "styleapi_inherit.xlsx");
        Fixture.Write(inheritFixture, sheetXml:
            "<cols><col min=\"6\" max=\"6\" width=\"10\" style=\"1\" customWidth=\"1\"/></cols>" +
            "<sheetData>" +
            "<row r=\"1\"><c r=\"A1\" s=\"1\"><v>1</v></c><c r=\"B1\" s=\"2\"><v>2</v></c><c r=\"C1\" s=\"3\"><v>3</v></c>" +
            "<c r=\"D1\" s=\"0\"><v>4</v></c><c r=\"E1\"><v>5</v></c><c r=\"G1\" s=\"1\"/><c r=\"H1\" s=\"0\"/></row>" +
            "<row r=\"2\" s=\"2\" customFormat=\"1\"><c r=\"A2\"><v>6</v></c></row>" +
            "<row r=\"3\" s=\"1\"><c r=\"A3\"><v>7</v></c></row>" +
            "</sheetData>",
            stylesXml:
            "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"16\"/><name val=\"Cambria\"/></font></fonts>" +
            "<fills count=\"1\"><fill><patternFill patternType=\"none\"/></fill></fills>" +
            "<borders count=\"1\"><border/></borders>" +
            "<cellStyleXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/>" +
            "<xf numFmtId=\"10\" fontId=\"1\" fillId=\"0\" borderId=\"0\"><alignment horizontal=\"center\"/></xf></cellStyleXfs>" +
            "<cellXfs count=\"4\">" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"1\" applyFont=\"0\" applyNumberFormat=\"0\"/>" +
            "<xf numFmtId=\"2\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"1\" applyFont=\"1\" applyNumberFormat=\"1\"/>" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"1\" applyAlignment=\"0\"><alignment horizontal=\"right\"/></xf>" +
            "</cellXfs>" +
            "<cellStyles count=\"2\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/><cellStyle name=\"Title\" xfId=\"1\" builtinId=\"15\"/></cellStyles>");

        Check("resolve: apply=\"0\" takes the parent style's component", () =>
        {
            var s = Workbook.Load(inheritFixture).Sheets[0].Cell("A1").Style!.Value;
            Expect.Eq("Cambria", s.FontName, "font from parent");
            Expect.True(s.Bold, "bold from parent");
            Expect.Eq("0.00%", s.NumberFormat, "numFmt from parent");
            Expect.Eq(HorizontalAlign.Center, s.HAlign, "alignment absent → parent");
            Expect.Eq("Title", s.StyleName, "style name");
        });

        Check("resolve: apply=\"1\" keeps the cell format's own component", () =>
        {
            var s = Workbook.Load(inheritFixture).Sheets[0].Cell("B1").Style!.Value;
            Expect.Eq("Calibri", s.FontName, "own font");
            Expect.Eq("0.00", s.NumberFormat, "own numFmt");
            var c = Workbook.Load(inheritFixture).Sheets[0].Cell("C1").Style!.Value;
            Expect.Eq(HorizontalAlign.Center, c.HAlign, "applyAlignment=0 ignores own 'right'");
        });

        Check("resolve: xf 0 and absent s= leave Style null; DefaultCellStyle covers them", () =>
        {
            var wb = Workbook.Load(inheritFixture);
            var d1 = wb.Sheets[0].Cell("D1");
            Expect.Eq(0, d1.StyleIndex, "s=0 index");
            Expect.True(d1.Style is null, "s=0 style null");
            var e1 = wb.Sheets[0].Cell("E1");
            Expect.Eq(-1, e1.StyleIndex, "no s= → -1");
            Expect.True(e1.Style is null, "no s= style null");
            Expect.Eq("Calibri", wb.StyleTable.DefaultCellStyle!.Value.FontName, "default style");
        });

        Check("resolve: styled empty cells only with IncludeStyledEmptyCells", () =>
        {
            var off = Workbook.Load(inheritFixture).Sheets[0];
            Expect.True(!off.PopulatedCells.Any(c => c.Address == "G1"), "default: G1 not loaded");
            var on = Workbook.Load(inheritFixture, null, new XlsxReaderOptions { IncludeStyledEmptyCells = true }).Sheets[0];
            var g1 = on.PopulatedCells.Single(c => c.Address == "G1");
            Expect.True(g1.Value is null, "G1 has no value");
            Expect.Eq("Cambria", g1.Style!.Value.FontName, "G1 style");
            Expect.True(!on.PopulatedCells.Any(c => c.Address == "H1"), "s=0 empty cell never materialized");
            Expect.Eq(off.PopulatedCells.Count() + 1, on.PopulatedCells.Count(), "exactly one extra cell");
        });

        Check("resolve: column and row styles", () =>
        {
            var s = Workbook.Load(inheritFixture).Sheets[0];
            Expect.Eq("Cambria", s.Columns[6].Style!.Value.FontName, "column F style");
            Expect.True(s.Columns[5].Style is null, "column E none");
            Expect.Eq("0.00", s.Rows[2].Style!.Value.NumberFormat, "row 2 customFormat");
            Expect.Eq(1, s.Rows[3].StyleIndex, "row 3 raw s=");
            Expect.True(s.Rows[3].Style is null, "row 3 without customFormat → no style");
        });

        Check("resolve: setting Style in code clears StyleIndex", () =>
        {
            var c = Workbook.Load(inheritFixture).Sheets[0].Cell("A1");
            c.Style = new CellStyleBuilder().Bold().Build();
            Expect.Eq(-1, c.StyleIndex, "index cleared");
        });

        Check("resolve: streaming reader exposes per-cell style indexes", () =>
        {
            using var r = XlsxReader.Open(inheritFixture, new XlsxReaderOptions { TreatFirstRowAsHeaders = false });
            var row = r.Sheet(1).Rows.First();
            Expect.Seq(new[] { 1, 2, 3, 0, -1, -1, 1, 0 }, Enumerable.Range(0, 8).Select(row.GetStyleIndex), "A1..H1");
            Expect.Eq(-1, row.GetStyleIndex(99), "out of range");
            Expect.Eq("Cambria", r.StyleTable.ResolveCellFormat(1)!.Value.FontName, "resolve via table");
            Expect.True(r.StyleTable.ResolveCellFormat(99) is null, "bad index → null");
        });

        Check("resolve: load → save keeps the writer-supported style properties", () =>
        {
            var p = Path.Combine(outputDir, "styleapi_resave.xlsx");
            Workbook.Load(styleFixture).SaveTo(p);
            var s = Workbook.Load(p).Sheets[0].Cell("A1").Style!.Value;
            Expect.Eq("Nirmala UI", s.FontName, "font");
            Expect.True(s.Bold && s.Underline, "bold / underline");
            Expect.Eq("1F4E79", s.FontColor, "font colour");
            Expect.Near("D9E1F2", s.FillColor, 2, "solid fill");
            Expect.Eq(BorderStyle.MediumDashDot, s.BorderRight.Style, "new border style survives save");
            Expect.Eq(BorderStyle.SlantDashDot, s.BorderBottom.Style, "new border style survives save");
            Expect.Eq("dd/mm/yyyy;@", s.NumberFormat, "number format");
            Expect.Eq(HorizontalAlign.CenterContinuous, s.HAlign, "alignment");
        });

        Console.WriteLine();
        Console.WriteLine($"[StyleApiTest] {pass} passed, {fail} failed.");
        Console.WriteLine();
        if (fail > 0) throw new Exception($"StyleApiTest: {fail} check(s) failed.");
    }

    // ---- Fixture XML -------------------------------------------------------------------

    /// <summary>Row 1: A1 xf1 (custom date), B1 xf2 (built-in 14), C1 xf3 (numFmt 44).</summary>
    private const string SampleSheetXml =
        "<sheetData><row r=\"1\">" +
        "<c r=\"A1\" s=\"1\"><v>45000</v></c>" +
        "<c r=\"B1\" s=\"2\"><v>45001</v></c>" +
        "<c r=\"C1\" s=\"3\"><v>1234.5</v></c>" +
        "</row></sheetData>";

    /// <summary>styles.xml body shaped like Excel's output, exercising every list.</summary>
    private const string SampleStylesXml =
        "<numFmts count=\"2\">" +
        "<numFmt numFmtId=\"164\" formatCode=\"dd/mm/yyyy;@\"/>" +
        "<numFmt numFmtId=\"165\" formatCode=\"&quot;₹&quot;#,##0.00\"/>" +
        "</numFmts>" +
        "<fonts count=\"3\">" +
        "<font><sz val=\"11\"/><color theme=\"1\"/><name val=\"Calibri\"/><family val=\"2\"/><scheme val=\"minor\"/></font>" +
        "<font><b/><i val=\"0\"/><strike/><u val=\"double\"/><vertAlign val=\"superscript\"/><sz val=\"14\"/>" +
        "<color rgb=\"FF1F4E79\"/><name val=\"Nirmala UI\"/><charset val=\"1\"/></font>" +
        "<font><u/><sz val=\"10\"/><color indexed=\"10\"/><name val=\"Arial\"/></font>" +
        "</fonts>" +
        "<fills count=\"4\">" +
        "<fill><patternFill patternType=\"none\"/></fill>" +
        "<fill><patternFill patternType=\"gray125\"/></fill>" +
        "<fill><patternFill patternType=\"solid\"><fgColor theme=\"4\" tint=\"0.79998168889431442\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
        "<fill><gradientFill degree=\"90\"><stop position=\"0\"><color theme=\"0\"/></stop><stop position=\"1\"><color theme=\"4\"/></stop></gradientFill></fill>" +
        "</fills>" +
        "<borders count=\"2\">" +
        "<border><left/><right/><top/><bottom/><diagonal/></border>" +
        "<border diagonalUp=\"1\">" +
        "<left style=\"thin\"><color indexed=\"64\"/></left>" +
        "<right style=\"mediumDashDot\"><color auto=\"1\"/></right>" +
        "<top style=\"double\"><color theme=\"5\" tint=\"-0.249977111117893\"/></top>" +
        "<bottom style=\"slantDashDot\"/><diagonal style=\"hair\"/></border>" +
        "</borders>" +
        "<cellStyleXfs count=\"2\">" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/>" +
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" applyNumberFormat=\"0\" applyFill=\"0\" applyBorder=\"0\" applyAlignment=\"0\" applyProtection=\"0\"/>" +
        "</cellStyleXfs>" +
        "<cellXfs count=\"4\">" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
        "<xf numFmtId=\"164\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\" " +
        "applyBorder=\"1\" applyAlignment=\"1\" applyProtection=\"1\" quotePrefix=\"1\">" +
        "<alignment horizontal=\"centerContinuous\" vertical=\"top\" wrapText=\"1\" indent=\"2\" textRotation=\"45\" readingOrder=\"2\" shrinkToFit=\"1\"/>" +
        "<protection locked=\"0\" hidden=\"1\"/></xf>" +
        "<xf numFmtId=\"14\" fontId=\"2\" fillId=\"3\" borderId=\"0\" xfId=\"1\" applyNumberFormat=\"1\"/>" +
        "<xf numFmtId=\"44\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "</cellXfs>" +
        "<cellStyles count=\"3\">" +
        "<cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/>" +
        "<cellStyle name=\"Heading 1\" xfId=\"1\" builtinId=\"16\"/>" +
        "<cellStyle name=\"My Style\" xfId=\"1\"/>" +
        "</cellStyles>" +
        "<dxfs count=\"2\">" +
        "<dxf><font><b/><color rgb=\"FF9C0006\"/></font><fill><patternFill><bgColor rgb=\"FFFFC7CE\"/></patternFill></fill></dxf>" +
        "<dxf><numFmt numFmtId=\"166\" formatCode=\"0.0%\"/><border><left style=\"thin\"/></border></dxf>" +
        "</dxfs>";

    /// <summary>The colour and font schemes of Excel's stock "Office Theme" (2013+).</summary>
    private const string OfficeThemeXml =
        "<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"Office Theme\">" +
        "<a:themeElements><a:clrScheme name=\"Office\">" +
        "<a:dk1><a:sysClr val=\"windowText\" lastClr=\"000000\"/></a:dk1>" +
        "<a:lt1><a:sysClr val=\"window\" lastClr=\"FFFFFF\"/></a:lt1>" +
        "<a:dk2><a:srgbClr val=\"44546A\"/></a:dk2><a:lt2><a:srgbClr val=\"E7E6E6\"/></a:lt2>" +
        "<a:accent1><a:srgbClr val=\"4472C4\"/></a:accent1><a:accent2><a:srgbClr val=\"ED7D31\"/></a:accent2>" +
        "<a:accent3><a:srgbClr val=\"A5A5A5\"/></a:accent3><a:accent4><a:srgbClr val=\"FFC000\"/></a:accent4>" +
        "<a:accent5><a:srgbClr val=\"5B9BD5\"/></a:accent5><a:accent6><a:srgbClr val=\"70AD47\"/></a:accent6>" +
        "<a:hlink><a:srgbClr val=\"0563C1\"/></a:hlink><a:folHlink><a:srgbClr val=\"954F72\"/></a:folHlink>" +
        "</a:clrScheme>" +
        "<a:fontScheme name=\"Office\">" +
        "<a:majorFont><a:latin typeface=\"Calibri Light\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:majorFont>" +
        "<a:minorFont><a:latin typeface=\"Calibri\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/>" +
        "<a:font script=\"Taml\" typeface=\"Latha\"/><a:font script=\"Mlym\" typeface=\"Kartika\"/></a:minorFont>" +
        "</a:fontScheme></a:themeElements></a:theme>";

    /// <summary>Tiny assertion helpers that throw with a readable message.</summary>
    internal static class Expect
    {
        public static void Eq<T>(T expected, object? actual, string what)
        {
            if (actual is not T t || !EqualityComparer<T>.Default.Equals(expected, t))
                throw new Exception($"{what}: expected <{expected}> but got <{actual ?? "null"}>");
        }

        /// <summary>RGB hex values equal within <paramref name="tolerance"/> per channel.</summary>
        public static void Near(string expectedHex, string? actualHex, int tolerance, string what)
        {
            if (actualHex is null || actualHex.Length != 6)
                throw new Exception($"{what}: expected ~<{expectedHex}> but got <{actualHex ?? "null"}>");
            for (int i = 0; i < 6; i += 2)
            {
                int e = Convert.ToInt32(expectedHex.Substring(i, 2), 16);
                int a = Convert.ToInt32(actualHex.Substring(i, 2), 16);
                if (Math.Abs(e - a) > tolerance)
                    throw new Exception($"{what}: expected ~<{expectedHex}> (±{tolerance}) but got <{actualHex}>");
            }
        }

        public static void True(bool condition, string what)
        {
            if (!condition) throw new Exception($"{what}: expected true");
        }

        public static void Seq<T>(IEnumerable<T> expected, IEnumerable<T> actual, string what)
        {
            var e = expected.ToArray();
            var a = actual.ToArray();
            if (!e.SequenceEqual(a))
                throw new Exception($"{what}: expected [{string.Join(", ", e)}] but got [{string.Join(", ", a)}]");
        }
    }

    /// <summary>
    /// Assembles a minimal but well-formed xlsx from raw part XML, in the shape Excel writes.
    /// Only the parts supplied are included (styles and theme are optional).
    /// </summary>
    internal static class Fixture
    {
        private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        public static void Write(string path, string sheetXml, string? stylesXml = null,
                                 string? themeXml = null, string sheetName = "Sheet1")
        {
            if (File.Exists(path)) File.Delete(path);
            using var fs = new FileStream(path, FileMode.CreateNew);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

            var overrides = new StringBuilder();
            overrides.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
            overrides.Append("<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            if (stylesXml is not null)
                overrides.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
            if (themeXml is not null)
                overrides.Append("<Override PartName=\"/xl/theme/theme1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.theme+xml\"/>");

            Add(zip, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                overrides + "</Types>");

            Add(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                "</Relationships>");

            Add(zip, "xl/workbook.xml",
                $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                $"<workbook xmlns=\"{Main}\" xmlns:r=\"{R}\"><sheets>" +
                $"<sheet name=\"{sheetName}\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");

            var rels = new StringBuilder();
            rels.Append("<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>");
            if (stylesXml is not null)
                rels.Append("<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
            if (themeXml is not null)
                rels.Append("<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme\" Target=\"theme/theme1.xml\"/>");
            Add(zip, "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                rels + "</Relationships>");

            Add(zip, "xl/worksheets/sheet1.xml",
                $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                $"<worksheet xmlns=\"{Main}\" xmlns:r=\"{R}\">{sheetXml}</worksheet>");

            if (stylesXml is not null)
                Add(zip, "xl/styles.xml",
                    $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    $"<styleSheet xmlns=\"{Main}\">{stylesXml}</styleSheet>");

            if (themeXml is not null)
                Add(zip, "xl/theme/theme1.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" + themeXml);
        }

        private static void Add(ZipArchive zip, string name, string content)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var s = entry.Open();
            var bytes = new UTF8Encoding(false).GetBytes(content);
            s.Write(bytes, 0, bytes.Length);
        }
    }
}
