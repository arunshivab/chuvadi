using System;
using System.Diagnostics;
using System.IO;
using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.ManualTests;

/// <summary>
/// Exercises the full public XlsxWriter / SheetWriter API.
///
/// Produces TWO files:
///   1. features.xlsx     — multi-sheet, all cell types, formulas, styles, merges, autofilter
///   2. stress_100k.xlsx  — 100,000 rows × 6 columns, tracks memory + time
///
/// Verification:
///   - Open features.xlsx in Excel. Should show three sheets with the documented content.
///   - stress_100k.xlsx should be produced in seconds with flat memory usage. The console
///     prints peak working set so you can confirm streaming behavior.
/// </summary>
internal static class StreamingWriterTest
{
    public static void Run(string outputDir)
    {
        BuildFeaturesFile(outputDir);
        BuildStressFile(outputDir);
    }

    // ---- features.xlsx -------------------------------------------------------------

    private static void BuildFeaturesFile(string outputDir)
    {
        Console.WriteLine("[StreamingWriterTest] Building features.xlsx ...");
        var path = Path.Combine(outputDir, "features.xlsx");
        if (File.Exists(path)) File.Delete(path);

        var headerStyle = new CellStyleBuilder()
            .Bold()
            .Background("#1A237E")
            .Foreground("#FFFFFF")
            .HAlign(HorizontalAlign.Center)
            .Build();

        var redBoldStyle = new CellStyleBuilder()
            .Bold()
            .Foreground("#C62828")
            .Build();

        var currencyStyle = new CellStyleBuilder()
            .Format("$#,##0.00")
            .Build();

        using (var writer = XlsxWriter.Create(path))
        {
            // ---- Sheet 1: Cell types showcase ----
            using (var sheet = writer.AddSheet("CellTypes"))
            {
                sheet.SetColumnWidth(1, 4, 18);

                sheet.WriteHeader("Description", "Value", "Type", "Notes");
                sheet.WriteRow("Integer",                  42,                       "int",         "Numeric, no style");
                sheet.WriteRow("Long",                     1234567890123L,           "long",        "Big numbers OK");
                sheet.WriteRow("Double",                   3.14159265358979,         "double",      "IEEE 754");
                sheet.WriteRow("Decimal",                  123.45m,                  "decimal",     "Converted to double");
                sheet.WriteRow("Bool true",                true,                     "bool",        "Excel: TRUE");
                sheet.WriteRow("Bool false",               false,                    "bool",        "Excel: FALSE");
                sheet.WriteRow("DateTime (date only)",     new DateTime(2026, 1, 15),"DateTime",    "Auto-formatted as date");
                sheet.WriteRow("DateTime (with time)",     new DateTime(2026, 1, 15, 14, 30, 45), "DateTime", "Auto-formatted with time");
                sheet.WriteRow("TimeSpan",                 TimeSpan.FromHours(36.5), "TimeSpan",    "Auto-formatted, 24h+");
                sheet.WriteRow("String",                   "Hello, world",           "string",      "Shared string");
                sheet.WriteRow("Empty",                    null,                     "null",        "Empty cell");
                sheet.WriteRow("Repeated string",          "Hello, world",           "string",      "Dedup test");
            }

            // ---- Sheet 2: Styled patient report (typical app use) ----
            using (var sheet = writer.AddSheet("Report"))
            {
                sheet.SetColumnWidth(1, 1, 6);
                sheet.SetColumnWidth(2, 2, 24);
                sheet.SetColumnWidth(3, 4, 14);
                sheet.SetColumnWidth(5, 5, 12);

                // Headers using styled WriteRow.
                sheet.WriteRow(row => row
                    .Cell("ID",       headerStyle)
                    .Cell("Patient",  headerStyle)
                    .Cell("Billed",   headerStyle)
                    .Cell("Paid",     headerStyle)
                    .Cell("Visit",    headerStyle));

                // Some data rows.
                sheet.WriteRow(row => row
                    .Cell(1)
                    .Cell("Anand Kumar")
                    .Cell(2500m, currencyStyle)
                    .Cell(2500m, currencyStyle)
                    .Cell(new DateTime(2026, 1, 5)));

                sheet.WriteRow(row => row
                    .Cell(2)
                    .Cell("Priya Sharma")
                    .Cell(4800m, currencyStyle)
                    .Cell(3000m, currencyStyle)
                    .Cell(new DateTime(2026, 1, 8)));

                sheet.WriteRow(row => row
                    .Cell(3)
                    .Cell("OVERDUE",     redBoldStyle)
                    .Cell(7200m,         currencyStyle)
                    .Cell(0m,            currencyStyle)
                    .Cell(new DateTime(2026, 1, 12)));

                // Totals row using formulas.
                sheet.WriteRow(row => row
                    .Cell(null)
                    .Cell("Totals", headerStyle)
                    .Formula("SUM(C2:C4)", currencyStyle)
                    .Formula("SUM(D2:D4)", currencyStyle)
                    .Cell(null));

                // Merge the title bar - well, actually let's merge B5:C5 which is "Totals" + the SUM.
                // Actually let's add an autofilter on the data range. That's more useful.
                sheet.AutoFilter("A1:E4");
            }

            // ---- Sheet 3: Merged title + small data ----
            using (var sheet = writer.AddSheet("Title"))
            {
                sheet.SetColumnWidth(1, 4, 22);

                // Big merged title.
                sheet.WriteRow(row => row
                    .Cell("Q1 2026 Summary Report", new CellStyleBuilder()
                        .Bold()
                        .FontSize(16)
                        .Background("#1A237E")
                        .Foreground("#FFFFFF")
                        .HAlign(HorizontalAlign.Center)
                        .Build()));
                sheet.MergeCells("A1:D1");

                // Blank row
                sheet.WriteRow();

                // Sub-data.
                sheet.WriteHeader("Region", "Revenue", "Growth %", "Status");
                sheet.WriteRow("North",  125000m, 0.12, "On track");
                sheet.WriteRow("South",  98000m,  0.08, "On track");
                sheet.WriteRow("East",   73000m,  -0.03, "Underperforming");
                sheet.WriteRow("West",   145000m, 0.21, "Exceeding");
            }

            writer.Save();
        }

        Console.WriteLine($"[StreamingWriterTest] Wrote: {path} ({new FileInfo(path).Length:N0} bytes)");
        Console.WriteLine("[StreamingWriterTest] Expected sheets: CellTypes, Report, Title.");
        Console.WriteLine();
    }

