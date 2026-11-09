using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Chuvadi.Sheets.Model;

namespace Chuvadi.Sheets.ManualTests;

/// <summary>
/// Sheets 1.8.0: load benchmark for a 10 MB workbook, read from memory (no disk, so no
/// antivirus scan on open). The workbook is generated here with a fixed seed: 3 sheets × 12
/// columns of ids, dates, styled money, repeated categories, status and sparse remarks
/// (about 1.75 million cells).
///
/// Checked: the model and the streaming reader return the same values; the loaded model
/// keeps at most 200 bytes per cell (1.7.0 kept about 383). Times are printed for
/// information, against the 2 s target; only a generous 15 s limit fails the test, so a busy
/// machine does not.
///
/// Sheets 2.0.0 adds a save of 50,000 hyperlinks (the shared package checked relationship ids
/// by scanning a list, which made this quadratic: about 6 s; now milliseconds) and a check
/// that a duplicate relationship id is refused exactly as before.
///
/// Not run by the xUnit suite: tests there run in parallel, which would distort both the
/// timings and the memory measurement.
/// </summary>
internal static class LoadBenchmarkTest
{
    private const int RowsPerSheet = 52_000;
    private const double MaxBytesPerCell = 200;
    private const double HardLimitMs = 15_000;
    private const int HyperlinkCount = 50_000;

    public static void Run(string outputDir)
    {
        Console.WriteLine("[LoadBenchmarkTest] 1.8.0 load benchmark (10 MB workbook, from memory)...");
        Console.WriteLine();
        int pass = 0, fail = 0;
        var failed = new FailedChecks();

        void Check(string label, Action body)
        {
            try { body(); Console.WriteLine($"    PASS: {label}"); pass++; }
            catch (Exception ex) { Console.WriteLine($"    FAIL: {label}"); Console.WriteLine($"          {ex.GetType().Name}: {ex.Message}"); fail++; failed.Add(label, ex); }
        }

        var bytes = MakeWorkbook();
        Console.WriteLine($"          (workbook: {bytes.Length / 1048576.0:F2} MB, 3 sheets × {RowsPerSheet:N0} rows × 12 columns, {Environment.ProcessorCount} logical processors)");

        Check("Workbook.Load and XlsxReader return the same values for every cell", () =>
        {
            var wb = Workbook.Load(new MemoryStream(bytes, writable: false));
            using var r = XlsxReader.Open(new MemoryStream(bytes, writable: false), new XlsxReaderOptions { TreatFirstRowAsHeaders = false });
            long compared = 0;
            foreach (var info in r.Sheets)
            {
                var sheet = wb[info.Name];
                foreach (var row in r.Sheet(info.Name).Rows)
                    for (int c = 0; c < row.Count; c++)
                    {
                        var v = row[c];
                        if (v is null) continue;
                        var m = sheet.Cell(row.RowNumber, c + 1).Value;
                        if (!Equals(v, m)) throw new Exception($"{info.Name}!R{row.RowNumber}C{c + 1}: streaming <{v}> model <{m}>");
                        compared++;
                    }
            }
            if (compared < 1_500_000) throw new Exception($"only {compared:N0} cells compared");
            Console.WriteLine($"          ({compared:N0} cells compared)");
        });

        Check($"the loaded model keeps at most {MaxBytesPerCell:F0} bytes per cell", () =>
        {
            Workbook.Load(new MemoryStream(bytes, writable: false));   // warm-up
            long before = Collect();
            var wb = Workbook.Load(new MemoryStream(bytes, writable: false));
            long after = Collect();
            long cells = wb.Sheets.Sum(s => (long)s.PopulatedCells.Count());
            double perCell = (after - before) / (double)cells;
            Console.WriteLine($"          ({cells:N0} cells; {(after - before) / 1048576.0:F0} MB kept, {perCell:F0} bytes per cell)");
            GC.KeepAlive(wb);
            if (perCell > MaxBytesPerCell) throw new Exception($"{perCell:F0} bytes per cell");
        });

        Check($"load times (target 2 s; fails only above {HardLimitMs / 1000:F0} s)", () =>
        {
            var model = Time(3, () => Workbook.Load(new MemoryStream(bytes, writable: false)));
            var stream = Time(3, () =>
            {
                using var r = XlsxReader.Open(new MemoryStream(bytes, writable: false), new XlsxReaderOptions { TreatFirstRowAsHeaders = false });
                foreach (var info in r.Sheets)
                    foreach (var row in r.Sheet(info.Name).Rows)
                        for (int c = 0; c < row.Count; c++) _ = row[c];
            });
            Console.WriteLine($"          (Workbook.Load: median {model.Median:F0} ms, max {model.Max:F0} ms; XlsxReader, every cell: median {stream.Median:F0} ms, max {stream.Max:F0} ms)");
            Console.WriteLine($"          (2 s target: Workbook.Load {(model.Median <= 2000 ? "met" : "not met")}, XlsxReader {(stream.Median <= 2000 ? "met" : "not met")} on this machine)");
            if (model.Max > HardLimitMs || stream.Max > HardLimitMs)
                throw new Exception($"a load took longer than {HardLimitMs:F0} ms");
        });

        // ---- 2.0.0: relationship ids (shared OoxmlPackage) -------------------------------

        Check($"saving {HyperlinkCount:N0} hyperlinks: every link kept, in order (fails only above {HardLimitMs / 1000:F0} s)", () =>
        {
            var wb = new Workbook();
            var sheet = wb.AddSheet("Links");
            for (int i = 1; i <= HyperlinkCount; i++)
            {
                sheet.Cell(i, 1).Value = "link " + i;
                sheet.Hyperlinks.Add(new Hyperlink($"A{i}", target: $"https://example.com/item/{i}"));
            }
            Collect();
            var sw = Stopwatch.StartNew();
            var ms = new MemoryStream();
            wb.SaveTo(ms);
            sw.Stop();
            var saved = ms.ToArray();   // SaveTo closes the stream; ToArray still works
            var back = Workbook.Load(new MemoryStream(saved, writable: false))["Links"];
            if (back.Hyperlinks.Count != HyperlinkCount) throw new Exception($"{back.Hyperlinks.Count} links read back");
            for (int i = 0; i < HyperlinkCount; i += 997)
                if (back.Hyperlinks[i].Reference != $"A{i + 1}" || back.Hyperlinks[i].Target != $"https://example.com/item/{i + 1}")
                    throw new Exception($"link {i + 1}: {back.Hyperlinks[i].Reference} {back.Hyperlinks[i].Target}");
            Console.WriteLine($"          (saved {HyperlinkCount:N0} hyperlinks in {sw.ElapsedMilliseconds:N0} ms; {saved.Length / 1024:N0} KB)");
            if (sw.ElapsedMilliseconds > HardLimitMs) throw new Exception($"saving took {sw.ElapsedMilliseconds} ms");
        });

        Check("a duplicate relationship id is still refused, with the same exception and message", () =>
        {
            using var pkg = Chuvadi.Internal.OoxmlPackage.Create(new MemoryStream());
            pkg.AddRelationship("/xl/workbook.xml", "/xl/worksheets/sheet1.xml", "http://example.com/rel", "rId1");
            pkg.AddExternalRelationship("/xl/workbook.xml", "https://example.com/", "http://example.com/link", "rId2");
            try
            {
                pkg.AddExternalRelationship("/xl/workbook.xml", "https://example.com/again", "http://example.com/link", "rId1");
                throw new Exception("no exception");
            }
            catch (InvalidOperationException ex)
            {
                const string expected = "Relationship ID 'rId1' is already used for source '/xl/workbook.xml'.";
                if (ex.Message != expected) throw new Exception($"message: {ex.Message}");
            }
            // Ids are per source and compared exactly (ordinal): these are all allowed.
            pkg.AddRelationship("/xl/worksheets/sheet1.xml", "/xl/drawings/drawing1.xml", "http://example.com/rel", "rId1");
            pkg.AddExternalRelationship("/xl/workbook.xml", "https://example.com/case", "http://example.com/link", "RID1");
        });

        Console.WriteLine();
        Console.WriteLine($"  LoadBenchmarkTest: {pass} passed, {fail} failed.");
        Console.WriteLine();
        if (fail > 0) throw new Exception(failed.Summary($"LoadBenchmarkTest: {fail} check(s) failed."));
    }

