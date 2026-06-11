using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.ManualTests;

/// <summary>
/// Exercises the ergonomic-export and model-API surface.
///
/// Produces FIVE files:
///   1. ext_list.xlsx     — IEnumerable&lt;T&gt;.ToXlsx() with attributes
///   2. ext_fluent.xlsx   — IEnumerable&lt;T&gt;.ToXlsx() with fluent config (overrides attributes)
///   3. ext_datatable.xlsx — DataTable.ToXlsx()
///   4. ext_multi.xlsx    — XlsxExport multi-sheet builder
///   5. model.xlsx        — Workbook model with defined names, data validation, conditional formatting
/// </summary>
internal static class ErgonomicExportTest
{
    public static void Run(string outputDir)
    {
        BuildExtListFile(outputDir);
        BuildExtFluentFile(outputDir);
        BuildExtDataTableFile(outputDir);
        BuildExtMultiFile(outputDir);
        BuildModelFile(outputDir);
    }

    // ---- Data classes for the IEnumerable<T> tests --------------------------------

    public class Patient
    {
        [Column("Patient ID", Order = 1, Width = 12)]
        public string Id { get; set; } = "";

        [Column("Full Name", Order = 2, Width = 28)]
        public string Name { get; set; } = "";

        [Column("DOB", Order = 3, Format = "yyyy-mm-dd", Width = 14)]
        public DateTime Dob { get; set; }

        [Column("Balance", Order = 4, Format = "$#,##0.00", Width = 14)]
        public decimal Balance { get; set; }

        [ColumnStyle(Bold = true)]
        [Column("Status", Order = 5, Width = 12)]
        public string Status { get; set; } = "";

        [ColumnIgnore]
        public string InternalNotes { get; set; } = "";
    }

    private static List<Patient> SamplePatients() => new()
    {
        new() { Id = "P0001", Name = "Anand Kumar",   Dob = new(1985, 3, 12),  Balance = 1500.00m, Status = "Active",   InternalNotes = "confidential" },
        new() { Id = "P0002", Name = "Priya Sharma",  Dob = new(1992, 11, 3),  Balance = 0m,       Status = "Closed",   InternalNotes = "confidential" },
        new() { Id = "P0003", Name = "Rahul Verma",   Dob = new(1978, 6, 28),  Balance = 4250.50m, Status = "Active",   InternalNotes = "confidential" },
        new() { Id = "P0004", Name = "Kavita Reddy",  Dob = new(2001, 1, 15),  Balance = 750.25m,  Status = "Pending",  InternalNotes = "confidential" },
        new() { Id = "P0005", Name = "Suresh Iyer",   Dob = new(1969, 9, 9),   Balance = 12350m,   Status = "Active",   InternalNotes = "confidential" },
    };

    // ---- ext_list.xlsx — IEnumerable<T>.ToXlsx() with attributes only --------------

    private static void BuildExtListFile(string outputDir)
    {
        Console.WriteLine("[ErgonomicExportTest] Building ext_list.xlsx (attributes only)...");
        var path = Path.Combine(outputDir, "ext_list.xlsx");
        if (File.Exists(path)) File.Delete(path);

        SamplePatients().ToXlsx(path);

        Console.WriteLine($"[ErgonomicExportTest]   Wrote: {path} ({new FileInfo(path).Length:N0} bytes)");
        Console.WriteLine("[ErgonomicExportTest]   Columns ordered by [Column(Order=...)]; InternalNotes ignored; Status bold via [ColumnStyle].");
    }

    // ---- ext_fluent.xlsx — fluent overrides + AutoFilter + FreezeHeaderRow ---------

    private static void BuildExtFluentFile(string outputDir)
    {
        Console.WriteLine("[ErgonomicExportTest] Building ext_fluent.xlsx (fluent config)...");
        var path = Path.Combine(outputDir, "ext_fluent.xlsx");
        if (File.Exists(path)) File.Delete(path);

        var headerStyle = new CellStyleBuilder()
            .Bold()
            .Background("#1A237E")
            .Foreground("#FFFFFF")
            .HAlign(HorizontalAlign.Center)
            .Build();

        var overdueStyle = new CellStyleBuilder().Bold().Foreground("#C62828").Build();

        SamplePatients().ToXlsx(path, cfg => cfg
            .SheetName("Active Patients")
            .HeaderStyle(headerStyle)
            .AutoFilter()
            .FreezeHeaderRow()
            .Column(p => p.Id, width: 14)
            .Column(p => p.Name, header: "Patient Name")
            .Ignore(p => p.Status)  // drop the Status column entirely
            .ColumnStyle(p => p.Balance, overdueStyle));

        Console.WriteLine($"[ErgonomicExportTest]   Wrote: {path} ({new FileInfo(path).Length:N0} bytes)");
        Console.WriteLine("[ErgonomicExportTest]   Fluent config overrides attribute-set header/width; Status excluded; autofilter+freeze.");
    }