    // ---- stress_100k.xlsx ----------------------------------------------------------

    private static void BuildStressFile(string outputDir)
    {
        const int rowCount = 100_000;
        Console.WriteLine($"[StreamingWriterTest] Building stress_100k.xlsx ({rowCount:N0} rows) ...");
        var path = Path.Combine(outputDir, "stress_100k.xlsx");
        if (File.Exists(path)) File.Delete(path);

        // Force a GC before measurement to get a clean baseline.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var bytesBefore = GC.GetTotalMemory(forceFullCollection: true);
        var sw = Stopwatch.StartNew();

        using (var writer = XlsxWriter.Create(path))
        using (var sheet = writer.AddSheet("Data"))
        {
            sheet.SetColumnWidth(1, 6, 14);
            sheet.WriteHeader("Id", "Name", "Score", "Date", "IsActive", "Category");

            // Categories we cycle through to exercise shared-string dedup.
            var categories = new[] { "Alpha", "Beta", "Gamma", "Delta", "Epsilon" };
            var baseDate = new DateTime(2020, 1, 1);

            for (int i = 1; i <= rowCount; i++)
            {
                sheet.WriteRow(
                    i,
                    $"Row {i}",                                       // unique strings — fills shared table
                    (i * 7919) % 100 / 100.0,                          // pseudorandom double
                    baseDate.AddDays(i % 2000),                        // dates
                    (i % 2) == 0,                                      // bool
                    categories[i % categories.Length]);                // dedup-friendly string
            }

            writer.Save();
        }

        sw.Stop();
        var bytesAfter = GC.GetTotalMemory(forceFullCollection: false);
        var workingSet = Process.GetCurrentProcess().WorkingSet64;

        Console.WriteLine($"[StreamingWriterTest] Wrote: {path} ({new FileInfo(path).Length:N0} bytes)");
        Console.WriteLine($"[StreamingWriterTest]   Time:           {sw.ElapsedMilliseconds:N0} ms");
        Console.WriteLine($"[StreamingWriterTest]   Managed delta:  {(bytesAfter - bytesBefore) / 1024.0 / 1024.0:F2} MB");
        Console.WriteLine($"[StreamingWriterTest]   Working set:    {workingSet / 1024.0 / 1024.0:F2} MB");
        Console.WriteLine();
    }
}
