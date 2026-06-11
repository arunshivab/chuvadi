using System;
using System.IO;
using System.Threading.Tasks;
using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.ManualTests;

/// <summary>
/// Step-4 verification: exercises freeze panes, tables, hyperlinks, comments, and the async API.
///
/// Produces THREE files:
///   1. polish.xlsx       — all five new features in one workbook (sync API).
///   2. polish_async.xlsx — same content via the async API; should be functionally identical.
///   3. polish_freeze.xlsx — dedicated freeze pane test on a larger sheet.
/// </summary>
internal static class FeaturesPolishTest
{
    public static void Run(string outputDir)
    {
        BuildPolishFile(outputDir);
        BuildPolishAsyncFile(outputDir).GetAwaiter().GetResult();
        BuildFreezeFile(outputDir);
    }

    // ---- polish.xlsx (sync, covers all step 4 features) ----------------------------

    private static void BuildPolishFile(string outputDir)
    {
        Console.WriteLine("[FeaturesPolishTest] Building polish.xlsx (sync)...");
        var path = Path.Combine(outputDir, "polish.xlsx");
        if (File.Exists(path)) File.Delete(path);

        var headerStyle = new CellStyleBuilder()
            .Bold()
            .Background("#1A237E")
            .Foreground("#FFFFFF")
            .HAlign(HorizontalAlign.Center)
            .Build();

        using (var writer = XlsxWriter.Create(path))
        {
            // ---- Sheet 1: Table + hyperlinks ----
            using (var sheet = writer.AddSheet("Patients"))
            {
                sheet.SetColumnWidth(1, 1, 6);
                sheet.SetColumnWidth(2, 2, 24);
                sheet.SetColumnWidth(3, 3, 32);
                sheet.SetColumnWidth(4, 4, 14);
                sheet.FreezeRows(1); // Keep header visible while scrolling.

                sheet.WriteHeader("ID", "Patient", "Records", "Last Visit");
                sheet.WriteRow(row => row
                    .Cell(1)
                    .Cell("Anand Kumar")
                    .Hyperlink("https://example.com/patients/1", "View records", "Open patient chart")
                    .Cell(new DateTime(2026, 1, 5)));
                sheet.WriteRow(row => row
                    .Cell(2)
                    .Cell("Priya Sharma")
                    .Hyperlink("https://example.com/patients/2", "View records")
                    .Cell(new DateTime(2026, 1, 8)));
                sheet.WriteRow(row => row
                    .Cell(3)
                    .Cell("Rahul Verma")
                    .Hyperlink("mailto:rahul@example.com", "Email patient")
                    .Cell(new DateTime(2026, 1, 12)));

                sheet.AddTable(
                    "A1:D4", "PatientsTable", TableStyle.Medium2,
                    "ID", "Patient", "Records", "Last Visit");
            }

            // ---- Sheet 2: Comments ----
            using (var sheet = writer.AddSheet("Reviews"))
            {
                sheet.SetColumnWidth(1, 1, 8);
                sheet.SetColumnWidth(2, 2, 28);
                sheet.SetColumnWidth(3, 3, 14);

                sheet.WriteHeader("Score", "Reviewer", "Status");

                sheet.WriteRow(row => row
                    .Cell(95)
                    .Cell("Dr. Mehta")
                    .Comment("Approved", "Arun", "Excellent work, no concerns. Signed off."));

                sheet.WriteRow(row => row
                    .Cell(72)
                    .Cell("Dr. Reddy")
                    .Comment("Pending", "Arun", "Awaiting second review per protocol section 4.3."));

                sheet.WriteRow(row => row
                    .Cell(88)
                    .Cell("Dr. Singh")
                    .Comment("Approved", "Arun", "Looks good. Minor formatting note in section 2."));
            }

            // ---- Sheet 3: Freeze + table on same sheet ----
            using (var sheet = writer.AddSheet("Inventory"))
            {
                sheet.SetColumnWidth(1, 5, 14);
                sheet.FreezeRows(1);
                sheet.FreezeColumns(1);

                sheet.WriteHeader("SKU", "Name", "Qty", "Price", "Updated");
                for (int i = 1; i <= 25; i++)
                {
                    sheet.WriteRow(
                        $"SKU{i:D4}",
                        $"Item {i}",
                        i * 10,
                        i * 12.50,
                        new DateTime(2026, 1, 1).AddDays(i));
                }

                sheet.AddTable(
                    "A1:E26", "InventoryTable", TableStyle.Medium9,
                    "SKU", "Name", "Qty", "Price", "Updated");
            }

            writer.Save();
        }

        Console.WriteLine($"[FeaturesPolishTest] Wrote: {path} ({new FileInfo(path).Length:N0} bytes)");
        Console.WriteLine("[FeaturesPolishTest] Sheets: Patients (table+hyperlinks+freeze), Reviews (comments), Inventory (table+freeze x/y).");
        Console.WriteLine();
    }