    // ---- ext_datatable.xlsx — DataTable.ToXlsx() ----------------------------------

    private static void BuildExtDataTableFile(string outputDir)
    {
        Console.WriteLine("[ErgonomicExportTest] Building ext_datatable.xlsx ...");
        var path = Path.Combine(outputDir, "ext_datatable.xlsx");
        if (File.Exists(path)) File.Delete(path);

        var dt = new DataTable("Invoices");
        dt.Columns.Add("InvoiceNo", typeof(string));
        dt.Columns.Add("Date", typeof(DateTime));
        dt.Columns.Add("Customer", typeof(string));
        dt.Columns.Add("Amount", typeof(decimal));
        dt.Columns.Add("Paid", typeof(bool));
        dt.Columns.Add("Internal", typeof(string));

        dt.Rows.Add("INV-001", new DateTime(2026, 1, 10), "Acme Corp",   12500.00m, true,  "internal-1");
        dt.Rows.Add("INV-002", new DateTime(2026, 1, 18), "Globex Ltd",   3450.75m, false, "internal-2");
        dt.Rows.Add("INV-003", new DateTime(2026, 1, 22), "Initech",     22100.50m, true,  "internal-3");
        dt.Rows.Add("INV-004", new DateTime(2026, 2,  3), "Umbrella",      875.20m, false, "internal-4");

        dt.ToXlsx(path, cfg => cfg
            .HeaderStyle(new CellStyleBuilder().Bold().Background("#0E5A8A").Foreground("#FFFFFF").Build())
            .Column("InvoiceNo", header: "Invoice #", width: 14)
            .Column("Customer", width: 22)
            .ColumnFormat("Date", "yyyy-mm-dd")
            .ColumnFormat("Amount", "$#,##0.00")
            .Ignore("Internal")
            .AutoFilter()
            .FreezeHeaderRow());

        Console.WriteLine($"[ErgonomicExportTest]   Wrote: {path} ({new FileInfo(path).Length:N0} bytes)");
        Console.WriteLine("[ErgonomicExportTest]   Sheet name from DataTable.TableName; 'Internal' column dropped.");
    }

    // ---- ext_multi.xlsx — XlsxExport multi-sheet ----------------------------------

    private static void BuildExtMultiFile(string outputDir)
    {
        Console.WriteLine("[ErgonomicExportTest] Building ext_multi.xlsx (multi-sheet builder)...");
        var path = Path.Combine(outputDir, "ext_multi.xlsx");
        if (File.Exists(path)) File.Delete(path);

        var dt = new DataTable("Visits");
        dt.Columns.Add("PatientId", typeof(string));
        dt.Columns.Add("Date", typeof(DateTime));
        dt.Columns.Add("Provider", typeof(string));
        dt.Rows.Add("P0001", new DateTime(2026, 1, 5), "Dr. Mehta");
        dt.Rows.Add("P0001", new DateTime(2026, 2, 1), "Dr. Singh");
        dt.Rows.Add("P0003", new DateTime(2026, 1, 18), "Dr. Mehta");

        new XlsxExport(path)
            .AddSheet("Patients", SamplePatients(), cfg => cfg
                .AutoFilter()
                .FreezeHeaderRow())
            .AddSheet("Visits", dt, cfg => cfg
                .ColumnFormat("Date", "yyyy-mm-dd")
                .AutoFilter()
                .FreezeHeaderRow())
            .Save();

        Console.WriteLine($"[ErgonomicExportTest]   Wrote: {path} ({new FileInfo(path).Length:N0} bytes)");
        Console.WriteLine("[ErgonomicExportTest]   Two sheets: Patients (IEnumerable<T>) + Visits (DataTable).");
    }

    // ---- model.xlsx — Workbook model end-to-end -----------------------------------

