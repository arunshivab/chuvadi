using System;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Chuvadi.Sheets.Internal;   // MiniFormula (InternalsVisibleTo)
using Chuvadi.Sheets.Model;

namespace Chuvadi.Sheets.ManualTests;

/// <summary>
/// 2.0.5 (SH-11, D-306): dates from 31-Dec-1899 to 28-Feb-1900 are written with Excel's 1900 rule.
/// Excel's 1900 date system counts the fictitious 29-Feb-1900, so 1-Jan-1900 is serial 1; DateTime.ToOADate
/// gives 2. The reader already used Excel's rule, so load → save moved those dates one day later, and
/// 28-Feb-1900 became serial 60 (29-Feb-1900), which reloads as a plain number.
/// Checks every write path: XlsxWriter cells (DateTime, DateOnly, DateTimeOffset), the model, a load → save
/// round trip, data-validation date bounds, and the formula evaluator used by conditional formats.
/// Data is fictional (D-60).
/// </summary>
internal static class Dates1900Test
{
    public static void Run(string outputDir)
    {
        Console.WriteLine("[Dates1900Test] 2.0.5 dates before 1-Mar-1900 written with Excel's 1900 rule...");
        Console.WriteLine();
        int pass = 0, fail = 0;
        var failed = new FailedChecks();
        string dir = Path.Combine(outputDir, "dates1900");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);

        void Check(string label, Action body)
        {
            try { body(); Console.WriteLine($"    PASS: {label}"); pass++; }
            catch (Exception ex) { Console.WriteLine($"    FAIL: {label}"); Console.WriteLine($"          {ex.GetType().Name}: {ex.Message}"); fail++; failed.Add(label, ex); }
        }

        // Expected serials, as Excel stores them. 31-Dec-1899 is Excel's "0-Jan-1900"; 30-Dec-1899 09:00 is the
        // time-only value the reader returns for serial 0.375.
        var cases = new (DateTime Value, string Serial)[]
        {
            (new DateTime(1900, 1, 1), "1"),
            (new DateTime(1900, 1, 30), "30"),
            (new DateTime(1900, 2, 28), "59"),
            (new DateTime(1900, 2, 28, 12, 0, 0), "59.5"),
            (new DateTime(1900, 3, 1), "61"),
            (new DateTime(2024, 10, 2), "45567"),
            (new DateTime(1899, 12, 30, 9, 0, 0), "0.375"),
            (new DateTime(1899, 12, 31), "0"),
        };

        Check("XlsxWriter: DateTime, DateOnly and DateTimeOffset cells get Excel's serial", () =>
        {
            var p = Path.Combine(dir, "writer.xlsx");
            using (var w = XlsxWriter.Create(p))
            {
                var s = w.AddSheet("Dates");
                foreach (var (value, _) in cases)
                    s.WriteRow(value, DateOnly.FromDateTime(value), new DateTimeOffset(value, TimeSpan.Zero));
                w.Save();
            }
            var v = CellValues(p);
            for (int i = 0; i < cases.Length; i++)
            {
                int row = i + 1;
                Eq($"A{row} ({cases[i].Value:yyyy-MM-dd HH:mm})", cases[i].Serial, v[$"A{row}"]);
                Eq($"B{row} DateOnly", cases[i].Value.TimeOfDay == TimeSpan.Zero ? cases[i].Serial : Math.Floor(double.Parse(cases[i].Serial, System.Globalization.CultureInfo.InvariantCulture)).ToString(System.Globalization.CultureInfo.InvariantCulture), v[$"B{row}"]);
                Eq($"C{row} DateTimeOffset", cases[i].Serial, v[$"C{row}"]);
            }
        });

        Check("model: a DateTime cell value is saved with Excel's serial", () =>
        {
            var p = Path.Combine(dir, "model.xlsx");
            var wb = new Workbook();
            var s = wb.AddSheet("Dates");
            for (int i = 0; i < cases.Length; i++) s.Cell($"A{i + 1}").Value = cases[i].Value;
            wb.SaveTo(p);
            var v = CellValues(p);
            for (int i = 0; i < cases.Length; i++) Eq($"A{i + 1}", cases[i].Serial, v[$"A{i + 1}"]);
        });