    // ---- polish_async.xlsx (async API, identical content) --------------------------

    private static async Task BuildPolishAsyncFile(string outputDir)
    {
        Console.WriteLine("[FeaturesPolishTest] Building polish_async.xlsx (async)...");
        var path = Path.Combine(outputDir, "polish_async.xlsx");
        if (File.Exists(path)) File.Delete(path);

        await using (var writer = XlsxWriter.Create(path))
        {
            await using (var sheet = writer.AddSheet("Async"))
            {
                sheet.SetColumnWidth(1, 4, 16);
                sheet.FreezeRows(1);

                await sheet.WriteHeaderAsync("Id", "Name", "Score", "Date");
                for (int i = 1; i <= 100; i++)
                {
                    await sheet.WriteRowAsync(
                        i,
                        $"Item {i}",
                        i * 1.5,
                        new DateTime(2026, 1, 1).AddDays(i));
                }
            }
            await writer.SaveAsync();
        }

        Console.WriteLine($"[FeaturesPolishTest] Wrote: {path} ({new FileInfo(path).Length:N0} bytes)");
        Console.WriteLine("[FeaturesPolishTest] 100 rows via async API.");
        Console.WriteLine();
    }

    // ---- polish_freeze.xlsx (dedicated freeze pane verification) -------------------

    private static void BuildFreezeFile(string outputDir)
    {
        Console.WriteLine("[FeaturesPolishTest] Building polish_freeze.xlsx ...");
        var path = Path.Combine(outputDir, "polish_freeze.xlsx");
        if (File.Exists(path)) File.Delete(path);

        var headerStyle = new CellStyleBuilder().Bold().Background("#0563C1").Foreground("#FFFFFF").Build();

        using (var writer = XlsxWriter.Create(path))
        {
            using (var sheet = writer.AddSheet("FreezeBoth"))
            {
                sheet.SetColumnWidth(1, 1, 18);
                sheet.SetColumnWidth(2, 20, 10);
                sheet.FreezeRows(1);
                sheet.FreezeColumns(1);

                // Header row.
                var headers = new object?[20];
                headers[0] = "Row Label";
                for (int c = 1; c < 20; c++) headers[c] = $"Col {c}";
                sheet.WriteRow(headers);

                // Data rows — enough to require scrolling.
                for (int r = 1; r <= 50; r++)
                {
                    var row = new object?[20];
                    row[0] = $"Row {r}";
                    for (int c = 1; c < 20; c++) row[c] = r * 100 + c;
                    sheet.WriteRow(row);
                }
            }
            writer.Save();
        }

        Console.WriteLine($"[FeaturesPolishTest] Wrote: {path} ({new FileInfo(path).Length:N0} bytes)");
        Console.WriteLine("[FeaturesPolishTest] 50 rows × 20 cols with row 1 and col A frozen.");
        Console.WriteLine();
    }
}
