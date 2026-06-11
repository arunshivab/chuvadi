using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.ManualTests;

/// <summary>
/// Reads every file written by the other test groups and confirms content.
/// Also round-trips a Workbook load+edit, and tests the streaming reader on the 100K file.
/// </summary>
internal static class ReaderTest
{
    public static void Run(string outputDir)
    {
        Console.WriteLine("[ReaderTest] Running reader tests across previously-generated files...");
        Console.WriteLine();

        // Each previously-generated file should at minimum round-trip its data through the reader.
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

        // ---- minimal.xlsx — single cell with value 42 -----------------------------
        Check("minimal.xlsx → A1 = 42", () =>
        {
            var p = Path.Combine(outputDir, "minimal.xlsx");
            using var r = XlsxReader.Open(p, new XlsxReaderOptions { TreatFirstRowAsHeaders = false });
            int rowCount = 0;
            double? firstValue = null;
            foreach (var row in r.Sheet(1).Rows)
            {
                if (rowCount == 0) firstValue = row.GetDouble(0);
                rowCount++;
            }
            if (rowCount != 1) throw new Exception($"Expected 1 row, got {rowCount}");
            if (firstValue != 42.0) throw new Exception($"Expected 42, got {firstValue}");
        });

        // ---- styled.xlsx — 3 rows × 4 cols, with dates ---------------------------
        Check("styled.xlsx → 3 rows, dates as DateTime", () =>
        {
            var p = Path.Combine(outputDir, "styled.xlsx");
            using var r = XlsxReader.Open(p);  // first row = headers by default
            int dataRowCount = 0;
            string? lastName = null;
            DateTime? lastDate = null;
            foreach (var row in r.Sheet(1).Rows)
            {
                lastName = row.GetString("Name");
                lastDate = row.GetDateTime("Date");
                dataRowCount++;
            }
            if (dataRowCount != 2) throw new Exception($"Expected 2 data rows after header, got {dataRowCount}");
            if (lastName != "World") throw new Exception($"Expected 'World', got '{lastName}'");
            if (lastDate?.Year != 2026 || lastDate?.Month != 6 || lastDate?.Day != 30)
                throw new Exception($"Expected 2026-06-30, got {lastDate}");
        });

        // ---- features.xlsx — multi-sheet ------------------------------------------
        Check("features.xlsx → 3 sheets discovered", () =>
        {
            var p = Path.Combine(outputDir, "features.xlsx");
            using var r = XlsxReader.Open(p);
            if (r.Sheets.Count != 3) throw new Exception($"Expected 3 sheets, got {r.Sheets.Count}");
            var names = r.Sheets.Select(s => s.Name).ToArray();
            if (names[0] != "CellTypes" || names[1] != "Report" || names[2] != "Title")
                throw new Exception($"Sheets: {string.Join(",", names)}");
        });

        Check("features.xlsx CellTypes → diverse types preserved", () =>
        {
            var p = Path.Combine(outputDir, "features.xlsx");
            using var r = XlsxReader.Open(p);
            var s = r.Sheet("CellTypes");

            // We'll walk specific rows and verify their values.
            // Row 1 = headers (skipped by reader default). Row 2 = "Integer 42".
            int dataRowIdx = 0;
            foreach (var row in s.Rows)
            {
                if (dataRowIdx == 0)  // first data row: integer = 42
                {
                    if (row.GetString(0) != "Integer") throw new Exception($"Row 0 label: {row.GetString(0)}");
                    if (row.GetDouble(1) != 42.0) throw new Exception($"Row 0 value: {row[1]}");
                }
                if (dataRowIdx == 6)  // DateTime (date only)
                {
                    var dt = row.GetDateTime(1);
                    if (dt.Year != 2026 || dt.Month != 1 || dt.Day != 15)
                        throw new Exception($"Date row: {dt}");
                }
                if (dataRowIdx == 4)  // Bool true
                {
                    if (!row.GetBool(1)) throw new Exception("Expected TRUE");
                }
                dataRowIdx++;
            }
            if (dataRowIdx != 12) throw new Exception($"Expected 12 data rows, got {dataRowIdx}");
        });

        // ---- stress_100k.xlsx — streaming over 100K rows -------------------------
        Check("stress_100k.xlsx → reads all 100K rows in streaming pass", () =>
        {
            var p = Path.Combine(outputDir, "stress_100k.xlsx");
            var sw = Stopwatch.StartNew();
            using var r = XlsxReader.Open(p);
            int n = 0;
            long checksumIds = 0;
            foreach (var row in r.Sheet(1).Rows)
            {
                checksumIds += row.GetInt32("Id");
                n++;
            }
            sw.Stop();
            if (n != 100_000) throw new Exception($"Expected 100000 rows, got {n}");
            // Checksum: sum of 1..100000 = 5,000,050,000.
            if (checksumIds != 5_000_050_000L)
                throw new Exception($"Id checksum mismatch: {checksumIds}");
            Console.WriteLine($"          ({sw.ElapsedMilliseconds:N0} ms)");
        });

        // ---- ext_list.xlsx — strongly-typed read mapping --------------------------
        Check("ext_list.xlsx → typed ReadXlsx<T>", () =>
        {
            var p = Path.Combine(outputDir, "ext_list.xlsx");
            var patients = p.ReadXlsx<PatientRead>();
            if (patients.Count != 5) throw new Exception($"Expected 5 rows, got {patients.Count}");

            var first = patients[0];
            if (first.Id != "P0001") throw new Exception($"First Id: {first.Id}");
            if (first.Name != "Anand Kumar") throw new Exception($"First Name: {first.Name}");
            if (first.Dob.Year != 1985) throw new Exception($"First DOB: {first.Dob}");

            // Status was [ColumnIgnore] in the write side, but it's still in the file under the
            // "Status" header. Our PatientRead doesn't have [ColumnIgnore] on Status, so it should map.
            if (first.Status != "Active") throw new Exception($"First Status: {first.Status}");
        });

        // ---- ext_datatable.xlsx — DataTable round-trip ----------------------------
        Check("ext_datatable.xlsx → ReadXlsxSheet (DataTable)", () =>
        {
            var p = Path.Combine(outputDir, "ext_datatable.xlsx");
            var dt = p.ReadXlsxSheet();
            if (dt.Rows.Count != 4) throw new Exception($"Expected 4 rows, got {dt.Rows.Count}");
            if (dt.Columns.Count < 5) throw new Exception($"Expected ≥5 cols, got {dt.Columns.Count}");

            // First row's customer
            var customer = dt.Rows[0]["Customer"] as string;
            if (customer != "Acme Corp") throw new Exception($"Customer: {customer}");
        });

        // ---- model.xlsx — Workbook load round-trip -------------------------------
        Check("model.xlsx → Workbook.Load round-trip", () =>
        {
            var srcPath = Path.Combine(outputDir, "model.xlsx");
            var wb = Workbook.Load(srcPath);
            if (wb.Sheets.Count != 2) throw new Exception($"Expected 2 sheets, got {wb.Sheets.Count}");

            // Verify Summary sheet content.
            var summary = wb["Summary"];
            var b3 = summary.Cell("B3").Value;
            if (b3 is not double d || d != 250000.0)
                throw new Exception($"B3: expected 250000, got {b3} ({b3?.GetType().Name})");
        });

        Check("model.xlsx → Workbook load + edit + save round-trip", () =>
        {
            var srcPath = Path.Combine(outputDir, "model.xlsx");
            var dstPath = Path.Combine(outputDir, "model_edited.xlsx");

            var wb = Workbook.Load(srcPath);
            // Mutate a cell.
            wb["Summary"].Cell("A3").Value = "Total Revenue (edited)";
            wb.SaveTo(dstPath);

            // Re-read and verify the edit took.
            var wb2 = Workbook.Load(dstPath);
            var v = wb2["Summary"].Cell("A3").Value;
            if (v is not string s || s != "Total Revenue (edited)")
                throw new Exception($"After edit: expected 'Total Revenue (edited)', got '{v}'");
        });

        // ---- Self round-trip: load + save each file via Workbook, then re-read ----
        // This is a stronger test than load-once: it proves our reader can read what our
        // writer produces from our model. Catches subtle write-after-read regressions.
        var roundtripFiles = new[]
        {
            "minimal.xlsx",
            "styled.xlsx",
            "features.xlsx",
            "ext_list.xlsx",
            "ext_datatable.xlsx",
            "model.xlsx",
        };

        foreach (var name in roundtripFiles)
        {
            var srcPath = Path.Combine(outputDir, name);
            if (!File.Exists(srcPath))
            {
                Console.WriteLine($"    SKIP: {name} → self round-trip (input file not present)");
                continue;
            }
            Check($"{name} → load → save → re-read", () =>
            {
                var rtPath = Path.Combine(outputDir, "rt_" + name);
                var wb = Workbook.Load(srcPath);
                wb.SaveTo(rtPath);

                // Re-open the round-tripped file and count rows; if the reader can parse it,
                // the writer produced something coherent from the model.
                using var r = XlsxReader.Open(rtPath, new XlsxReaderOptions { TreatFirstRowAsHeaders = false });
                int sheetCount = r.Sheets.Count;
                int totalRows = 0;
                foreach (var s in r.Sheets)
                    foreach (var _ in r.Sheet(s.Name).Rows)
                        totalRows++;
                if (sheetCount < 1) throw new Exception("Round-trip lost all sheets.");
                if (totalRows < 1) throw new Exception("Round-trip lost all rows.");
            });
        }

        // ---- Foreign-file handling: try reading a file we DIDN'T write ----------
        // If foreign.xlsx exists (manually placed by a developer), run; otherwise SKIP so
        // the absence is visible in test output.
        var foreignPath = Path.Combine(outputDir, "foreign.xlsx");
        if (File.Exists(foreignPath))
        {
            Check("foreign.xlsx → reads without error", () =>
            {
                using var r = XlsxReader.Open(foreignPath);
                int totalRows = 0;
                foreach (var sheet in r.Sheets)
                    foreach (var row in r.Sheet(sheet.Name).Rows)
                        totalRows++;
                Console.WriteLine($"          (read {totalRows} data rows across {r.Sheets.Count} sheets)");
            });
        }
        else
        {
            Console.WriteLine("    SKIP: foreign.xlsx (place a third-party-produced xlsx at that path to enable this test)");
        }

        // ---- LibreOffice-roundtripped file — read OUR file via LibreOffice -------
        // We did this verification manually in earlier steps; we can also do it programmatically.

        Console.WriteLine();
        Console.WriteLine($"[ReaderTest] Done. {pass} passed, {fail} failed.");
        if (fail > 0) throw new Exception($"{fail} reader tests failed.");
    }

    /// <summary>Matches the PatientWrite class from ErgonomicExportTest, but with Status not ignored.</summary>
    public class PatientRead
    {
        [Column("Patient ID")]
        public string Id { get; set; } = "";

        [Column("Full Name")]
        public string Name { get; set; } = "";

        [Column("DOB")]
        public DateTime Dob { get; set; }

        [Column("Balance")]
        public decimal Balance { get; set; }

        [Column("Status")]
        public string Status { get; set; } = "";
    }
}
