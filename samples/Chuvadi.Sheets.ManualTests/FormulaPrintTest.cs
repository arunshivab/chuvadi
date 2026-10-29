using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using Chuvadi.Sheets.Excel;
using Chuvadi.Sheets.Internal;

namespace Chuvadi.Sheets.ManualTests;

/// <summary>
/// Sheets 1.5.0: formulas with cached values on read and write (option A), page setup on the
/// streaming reader and on write, defined names on read and write, a zip that lies about its
/// sizes, and cancellation while an encrypted file is read.
/// </summary>
internal static class FormulaPrintTest
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static void Run(string outputDir)
    {
        Console.WriteLine("[FormulaPrintTest] 1.5.0 formulas, page setup, defined names...");
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

        static T Throws<T>(Action a, string what) where T : Exception
        {
            try { a(); }
            catch (T ex) { return ex; }
            catch (Exception ex) { throw new Exception($"{what}: expected {typeof(T).Name}, got {ex.GetType().Name}: {ex.Message}"); }
            throw new Exception($"{what}: expected {typeof(T).Name}, nothing thrown");
        }

        // ---- Formulas: reference shifting --------------------------------------------------

        Check("formula text: shared-formula reference shifting", () =>
        {
            Eq("B2+C$2*$C4", FormulaText.Shift("A1+B$2*$C3", 1, 1), "mixed refs");
            Eq("SUM(A3:A12)", FormulaText.Shift("SUM(A1:A10)", 2, 0), "range");
            Eq("Sheet2!A2+'My Sheet'!B3", FormulaText.Shift("Sheet2!A1+'My Sheet'!B2", 1, 0), "sheet refs");
            Eq("\"A1\"&A2", FormulaText.Shift("\"A1\"&A1", 1, 0), "string literal untouched");
            Eq("LOG10(A2)", FormulaText.Shift("LOG10(A1)", 1, 0), "function name untouched");
            Eq("SUM(B:B)+SUM(2:2)", FormulaText.Shift("SUM(A:A)+SUM(1:1)", 1, 1), "whole column/row");
            Eq("SUM($A:$A)+SUM($1:$1)", FormulaText.Shift("SUM($A:$A)+SUM($1:$1)", 3, 3), "absolute whole column/row");
            Eq("#REF!", FormulaText.Shift("A1", -1, 0), "off the top");
            Eq("#REF!+1", FormulaText.Shift("XFD1+1", 0, 1), "off the right");
            Eq("Table1[Amount]*A2", FormulaText.Shift("Table1[Amount]*A1", 1, 0), "structured ref untouched");
            Eq("1E+5*A2+0.5", FormulaText.Shift("1E+5*A1+0.5", 1, 0), "number literals");
            Eq("TaxRate*B3", FormulaText.Shift("TaxRate*B2", 1, 0), "defined name untouched");
            Eq("$A$1", FormulaText.Shift("$A$1", 5, 5), "absolute");
            Eq("IF(A2>0,\"ok\",#N/A)", FormulaText.Shift("IF(A1>0,\"ok\",#N/A)", 1, 0), "error literal");
            True(FormulaText.HasBracketReference("[1]Sheet1!A1") && FormulaText.HasBracketReference("SUM(Table1[x])")
                 && FormulaText.HasBracketReference("'C:\\x\\[B.xlsx]S'!A1") && !FormulaText.HasBracketReference("\"[x]\"&A1"),
                 "bracket references detected; brackets in strings ignored");
        });

        // ---- Formulas: Excel-shaped fixture -----------------------------------------------

        var fPath = Path.Combine(outputDir, "f150_formulas.xlsx");
        ReadSafetyTest.Package.Write(fPath, new (string, string, string?)[]
        {
            ("Calc",
             "<sheetData>" +
             "<row r=\"1\"><c r=\"A1\"><v>5</v></c></row>" +
             "<row r=\"2\"><c r=\"A2\"><v>1</v></c><c r=\"B2\"><v>2</v></c>" +
             "<c r=\"C2\"><f t=\"shared\" ref=\"C2:C4\" si=\"0\">A2*B2</f><v>2</v></c>" +
             "<c r=\"D2\"><f t=\"array\" ref=\"D2:D3\">A2:A3*2</f><v>2</v></c>" +
             "<c r=\"E2\"><f>SUM(A2:A4)</f><v>6</v></c>" +
             "<c r=\"F2\" t=\"str\"><f>\"x\"&amp;A2</f><v>x1</v></c>" +
             "<c r=\"G2\" t=\"e\"><f>1/0</f><v>#DIV/0!</v></c>" +
             "<c r=\"H2\"><f t=\"dataTable\" ref=\"H2:H3\" dt2D=\"0\" dtr=\"0\" r1=\"A1\"/><v>5</v></c>" +
             "<c r=\"I2\" t=\"b\"><f>A2=1</f><v>1</v></c>" +
             "<c r=\"J2\"><f>[1]Other!A1</f><v>42</v></c>" +
             "<c r=\"K2\"><f>SUM(Table1[Amount])</f><v>99</v></c>" +
             "<c r=\"L2\"><f>Ext*2</f><v>84</v></c>" +
             "</row>" +
             "<row r=\"3\"><c r=\"A3\"><v>2</v></c><c r=\"B3\"><v>3</v></c>" +
             "<c r=\"C3\"><f t=\"shared\" si=\"0\"/><v>6</v></c><c r=\"D3\"><v>4</v></c></row>" +
             "<row r=\"4\"><c r=\"A4\"><v>3</v></c><c r=\"B4\"><v>4</v></c>" +
             "<c r=\"C4\"><f t=\"shared\" si=\"0\"/></c></row>" +
             "</sheetData>", null),
        }, workbookExtra:
            "<definedNames>" +
            "<definedName name=\"TaxRate\" comment=\"GST\">0.18</definedName>" +
            "<definedName name=\"Local\" localSheetId=\"0\" hidden=\"1\">Calc!$A$1</definedName>" +
            "<definedName name=\"Ext\">[1]Other!$A$1</definedName>" +
            "<definedName name=\"Tbl\">Table1[Amount]</definedName>" +
            "<definedName name=\"_xlnm.Print_Area\" localSheetId=\"0\">Calc!$A$1:$K$4</definedName>" +
            "</definedNames>");

        Check("formulas (streaming): text, shared children, array, data table, cached values", () =>
        {
            using var r = XlsxReader.Open(fPath, new XlsxReaderOptions { TreatFirstRowAsHeaders = false });
            var rows = new List<(string?[] F, object?[] V, bool[] H, CellFormula?[] I)>();
            foreach (var row in r.Sheet("Calc").Rows)
            {
                int n = row.Count;
                var f = new string?[n]; var v = new object?[n]; var h = new bool[n]; var info = new CellFormula?[n];
                for (int c = 0; c < n; c++) { f[c] = row.GetFormula(c); v[c] = row[c]; h[c] = row.HasCachedValue(c); info[c] = row.GetFormulaInfo(c); }
                rows.Add((f, v, h, info));
            }
            var r2 = rows[1]; var r3 = rows[2]; var r4 = rows[3];
            Eq("A2*B2", r2.F[2], "shared master text");
            Eq(FormulaKind.Shared, r2.I[2]!.Kind, "shared kind"); Eq("C2:C4", r2.I[2]!.Range, "shared range");
            Eq("A3*B3", r3.F[2], "shared child C3 shifted");
            Eq("A4*B4", r4.F[2], "shared child C4 shifted");
            Eq(6.0, r3.V[2], "C3 cached"); True(r3.H[2], "C3 has cached");
            True(r4.V[2] is null && !r4.H[2], "C4 has no cached value");
            Eq("A2:A3*2", r2.F[3], "array text"); Eq(FormulaKind.Array, r2.I[3]!.Kind, "array kind"); Eq("D2:D3", r2.I[3]!.Range, "array range");
            True(r3.F[3] is null && (double)r3.V[3]! == 4, "array result cell is a value");
            Eq("SUM(A2:A4)", r2.F[4], "normal"); Eq(FormulaKind.Normal, r2.I[4]!.Kind, "normal kind");
            Eq("\"x\"&A2", r2.F[5], "string formula"); Eq("x1", r2.V[5], "string cached");
            Eq("#DIV/0!", r2.V[6], "error cached");
            True(r2.F[7] is null && r2.I[7]!.Kind == FormulaKind.DataTable && r2.I[7]!.Range == "H2:H3", "data table: no text, kind and range");
            Eq(true, r2.V[8], "bool cached");
            True(rows[0].F[0] is null && !rows[0].H[0], "constant has no formula");
        });

        Check("formulas (model): Formula, CachedValue, HasCachedValue, ArrayFormulaRange, DisplayText", () =>
        {
            var s = Workbook.Load(fPath)["Calc"];
            Eq("A3*B3", s.Cell("C3").Formula, "shared child expanded");
            Eq(6.0, s.Cell("C3").CachedValue, "cached"); Eq(6.0, s.Cell("C3").Value, "Value returns the cached result");
            True(s.Cell("C3").HasCachedValue && !s.Cell("C4").HasCachedValue, "HasCachedValue");
            Eq("A4*B4", s.Cell("C4").Formula, "uncached formula still loads");
            Eq("D2:D3", s.Cell("D2").ArrayFormulaRange, "array range");
            True(s.Cell("D3").Formula is null && (double)s.Cell("D3").Value! == 4, "array result cell is a value");
            True(s.Cell("H2").Formula is null && (double)s.Cell("H2").Value! == 5, "data table cell loads its value only");
            Eq("6", s.Cell("C3").DisplayText, "display text of a formula cell");
            Eq("[1]Other!A1", s.Cell("J2").Formula, "external formula text kept in the model");
        });

        Check("formulas (model): Value/Formula/CachedValue rules", () =>
        {
            var c = new Workbook().AddSheet("S").Cell("A1");
            Throws<InvalidOperationException>(() => c.CachedValue = 1, "cached value on a constant");
            Throws<InvalidOperationException>(() => c.ArrayFormulaRange = "A1:A2", "array range on a constant");
            c.Formula = "1+1"; c.CachedValue = 2.0;
            True(c.HasCachedValue && (double)c.Value! == 2, "set formula then cached");
            c.Formula = "2+2";
            True(!c.HasCachedValue, "new formula clears the cached result");
            c.Value = 7;
            True(c.Formula is null && c.CachedValue is null && (int)c.Value! == 7, "value clears the formula");
        });

        var wPath = Path.Combine(outputDir, "f150_formulas_written.xlsx");
        Check("formulas (writer): cached values by type, array formula, styles, calcPr", () =>
        {
            using (var w = XlsxWriter.Create(wPath, new XlsxWriterOptions { FullCalculationOnLoad = true }))
            {
                var sh = w.AddSheet("W");
                sh.WriteRow(1, 2, 3);
                sh.WriteRow(rb => rb
                    .Formula("SUM(A1:C1)", 6)
                    .Formula("\"n=\"&A1", "n=1")
                    .Formula("A1>0", true)
                    .Formula("1/0", "#DIV/0!")
                    .Formula("DATE(2026,10,28)", new DateTime(2026, 10, 28))
                    .ArrayFormula("F2:F3", "A1:A2*2", 2)
                    .Formula("A1+1")
                    .Formula("A1*10", 10, new CellStyle { NumberFormat = "0.00" }));
                sh.WriteRow(rb => rb.Empty().Empty().Empty().Empty().Empty().Cell(4));
                w.Save();
            }
            var xml = PartText(wPath, "xl/worksheets/sheet1.xml");
            True(xml.Contains("<c r=\"A2\"><f>SUM(A1:C1)</f><v>6</v></c>"), "number cached");
            True(xml.Contains("<c r=\"B2\" t=\"str\"><f>\"n=\"&amp;A1</f><v>n=1</v></c>"), "string cached");
            True(xml.Contains("<c r=\"C2\" t=\"b\"><f>A1&gt;0</f><v>1</v></c>"), "bool cached");
            True(xml.Contains("<c r=\"D2\" t=\"e\"><f>1/0</f><v>#DIV/0!</v></c>"), "error cached");
            True(System.Text.RegularExpressions.Regex.IsMatch(xml, "<c r=\"E2\" s=\"\\d+\"><f>DATE\\(2026,10,28\\)</f><v>46323</v></c>"), "date cached with default date style");
            True(xml.Contains("<c r=\"F2\"><f t=\"array\" ref=\"F2:F3\">A1:A2*2</f><v>2</v></c>"), "array formula");
            True(xml.Contains("<c r=\"G2\"><f>A1+1</f></c>"), "no cached value = no <v>, as before");
            True(PartText(wPath, "xl/workbook.xml").Contains("<calcPr fullCalcOnLoad=\"1\" />"), "calcPr");

            using var r = XlsxReader.Open(wPath);
            foreach (var row in r.Sheet("W").Rows)
            {
                if (row.RowNumber != 2) continue;
                Eq("SUM(A1:C1)", row.GetFormula(0), "read back formula");
                Eq(6.0, row[0], "read back cached");
                Eq("n=1", row[1], "read back string"); Eq(true, row[2], "read back bool"); Eq("#DIV/0!", row[3], "read back error");
                Eq(new DateTime(2026, 10, 28), row[4], "read back date");
                Eq("F2:F3", row.GetFormulaInfo(5)!.Range, "read back array range");
                True(!row.HasCachedValue(6), "uncached stays uncached");
                Eq("10.00", row.GetDisplayString(7), "styled cached display");
            }
        });

        Check("formulas (model): load → save keeps formulas and cached results; external/table refs keep values", () =>
        {
            var saved = Path.Combine(outputDir, "f150_formulas_resaved.xlsx");
            Workbook.Load(fPath).SaveTo(saved);
            var s = Workbook.Load(saved)["Calc"];
            Eq("A3*B3", s.Cell("C3").Formula, "shared child saved as its own formula");
            Eq(6.0, s.Cell("C3").CachedValue, "cached result saved");
            True(s.Cell("C4").Formula == "A4*B4" && !s.Cell("C4").HasCachedValue, "uncached formula saved (was dropped before 1.5.0)");
            Eq("D2:D3", s.Cell("D2").ArrayFormulaRange, "array formula saved");
            Eq("\"x\"&A2", s.Cell("F2").Formula, "string formula"); Eq("x1", s.Cell("F2").Value, "string result");
            Eq("#DIV/0!", s.Cell("G2").Value, "error result");
            True(s.Cell("J2").Formula is null && (double)s.Cell("J2").Value! == 42, "external-workbook formula saved as its value");
            True(s.Cell("K2").Formula is null && (double)s.Cell("K2").Value! == 99, "table-reference formula saved as its value");
            True(s.Cell("L2").Formula is null && (double)s.Cell("L2").Value! == 84, "formula using a name the model could not keep saved as its value");
            True(!FormulaText.UsesAnyName("TaxRate*2", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Ext" }), "UsesAnyName negative");
            True(FormulaText.UsesAnyName("SUM(ext)+1", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Ext" })
                 && !FormulaText.UsesAnyName("\"Ext\"&EXT(1)", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Ext" }), "UsesAnyName: case, strings, functions");
            var x = PartText(saved, "xl/worksheets/sheet1.xml");
            True(x.Contains("<c r=\"G2\" t=\"e\"><f>1/0</f><v>#DIV/0!</v></c>"), "error written as an error");
        });

        Check("formulas: LibreOffice recalculates written formulas to the cached values", () =>
        {
            var csv = LibreOfficeCsv(wPath, outputDir);
            if (csv is null) { Console.WriteLine("          (LibreOffice not available: skipped)"); return; }
            var line = csv.Split('\n')[1];
            True(line.StartsWith("6,n=1,TRUE,#DIV/0!,", StringComparison.Ordinal), $"LibreOffice values: {line}");
        });

        Check("auto-fit measures formula cells by their cached value", () =>
        {
            var p = Path.Combine(outputDir, "f150_autofit_formula.xlsx");
            using (var w = XlsxWriter.Create(p))
            {
                var sh = w.AddSheet("A").AutoFitColumns();
                sh.WriteRow(rb => rb.Formula("REPT(\"x\",40)", new string('x', 40)).Formula("1+1"));
                w.Save();
            }
            var xml = PartText(p, "xl/worksheets/sheet1.xml");
            True(xml.Contains("<col min=\"1\" max=\"1\""), "column with a cached string is fitted");
            True(!xml.Contains("<col min=\"2\""), "uncached formula column not measured");
        });

        // ---- Page setup: streaming reader --------------------------------------------------

        var pPath = Path.Combine(outputDir, "f150_layout.xlsx");
        ReadSafetyTest.Package.Write(pPath, new (string, string, string?)[]
        {
            ("Report",
             "<sheetPr><pageSetUpPr fitToPage=\"1\"/></sheetPr><dimension ref=\"A1:D6\"/>" +
             "<sheetViews><sheetView showGridLines=\"0\" zoomScale=\"90\" workbookViewId=\"0\"/></sheetViews>" +
             "<sheetFormatPr defaultRowHeight=\"15\" defaultColWidth=\"10\"/>" +
             "<cols><col min=\"1\" max=\"1\" width=\"20\" customWidth=\"1\"/><col min=\"2\" max=\"2\" width=\"5\" hidden=\"1\"/>" +
             "<col min=\"3\" max=\"3\" width=\"12\" style=\"2\"/></cols>" +
             "<sheetData>" +
             "<row r=\"1\" ht=\"30\" customHeight=\"1\"><c r=\"A1\" s=\"1\"><v>1</v></c></row>" +
             "<row r=\"2\" s=\"1\" customFormat=\"1\"><c r=\"A2\"><v>2</v></c></row>" +
             "<row r=\"3\" hidden=\"1\" outlineLevel=\"1\"><c r=\"A3\"><v>3</v></c></row>" +
             "<row r=\"6\"><c r=\"D6\"><v>6</v></c></row>" +
             "</sheetData>" +
             "<mergeCells count=\"1\"><mergeCell ref=\"A5:B5\"/></mergeCells>" +
             "<printOptions gridLines=\"1\" horizontalCentered=\"1\"/>" +
             "<pageMargins left=\"0.25\" right=\"0.25\" top=\"0.5\" bottom=\"0.5\" header=\"0.2\" footer=\"0.2\"/>" +
             "<pageSetup paperSize=\"9\" orientation=\"landscape\" fitToWidth=\"1\" fitToHeight=\"0\"/>" +
             "<headerFooter differentFirst=\"1\"><oddHeader>&amp;CReport</oddHeader><firstFooter>&amp;LFirst</firstFooter></headerFooter>" +
             "<rowBreaks count=\"1\" manualBreakCount=\"1\"><brk id=\"3\" max=\"16383\" man=\"1\"/></rowBreaks>", null),
        },
        styles:
            "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
            "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
            "<borders count=\"1\"><border/></borders>" +
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
            "<cellXfs count=\"3\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
            "<xf numFmtId=\"10\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/></cellXfs>",
        workbookExtra:
            "<definedNames>" +
            "<definedName name=\"_xlnm.Print_Area\" localSheetId=\"0\">Report!$A$1:$D$6</definedName>" +
            "<definedName name=\"_xlnm.Print_Titles\" localSheetId=\"0\">Report!$1:$1</definedName>" +
            "</definedNames>");

        Check("page setup (streaming): ReadLayout before rows — page, columns, merges, view, used range", () =>
        {
            using var r = XlsxReader.Open(pPath);
            var sr = r.Sheet("Report");
            var L = sr.ReadLayout();
            True(ReferenceEquals(L, sr.ReadLayout()), "cached");
            var p = L.PageSetup;
            True(p.FitToPage && p.FitToWidth == 1 && p.FitToHeight == 0, "fit to 1 page wide");
            Eq<int?>(9, p.PaperSizeCode, "paper code"); Eq("A4", p.Paper!.Name, "paper"); Eq(PageOrientation.Landscape, p.Orientation, "orientation");
            True(p.PrintGridlines && p.CenterHorizontally && !p.PrintHeadings, "print options");
            Eq(0.25, p.Margins.Left, "left margin"); Eq(0.2, p.Margins.Footer, "footer distance");
            Eq("&CReport", p.HeaderFooter.OddHeader, "odd header"); Eq("&LFirst", p.HeaderFooter.FirstFooter, "first footer");
            True(p.HeaderFooter.DifferentFirst, "different first");
            True(p.RowBreaks.Count == 1 && p.RowBreaks[0].Id == 3 && p.RowBreaks[0].Manual, "row break");
            True(p.PrintAreas.SequenceEqual(new[] { "A1:D6" }), "print area");
            Eq((1, 1), p.PrintTitleRows!.Value, "title rows");
            Eq("A1:D6", L.UsedRange, "used range");
            True(!L.ShowGridLines && L.ZoomScale == 90, "view");
            True(L.MergeRanges.SequenceEqual(new[] { "A5:B5" }), "merges");
            Eq(20.0, L.ColumnWidth(1), "col A width"); True(L.IsColumnHidden(2), "col B hidden");
            Eq(10.0, L.ColumnWidth(4), "default column width from sheetFormatPr");
            Eq(2, L.ColumnStyleIndex(3), "column style index");
            Eq("0.00%", L.ColumnStyle(3)!.Value.NumberFormat ?? "", "column style resolves");
            Eq(10.0, L.Format.DefaultColumnWidth ?? 0, "format");
        });

        Check("page setup (streaming): row attributes, styles and effective style per cell", () =>
        {
            using var r = XlsxReader.Open(pPath, new XlsxReaderOptions { TreatFirstRowAsHeaders = false });
            var sr = r.Sheet("Report");
            var L = sr.ReadLayout();
            var seen = new List<string>();
            foreach (var row in sr.Rows)
            {
                var a = row.Attributes;
                switch (row.RowNumber)
                {
                    case 1:
                        True(a.Height == 30 && a.CustomHeight, "row 1 height");
                        True(row.GetStyle(0)!.Value.Bold, "A1 bold via GetStyle");
                        Eq(1, row.GetEffectiveStyleIndex(0, L), "A1 own style");
                        Eq(2, row.GetEffectiveStyleIndex(2, L), "C1 missing → column style");
                        break;
                    case 2:
                        True(a.CustomFormat && a.StyleIndex == 1, "row style");
                        Eq(0, row.GetEffectiveStyleIndex(0, L), "A2 present without s= → xf 0 (row style does not apply)");
                        Eq(1, row.GetEffectiveStyleIndex(2, L), "C2 missing → row style wins over column style");
                        True(row.HasCell(0) && !row.HasCell(2), "HasCell");
                        break;
                    case 3:
                        True(a.Hidden && a.OutlineLevel == 1, "hidden grouped row");
                        break;
                    case 6:
                        True(!a.IsDefined && a.Height == 0, "row with no attributes");
                        break;
                }
                seen.Add(row.RowNumber.ToString());
            }
            Eq("1,2,3,6", string.Join(",", seen), "rows still stream after ReadLayout");
        });

        Check("page setup (streaming): ReadLayout honours cancellation and read limits", () =>
        {
            using var cts = new CancellationTokenSource();
            using var r2 = XlsxReader.Open(pPath, new XlsxReaderOptions { CancellationToken = cts.Token });
            cts.Cancel();
            Throws<OperationCanceledException>(() => r2.Sheet(1).ReadLayout(), "token cancelled after open");
            Throws<InvalidDataException>(() =>
            {
                using var r3 = XlsxReader.Open(pPath, new XlsxReaderOptions { MaxPartBytes = 100 });
                r3.Sheet(1).ReadLayout();
            }, "part limit");
        });

        Check("model: Sheet.GetEffectiveStyle (cell, row, column) without creating cells", () =>
        {
            var s = Workbook.Load(pPath)["Report"];
            int before = s.PopulatedCells.Count();
            True(s.GetEffectiveStyle(1, 1)!.Value.Bold, "own style");
            True(s.GetEffectiveStyle(2, 3)!.Value.Bold, "row style");
            Eq("0.00%", s.GetEffectiveStyle(1, 3)!.Value.NumberFormat ?? "", "column style");
            True(s.GetEffectiveStyle(2, 1) is null, "present cell with default style");
            Eq(before, s.PopulatedCells.Count(), "no cells created");
        });

        // ---- Page setup: write -------------------------------------------------------------

        var psPath = Path.Combine(outputDir, "f150_pagesetup_written.xlsx");
        Check("page setup (writer): every setting written; set after rows; with auto-fit", () =>
        {
            using (var w = XlsxWriter.Create(psPath))
            {
                var sh = w.AddSheet("My Sheet's").AutoFitColumns();
                sh.WriteHeader("Name", "Amount");
                for (int i = 0; i < 50; i++) sh.WriteRow("Patient " + i, i * 1.5);
                // Set after the rows: <sheetPr> is inserted at save, <cols> too (auto-fit).
                var p = sh.PageSetup;
                p.PaperSizeCode = 9; p.Orientation = PageOrientation.Landscape; p.Scale = 80;
                p.FitTo(1, 0);
                p.FirstPageNumber = 3; p.UseFirstPageNumber = true; p.PageOrder = PageOrder.OverThenDown;
                p.BlackAndWhite = true; p.Draft = true; p.CellComments = PrintCellComments.AtEnd; p.Errors = PrintErrors.Dash;
                p.HorizontalDpi = 600; p.VerticalDpi = 600; p.Copies = 2;
                p.Margins.Set(0.5, 0.5, 1, 1); p.Margins.Header = 0.4; p.Margins.Footer = 0.4;
                p.HeaderFooter.OddHeader = "&CApulki  Report"; p.HeaderFooter.OddFooter = "&RPage &P of &N";
                p.HeaderFooter.DifferentOddEven = true; p.HeaderFooter.EvenFooter = "&LEven";
                p.HeaderFooter.DifferentFirst = true; p.HeaderFooter.FirstHeader = "&CCover";
                p.HeaderFooter.ScaleWithDocument = false; p.HeaderFooter.AlignWithMargins = false;
                p.PrintGridlines = true; p.PrintHeadings = true; p.CenterHorizontally = true; p.CenterVertically = true;
                p.AddRowBreak(25).AddColumnBreak(1);
                p.SetPrintArea("A1:B51", "$D$1:$E$5");
                p.PrintTitleRows = (1, 1); p.PrintTitleColumns = (1, 1);
                w.AddSheet("Plain").WriteRow("x");
                w.Save();
            }

            var xml = PartText(psPath, "xl/worksheets/sheet1.xml");
            True(xml.Contains("2006/main\"><sheetPr><pageSetUpPr fitToPage=\"1\" /></sheetPr><cols>"), "sheetPr inserted right after <worksheet>, before <cols>");
            True(xml.Contains("<cols><col min=\"1\""), "auto-fit cols inserted too");
            AssertSchemaOrder(xml);
            var plain = PartText(psPath, "xl/worksheets/sheet2.xml");
            True(!plain.Contains("sheetPr") && !plain.Contains("pageSetup") && !plain.Contains("pageMargins") && !plain.Contains("printOptions"), "untouched sheet writes nothing new");
            var wb = PartText(psPath, "xl/workbook.xml");
            True(wb.Contains("<definedName name=\"_xlnm.Print_Area\" localSheetId=\"0\">'My Sheet''s'!$A$1:$B$51,'My Sheet''s'!$D$1:$E$5</definedName>"), "print area name, quoted sheet");
            True(wb.Contains("<definedName name=\"_xlnm.Print_Titles\" localSheetId=\"0\">'My Sheet''s'!$1:$1,'My Sheet''s'!$A:$A</definedName>"), "print titles name");

            // Read back through both read paths.
            foreach (var p in new[] { Workbook.Load(psPath).Sheets[0].PageSetup, ReadLayoutOf(psPath, 1).PageSetup })
            {
                True(p.FitToPage && p.FitToWidth == 1 && p.FitToHeight == 0, "fit");
                True(p.PaperSizeCode == 9 && p.Orientation == PageOrientation.Landscape && p.Scale == 80, "paper/orientation/scale");
                True(p.FirstPageNumber == 3 && p.UseFirstPageNumber && p.PageOrder == PageOrder.OverThenDown, "numbering/order");
                True(p.BlackAndWhite && p.Draft && p.CellComments == PrintCellComments.AtEnd && p.Errors == PrintErrors.Dash, "flags");
                True(p.HorizontalDpi == 600 && p.VerticalDpi == 600 && p.Copies == 2, "dpi/copies");
                True(p.Margins.Left == 0.5 && p.Margins.Top == 1 && p.Margins.Header == 0.4, "margins");
                var h = p.HeaderFooter;
                True(h.OddHeader == "&CApulki  Report" && h.OddFooter == "&RPage &P of &N" && h.EvenFooter == "&LEven" && h.FirstHeader == "&CCover", "header/footer text (double space kept)");
                True(h.DifferentOddEven && h.DifferentFirst && !h.ScaleWithDocument && !h.AlignWithMargins, "header/footer flags");
                True(p.PrintGridlines && p.PrintHeadings && p.CenterHorizontally && p.CenterVertically, "print options");
                True(p.RowBreaks.Count == 1 && p.RowBreaks[0].Id == 25 && p.ColumnBreaks[0].Id == 1, "breaks");
                True(p.PrintAreas.SequenceEqual(new[] { "A1:B51", "D1:E5" }), "print areas");
                True(p.PrintTitleRows == (1, 1) && p.PrintTitleColumns == (1, 1), "titles");
            }
        });

        Check("page setup (writer): validation", () =>
        {
            var p = new PageSetup();
            Throws<ArgumentOutOfRangeException>(() => p.Scale = 5, "scale");
            Throws<ArgumentOutOfRangeException>(() => p.FitToWidth = -1, "fit width");
            Throws<ArgumentOutOfRangeException>(() => p.Margins.Left = -0.1, "margin");
            Throws<ArgumentOutOfRangeException>(() => p.PrintTitleRows = (3, 1), "titles order");
            Throws<ArgumentException>(() => p.SetPrintArea("Sheet1!A1:B2"), "sheet-qualified area");
            Throws<ArgumentException>(() => p.SetPrintArea("bogus"), "bad area");
            Throws<ArgumentOutOfRangeException>(() => p.Copies = 0, "copies");
            True(!p.IsDefined && !p.Margins.IsDefined, "failed sets mark nothing");
            p.SetPrintArea("a1:c3", "B:C", "2:4", "E5");
            True(p.PrintAreas.SequenceEqual(new[] { "A1:C3", "B:C", "2:4", "E5" }), "normalised areas");
        });

        Check("page setup (model): load → edit → save round-trip; export config hook", () =>
        {
            var wb = Workbook.Load(pPath);
            var p = wb["Report"].PageSetup;
            p.Orientation = PageOrientation.Portrait;
            p.AddColumnBreak(2);
            p.PrintTitleRows = null;
            var saved = Path.Combine(outputDir, "f150_layout_resaved.xlsx");
            wb.SaveTo(saved);
            var q = Workbook.Load(saved)["Report"].PageSetup;
            True(q.FitToPage && q.FitToHeight == 0 && q.PaperSizeCode == 9, "kept");
            Eq(PageOrientation.Portrait, q.Orientation, "edited orientation");
            True(q.RowBreaks.Count == 1 && q.ColumnBreaks.Count == 1, "breaks kept + added");
            True(q.PrintAreas.SequenceEqual(new[] { "A1:D6" }) && q.PrintTitleRows is null, "area kept, titles removed");
            True(q.HeaderFooter.FirstFooter == "&LFirst" && q.HeaderFooter.DifferentFirst, "first-page footer kept");
            Eq(0.25, q.Margins.Left, "margins kept");

            var exp = Path.Combine(outputDir, "f150_export_pagesetup.xlsx");
            new[] { new { Name = "A", Qty = 1 } }.ToXlsx(exp, cfg => cfg.PageSetup(ps => { ps.Orientation = PageOrientation.Landscape; ps.PrintTitleRows = (1, 1); }));
            var e = Workbook.Load(exp).Sheets[0].PageSetup;
            True(e.Orientation == PageOrientation.Landscape && e.PrintTitleRows == (1, 1), "export config PageSetup");
        });

        Check("writer: element order fixed for hyperlinks + conditional formats + validation + comments + table", () =>
        {
            var p = Path.Combine(outputDir, "f150_order.xlsx");
            using (var w = XlsxWriter.Create(p))
            {
                var sh = w.AddSheet("O");
                sh.WriteHeader("H1", "H2");
                sh.WriteRow(rb => rb.Hyperlink("https://example.com", "link").Comment(5, "Arun", "note"));
                sh.AddConditionalFormat("B2:B2", ConditionalFormat.DataBar.Blue);
                sh.AddDataValidation("B2:B2", DataValidation.WholeNumber(0, 10));
                sh.AddTable("A1:B2", "T1", TableStyle.Medium2, "H1", "H2");
                sh.SetHeaderFooter("&CH", null);
                sh.PageSetup.Orientation = PageOrientation.Landscape;
                w.Save();
            }
            AssertSchemaOrder(PartText(p, "xl/worksheets/sheet1.xml"));
            var back = Workbook.Load(p).Sheets[0];
            Eq("link", back.Cell("A2").Value, "readable");
        });

        // ---- Defined names -----------------------------------------------------------------

        Check("defined names (writer): workbook and sheet scope, hidden, comment; print names guarded", () =>
        {
            var p = Path.Combine(outputDir, "f150_names.xlsx");
            using (var w = XlsxWriter.Create(p))
            {
                w.AddSheet("Data").WriteRow(1);
                w.AddSheet("Calc").WriteRow(2);
                w.AddDefinedName("TaxRate", "0.18", comment: "GST");
                w.AddDefinedName("Start", "Calc!$A$1", "Calc", hidden: true);
                w.AddDefinedName("Start", "Data!$A$1", "Data");
                Throws<ArgumentException>(() => w.AddDefinedName("_xlnm.Print_Area", "Data!$A$1", "Data"), "print area guarded");
                Throws<ArgumentException>(() => w.AddDefinedName("1bad", "1"), "invalid name");
                w.Save();
            }
            using var r = XlsxReader.Open(p);
            var names = r.DefinedNames;
            True(names.Any(n => n.Name == "TaxRate" && n.LocalSheetIndex is null && n.Comment == "GST"), "workbook name with comment");
            True(names.Any(n => n.Name == "Start" && n.LocalSheetIndex == 1 && n.Hidden && n.Formula == "Calc!$A$1"), "sheet-scoped hidden");
            True(names.Any(n => n.Name == "Start" && n.LocalSheetIndex == 0), "same name, other sheet");

            var bad = Path.Combine(outputDir, "f150_names_bad.xlsx");
            Throws<InvalidOperationException>(() =>
            {
                using var w = XlsxWriter.Create(bad);
                w.AddSheet("A").WriteRow(1);
                w.AddDefinedName("X", "A!$A$1", "Missing");
                w.Save();
            }, "scope to a missing sheet");
        });

        Check("defined names (model): load, scope by sheet name, skip external/table names, save back", () =>
        {
            var wb = Workbook.Load(fPath);
            var d = wb.DefinedNames;
            Eq(2, d.Count, "TaxRate + Local (Ext, Tbl and Print_Area not in Entries)");
            Eq("GST", d.Find("TaxRate")!.Comment, "comment");
            var local = d.Find("Local", "Calc")!;
            True(local.Hidden && local.Formula == "Calc!$A$1", "scoped hidden name");
            Eq(5, wb.LoadedDefinedNames.Count, "all five still listed as loaded");
            True(d.Entries.All(n => !n.Name.StartsWith("_xlnm.Print")), "print names in PageSetup");
            Throws<ArgumentException>(() => d.Add("_xlnm.Print_Titles", "Calc!$1:$1", "Calc"), "print titles guarded");
            d.Add("Rate2", "TaxRate*2", "Calc");
            True(d.Remove("Local", "Calc") && d.Find("Local", "Calc") is null, "remove scoped");

            var saved = Path.Combine(outputDir, "f150_names_resaved.xlsx");
            wb.SaveTo(saved);
            var back = Workbook.Load(saved);
            True(back.DefinedNames.TryGet("TaxRate", out var t) && t == "0.18", "workbook name saved");
            Eq("TaxRate*2", back.DefinedNames.Find("Rate2", "Calc")!.Formula, "scoped name saved");
            True(back.Sheets[0].PageSetup.PrintAreas.SequenceEqual(new[] { "A1:K4" }), "print area saved via PageSetup");
            Eq(1, back.LoadedDefinedNames.Count(n => n.Name == "_xlnm.Print_Area"), "no duplicate print area");
        });

        // ---- Zip that lies about its sizes -------------------------------------------------

        Check("C4: a zip that declares small sizes but inflates large is stopped by the counted limits", () =>
        {
            var p = Path.Combine(outputDir, "f150_lying.xlsx");
            var sb = new StringBuilder("<sheetData>");
            for (int i = 1; i <= 200_000; i++) sb.Append("<row r=\"").Append(i).Append("\"><c r=\"A").Append(i).Append("\"><v>1</v></c></row>");
            sb.Append("</sheetData>");
            ReadSafetyTest.Package.Write(p, new[] { ("Big", sb.ToString(), (string?)null) });
            long real = LieAboutSize(p, "xl/worksheets/sheet1.xml", 1000);
            True(real > 8_000_000, $"sheet really inflates to {real:N0} bytes");

            using (var z = new ZipArchive(File.OpenRead(p), ZipArchiveMode.Read))
                Eq(1000L, z.GetEntry("xl/worksheets/sheet1.xml")!.Length, "central directory now lies");

            // The declared-size checks pass (the zip claims 1,000 bytes). .NET's zip reader stops
            // inflating at the declared size, so the reader never gets more than 1,000 bytes: the
            // sheet arrives truncated and fails as malformed XML. The counting streams are the
            // second line of defence should a zip layer ever return more than it declares.
            static bool Rejected(Exception e) =>
                e is XmlException || e is XlsxLimitExceededException || e is InvalidDataException || e is XlsxFormatException;

            var ex = Throws<Exception>(() => Workbook.Load(p, null, new XlsxReaderOptions { MaxTotalUncompressedBytes = 4_000_000, MaxCompressionRatio = null }), "total limit");
            True(Rejected(ex), $"rejected ({ex.GetType().Name}: {ex.Message})");

            long bytesSeen = 0;
            var ex2 = Throws<Exception>(() =>
            {
                using var r = XlsxReader.Open(p, new XlsxReaderOptions { MaxPartBytes = 2_000_000, TreatFirstRowAsHeaders = false });
                foreach (var row in r.Sheet(1).Rows) bytesSeen = row.RowNumber;
            }, "per-part limit");
            True(Rejected(ex2), $"rejected ({ex2.GetType().Name})");
            True(bytesSeen < 100, $"only the declared bytes were inflated (rows seen: {bytesSeen})");

            var ex3 = Throws<Exception>(() =>
            {
                using var r = XlsxReader.Open(p);
                foreach (var _ in r.Sheet(1).Rows) { }
            }, "no limits at all");
            True(Rejected(ex3), "even without limits the lie cannot inflate more than declared");
            Console.WriteLine($"          (lying entry: {ex3.GetType().Name}: {ex3.Message})");
        });

        // ---- Cancellation while an encrypted file is read ----------------------------------

        Check("C4: cancellation while an encrypted file is read; decrypt window measured", () =>
        {
            var p = Path.Combine(outputDir, "f150_encrypted_big.xlsx");
            using (var w = XlsxWriter.Create(p, new XlsxWriterOptions { Encryption = new EncryptionOptions { Password = "pw" } }))
            {
                var sh = w.AddSheet("E");
                for (int i = 0; i < 150_000; i++) sh.WriteRow(i, "row " + i, i * 0.5);
                w.Save();
            }
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Throws<OperationCanceledException>(() => XlsxReader.Open(p, "pw", new XlsxReaderOptions { CancellationToken = cts.Token }), "pre-cancelled");

            // Cancel from another thread shortly after open starts; the read of the container
            // checks the token per 512-byte sector.
            using var slow = new SlowStream(File.ReadAllBytes(p));
            using var cts2 = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            var sw = Stopwatch.StartNew();
            Throws<OperationCanceledException>(() => XlsxReader.Open(slow, "pw", new XlsxReaderOptions { CancellationToken = cts2.Token }), "cancel mid-read");
            sw.Stop();
            True(sw.ElapsedMilliseconds < 1000, $"stopped in {sw.ElapsedMilliseconds} ms");

            // The in-memory part (key derivation, AES, HMAC) is not interruptible: measure it.
            var bytes = File.ReadAllBytes(p);
            var t = Stopwatch.StartNew();
            var plain = Chuvadi.Internal.Crypto.EncryptedPackageReader.DecryptToPlaintextPackage(new MemoryStream(bytes), "pw");
            t.Stop();
            Console.WriteLine($"          (cancel landed after {sw.ElapsedMilliseconds} ms; in-memory decrypt of {bytes.Length / 1024.0 / 1024:0.0} MB → {plain.Length / 1024.0 / 1024:0.0} MB took {t.ElapsedMilliseconds} ms, not interruptible)");
        });

        Console.WriteLine();
        Console.WriteLine($"  FormulaPrintTest: {pass} passed, {fail} failed.");
        Console.WriteLine();
        if (fail > 0) throw new Exception($"FormulaPrintTest: {fail} check(s) failed.");
    }

    // ---- Helpers ---------------------------------------------------------------------------

    private static SheetLayoutInfo ReadLayoutOf(string path, int sheet)
    {
        using var r = XlsxReader.Open(path);
        return r.Sheet(sheet).ReadLayout();
    }

    private static string PartText(string path, string part)
    {
        using var z = ZipFile.OpenRead(path);
        using var s = z.GetEntry(part)!.Open();
        return new StreamReader(s, Encoding.UTF8).ReadToEnd();
    }

    /// <summary>CT_Worksheet child order (ECMA-376 Part 1, 18.3.1.99).</summary>
    private static readonly string[] WorksheetOrder =
    {
        "sheetPr", "dimension", "sheetViews", "sheetFormatPr", "cols", "sheetData", "sheetCalcPr", "sheetProtection",
        "protectedRanges", "scenarios", "autoFilter", "sortState", "dataConsolidate", "customSheetViews", "mergeCells",
        "phoneticPr", "conditionalFormatting", "dataValidations", "hyperlinks", "printOptions", "pageMargins", "pageSetup",
        "headerFooter", "rowBreaks", "colBreaks", "customProperties", "cellWatches", "ignoredErrors", "smartTags", "drawing",
        "legacyDrawing", "legacyDrawingHF", "drawingHF", "picture", "oleObjects", "controls", "webPublishItems", "tableParts", "extLst",
    };

    private static void AssertSchemaOrder(string xml)
    {
        using var r = XmlReader.Create(new StringReader(xml));
        r.MoveToContent();
        int depth = r.Depth, last = -1;
        string? lastName = null;
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || r.Depth != depth + 1) continue;
            int idx = Array.IndexOf(WorksheetOrder, r.LocalName);
            if (idx < 0) throw new Exception($"unknown worksheet child <{r.LocalName}>");
            if (idx < last && r.LocalName != lastName) throw new Exception($"<{r.LocalName}> written after <{lastName}>");
            if (idx > last) { last = idx; lastName = r.LocalName; }
        }
    }

    /// <summary>Rewrites the declared uncompressed size of one entry (local header and central directory). Returns the real size.</summary>
    private static long LieAboutSize(string path, string entryName, uint fake)
    {
        var bytes = File.ReadAllBytes(path);
        var name = Encoding.UTF8.GetBytes(entryName);
        long real = 0;
        for (int i = 0; i + 46 < bytes.Length; i++)
        {
            uint sig = BitConverter.ToUInt32(bytes, i);
            if (sig == 0x04034b50)
            {
                int nl = BitConverter.ToUInt16(bytes, i + 26);
                if (nl == name.Length && bytes.AsSpan(i + 30, nl).SequenceEqual(name))
                {
                    real = BitConverter.ToUInt32(bytes, i + 22);
                    BitConverter.TryWriteBytes(bytes.AsSpan(i + 22, 4), fake);
                }
            }
            else if (sig == 0x02014b50)
            {
                int nl = BitConverter.ToUInt16(bytes, i + 28);
                if (nl == name.Length && bytes.AsSpan(i + 46, nl).SequenceEqual(name))
                    BitConverter.TryWriteBytes(bytes.AsSpan(i + 24, 4), fake);
            }
        }
        File.WriteAllBytes(path, bytes);
        return real;
    }

    /// <summary>A seekable in-memory stream that sleeps a little on each read (a slow disk or network share).</summary>
    private sealed class SlowStream : MemoryStream
    {
        public SlowStream(byte[] data) : base(data, writable: false) { }
        public override int Read(byte[] buffer, int offset, int count) { Thread.Sleep(1); return base.Read(buffer, offset, count); }
        public override int Read(Span<byte> buffer) { Thread.Sleep(1); return base.Read(buffer); }
    }

    /// <summary>Converts the first sheet to CSV with LibreOffice (recalculating), or null when unavailable.</summary>
    private static string? LibreOfficeCsv(string xlsx, string outputDir)
    {
        try
        {
            var dir = Path.Combine(outputDir, "lo_csv");
            Directory.CreateDirectory(dir);
            var psi = new ProcessStartInfo("soffice", $"--headless --calc --convert-to csv --outdir \"{dir}\" \"{xlsx}\"")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var proc = Process.Start(psi);
            if (proc is null || !proc.WaitForExit(120_000)) return null;
            var csv = Path.Combine(dir, Path.GetFileNameWithoutExtension(xlsx) + ".csv");
            return File.Exists(csv) ? File.ReadAllText(csv).Replace("\r", "") : null;
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }
}
