using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.ManualTests;

/// <summary>
/// Sheets 1.6.0: round-trip of the rest of a sheet (rows, columns, views, panes, tab colour,
/// visibility), conditional formatting and data validation on read/write with per-cell
/// evaluation, and drawings (pictures, shapes, groups, charts) on read. The fixture is an
/// Excel-shaped package including Excel 2010 extension blocks.
/// </summary>
internal static class LayoutFidelityTest
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string X14 = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private const string Xm = "http://schemas.microsoft.com/office/excel/2006/main";

    internal static readonly byte[] Png1x1 = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    public static void Run(string outputDir)
    {
        Console.WriteLine("[LayoutFidelityTest] 1.6.0 rows/columns/views, conditional formats, validation, drawings...");
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

        var fx = Path.Combine(outputDir, "f160_fidelity.xlsx");
        WriteFixture(fx);
        var today = new DateTime(2026, 10, 28);

        // ---- 1. Rows, columns, view, tab colour, visibility --------------------------------

        Check("read (model): view, panes, selection, zoom, tab colour, outline flags, visibility", () =>
        {
            var wb = Workbook.Load(fx);
            var s = wb["Data"];
            var v = s.View;
            True(v.TabSelected && v.ZoomScale == 120 && v.ZoomScaleNormal == 120, "tab selected, zoom");
            True(v.Pane is { State: PaneState.Frozen, XSplit: 1, YSplit: 1, TopLeftCell: "B2", ActivePane: PaneKind.BottomRight }, "frozen pane");
            True(s.FreezeRowCount == 1 && s.FreezeColumnCount == 1, "freeze shortcuts");
            Eq(3, v.Selections.Count, "selections");
            Eq(new SheetSelection(PaneKind.BottomRight, "C5", "C5:D6"), v.Selections[2], "active selection");
            Eq("00B050", s.TabColor, "tab colour");
            True(!s.Format.OutlineSummaryBelow, "summary rows above");
            Eq(SheetVisibility.Hidden, wb["Lists"].Visibility, "hidden sheet");
            True(ReferenceEquals(wb.ActiveSheet, s), "active sheet");
            True(s.Rows[3].Height == 30 && s.Rows[3].CustomHeight, "row height");
            True(s.Rows[7].Hidden && s.Rows[7].OutlineLevel == 1, "hidden grouped row");
            True(s.Rows[12].Style is { Bold: true } && s.Rows[12].Height == 20, "styled empty row");
            True(s.Columns[5].Hidden && s.Columns[5].OutlineLevel == 1, "hidden grouped column");
        });

        Check("round-trip (model): load → save keeps rows, columns, view, panes, tab colour, visibility", () =>
        {
            var saved = Path.Combine(outputDir, "f160_fidelity_resaved.xlsx");
            Workbook.Load(fx).SaveTo(saved);
            var wb = Workbook.Load(saved);
            var s = wb["Data"];
            True(s.View.Pane is { State: PaneState.Frozen, XSplit: 1, YSplit: 1 } && s.View.ZoomScale == 120 && s.View.Selections.Count == 3, "view kept");
            Eq("00B050", s.TabColor, "tab colour kept");
            True(!s.Format.OutlineSummaryBelow, "outline flag kept");
            Eq(1, s.Format.OutlineLevelRow, "outline level recomputed");
            True(s.Rows[3].Height == 30 && s.Rows[7].Hidden && s.Rows[7].OutlineLevel == 1, "rows kept");
            True(s.Rows[12].Style is { Bold: true } && s.Rows[12].Height == 20, "trailing styled row kept");
            True(s.Columns[5].Hidden && s.Columns[5].OutlineLevel == 1 && s.Columns[1].Width == 18, "columns kept");
            Eq(SheetVisibility.Hidden, wb["Lists"].Visibility, "visibility kept");
            AssertSchemaOrder(PartText(saved, "xl/worksheets/sheet1.xml"));
        });

        Check("model setters: rows and columns (validation, read-only streaming rows)", () =>
        {
            var wb = new Workbook();
            var s = wb.AddSheet("M");
            s.Cell("A1").Value = "x";
            s.Rows[2].Height = 40;
            s.Rows[4].Hidden = true; s.Rows[4].OutlineLevel = 2;
            s.Rows[6].Style = new CellStyle { FillColor = "FFFF00" };
            s.Columns[2].Hidden = true; s.Columns[3].OutlineLevel = 1; s.Columns[3].Collapsed = true;
            s.Columns[4].Style = new CellStyle { Bold = true };
            s.Columns[4].Width = 22;
            Throws<ArgumentOutOfRangeException>(() => s.Rows[2].Height = 500, "height limit");
            Throws<ArgumentOutOfRangeException>(() => s.Rows[2].OutlineLevel = 8, "outline limit");
            Throws<ArgumentOutOfRangeException>(() => s.Columns[2].OutlineLevel = -1, "column outline limit");
            var p = Path.Combine(outputDir, "f160_model_rows.xlsx");
            wb.SaveTo(p);
            var b = Workbook.Load(p)["M"];
            True(b.Rows[2].Height == 40 && b.Rows[2].CustomHeight, "height");
            True(b.Rows[4].Hidden && b.Rows[4].OutlineLevel == 2, "hidden row");
            Eq("FFFF00", b.Rows[6].Style?.FillColor, "row style");
            True(b.Columns[2].Hidden && b.Columns[3].OutlineLevel == 1 && b.Columns[3].Collapsed, "columns");
            True(b.Columns[4].Style is { Bold: true } && b.Columns[4].Width == 22, "column style + width");
            Eq(2, b.Format.OutlineLevelRow, "outlineLevelRow written");
            using var r = XlsxReader.Open(p, new XlsxReaderOptions { TreatFirstRowAsHeaders = false });
            foreach (var row in r.Sheet(1).Rows)
                Throws<InvalidOperationException>(() => row.Attributes.Hidden = true, "streaming row is read-only");
        });

        var wPath = Path.Combine(outputDir, "f160_writer_shape.xlsx");
        Check("writer: RowFormat, SetColumns, View (freeze/split/selection), Format, TabColor, Visibility, ActiveSheetIndex", () =>
        {
            using (var w = XlsxWriter.Create(wPath))
            {
                var a = w.AddSheet("A").AutoFitColumns();
                a.WriteHeader("Name", "Amount");
                a.WriteRow(new RowFormat { Height = 32 }, "Tall", 1);
                a.WriteRow(new RowFormat { Hidden = true, OutlineLevel = 1 }, "Hidden", 2);
                a.WriteRow(new RowFormat { OutlineLevel = 1, Collapsed = true, Style = new CellStyle { Italic = true } });
                // Shape settings after the rows: the prologue is built at save.
                a.SetColumns(3, 4, new ColumnFormat { Width = 14, Hidden = true, OutlineLevel = 1 });
                a.SetColumns(6, 6, new ColumnFormat { Style = new CellStyle { FillColor = "DDEBF7" } });
                a.View.Freeze(1, 1).Select("B2");
                a.View.ZoomScale = 85; a.View.ShowGridLines = false; a.View.ShowZeros = false;
                a.Format.DefaultRowHeight = 18;
                a.Format.OutlineSummaryBelow = false;
                a.TabColor = "#C00000";
                a.PageSetup.FitTo(1, 0);
                var b = w.AddSheet("B");
                b.WriteRow("visible");
                b.View.Split(2400, 1800, "D5");
                var c = w.AddSheet("C");
                c.Visibility = SheetVisibility.VeryHidden;
                w.ActiveSheetIndex = 1;
                w.Save();
            }
            var x = PartText(wPath, "xl/worksheets/sheet1.xml");
            AssertSchemaOrder(x);
            True(x.Contains("<sheetPr><tabColor rgb=\"FFC00000\" /><outlinePr summaryBelow=\"0\" /><pageSetUpPr fitToPage=\"1\" /></sheetPr>"), "sheetPr");
            True(x.Contains("<pane xSplit=\"1\" ySplit=\"1\" topLeftCell=\"B2\" activePane=\"bottomRight\" state=\"frozen\" />"), "frozen pane");
            True(x.Contains("<selection pane=\"bottomRight\" activeCell=\"B2\" sqref=\"B2\" />"), "selection");
            True(x.Contains("<sheetFormatPr defaultRowHeight=\"18\" outlineLevelRow=\"1\" outlineLevelCol=\"1\" />"), "sheetFormatPr with outline levels");
            True(x.Contains("<row r=\"2\" ht=\"32\" customHeight=\"1\">"), "row height");
            True(x.Contains("<row r=\"3\" hidden=\"1\" outlineLevel=\"1\">"), "hidden row");
            True(System.Text.RegularExpressions.Regex.IsMatch(x, "<row r=\"4\" s=\"\\d+\" customFormat=\"1\" outlineLevel=\"1\" collapsed=\"1\" />"), "empty styled row");
            True(x.Contains("<col min=\"3\" max=\"4\" width=\"14\" hidden=\"1\" customWidth=\"1\" outlineLevel=\"1\" />"), "column span");
            var wbx = PartText(wPath, "xl/workbook.xml");
            True(wbx.Contains("<bookViews><workbookView activeTab=\"1\" /></bookViews>"), "active tab");
            True(wbx.Contains("<sheet name=\"C\" sheetId=\"3\" state=\"veryHidden\""), "very hidden");
            var s2 = PartText(wPath, "xl/worksheets/sheet2.xml");
            True(s2.Contains("<pane xSplit=\"2400\" ySplit=\"1800\" topLeftCell=\"D5\" activePane=\"bottomRight\" state=\"split\" />"), "split pane");

            var back = Workbook.Load(wPath);
            var s = back["A"];
            True(s.Rows[2].Height == 32 && s.Rows[3].Hidden && s.Rows[4].Collapsed && s.Rows[4].Style is { Italic: true }, "rows read back");
            True(s.Columns[3].Hidden && s.Columns[4].Hidden && s.Columns[6].Style?.FillColor == "DDEBF7", "columns read back");
            True(s.View.FrozenRows == 1 && s.View.FrozenColumns == 1 && s.View.ZoomScale == 85 && !s.View.ShowGridLines && !s.View.ShowZeros, "view read back");
            True(back["B"].View.Pane is { State: PaneState.Split, XSplit: 2400, YSplit: 1800 }, "split read back");
            True(ReferenceEquals(back.ActiveSheet, back["B"]) && back["C"].Visibility == SheetVisibility.VeryHidden, "active/visibility read back");

            Throws<InvalidOperationException>(() =>
            {
                using var w2 = XlsxWriter.Create(new MemoryStream());
                w2.AddSheet("only").Visibility = SheetVisibility.Hidden;
                w2.Save();
            }, "no visible sheet");
            Throws<InvalidOperationException>(() =>
            {
                using var w3 = XlsxWriter.Create(new MemoryStream());
                w3.AddSheet("v");
                w3.AddSheet("h").Visibility = SheetVisibility.Hidden;
                w3.ActiveSheetIndex = 1;
                w3.Save();
            }, "hidden active sheet");
            Throws<ArgumentException>(() => new Workbook().AddSheet("t").TabColor = "red", "tab colour validation");
        });

        Check("LibreOffice keeps the writer's panes, hidden rows/columns and tab colour on re-save", () =>
        {
            var re = LibreOfficeResave(wPath, outputDir);
            if (re is null) { Console.WriteLine("          (LibreOffice not available: skipped)"); return; }
            var s = Workbook.Load(re)["A"];
            True(s.View.FrozenRows == 1 && s.View.FrozenColumns == 1, $"frozen panes after LibreOffice ({s.View.Pane?.State})");
            True(s.Rows[3].Hidden && s.Columns[3].Hidden, "hidden row and column after LibreOffice");
            Eq("C00000", s.TabColor, "tab colour after LibreOffice");
            True(Math.Abs(s.Rows[2].Height - 32) < 0.6, $"row height after LibreOffice ({s.Rows[2].Height})");
        });

        // ---- 2. Conditional formatting and data validation --------------------------------

        Check("conditional formats (read): types, priorities, dxf styles, scales, bars with x14 details, icon set, extension rule", () =>
        {
            var s = Workbook.Load(fx)["Data"];
            var rules = s.ConditionalFormats.SelectMany(f => f.Rules.Select(r => (f.SqRef, r))).ToList();
            Eq(7, rules.Count, "rule count");
            var cellIs = rules.Single(x => x.r.Type == ConditionalRuleType.CellIs).r;
            True(cellIs.Operator == ComparisonOp.GreaterThan && cellIs.Formulas.SequenceEqual(new[] { "80" }) && cellIs.Priority == 1, "cellIs");
            True(cellIs.Style is { FontColor: "9C0006", FillColor: "FFC7CE" } && cellIs.DxfId == 0 && cellIs.LoadedFormat is not null, "dxf resolved");
            var scale = rules.Single(x => x.r.Type == ConditionalRuleType.ColorScale).r.ColorScale!;
            True(scale.Values[1] is { Type: ConditionalValueType.Percentile, Value: "50" } && scale.Colors.SequenceEqual(new[] { "F8696B", "FFEB84", "63BE7B" }), "colour scale");
            var bar = rules.Single(x => x.r.Type == ConditionalRuleType.DataBar).r.DataBar!;
            True(bar.HasExtension && !bar.Gradient && bar.MinLength == 0 && bar.MaxLength == 100, "x14 bar details");
            True(bar.Min.Type == ConditionalValueType.AutoMin && bar.BorderColor == "638EC6" && bar.NegativeFillColor == "FF0000" && bar.AxisPosition == DataBarAxisPosition.Middle, "x14 bar colours");
            var expr = rules.Single(x => x.r.Type == ConditionalRuleType.Expression && !x.r.FromExtension).r;
            True(expr.StopIfTrue && expr.Formulas[0] == "$B2<50" && expr.Style?.Bold == true, "expression");
            var text = rules.Single(x => x.r.Type == ConditionalRuleType.ContainsText).r;
            Eq("late", text.Text, "text rule");
            var icons = rules.Single(x => x.r.Type == ConditionalRuleType.IconSet).r.IconSet!;
            True(icons.IconSet == "3Arrows" && icons.Values.Count == 3, "icon set");
            var ext = rules.Single(x => x.r.FromExtension);
            True(ext.r.Type == ConditionalRuleType.Expression && ext.r.Formulas[0] == "Lists!$A$1=\"on\"" && ext.r.Style?.Italic == true && ext.SqRef == "F2:F11", "extension-only rule with inline dxf");
            var tp = Workbook.Load(fx)["Data"].ConditionalFormats.SelectMany(f => f.Rules).Count(r => r.Type == ConditionalRuleType.TimePeriod);
            Eq(0, tp, "no stray rules");
        });

        Check("data validation (read): kinds, operators, error style, messages, x14 cross-sheet list", () =>
        {
            var s = Workbook.Load(fx)["Data"];
            Eq(3, s.DataValidations.Count, "count");
            var whole = s.DataValidations.Single(d => d.Validation.Kind == DataValidationType.WholeNumber);
            True(whole.Range == "B2:B11" && whole.Validation.Comparison == ComparisonOp.Between && whole.Validation.Formula1 == "0" && whole.Validation.Formula2 == "100", "whole number");
            True(whole.Validation.ErrorStyle == DataValidationErrorStyle.Warning && whole.Validation.ShowErrorMessage == true, "error style");
            var list = s.DataValidations.Single(d => d.Range == "D2:D11").Validation;
            True(list.Kind == DataValidationType.List && list.Formula1 == "\"On time,Late,Cancelled\"" && list.PromptTitle == "Status" && list.ShowInputMessage == true, "inline list");
            var x14 = s.DataValidations.Single(d => d.Range == "A2:A11").Validation;
            Eq("Lists!$A$1:$A$3", x14.Formula1, "x14 list");
        });

        Check("round-trip: conditional formats and validation survive load → save → load (incl. x14 blocks)", () =>
        {
            var saved = Path.Combine(outputDir, "f160_fidelity_resaved.xlsx");
            Workbook.Load(fx).SaveTo(saved);
            var a = Workbook.Load(fx)["Data"];
            var b = Workbook.Load(saved)["Data"];
            string Sig(Sheet sh) => string.Join("|", sh.ConditionalFormats.SelectMany(f => f.Rules.Select(r =>
                $"{f.SqRef};{r.Type};{r.Priority};{r.StopIfTrue};{r.Operator};{string.Join(",", r.Formulas)};{r.Text};" +
                $"{r.Style?.FontColor};{r.Style?.FillColor};{r.Style?.Bold};{r.Style?.Italic};" +
                $"{(r.ColorScale is null ? "" : string.Join(",", r.ColorScale.Colors))};" +
                $"{(r.DataBar is null ? "" : $"{r.DataBar.Color},{r.DataBar.Gradient},{r.DataBar.MinLength},{r.DataBar.MaxLength},{r.DataBar.NegativeFillColor},{r.DataBar.AxisPosition},{r.DataBar.Min.Type}")};" +
                $"{r.IconSet?.IconSet};{r.FromExtension}")).OrderBy(x => x));
            Eq(Sig(a), Sig(b), "rules identical");
            string DvSig(Sheet sh) => string.Join("|", sh.DataValidations.Select(d =>
                $"{d.Range};{d.Validation.Kind};{d.Validation.Comparison};{d.Validation.Formula1};{d.Validation.Formula2};{d.Validation.ErrorStyle};{d.Validation.ShowErrorMessage};{d.Validation.ShowInputMessage};{d.Validation.PromptTitle}").OrderBy(x => x));
            Eq(DvSig(a), DvSig(b), "validations identical");
            var styles = PartText(saved, "xl/styles.xml");
            True(styles.Contains("<dxfs count="), "dxfs written");
        });

        Check("LibreOffice opens the saved conditional formats and keeps them", () =>
        {
            var saved = Path.Combine(outputDir, "f160_fidelity_resaved.xlsx");
            var re = LibreOfficeResave(saved, outputDir);
            if (re is null) { Console.WriteLine("          (LibreOffice not available: skipped)"); return; }
            var s = Workbook.Load(re)["Data"];
            var types = s.ConditionalFormats.SelectMany(f => f.Rules).Select(r => r.Type).ToHashSet();
            True(types.SetEquals(new[] { ConditionalRuleType.CellIs, ConditionalRuleType.ColorScale, ConditionalRuleType.DataBar, ConditionalRuleType.Expression, ConditionalRuleType.ContainsText, ConditionalRuleType.IconSet }),
                 $"types after LibreOffice: {string.Join(",", types)}");
            Eq(3, s.DataValidations.Count, "validations after LibreOffice");
        });

        Check("ConditionalFormat.CellIs now writes its style as a dxf (shows in Excel)", () =>
        {
            var p = Path.Combine(outputDir, "f160_cellis.xlsx");
            using (var w = XlsxWriter.Create(p))
            {
                var sh = w.AddSheet("C");
                sh.WriteRow(150);
                sh.AddConditionalFormat("A1:A10", ConditionalFormat.CellIs(ComparisonOp.GreaterThan, 100, new CellStyleBuilder().Bold().Foreground("#C62828").Background("#FFEBEE").Build()));
                w.Save();
            }
            var st = PartText(p, "xl/styles.xml");
            True(st.Contains("<dxfs count=\"1\"><dxf><font><b /><color rgb=\"FFC62828\" /></font><fill><patternFill><bgColor rgb=\"FFFFEBEE\" /></patternFill></fill></dxf></dxfs>"), "dxf content");
            True(PartText(p, "xl/worksheets/sheet1.xml").Contains("<cfRule type=\"cellIs\" priority=\"1\" operator=\"greaterThan\" dxfId=\"0\"><formula>100</formula></cfRule>"), "rule");
            var r = Workbook.Load(p)["C"].EvaluateConditionalFormats().Evaluate(1, 1)!;
            True(r.FillColor == "FFEBEE" && r.FontColor == "C62828" && r.Bold == true, "evaluated back");
        });

        Check("evaluator on the fixture: priority, stopIfTrue, colour scale, icon set, data bar, text, expression, not-evaluated", () =>
        {
            var s = Workbook.Load(fx)["Data"];
            var ev = s.EvaluateConditionalFormats(new ConditionalFormatOptions { Today = today });
            // B2 = 90: cellIs > 80 (priority 1) wins the fill over the colour scale.
            var b2 = ev.Evaluate(2, 2)!;
            True(b2.FillColor == "FFC7CE" && b2.FontColor == "9C0006", "cellIs beats the colour scale");
            True(b2.Icon is { IconSet: "3Arrows", Index: 2, Count: 3 }, $"icon for 90 ({b2.Icon?.Index})");
            // B4 = 32.5: half way from min (10) to the median (55) → half way from red to yellow.
            var b4 = ev.Evaluate(4, 2)!;
            Eq("FCAA78", b4.FillColor, "colour scale interpolation");
            True(b4.Icon!.Index == 0, "low icon");
            Eq("F8696B", ev.Evaluate(11, 2)!.FillColor, "minimum colour (10)");
            // Expression $B2<50 → bold, and stopIfTrue blocks nothing else on A (only rule).
            True(ev.Evaluate(4, 1)!.Bold == true && ev.Evaluate(2, 1)!.Bold is null, "expression with relative row");
            True(ev.Evaluate(3, 4)!.FillColor == "FFC7CE" && ev.Evaluate(2, 4)!.IsEmpty, "contains text 'late' (case-insensitive)");
            var c2 = ev.Evaluate(2, 3)!.DataBar!;
            True(c2.Minimum == -30 && c2.Maximum == 60 && c2.LengthPercent > 0, $"auto min/max bar ({c2.Minimum}..{c2.Maximum})");
            True(ev.Evaluate(5, 3)!.DataBar is { IsNegative: true, FillColor: "FF0000" }, "negative bar colour");
            var f2 = ev.Evaluate(2, 6)!;
            True(f2.NotEvaluated.Count == 1 && f2.NotEvaluated[0].FromExtension, "other-sheet rule reported as not evaluated");
            True(ev.Evaluate(1, 1) is null, "header row not covered");
            var applied = b2.ApplyTo(new CellStyle { FontName = "Arial", Bold = true });
            True(applied.FillColor == "FFC7CE" && applied.FontColor == "9C0006" && applied.FontName == "Arial" && applied.Bold, "ApplyTo merges onto the base style");
        });

        Check("evaluator: cellIs (text, between, references), top/bottom, average ± stdDev, duplicates, blanks, errors, time periods", () =>
        {
            var wb = new Workbook();
            var s = wb.AddSheet("E");
            double[] nums = { 5, 10, 15, 20, 25, 30, 35, 40, 45, 50 };
            for (int i = 0; i < nums.Length; i++) s.Cell(i + 1, 1).Value = nums[i];
            string?[] texts = { "apple", "Banana", "apple", "cherry", null, " ", "#N/A", "kiwi", "APPLE", "date" };
            for (int i = 0; i < texts.Length; i++) if (texts[i] is not null) s.Cell(i + 1, 2).Value = texts[i];
            s.Cell("D1").Value = 20;   // threshold cell
            var dates = new[] { today, today.AddDays(-1), today.AddDays(1), today.AddDays(-5), today.AddMonths(-1), today.AddDays(-7), today.AddDays(7), today.AddDays(-30) };
            for (int i = 0; i < dates.Length; i++) s.Cell(i + 1, 3).Value = dates[i];
            var hit = new DifferentialStyle { FillColor = "00FF00" };
            ConditionalRule Rule(ConditionalRuleType t, Action<ConditionalRule>? set = null)
            {
                var r = new ConditionalRule(t) { Style = hit };
                set?.Invoke(r);
                return r;
            }
            bool Hit(ConditionalRule r, string range, int row, int col)
            {
                var f = new ConditionalFormatting(new[] { range }, new[] { r });
                var res = new ConditionalFormatEvaluator(new[] { f }, null, new MapSource(s), new ConditionalFormatOptions { Today = today }).Evaluate(row, col);
                if (res is { NotEvaluated.Count: > 0 }) throw new Exception($"{r.Type} not evaluated");
                return res is { FillColor: "00FF00" };
            }
            var gtRef = Rule(ConditionalRuleType.CellIs, r => { r.Operator = ComparisonOp.GreaterThan; r.Formulas.Add("$D$1"); });
            True(Hit(gtRef, "A1:A10", 5, 1) && !Hit(gtRef, "A1:A10", 4, 1), "cellIs vs absolute reference");
            var between = Rule(ConditionalRuleType.CellIs, r => { r.Operator = ComparisonOp.Between; r.Formulas.Add("30"); r.Formulas.Add("10"); });
            True(Hit(between, "A1:A10", 2, 1) && Hit(between, "A1:A10", 6, 1) && !Hit(between, "A1:A10", 7, 1), "between with reversed bounds");
            var eqText = Rule(ConditionalRuleType.CellIs, r => { r.Operator = ComparisonOp.Equal; r.Formulas.Add("\"APPLE\""); });
            True(Hit(eqText, "B1:B10", 1, 2) && !Hit(eqText, "B1:B10", 2, 2), "cellIs text, case-insensitive");
            var odd = Rule(ConditionalRuleType.Expression, r => r.Formulas.Add("MOD(ROW(),2)=1"));
            True(Hit(odd, "A1:A10", 3, 1) && !Hit(odd, "A1:A10", 4, 1), "banding expression");
            var rel = Rule(ConditionalRuleType.Expression, r => r.Formulas.Add("AND(A1>=SUM($A$1:$A$2),A1<AVERAGE($A$1:$A$10))"));
            True(Hit(rel, "A1:A10", 3, 1) && Hit(rel, "A1:A10", 5, 1) && !Hit(rel, "A1:A10", 6, 1), "functions over ranges");
            var top3 = Rule(ConditionalRuleType.Top10, r => r.Rank = 3);
            True(Hit(top3, "A1:A10", 8, 1) && !Hit(top3, "A1:A10", 7, 1), "top 3");
            var bottom20 = Rule(ConditionalRuleType.Top10, r => { r.Rank = 20; r.Percent = true; r.Bottom = true; });
            True(Hit(bottom20, "A1:A10", 2, 1) && !Hit(bottom20, "A1:A10", 3, 1), "bottom 20%");
            var above = Rule(ConditionalRuleType.AboveAverage);
            True(Hit(above, "A1:A10", 6, 1) && !Hit(above, "A1:A10", 5, 1), "above average (27.5)");
            var sd = Rule(ConditionalRuleType.AboveAverage, r => r.StdDev = 1);
            True(Hit(sd, "A1:A10", 10, 1) && !Hit(sd, "A1:A10", 8, 1), "1 std dev above (27.5 + 15.14)");
            var dup = Rule(ConditionalRuleType.DuplicateValues);
            True(Hit(dup, "B1:B10", 1, 2) && Hit(dup, "B1:B10", 9, 2) && !Hit(dup, "B1:B10", 2, 2), "duplicates (case-insensitive)");
            var uniq = Rule(ConditionalRuleType.UniqueValues);
            True(Hit(uniq, "B1:B10", 4, 2) && !Hit(uniq, "B1:B10", 3, 2), "unique");
            var begins = Rule(ConditionalRuleType.BeginsWith, r => r.Text = "ba");
            var ends = Rule(ConditionalRuleType.EndsWith, r => r.Text = "RY");
            True(Hit(begins, "B1:B10", 2, 2) && Hit(ends, "B1:B10", 4, 2), "begins / ends with");
            var blanks = Rule(ConditionalRuleType.ContainsBlanks);
            True(Hit(blanks, "B1:B10", 5, 2) && Hit(blanks, "B1:B10", 6, 2) && !Hit(blanks, "B1:B10", 1, 2), "blanks (empty and whitespace)");
            var errs = Rule(ConditionalRuleType.ContainsErrors);
            True(Hit(errs, "B1:B10", 7, 2) && !Hit(errs, "B1:B10", 8, 2), "errors");
            bool P(ConditionalTimePeriod p, int row) => Hit(Rule(ConditionalRuleType.TimePeriod, r => r.TimePeriod = p), "C1:C8", row, 3);
            True(P(ConditionalTimePeriod.Today, 1) && P(ConditionalTimePeriod.Yesterday, 2) && P(ConditionalTimePeriod.Tomorrow, 3), "today/yesterday/tomorrow");
            True(P(ConditionalTimePeriod.Last7Days, 4) && !P(ConditionalTimePeriod.Last7Days, 6), "last 7 days");
            True(P(ConditionalTimePeriod.LastMonth, 5) && P(ConditionalTimePeriod.LastWeek, 6) && P(ConditionalTimePeriod.NextWeek, 7) && P(ConditionalTimePeriod.ThisMonth, 1), "months and weeks");
            var vlookup = Rule(ConditionalRuleType.Expression, r => r.Formulas.Add("VLOOKUP(A1,$A$1:$B$10,2,FALSE)=\"x\""));
            var res = new ConditionalFormatEvaluator(new[] { new ConditionalFormatting(new[] { "A1:A10" }, new[] { vlookup }) }, null, new MapSource(s)).Evaluate(1, 1)!;
            True(res.NotEvaluated.Count == 1 && res.IsEmpty, "unsupported function reported, not guessed");
            // Priority and stopIfTrue across blocks.
            var first = Rule(ConditionalRuleType.CellIs, r => { r.Priority = 1; r.StopIfTrue = true; r.Operator = ComparisonOp.GreaterThan; r.Formulas.Add("0"); r.Style = new DifferentialStyle { Bold = true }; });
            var second = Rule(ConditionalRuleType.CellIs, r => { r.Priority = 2; r.Operator = ComparisonOp.GreaterThan; r.Formulas.Add("0"); });
            var both = new ConditionalFormatEvaluator(new[] { new ConditionalFormatting(new[] { "A1:A10" }, new[] { second }), new ConditionalFormatting(new[] { "A1" }, new[] { first }) }, null, new MapSource(s)).Evaluate(1, 1)!;
            True(both.Bold == true && both.FillColor is null && both.MatchedRules.Count == 1, "stopIfTrue on the higher-priority rule");
        });

        Check("writer: every rule type written, read back equal, and saved formulas generated for text/time rules", () =>
        {
            var p = Path.Combine(outputDir, "f160_rules_written.xlsx");
            var st = new DifferentialStyle { FillColor = "FFEB9C", FontColor = "9C5700", NumberFormat = "0.0%", BorderBottom = new Border(BorderStyle.Thin, "9C5700") };
            var rules = new List<ConditionalRule>
            {
                new(ConditionalRuleType.ContainsText) { Text = "late", Style = st },
                new(ConditionalRuleType.TimePeriod) { TimePeriod = ConditionalTimePeriod.LastWeek, Style = st },
                new(ConditionalRuleType.Top10) { Rank = 5, Percent = true, Bottom = true, Style = st },
                new(ConditionalRuleType.AboveAverage) { AboveAverage = false, StdDev = 2, Style = st },
                new(ConditionalRuleType.DuplicateValues) { Style = st },
                new(ConditionalRuleType.ContainsBlanks) { Style = st, StopIfTrue = true },
                new(ConditionalRuleType.IconSet) { IconSet = new IconSetInfo("4Rating", new[] { new ConditionalValue(ConditionalValueType.Percent, "0"), new ConditionalValue(ConditionalValueType.Percent, "25"), new ConditionalValue(ConditionalValueType.Percent, "50"), new ConditionalValue(ConditionalValueType.Percent, "75", greaterOrEqual: false) }) { Reverse = true, ShowValue = false } },
                new(ConditionalRuleType.DataBar) { DataBar = new DataBarInfo(new ConditionalValue(ConditionalValueType.AutoMin), new ConditionalValue(ConditionalValueType.AutoMax), "5B9BD5") { HasExtension = true, Gradient = false, MinLength = 0, MaxLength = 100, NegativeFillColor = "FF0000" } },
            };
            using (var w = XlsxWriter.Create(p))
            {
                var sh = w.AddSheet("R");
                for (int i = 0; i < 5; i++) sh.WriteRow(i * 10, "x");
                sh.AddConditionalFormatting(new ConditionalFormatting(new[] { "A1:A5", "C1:C5" }, rules));
                w.Save();
            }
            var x = PartText(p, "xl/worksheets/sheet1.xml");
            AssertSchemaOrder(x);
            True(x.Contains("<conditionalFormatting sqref=\"A1:A5 C1:C5\">"), "multi-range sqref");
            True(x.Contains("operator=\"containsText\"") && x.Contains("<formula>NOT(ISERROR(SEARCH(\"late\",A1)))</formula>"), "containsText formula generated");
            True(x.Contains("timePeriod=\"lastWeek\""), "time period");
            True(x.Contains("<iconSet iconSet=\"4Rating\" showValue=\"0\" reverse=\"1\">") && x.Contains("gte=\"0\""), "icon set");
            True(x.Contains("<x14:dataBar minLength=\"0\" maxLength=\"100\" gradient=\"0\">") && x.Contains("<x14:negativeFillColor rgb=\"FFFF0000\" />"), "x14 data bar");
            var back = Workbook.Load(p)["R"].ConditionalFormats.Single();
            True(back.Ranges.SequenceEqual(new[] { "A1:A5", "C1:C5" }), "ranges back");
            Eq(rules.Count, back.Rules.Count, "rules back");
            var priorities = back.Rules.Select(r => r.Priority).ToArray();
            True(priorities.SequenceEqual(Enumerable.Range(1, rules.Count)), $"priorities assigned in order ({string.Join(",", priorities)})");
            var bst = back.Rules[0].Style!;
            True(bst.FillColor == "FFEB9C" && bst.FontColor == "9C5700" && bst.NumberFormat == "0.0%" && bst.BorderBottom is { Style: BorderStyle.Thin }, "dxf round-trip");
            True(back.Rules[2] is { Rank: 5, Percent: true, Bottom: true } && back.Rules[3] is { AboveAverage: false, StdDev: 2 }, "top10/average attributes");
            True(back.Rules[5].StopIfTrue, "stopIfTrue");
            True(back.Rules[6].IconSet is { IconSet: "4Rating", Reverse: true, ShowValue: false } && !back.Rules[6].IconSet!.Values[3].GreaterOrEqual, "icon set back");
            True(back.Rules[7].DataBar is { HasExtension: true, Gradient: false, NegativeFillColor: "FF0000" } && back.Rules[7].DataBar!.Min.Type == ConditionalValueType.AutoMin, "data bar back");
        });

        Check("data validation (write): new factories and options round-trip", () =>
        {
            var p = Path.Combine(outputDir, "f160_dv.xlsx");
            var wb = new Workbook();
            var s = wb.AddSheet("V");
            s.Cell("A1").Value = 1;
            var custom = DataValidation.Custom("COUNTIF($A:$A,A1)=1");
            custom.ErrorStyle = DataValidationErrorStyle.Information;
            custom.ShowErrorMessage = true;
            var time = DataValidation.Time(TimeSpan.FromHours(9), TimeSpan.FromHours(17));
            time.ShowInputMessage = false; time.PromptTitle = "Hours";
            var date = DataValidation.Create(DataValidationType.Date, ComparisonOp.GreaterThan, "TODAY()");
            s.AddDataValidation("A1:A100", custom);
            s.AddDataValidation("B1:B100", time);
            s.AddDataValidation("C1:C100", date);
            wb.SaveTo(p);
            var b = Workbook.Load(p)["V"].DataValidations;
            True(b[0].Validation is { Kind: DataValidationType.Custom, Formula1: "COUNTIF($A:$A,A1)=1", ErrorStyle: DataValidationErrorStyle.Information, ShowErrorMessage: true }, "custom");
            True(b[1].Validation is { Kind: DataValidationType.Time, Comparison: ComparisonOp.Between, ShowInputMessage: false, PromptTitle: "Hours" }, "time (prompt kept, not shown)");
            Eq(0.375, double.Parse(b[1].Validation.Formula1!, CultureInfo.InvariantCulture), "time bound");
            True(b[2].Validation is { Kind: DataValidationType.Date, Comparison: ComparisonOp.GreaterThan, Formula1: "TODAY()" }, "date with formula");
        });

        Check("streaming: ReadLayout exposes view, tab colour, conditional formats and validation", () =>
        {
            using var r = XlsxReader.Open(fx);
            var L = r.Sheet("Data").ReadLayout();
            True(L.View.Pane is { State: PaneState.Frozen } && L.TabColor == "00B050", "view and tab colour");
            Eq(6, L.ConditionalFormats.Count, "conditional format blocks (incl. extension)");
            Eq(3, L.DataValidations.Count, "validations");
        });

        // ---- 3. Drawings -------------------------------------------------------------------

        Check("drawings (model): picture anchor, bytes, crop, alt text, rotation; shape text and colours; chart; group", () =>
        {
            var d = Workbook.Load(fx)["Data"].Drawings;
            Eq(4, d.Count, "top-level objects");
            var pic = d[0];
            True(pic.Kind == DrawingKind.Picture && pic.Name == "Picture 1" && pic.Description == "Apulki logo", "picture identity");
            True(pic.Anchor is { Type: DrawingAnchorType.TwoCell, EditAs: DrawingEditAs.OneCell }, "anchor type");
            Eq(new DrawingPoint(7, 19050, 2, 38100), pic.Anchor.From!.Value, "from (1-based)");
            Eq(new DrawingPoint(10, 0, 9, 0), pic.Anchor.To!.Value, "to");
            True(pic.ContentType == "image/png" && pic.ImagePartName == "/xl/media/image1.png", "image part");
            True(pic.GetImageBytes()!.SequenceEqual(Png1x1), "image bytes");
            Eq(new PictureCrop(0.1, 0, 0.05, 0), pic.Crop!.Value, "crop");
            True(pic.Rotation == 90 && pic.ExtentWidthEmu == 1905000, "rotation and size");
            var tb = d[1];
            True(tb.Kind == DrawingKind.Shape && tb.Geometry == "roundRect" && tb.Text == "Total: 42\nLine 2", "text box");
            True(tb.FillColor == "2F5597" && tb.LineColor == "FF0000" && tb.LineWidthEmu == 12700, "accent1 darker 25% fill, red line");
            True(tb.Anchor is { Type: DrawingAnchorType.OneCell, WidthEmu: 2286000, HeightEmu: 457200 } && tb.Anchor.From!.Value.Row == 14, "one-cell anchor");
            var chart = d[2];
            True(chart.Kind == DrawingKind.Chart && chart.ChartPartName == "/xl/charts/chart1.xml", "chart");
            True(chart.Anchor is { Type: DrawingAnchorType.Absolute, XEmu: 914400, YEmu: 914400, WidthEmu: 3000000 }, "absolute anchor");
            var grp = d[3];
            True(grp.Kind == DrawingKind.Group && grp.Children.Count == 2 && grp.ChildExtentWidthEmu == 100, "group");
            var oval = grp.Children[0];
            True(oval.Hidden && oval.NoFill && oval.FillColor is null && oval.LineColor == "ED7D31" && oval.Geometry == "ellipse", "style-referenced line colour, no fill");
            Eq(DrawingKind.Connector, grp.Children[1].Kind, "connector");
            Eq(2.0, Emu.ToPoints(25400), "EMU helper");
        });

        Check("drawings (streaming): lazy image bytes, cached list; LoadImageData=false", () =>
        {
            DrawingObject first;
            using (var r = XlsxReader.Open(fx))
            {
                var sr = r.Sheet("Data");
                var d = sr.ReadDrawings();
                True(ReferenceEquals(d, sr.ReadDrawings()) && d.Count == 4, "objects, cached");
                first = d[0];
                True(first.GetImageBytes()!.SequenceEqual(Png1x1), "lazy bytes while open");
                Eq(0, r.Sheet("Lists").ReadDrawings().Count, "sheet without drawing");
            }
            Throws<ObjectDisposedException>(() => first.GetImageBytes(), "bytes need the reader open");
            var meta = Workbook.Load(fx, null, new XlsxReaderOptions { LoadImageData = false })["Data"].Drawings[0];
            True(meta.GetImageBytes() is null && meta.ContentType == "image/png", "metadata only");
        });

        Console.WriteLine();
        Console.WriteLine($"  LayoutFidelityTest: {pass} passed, {fail} failed.");
        Console.WriteLine();
        if (fail > 0) throw new Exception($"LayoutFidelityTest: {fail} check(s) failed.");
    }

    // ---- Fixture -------------------------------------------------------------------------

    private static void WriteFixture(string path)
    {
        string[] names = { "Asha", "Bala", "Chitra", "Dev", "Esha", "Farid", "Gita", "Hari", "Indu", "Jai" };
        double[] scores = { 90, 45, 32.5, 70, 60, 50, 80, 40, 100, 10 };
        double[] deltas = { 20, -10, 5, -30, 60, 0, 15, -5, 25, 10 };
        string[] status = { "On time", "Late", "On time", "Cancelled", "Running late", "On time", "On time", "Late", "On time", "On time" };
        var rows = new StringBuilder();
        rows.Append("<row r=\"1\">");
        foreach (var (h, c) in new[] { ("Name", "A"), ("Score", "B"), ("Delta", "C"), ("Status", "D"), ("Date", "F") })
            rows.Append($"<c r=\"{c}1\" t=\"inlineStr\"><is><t>{h}</t></is></c>");
        rows.Append("</row>");
        for (int i = 0; i < 10; i++)
        {
            int r = i + 2;
            string attrs = r == 3 ? " ht=\"30\" customHeight=\"1\"" : r == 7 ? " hidden=\"1\" outlineLevel=\"1\"" : "";
            rows.Append($"<row r=\"{r}\"{attrs}>");
            rows.Append($"<c r=\"A{r}\" t=\"inlineStr\"><is><t>{names[i]}</t></is></c>");
            rows.Append($"<c r=\"B{r}\"><v>{scores[i].ToString(CultureInfo.InvariantCulture)}</v></c>");
            rows.Append($"<c r=\"C{r}\"><v>{deltas[i].ToString(CultureInfo.InvariantCulture)}</v></c>");
            rows.Append($"<c r=\"D{r}\" t=\"inlineStr\"><is><t>{status[i]}</t></is></c>");
            rows.Append($"<c r=\"F{r}\" s=\"2\"><v>{46323 - i}</v></c>");
            rows.Append("</row>");
        }
        rows.Append("<row r=\"12\" s=\"1\" customFormat=\"1\" ht=\"20\" customHeight=\"1\"/>");

        string sheet1 =
            "<sheetPr><tabColor rgb=\"FF00B050\"/><outlinePr summaryBelow=\"0\"/></sheetPr>" +
            "<dimension ref=\"A1:F12\"/>" +
            "<sheetViews><sheetView tabSelected=\"1\" zoomScale=\"120\" zoomScaleNormal=\"120\" workbookViewId=\"0\">" +
            "<pane xSplit=\"1\" ySplit=\"1\" topLeftCell=\"B2\" activePane=\"bottomRight\" state=\"frozen\"/>" +
            "<selection pane=\"topRight\" activeCell=\"B1\" sqref=\"B1\"/><selection pane=\"bottomLeft\" activeCell=\"A2\" sqref=\"A2\"/>" +
            "<selection pane=\"bottomRight\" activeCell=\"C5\" sqref=\"C5:D6\"/></sheetView></sheetViews>" +
            "<sheetFormatPr defaultRowHeight=\"15\" outlineLevelRow=\"1\" outlineLevelCol=\"1\"/>" +
            "<cols><col min=\"1\" max=\"1\" width=\"18\" customWidth=\"1\"/><col min=\"5\" max=\"5\" width=\"9\" hidden=\"1\" outlineLevel=\"1\" customWidth=\"1\"/></cols>" +
            "<sheetData>" + rows + "</sheetData>" +
            "<conditionalFormatting sqref=\"B2:B11\"><cfRule type=\"cellIs\" dxfId=\"0\" priority=\"1\" operator=\"greaterThan\"><formula>80</formula></cfRule>" +
            "<cfRule type=\"colorScale\" priority=\"5\"><colorScale><cfvo type=\"min\"/><cfvo type=\"percentile\" val=\"50\"/><cfvo type=\"max\"/>" +
            "<color rgb=\"FFF8696B\"/><color rgb=\"FFFFEB84\"/><color rgb=\"FF63BE7B\"/></colorScale></cfRule></conditionalFormatting>" +
            "<conditionalFormatting sqref=\"C2:C11\"><cfRule type=\"dataBar\" priority=\"2\"><dataBar><cfvo type=\"min\"/><cfvo type=\"max\"/><color rgb=\"FF638EC6\"/></dataBar>" +
            $"<extLst><ext uri=\"{{B025F937-C7B1-47D3-B67F-A62EFF666E3E}}\" xmlns:x14=\"{X14}\"><x14:id>{{DB000000-0000-0000-0000-000000000001}}</x14:id></ext></extLst></cfRule></conditionalFormatting>" +
            "<conditionalFormatting sqref=\"A2:A11\"><cfRule type=\"expression\" dxfId=\"1\" priority=\"3\" stopIfTrue=\"1\"><formula>$B2&lt;50</formula></cfRule></conditionalFormatting>" +
            "<conditionalFormatting sqref=\"D2:D11\"><cfRule type=\"containsText\" dxfId=\"0\" priority=\"4\" operator=\"containsText\" text=\"late\"><formula>NOT(ISERROR(SEARCH(\"late\",D2)))</formula></cfRule></conditionalFormatting>" +
            "<conditionalFormatting sqref=\"B2:B11\"><cfRule type=\"iconSet\" priority=\"6\"><iconSet iconSet=\"3Arrows\"><cfvo type=\"percent\" val=\"0\"/><cfvo type=\"percent\" val=\"33\"/><cfvo type=\"percent\" val=\"67\"/></iconSet></cfRule></conditionalFormatting>" +
            "<dataValidations count=\"2\"><dataValidation type=\"whole\" errorStyle=\"warning\" operator=\"between\" allowBlank=\"1\" showErrorMessage=\"1\" sqref=\"B2:B11\"><formula1>0</formula1><formula2>100</formula2></dataValidation>" +
            "<dataValidation type=\"list\" allowBlank=\"1\" showInputMessage=\"1\" promptTitle=\"Status\" prompt=\"Pick one\" sqref=\"D2:D11\"><formula1>\"On time,Late,Cancelled\"</formula1></dataValidation></dataValidations>" +
            "<drawing r:id=\"rId1\"/>" +
            "<extLst>" +
            $"<ext uri=\"{{78C0D931-6437-407d-A8EE-F0AAD7539E65}}\" xmlns:x14=\"{X14}\"><x14:conditionalFormattings>" +
            $"<x14:conditionalFormatting xmlns:xm=\"{Xm}\"><x14:cfRule type=\"dataBar\" id=\"{{DB000000-0000-0000-0000-000000000001}}\">" +
            "<x14:dataBar minLength=\"0\" maxLength=\"100\" gradient=\"0\" border=\"1\" negativeBarBorderColorSameAsPositive=\"0\" axisPosition=\"middle\">" +
            "<x14:cfvo type=\"autoMin\"/><x14:cfvo type=\"autoMax\"/><x14:borderColor rgb=\"FF638EC6\"/><x14:negativeFillColor rgb=\"FFFF0000\"/>" +
            "<x14:negativeBorderColor rgb=\"FFFF0000\"/><x14:axisColor rgb=\"FF000000\"/></x14:dataBar></x14:cfRule><xm:sqref>C2:C11</xm:sqref></x14:conditionalFormatting>" +
            $"<x14:conditionalFormatting xmlns:xm=\"{Xm}\"><x14:cfRule type=\"expression\" priority=\"7\" id=\"{{EE000000-0000-0000-0000-000000000002}}\">" +
            "<xm:f>Lists!$A$1=\"on\"</xm:f><x14:dxf><font><i/></font></x14:dxf></x14:cfRule><xm:sqref>F2:F11</xm:sqref></x14:conditionalFormatting>" +
            "</x14:conditionalFormattings></ext>" +
            $"<ext uri=\"{{CCE6A557-97BC-4b89-ADB6-D9C93CAAB3DF}}\" xmlns:x14=\"{X14}\"><x14:dataValidations count=\"1\" xmlns:xm=\"{Xm}\">" +
            "<x14:dataValidation type=\"list\" allowBlank=\"1\" showErrorMessage=\"1\"><x14:formula1><xm:f>Lists!$A$1:$A$3</xm:f></x14:formula1><xm:sqref>A2:A11</xm:sqref></x14:dataValidation>" +
            "</x14:dataValidations></ext></extLst>";

        string sheet2 = "<sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>on</t></is></c></row></sheetData>";

        string styles =
            "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"yyyy-mm-dd\"/></numFmts>" +
            "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
            "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
            "<borders count=\"1\"><border/></borders>" +
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
            "<cellXfs count=\"3\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
            "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/></cellXfs>" +
            "<dxfs count=\"2\"><dxf><font><color rgb=\"FF9C0006\"/></font><fill><patternFill><bgColor rgb=\"FFFFC7CE\"/></patternFill></fill></dxf>" +
            "<dxf><font><b/></font></dxf></dxfs>";

        string drawing =
            "<xdr:wsDr xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
            $"xmlns:r=\"{R}\">" +
            "<xdr:twoCellAnchor editAs=\"oneCell\"><xdr:from><xdr:col>6</xdr:col><xdr:colOff>19050</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>38100</xdr:rowOff></xdr:from>" +
            "<xdr:to><xdr:col>9</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>8</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>" +
            "<xdr:pic><xdr:nvPicPr><xdr:cNvPr id=\"2\" name=\"Picture 1\" descr=\"Apulki logo\"/><xdr:cNvPicPr/></xdr:nvPicPr>" +
            "<xdr:blipFill><a:blip r:embed=\"rId1\"/><a:srcRect l=\"10000\" r=\"5000\"/><a:stretch><a:fillRect/></a:stretch></xdr:blipFill>" +
            "<xdr:spPr><a:xfrm rot=\"5400000\"><a:off x=\"100\" y=\"200\"/><a:ext cx=\"1905000\" cy=\"1333500\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></xdr:spPr></xdr:pic><xdr:clientData/></xdr:twoCellAnchor>" +
            "<xdr:oneCellAnchor><xdr:from><xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>13</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from><xdr:ext cx=\"2286000\" cy=\"457200\"/>" +
            "<xdr:sp macro=\"\" textlink=\"\"><xdr:nvSpPr><xdr:cNvPr id=\"3\" name=\"TextBox 2\"/><xdr:cNvSpPr txBox=\"1\"/></xdr:nvSpPr>" +
            "<xdr:spPr><a:prstGeom prst=\"roundRect\"><a:avLst/></a:prstGeom><a:solidFill><a:schemeClr val=\"accent1\"><a:lumMod val=\"75000\"/></a:schemeClr></a:solidFill>" +
            "<a:ln w=\"12700\"><a:solidFill><a:srgbClr val=\"FF0000\"/></a:solidFill></a:ln></xdr:spPr>" +
            "<xdr:txBody><a:bodyPr/><a:p><a:r><a:t>Total</a:t></a:r><a:r><a:t>: 42</a:t></a:r></a:p><a:p><a:r><a:t>Line 2</a:t></a:r></a:p></xdr:txBody></xdr:sp><xdr:clientData/></xdr:oneCellAnchor>" +
            "<xdr:absoluteAnchor><xdr:pos x=\"914400\" y=\"914400\"/><xdr:ext cx=\"3000000\" cy=\"2000000\"/>" +
            "<xdr:graphicFrame macro=\"\"><xdr:nvGraphicFramePr><xdr:cNvPr id=\"4\" name=\"Chart 3\"/><xdr:cNvGraphicFramePr/></xdr:nvGraphicFramePr>" +
            "<xdr:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/></xdr:xfrm><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/chart\">" +
            "<c:chart xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" r:id=\"rId2\"/></a:graphicData></a:graphic></xdr:graphicFrame><xdr:clientData/></xdr:absoluteAnchor>" +
            "<xdr:twoCellAnchor><xdr:from><xdr:col>0</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>20</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>" +
            "<xdr:to><xdr:col>3</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>25</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>" +
            "<xdr:grpSp><xdr:nvGrpSpPr><xdr:cNvPr id=\"5\" name=\"Group 4\"/><xdr:cNvGrpSpPr/></xdr:nvGrpSpPr>" +
            "<xdr:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"100\" cy=\"100\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"100\" cy=\"100\"/></a:xfrm></xdr:grpSpPr>" +
            "<xdr:sp><xdr:nvSpPr><xdr:cNvPr id=\"6\" name=\"Oval 5\" hidden=\"1\"/><xdr:cNvSpPr/></xdr:nvSpPr><xdr:spPr><a:prstGeom prst=\"ellipse\"/><a:noFill/></xdr:spPr>" +
            "<xdr:style><a:lnRef idx=\"2\"><a:schemeClr val=\"accent2\"/></a:lnRef><a:fillRef idx=\"1\"><a:schemeClr val=\"accent1\"/></a:fillRef>" +
            "<a:effectRef idx=\"0\"><a:schemeClr val=\"accent1\"/></a:effectRef><a:fontRef idx=\"minor\"><a:schemeClr val=\"lt1\"/></a:fontRef></xdr:style></xdr:sp>" +
            "<xdr:cxnSp><xdr:nvCxnSpPr><xdr:cNvPr id=\"7\" name=\"Connector 6\"/><xdr:cNvCxnSpPr/></xdr:nvCxnSpPr><xdr:spPr><a:prstGeom prst=\"line\"/></xdr:spPr></xdr:cxnSp>" +
            "</xdr:grpSp><xdr:clientData/></xdr:twoCellAnchor>" +
            "</xdr:wsDr>";

        var parts = new List<(string Name, string? ContentType, byte[] Data)>();
        byte[] U(string s) => new UTF8Encoding(false).GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" + s);
        string Ws(string body) => $"<worksheet xmlns=\"{Main}\" xmlns:r=\"{R}\">{body}</worksheet>";
        string Rels(params (string Id, string Type, string Target)[] rels)
            => "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
               string.Concat(rels.Select(r => $"<Relationship Id=\"{r.Id}\" Type=\"{r.Type}\" Target=\"{r.Target}\"/>")) + "</Relationships>";

        parts.Add(("[Content_Types].xml", null, U(
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/><Default Extension=\"png\" ContentType=\"image/png\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
            "<Override PartName=\"/xl/drawings/drawing1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawing+xml\"/>" +
            "</Types>")));
        parts.Add(("_rels/.rels", null, U(Rels(("rId1", R + "/officeDocument", "xl/workbook.xml")))));
        parts.Add(("xl/workbook.xml", null, U(
            $"<workbook xmlns=\"{Main}\" xmlns:r=\"{R}\"><bookViews><workbookView activeTab=\"0\"/></bookViews><sheets>" +
            "<sheet name=\"Data\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"Lists\" sheetId=\"2\" state=\"hidden\" r:id=\"rId2\"/></sheets></workbook>")));
        parts.Add(("xl/_rels/workbook.xml.rels", null, U(Rels(
            ("rId1", R + "/worksheet", "worksheets/sheet1.xml"), ("rId2", R + "/worksheet", "worksheets/sheet2.xml"), ("rId3", R + "/styles", "styles.xml")))));
        parts.Add(("xl/styles.xml", null, U($"<styleSheet xmlns=\"{Main}\">{styles}</styleSheet>")));
        parts.Add(("xl/worksheets/sheet1.xml", null, U(Ws(sheet1))));
        parts.Add(("xl/worksheets/sheet2.xml", null, U(Ws(sheet2))));
        parts.Add(("xl/worksheets/_rels/sheet1.xml.rels", null, U(Rels(("rId1", R + "/drawing", "../drawings/drawing1.xml")))));
        parts.Add(("xl/drawings/drawing1.xml", null, U(drawing)));
        parts.Add(("xl/drawings/_rels/drawing1.xml.rels", null, U(Rels(("rId1", R + "/image", "../media/image1.png"), ("rId2", R + "/chart", "../charts/chart1.xml")))));
        parts.Add(("xl/media/image1.png", null, Png1x1));

        if (File.Exists(path)) File.Delete(path);
        using var fs = new FileStream(path, FileMode.CreateNew);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (name, _, data) in parts)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var s = e.Open();
            s.Write(data, 0, data.Length);
        }
    }

    // ---- Helpers ---------------------------------------------------------------------------

    /// <summary>An ICellValueSource over a sheet, written as an outside renderer would.</summary>
    private sealed class MapSource : ICellValueSource
    {
        private readonly Sheet _s;
        public MapSource(Sheet s) { _s = s; }
        public object? GetValue(int row, int column) => _s.PopulatedCells.FirstOrDefault(c => c.Row == row && c.Column == column)?.Value;
        public IEnumerable<(int Row, int Column, object? Value)> GetValues(int r1, int c1, int r2, int c2)
            => _s.PopulatedCells.Where(c => c.Row >= r1 && c.Row <= r2 && c.Column >= c1 && c.Column <= c2 && c.Value is not null)
                                .Select(c => (c.Row, c.Column, c.Value));
    }

    private static string PartText(string path, string part)
    {
        using var z = ZipFile.OpenRead(path);
        using var s = z.GetEntry(part)!.Open();
        return new StreamReader(s, Encoding.UTF8).ReadToEnd();
    }

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

    /// <summary>Re-saves an xlsx through LibreOffice (headless), or null when it is not installed.</summary>
    private static string? LibreOfficeResave(string xlsx, string outputDir)
    {
        try
        {
            var dir = Path.Combine(outputDir, "lo_resave");
            Directory.CreateDirectory(dir);
            var psi = new ProcessStartInfo("soffice", $"--headless --calc --convert-to xlsx:\"Calc MS Excel 2007 XML\" --outdir \"{dir}\" \"{xlsx}\"")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var proc = Process.Start(psi);
            if (proc is null || !proc.WaitForExit(120_000)) return null;
            var outPath = Path.Combine(dir, Path.GetFileName(xlsx));
            return File.Exists(outPath) ? outPath : null;
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }
}