    private static void BuildModelFile(string outputDir)
    {
        Console.WriteLine("[ErgonomicExportTest] Building model.xlsx (Workbook model)...");
        var path = Path.Combine(outputDir, "model.xlsx");
        if (File.Exists(path)) File.Delete(path);

        var wb = new Workbook();

        // Defined names — referenceable in any formula in the workbook.
        wb.DefinedNames.Add("TaxRate", "0.18");
        wb.DefinedNames.Add("CompanyName", "\"Acme Health Pvt Ltd\"");

        // ---- Sheet 1: Direct cell-by-cell building ----
        var summary = wb.AddSheet("Summary");
        summary.Columns[1].Width = 22;
        summary.Columns[2].Width = 18;
        summary.FreezeRows(1);

        var headerStyle = new CellStyleBuilder()
            .Bold().Background("#1A237E").Foreground("#FFFFFF")
            .HAlign(HorizontalAlign.Center)
            .Build();

        var currencyStyle = new CellStyleBuilder().Format("$#,##0.00").Build();

        // Title row, merged.
        summary.Cell("A1").Value = "Acme Health — Q1 2026 Summary";
        summary.Cell("A1").Style = new CellStyleBuilder()
            .Bold().FontSize(16).Background("#1A237E").Foreground("#FFFFFF")
            .HAlign(HorizontalAlign.Center).Build();
        summary.Range("A1:B1").Merge();

        // Header row at row 2 (no special meaning, just illustrative).
        summary.Cell("A2").Value = "Metric"; summary.Cell("A2").Style = headerStyle;
        summary.Cell("B2").Value = "Value";  summary.Cell("B2").Style = headerStyle;

        // Data cells with values, formulas, defined-name use.
        summary.Cell("A3").Value = "Total Revenue";
        summary.Cell("B3").Value = 250000m;
        summary.Cell("B3").Style = currencyStyle;

        summary.Cell("A4").Value = "Tax (TaxRate)";
        summary.Cell("B4").Formula = "B3 * TaxRate";  // uses defined name
        summary.Cell("B4").Style = currencyStyle;

        summary.Cell("A5").Value = "Net";
        summary.Cell("B5").Formula = "B3 - B4";
        summary.Cell("B5").Style = currencyStyle;

        summary.Cell("A6").Value = "Owner";
        summary.Cell("B6").Formula = "CompanyName";

        // ---- Sheet 2: Data validation + conditional formatting ----
        var data = wb.AddSheet("Tracker");
        data.Columns[1].Width = 8;
        data.Columns[2].Width = 24;
        data.Columns[3].Width = 12;
        data.Columns[4].Width = 14;
        data.FreezeRows(1);

        data.Cell("A1").Value = "ID";       data.Cell("A1").Style = headerStyle;
        data.Cell("B1").Value = "Patient";  data.Cell("B1").Style = headerStyle;
        data.Cell("C1").Value = "Status";   data.Cell("C1").Style = headerStyle;
        data.Cell("D1").Value = "Score";    data.Cell("D1").Style = headerStyle;

        var rng = new Random(42);
        for (int i = 0; i < 10; i++)
        {
            int r = i + 2;
            data.Cell(r, 1).Value = $"P{i + 1:D3}";
            data.Cell(r, 2).Value = $"Patient {i + 1}";
            data.Cell(r, 3).Value = (new[] { "Pending", "Active", "Closed" })[i % 3];
            data.Cell(r, 4).Value = rng.Next(40, 100);
        }

        // Data validation: Status column gets a dropdown.
        data.AddDataValidation("C2:C1000",
            DataValidation.List("Pending", "Active", "Closed"));

        // Data validation: Score must be 0..100.
        data.AddDataValidation("D2:D1000",
            DataValidation.WholeNumber(min: 0, max: 100));

        // Conditional formatting: Score gets a red→white→green scale.
        data.AddConditionalFormat("D2:D11",
            ConditionalFormat.ColorScale.RedWhiteGreen);

        // ---- Save ----
        wb.SaveTo(path);

        Console.WriteLine($"[ErgonomicExportTest]   Wrote: {path} ({new FileInfo(path).Length:N0} bytes)");
        Console.WriteLine("[ErgonomicExportTest]   Sheet1: title merge, defined-name formulas. Sheet2: dropdown + numeric validation + color scale.");
        Console.WriteLine();
    }
}