    private static long Collect()
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        return GC.GetTotalMemory(forceFullCollection: true);
    }

    private static (double Median, double Max) Time(int runs, Action body)
    {
        var times = new List<double>();
        for (int i = 0; i < runs; i++)
        {
            Collect();
            var sw = Stopwatch.StartNew();
            body();
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        return (times[times.Count / 2], times[^1]);
    }

    private static readonly string[] Cities = { "Northbrook", "Eastvale", "Westmoor", "Southgate", "Riverton", "Hillcrest", "Lakeside", "Brookfield" };
    private static readonly string[] Categories = { "Consumables", "Equipment", "Services", "Supplies", "Laboratory", "Imaging", "Facilities", "Admin" };
    private static readonly string[] Words = "alpha beta gamma delta review approved pending invoice shipment order batch quality report summary note follow-up audit payment cleared hold".Split(' ');

    /// <summary>The benchmark workbook, written to memory with a fixed seed.</summary>
    private static byte[] MakeWorkbook()
    {
        var rnd = new Random(42);
        var ms = new MemoryStream();
        var money = CellStyle.Default with { NumberFormat = "#,##0.00" };
        var date = CellStyle.Default with { NumberFormat = "dd-mmm-yyyy" };
        var head = CellStyle.Default with { Bold = true, FillColor = "DDEBF7" };
        var start = new DateTime(2024, 1, 1);
        using (var w = XlsxWriter.Create(ms))
        {
            for (int sh = 0; sh < 3; sh++)
            {
                var s = w.AddSheet($"Data{sh + 1}");
                s.WriteRow(rb => { foreach (var h in new[] { "Id", "Date", "Customer", "City", "Category", "Qty", "Unit price", "Amount", "Tax", "Paid", "Status", "Remarks" }) rb.Cell(h, head); });
                for (int i = 0; i < RowsPerSheet; i++)
                {
                    int qty = rnd.Next(1, 50);
                    double unit = Math.Round(rnd.NextDouble() * 900 + 10, 2);
                    s.WriteRow(rb => rb.Cell(sh * 1_000_000 + i + 1).Cell(start.AddDays(rnd.Next(0, 700)), date)
                        .Cell("Customer " + rnd.Next(1, 2000)).Cell(Cities[rnd.Next(Cities.Length)]).Cell(Categories[rnd.Next(Categories.Length)])
                        .Cell(qty).Cell(unit, money).Cell(qty * unit, money).Cell(Math.Round(qty * unit * 0.18, 2), money)
                        .Cell(rnd.Next(2) == 0).Cell(rnd.Next(3) switch { 0 => "Open", 1 => "Closed", _ => "On hold" })
                        .Cell(rnd.Next(4) == 0 ? Words[rnd.Next(Words.Length)] + " " + Words[rnd.Next(Words.Length)] : null));
                }
            }
            w.Save();
        }
        return ms.ToArray();
    }
}
