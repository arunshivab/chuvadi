using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.ManualTests;

/// <summary>
/// Column auto-fit (Sheets 1.3.0) across the Workbook model, the streaming SheetWriter and the
/// one-line export. Expected widths come from real font rendering (Pillow + the metric-compatible
/// fonts), not from the library's own tables: Calibri 11 "Apulki Medical Center, Baner, Pune"
/// is 14.18 em → 208 px → width (208 + 5) / 7 = 30.4 characters.
/// </summary>
internal static class AutoFitTest
{
    private const string LongText = "Apulki Medical Center, Baner, Pune";

    public static void Run(string outputDir)
    {
        Console.WriteLine("[AutoFitTest] Column auto-fit...");
        Console.WriteLine();
        int pass = 0, fail = 0;

        void Check(string label, Action body)
        {
            try { body(); Console.WriteLine($"    PASS: {label}"); pass++; }
            catch (Exception ex) { Console.WriteLine($"    FAIL: {label}"); Console.WriteLine($"          {ex.GetType().Name}: {ex.Message}"); fail++; }
        }

        static void Between(double lo, double hi, double actual, string what)
        {
            if (actual < lo || actual > hi) throw new Exception($"{what}: expected {lo}..{hi}, got {actual}");
        }

        static Sheet Back(string path) => Workbook.Load(path).Sheets[0];

        // ---- Workbook model ----------------------------------------------------------------

        var modelPath = Path.Combine(outputDir, "autofit_model.xlsx");
        Check("model: widths follow displayed content", () =>
        {
            var wb = new Workbook();
            var s = wb.AddSheet("Fit");
            s.Cell("A1").Value = "Name";
            s.Cell("A2").Value = LongText;
            s.Cell("B1").Value = "Amount";
            s.Cell("B2").Value = 1234567.891;
            s.Cell("B2").Style = new CellStyleBuilder().Format("#,##0.00").Build();          // "1,234,567.89"
            s.Cell("C1").Value = "Id";
            s.Cell("C2").Value = 7;                                                            // short → min width
            s.Cell("D1").Value = new string('x', 300);                                         // capped at 60
            s.Cell("E1").Value = new DateTime(2023, 3, 15);                                    // default yyyy-mm-dd
            s.Cell("F1").Value = "Big header";
            s.Cell("F1").Style = new CellStyleBuilder().Bold().Font("Calibri", 20).Build();
            s.AutoFitColumns();
            wb.SaveTo(modelPath);

            var b = Back(modelPath);
            Between(30.0, 32.0, b.Columns[1].Width, "A (long text, real 30.4)");
            Between(11.5, 13.5, b.Columns[2].Width, "B (\"1,234,567.89\", real 12.1)");
            Between(8.43, 8.43, b.Columns[3].Width, "C (min width)");
            Between(60, 60, b.Columns[4].Width, "D (max width)");
            Between(10.0, 11.5, b.Columns[5].Width, "E (yyyy-mm-dd)");
            Between(16.0, 20.0, b.Columns[6].Width, "F (20pt bold header)");
            if (!b.Columns[1].CustomWidth) throw new Exception("customWidth not set");
        });

        Check("model: explicit width wins; wrapped, merged and formula cells ignored", () =>
        {
            var p = Path.Combine(outputDir, "autofit_rules.xlsx");
            var wb = new Workbook();
            var s = wb.AddSheet("Rules");
            s.Cell("A1").Value = "A title much longer than any data value in its column";
            s.MergeCells("A1:C1");
            s.Cell("A2").Value = "short";
            s.Cell("B2").Value = LongText;
            s.Columns[2].Width = 5;                                                            // explicit wins
            s.Cell("C2").Value = LongText + LongText;
            s.Cell("C2").Style = new CellStyleBuilder().WrapText().Build();                    // wraps instead
            s.Cell("D2").Formula = "REPT(\"x\",200)";                                          // no value
            s.Cell("E2").Value = "abc";
            s.AutoFitColumns();
            wb.SaveTo(p);
            var b = Back(p);
            Between(8.43, 8.43, b.Columns[1].Width, "A: merged title ignored");
            Between(5, 5, b.Columns[2].Width, "B: explicit width");
            Between(0, 0, b.Columns[3].Width, "C: only merged + wrapped cells → nothing measured, default width");
            Between(0, 0, b.Columns[4].Width, "D: formula only → no width written");
            Between(8.43, 8.43, b.Columns[5].Width, "E: short → min");
        });

        Check("model: per-column AutoFit and options", () =>
        {
            var p = Path.Combine(outputDir, "autofit_columns.xlsx");
            var wb = new Workbook();
            var s = wb.AddSheet("Cols");
            s.Cell("A1").Value = LongText;
            s.Cell("B1").Value = LongText;
            s.Columns[2].AutoFit(new AutoFitOptions { MaxWidth = 20, ExtraWidth = 0 });
            if (!s.Columns[2].IsAutoFit || s.Columns[1].IsAutoFit) throw new Exception("IsAutoFit flags");
            wb.SaveTo(p);
            var b = Back(p);
            Between(0, 0, b.Columns[1].Width, "A not fitted");
            Between(20, 20, b.Columns[2].Width, "B capped at 20");
        });

        Check("model: loaded spans kept except for fitted data columns", () =>
        {
            var src = Path.Combine(outputDir, "autofit_spans_src.xlsx");
            var wb = new Workbook();
            var s = wb.AddSheet("Spans");
            s.Columns[1].Width = 3; s.Columns[2].Width = 3; s.Columns[3].Width = 3;
            s.Cell("B1").Value = LongText;
            wb.SaveTo(src);

            var loaded = Workbook.Load(src);
            loaded.Sheets[0].AutoFitColumns();
            var p = Path.Combine(outputDir, "autofit_spans.xlsx");
            loaded.SaveTo(p);
            var b = Back(p);
            Between(3, 3, b.Columns[1].Width, "A keeps loaded width (no data)");
            Between(30.0, 32.0, b.Columns[2].Width, "B refitted");
            Between(3, 3, b.Columns[3].Width, "C keeps loaded width (no data)");
        });

        Check("model: no auto-fit → no change in column output", () =>
        {
            var p = Path.Combine(outputDir, "autofit_off.xlsx");
            var wb = new Workbook();
            var s = wb.AddSheet("Off");
            s.Cell("A1").Value = LongText;
            wb.SaveTo(p);
            using var z = ZipFile.OpenRead(p);
            using var r = new StreamReader(z.GetEntry("xl/worksheets/sheet1.xml")!.Open());
            if (r.ReadToEnd().Contains("<cols")) throw new Exception("<cols> written without auto-fit");
        });

        // ---- Streaming writer --------------------------------------------------------------

        Check("streaming: measures rows, honours MaxRowsToMeasure", () =>
        {
            var p = Path.Combine(outputDir, "autofit_stream.xlsx");
            using (var w = XlsxWriter.Create(p))
            {
                using var sh = w.AddSheet("All");
                sh.AutoFitColumns();
                sh.WriteHeader("Text", "Late");
                for (int i = 0; i < 1000; i++) sh.WriteRow(i == 500 ? LongText : "row " + i, i == 500 ? LongText : "x");

                using var sh2 = w.AddSheet("Sampled");
                sh2.AutoFitColumns(new AutoFitOptions { MaxRowsToMeasure = 10 });
                sh2.WriteHeader("Text");
                for (int i = 0; i < 1000; i++) sh2.WriteRow(i == 500 ? LongText : "row " + i);
                w.Save();
            }
            var all = Workbook.Load(p);
            Between(30.0, 32.0, all.Sheets[0].Columns[1].Width, "All: row 500 measured");
            Between(30.0, 32.0, all.Sheets[0].Columns[2].Width, "All: col B row 500 measured");
            Between(8.43, 8.43, all.Sheets[1].Columns[1].Width, "Sampled: row 500 not measured");
            if (all.Sheets[0].Cell("A502").Value as string != LongText) throw new Exception("data intact");
        });

        Check("streaming: inline strings, encryption, explicit width, filter button", () =>
        {
            var p = Path.Combine(outputDir, "autofit_stream_opts.xlsx");
            using (var w = XlsxWriter.Create(p, new XlsxWriterOptions
            {
                UseInlineStrings = true,
                Encryption = new EncryptionOptions { Password = "pw" },
            }))
            {
                using var sh = w.AddSheet("Opts");
                sh.SetColumnWidth(2, 4);
                sh.AutoFitColumns();
                sh.WriteHeader("Department", LongText);
                sh.WriteRow("Oncology", LongText);
                sh.AutoFilter("A1:B2");
                w.Save();
            }
            var b = Workbook.Load(p, "pw").Sheets[0];
            // "Department" is ~4.9 em Calibri → 72 px + 16 px filter button → (88+5)/7 ≈ 13.3
            Between(12.5, 14.5, b.Columns[1].Width, "A header + filter button");
            Between(4, 4, b.Columns[2].Width, "B explicit width wins");
            if (b.Cell("B2").Value as string != LongText) throw new Exception("data intact");
        });

        Check("streaming: AutoFitColumns after the first row throws", () =>
        {
            using var w = XlsxWriter.Create(new MemoryStream());
            using var sh = w.AddSheet("Late");
            sh.WriteRow("a");
            try { sh.AutoFitColumns(); }
            catch (InvalidOperationException) { return; }
            throw new Exception("no exception");
        });

        // ---- One-line export ---------------------------------------------------------------

        Check("export: cfg.AutoFitColumns with a [Column] width override", () =>
        {
            var p = Path.Combine(outputDir, "autofit_export.xlsx");
            var rows = new List<Visit>
            {
                new() { Patient = LongText, Amount = 98765.4m, Date = new DateTime(2026, 10, 28), Code = "Z" },
                new() { Patient = "Short", Amount = 1m, Date = new DateTime(2026, 1, 1), Code = "Y" },
            };
            rows.ToXlsx(p, cfg => cfg.SheetName("Visits").AutoFilter().AutoFitColumns());
            var b = Back(p);
            Between(30.0, 32.5, b.Columns[1].Width, "Patient");
            Between(8.43, 11.0, b.Columns[2].Width, "Amount");
            Between(10.0, 13.0, b.Columns[3].Width, "Date (+ filter button)");
            Between(6, 6, b.Columns[4].Width, "Code: [Column(Width = 6)] wins");
        });

        Console.WriteLine();
        Console.WriteLine($"[AutoFitTest] {pass} passed, {fail} failed.");
        Console.WriteLine();
        if (fail > 0) throw new Exception($"AutoFitTest: {fail} check(s) failed.");
    }

    private sealed class Visit
    {
        public string Patient { get; set; } = "";
        public decimal Amount { get; set; }
        public DateTime Date { get; set; }
        [Column(Width = 6)] public string Code { get; set; } = "";
    }
}