        Check("load → save keeps serials 1, 30, 59, 61, 0.375 and a 36:30 duration (2.0.4: 2, 31, 60, 61, 0.375, 60:30)", () =>
        {
            var src = Path.Combine(dir, "source.xlsx");
            using (var w = XlsxWriter.Create(src))
            {
                var s = w.AddSheet("Dates");
                var style = new CellStyleBuilder().Format("dd-mm-yyyy hh:mm").Build();
                foreach (var serial in new[] { 1.0, 30.0, 59.0, 61.0, 0.375 })
                    s.WriteRow(r => r.Cell(serial, style));
                // A duration of 36:30:00 shown with built-in format 46 ([h]:mm:ss); the reader returns it as
                // 31-Dec-1899 12:30, so 2.0.4 saved it back as 60:30:00.
                s.WriteRow(r => r.Cell(1.5208333333333333, new CellStyleBuilder().Format("[h]:mm:ss").Build()));
                w.Save();
            }
            var wb = Workbook.Load(src);
            Eq("A1 read", new DateTime(1900, 1, 1), wb["Dates"].Cell("A1").Value);
            Eq("A3 read", new DateTime(1900, 2, 28), wb["Dates"].Cell("A3").Value);
            var resaved = Path.Combine(dir, "resaved.xlsx");
            wb.SaveTo(resaved);
            var v = CellValues(resaved);
            Eq("A1", "1", v["A1"]);
            Eq("A2", "30", v["A2"]);
            Eq("A3", "59", v["A3"]);
            Eq("A4", "61", v["A4"]);
            Eq("A5", "0.375", v["A5"]);
            Eq("A6 (36:30:00)", "1.5208333333333333", v["A6"]);
            var again = Workbook.Load(resaved)["Dates"];
            Eq("A1 reloaded", new DateTime(1900, 1, 1), again.Cell("A1").Value);
            Eq("A3 reloaded", new DateTime(1900, 2, 28), again.Cell("A3").Value);
            Eq("A5 reloaded", new DateTime(1899, 12, 30, 9, 0, 0), again.Cell("A5").Value);
        });

        Check("data validation: date bounds 1900-01-01 to 1900-02-28 are 1 and 59", () =>
        {
            var dv = DataValidation.Date(new DateTime(1900, 1, 1), new DateTime(1900, 2, 28));
            Eq("Formula1", "1", dv.Formula1);
            Eq("Formula2", "59", dv.Formula2);
            var later = DataValidation.Date(new DateTime(2024, 10, 2), null);
            Eq("Formula1 2024", "45567", later.Formula1);
        });

        Check("formula evaluator: a date cell compares as Excel's serial", () =>
        {
            Eq("1900-01-30", 30.0, MiniFormula.Normalize(new DateTime(1900, 1, 30), false));
            Eq("1900-03-01", 61.0, MiniFormula.Normalize(new DateTime(1900, 3, 1), false));
            Eq("DateOnly 1900-02-28", 59.0, MiniFormula.Normalize(new DateOnly(1900, 2, 28), false));
            Eq("1904 system", 0.0, MiniFormula.Normalize(new DateTime(1904, 1, 1), true));
        });

        Check("unchanged: dates from 1-Mar-1900 on, time-only values, and dates before 1899-12-30", () =>
        {
            var p = Path.Combine(dir, "unchanged.xlsx");
            var later = new DateTime(2024, 10, 2, 12, 0, 0);
            var early = new DateTime(1850, 1, 1);
            using (var w = XlsxWriter.Create(p))
            {
                w.AddSheet("Dates").WriteRow(later, new DateTime(1900, 3, 1), new DateTime(1899, 12, 30, 9, 0, 0), early);
                w.Save();
            }
            var v = CellValues(p);
            Eq("A1", later.ToOADate().ToString("R", System.Globalization.CultureInfo.InvariantCulture), v["A1"]);
            Eq("B1", "61", v["B1"]);
            Eq("C1", "0.375", v["C1"]);
            Eq("D1 (negative, as before)", early.ToOADate().ToString("R", System.Globalization.CultureInfo.InvariantCulture), v["D1"]);
        });

        Console.WriteLine();
        Console.WriteLine($"[Dates1900Test] {pass} passed, {fail} failed.");
        Console.WriteLine();
        if (fail > 0) throw new Exception(failed.Summary($"Dates1900Test: {fail} check(s) failed."));
    }

    /// <summary>The raw &lt;v&gt; text of every numeric cell on the first sheet, by address.</summary>
    private static System.Collections.Generic.Dictionary<string, string> CellValues(string path)
    {
        using var z = ZipFile.OpenRead(path);
        using var r = new StreamReader(z.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var xml = r.ReadToEnd();
        var map = new System.Collections.Generic.Dictionary<string, string>();
        foreach (Match m in Regex.Matches(xml, "<c r=\"([A-Z]+\\d+)\"[^>]*?>(?:<f>[^<]*</f>)?<v>([^<]*)</v>"))
            map[m.Groups[1].Value] = m.Groups[2].Value;
        return map;
    }

    private static void Eq(string what, object? expected, object? actual)
    {
        if (!Equals(expected, actual))
            throw new Exception($"{what}: expected <{expected}> got <{actual}>");
    }
}
