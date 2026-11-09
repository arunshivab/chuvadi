using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using Chuvadi.Sheets.Model;

namespace Chuvadi.Sheets.ManualTests;

/// <summary>
/// 2.0.3 (SH-10, D-283, D-285, D-286) and 2.0.4 (D-292): DataTable column names from the header row in
/// <c>ReadXlsx</c> (DataSet) and <c>ReadXlsxSheet</c> (DataTable).
/// <list type="bullet">
/// <item>Columns by position. A text header keeps its text exactly. A number, date, boolean or error
/// header is named by its display text.</item>
/// <item>Repeats (ignoring case) and empty headers are named as Excel names a table's columns: typed
/// names are reserved; one shared counter numbers repeats (name + N, never 1) and empty headers
/// (Column + N), skipping names already used.</item>
/// <item>A sheet with a header row and no data rows still gets its columns (A2).</item>
/// <item>Unchanged: RowReader by-name lookup and ReadXlsx&lt;T&gt; (first occurrence wins, text
/// headers only), and tables with unique text headers.</item>
/// </list>
/// Data is fictional (D-60).
/// </summary>
internal static class DataTablesTest
{
    public sealed class Visit
    {
        public string? Name { get; set; }
        public double Qty { get; set; }
        public string? Town { get; set; }
    }

    public static void Run(string outputDir)
    {
        Console.WriteLine("[DataTablesTest] 2.0.3–2.0.4 DataTable column names (Excel table names) from the header row...");
        Console.WriteLine();
        int pass = 0, fail = 0;
        var failed = new FailedChecks();
        string dir = Path.Combine(outputDir, "datatables");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);

        void Check(string label, Action body)
        {
            try { body(); Console.WriteLine($"    PASS: {label}"); pass++; }
            catch (Exception ex) { Console.WriteLine($"    FAIL: {label}"); Console.WriteLine($"          {ex.GetType().Name}: {ex.Message}"); fail++; failed.Add(label, ex); }
        }

        // ---- A1: the naming rule ---------------------------------------------------------------------

        Check("unique text headers: unchanged", () =>
        {
            var p = Make(dir, "unique", new object?[] { "Name", "Qty", "Town" });
            Columns(p, "Name", "Qty", "Town");
            Values(p);
        });

        Check("duplicate: Qty, Qty, Town -> Qty, Qty2, Town; the second column's values go to Qty2", () =>
        {
            var p = Make(dir, "dup", new object?[] { "Qty", "Qty", "Town" });
            Columns(p, "Qty", "Qty2", "Town");
            var t = p.ReadXlsxSheet();
            Eq("row 1 Qty", "Asha", t.Rows[0]["Qty"]);
            Eq("row 1 Qty2", 3.0, t.Rows[0]["Qty2"]);
            Eq("row 2 Town", "Kochi", t.Rows[1]["Town"]);
        });

        Check("three duplicates: Qty, Qty, Qty -> Qty, Qty2, Qty3", () =>
            Columns(Make(dir, "dup3", new object?[] { "Qty", "Qty", "Qty" }), "Qty", "Qty2", "Qty3"));

        Check("duplicate in the last column: Name, Town, Town -> Name, Town, Town2 (2.0.2: Column3)", () =>
            Columns(Make(dir, "duplast", new object?[] { "Name", "Town", "Town" }), "Name", "Town", "Town2"));

        Check("case only: qty, Qty -> qty, Qty2", () =>
            Columns(Make(dir, "case", new object?[] { "qty", "Qty", "Town" }), "qty", "Qty2", "Town"));

        Check("a typed Qty2 header after two Qty keeps its name: Qty, Qty, Qty2 -> Qty, Qty3, Qty2 (2.0.3: Qty2, Qty22)", () =>
            Columns(Make(dir, "realqty2", new object?[] { "Qty", "Qty", "Qty2" }), "Qty", "Qty3", "Qty2"));

        Check("blank header: Name, (blank), Town -> Name, Column1, Town (2.0.3: Column2)", () =>
        {
            var p = Make(dir, "blank", new object?[] { "Name", null, "Town" });
            Columns(p, "Name", "Column1", "Town");
            Eq("row 1 Column1", 3.0, p.ReadXlsxSheet().Rows[0]["Column1"]);
        });

        Check("blank third column next to a typed Column3 header -> Column3, Name, Column1 (2.0.3: Column32)", () =>
            Columns(Make(dir, "column3", new object?[] { "Column3", "Name", null }), "Column3", "Name", "Column1"));

        Check("number header: Name, 2024, Town -> Name, 2024, Town", () =>
            Columns(Make(dir, "number", new object?[] { "Name", 2024, "Town" }), "Name", "2024", "Town"));

        Check("boolean header: TRUE", () =>
            Columns(Make(dir, "bool", new object?[] { "Name", true, "Town" }), "Name", "TRUE", "Town"));

        Check("whitespace header kept exactly: \" \"", () =>
            Columns(Make(dir, "space", new object?[] { "Name", " ", "Town" }), "Name", " ", "Town"));

        Check("date header under dd-mm-yyyy, and a number under #,##0 with en-IN", () =>
        {
            var p = Path.Combine(dir, "dateheader.xlsx");
            var wb = new Workbook();
            var s = wb.AddSheet("Data");
            s.Cell("A1").Value = "Name";
            s.Cell("B1").Value = new DateTime(2024, 4, 1);
            s.Cell("B1").Style = new CellStyleBuilder().Format("dd-mm-yyyy").Build();
            s.Cell("C1").Value = 1234567.0;
            s.Cell("C1").Style = new CellStyleBuilder().Format("#,##0").Build();
            s.Cell("A2").Value = "Asha";
            s.Cell("B2").Value = 3.0;
            s.Cell("C2").Value = "Pune";
            wb.SaveTo(p);
            Names(p.ReadXlsxSheet(), "Name", "01-04-2024", "1,234,567");
            var inIn = p.ReadXlsxSheet(null, null, new XlsxReaderOptions { DisplayCulture = CultureInfo.GetCultureInfo("en-IN") });
            Names(inIn, "Name", "01-04-2024", "12,34,567");
        });

        Check("leading empty columns: headers in C1:D1 -> Column1, Column2, Qty, Town", () =>
        {
            var p = Path.Combine(dir, "leading.xlsx");
            var wb = new Workbook();
            var s = wb.AddSheet("Data");
            s.Cell("C1").Value = "Qty";
            s.Cell("D1").Value = "Town";
            s.Cell("C2").Value = 3.0;
            s.Cell("D2").Value = "Pune";
            wb.SaveTo(p);
            var t = p.ReadXlsxSheet();
            Names(t, "Column1", "Column2", "Qty", "Town");
            Eq("row 1 Qty", 3.0, t.Rows[0]["Qty"]);
            Eq("row 1 Column1", DBNull.Value, t.Rows[0]["Column1"]);
        });

        Check("data wider than the header row: extra columns are named as empty headers, by the shared counter", () =>
        {
            var p = Path.Combine(dir, "wider-data.xlsx");
            using (var w = XlsxWriter.Create(p))
            {
                var s = w.AddSheet("Data");
                s.WriteRow("Name", "Column3");
                s.WriteRow("Asha", 3.0, "Pune", "x");
                w.Save();
            }
            Names(p.ReadXlsxSheet(), "Name", "Column3", "Column1", "Column2");
        });

        Check("header row wider than the data: every header is a column, empty values are DBNull", () =>
        {
            var p = Path.Combine(dir, "wider-header.xlsx");
            using (var w = XlsxWriter.Create(p))
            {
                var s = w.AddSheet("Data");
                s.WriteRow("Name", "Qty", "Town", "Notes");
                s.WriteRow("Asha", 3.0);
                w.Save();
            }
            var t = p.ReadXlsxSheet();
            Names(t, "Name", "Qty", "Town", "Notes");
            Eq("row 1 Notes", DBNull.Value, t.Rows[0]["Notes"]);
        });

        Check("a styled empty cell after the last header adds no column", () =>
        {
            var p = Path.Combine(dir, "styled-trailing.xlsx");
            var wb = new Workbook();
            var s = wb.AddSheet("Data");
            s.Cell("A1").Value = "Name";
            s.Cell("B1").Value = "Qty";
            s.Cell("C1").Style = new CellStyleBuilder().Bold().Build();
            s.Cell("A2").Value = "Asha";
            s.Cell("B2").Value = 3.0;
            wb.SaveTo(p);
            Names(p.ReadXlsxSheet(), "Name", "Qty");
        });

        // ---- 2.0.4: Excel's own names, observed by Arun with Insert > Table on 22 header rows (D-292) ----
        // Expected names are exactly what Excel showed. null = an empty header cell.

        var excel = new (string Label, object?[] Headers, string[] Excel)[]
        {
            ("Qty, Qty, Town", new object?[] { "Qty", "Qty", "Town" }, new[] { "Qty", "Qty2", "Town" }),
            ("Qty, Qty, (blank), 2024", new object?[] { "Qty", "Qty", null, 2024 }, new[] { "Qty", "Qty2", "Column3", "2024" }),
            ("Name, (blank), Town", new object?[] { "Name", null, "Town" }, new[] { "Name", "Column1", "Town" }),
            ("Name, (blank), (blank)", new object?[] { "Name", null, null }, new[] { "Name", "Column1", "Column2" }),
            ("Name, 2024, Town", new object?[] { "Name", 2024, "Town" }, new[] { "Name", "2024", "Town" }),
            ("qty, Qty, Town", new object?[] { "qty", "Qty", "Town" }, new[] { "qty", "Qty2", "Town" }),
            ("Name, Town, Town", new object?[] { "Name", "Town", "Town" }, new[] { "Name", "Town", "Town2" }),
            ("Column3, Name, (blank)", new object?[] { "Column3", "Name", null }, new[] { "Column3", "Name", "Column1" }),
            ("Qty, Qty, Qty", new object?[] { "Qty", "Qty", "Qty" }, new[] { "Qty", "Qty2", "Qty3" }),
            ("Qty, Qty, Qty2", new object?[] { "Qty", "Qty", "Qty2" }, new[] { "Qty", "Qty3", "Qty2" }),
            ("Qty, Qty, (blank)", new object?[] { "Qty", "Qty", null }, new[] { "Qty", "Qty2", "Column3" }),
            ("Name, (blank), 2024", new object?[] { "Name", null, 2024 }, new[] { "Name", "Column1", "2024" }),
            ("2024, Name, (blank)", new object?[] { 2024, "Name", null }, new[] { "2024", "Name", "Column1" }),
            ("Qty, Qty, (blank), Town", new object?[] { "Qty", "Qty", null, "Town" }, new[] { "Qty", "Qty2", "Column3", "Town" }),
            ("Name, (blank), Town, (blank)", new object?[] { "Name", null, "Town", null }, new[] { "Name", "Column1", "Town", "Column2" }),
            ("Qty, Qty, (blank), (blank)", new object?[] { "Qty", "Qty", null, null }, new[] { "Qty", "Qty2", "Column3", "Column4" }),
            ("(blank), Name, Town", new object?[] { null, "Name", "Town" }, new[] { "Column1", "Name", "Town" }),
            ("Name, Qty, Town, (blank)", new object?[] { "Name", "Qty", "Town", null }, new[] { "Name", "Qty", "Town", "Column1" }),
            ("Qty, Qty, Town, Town", new object?[] { "Qty", "Qty", "Town", "Town" }, new[] { "Qty", "Qty2", "Town", "Town3" }),
            ("(blank), Qty, Qty", new object?[] { null, "Qty", "Qty" }, new[] { "Column1", "Qty", "Qty2" }),
            ("Qty, Qty, (blank), Qty", new object?[] { "Qty", "Qty", null, "Qty" }, new[] { "Qty", "Qty2", "Column3", "Qty4" }),
            ("Name, Name, Column3, (blank)", new object?[] { "Name", "Name", "Column3", null }, new[] { "Name", "Name2", "Column3", "Column4" }),
        };
        for (int e = 0; e < excel.Length; e++)
        {
            var c = excel[e];
            Check($"Excel {e + 1:00}: {c.Label} -> {string.Join(", ", c.Excel)}", () =>
                Columns(Make(dir, $"excel{e + 1:00}", c.Headers), c.Excel));
        }

        // ---- A2: header row with no data rows --------------------------------------------------------

        Check("header-only sheet: Qty, Qty, (blank), 2024 -> Qty, Qty2, Column3, 2024 with no rows", () =>
        {
            var p = Path.Combine(dir, "header-only.xlsx");
            using (var w = XlsxWriter.Create(p))
            {
                w.AddSheet("Data").WriteRow("Qty", "Qty", null, 2024);
                w.Save();
            }
            var t = p.ReadXlsxSheet();
            Names(t, "Qty", "Qty2", "Column3", "2024");
            Eq("rows", 0, t.Rows.Count);
        });

        Check("completely empty sheet: no columns, no rows", () =>
        {
            var p = Path.Combine(dir, "empty.xlsx");
            using (var w = XlsxWriter.Create(p))
            {
                w.AddSheet("Data");
                w.Save();
            }
            var t = p.ReadXlsxSheet();
            Eq("columns", 0, t.Columns.Count);
            Eq("rows", 0, t.Rows.Count);
        });

        // ---- Every entry point ----------------------------------------------------------------------

        Check("DataSet: one table per sheet, each named by the rule", () =>
        {
            var p = Path.Combine(dir, "dataset.xlsx");
            using (var w = XlsxWriter.Create(p))
            {
                w.AddSheet("First").WriteRow("Qty", "Qty").WriteRow(1.0, 2.0);
                w.AddSheet("Second").WriteRow("Name", null, "Name").WriteRow("Asha", 3.0, "Ravi");
                w.Save();
            }
            var ds = p.ReadXlsx();
            Eq("tables", 2, ds.Tables.Count);
            Names(ds.Tables["First"]!, "Qty", "Qty2");
            Names(ds.Tables["Second"]!, "Name", "Column1", "Name2");
            Names(p.ReadXlsxSheet("Second"), "Name", "Column1", "Name2");
        });

        Check("encrypted file: the same names", () =>
        {
            var p = Path.Combine(dir, "encrypted.xlsx");
            using (var w = XlsxWriter.Create(p, new XlsxWriterOptions { Encryption = new EncryptionOptions { Password = "pw", SpinCount = 1_000 } }))
            {
                w.AddSheet("Data").WriteRow("Qty", "Qty", null).WriteRow(1.0, 2.0, 3.0);
                w.Save();
            }
            Names(p.ReadXlsxSheet(null, "pw", null), "Qty", "Qty2", "Column3");
            Names(p.ReadXlsx("pw", null).Tables[0], "Qty", "Qty2", "Column3");
        });

        // ---- Unchanged: by-name lookup and ReadXlsx<T> ------------------------------------------------

        Check("RowReader by name and ReadXlsx<T> still take the first occurrence", () =>
        {
            var p = Make(dir, "byname", new object?[] { "Name", "Qty", "Town", "Qty" });   // the second Qty holds "first"
            using (var r = XlsxReader.Open(p))
            {
                var row = r.Sheet(1).Rows.First();
                Eq("by name Qty", 3.0, row["Qty"]);
                Eq("by name Town", "Pune", row["Town"]);
            }
            Names(p.ReadXlsxSheet(), "Name", "Qty", "Town", "Qty2");
            var list = p.ReadXlsx<Visit>();
            Eq("list count", 2, list.Count);
            Eq("list Qty", 3.0, list[0].Qty);
            Eq("list Town", "Pune", list[0].Town);
        });

        Console.WriteLine();
        Console.WriteLine($"[DataTablesTest] {pass} passed, {fail} failed.");
        Console.WriteLine();
        if (fail > 0) throw new Exception(failed.Summary($"DataTablesTest: {fail} check(s) failed."));
    }

    /// <summary>A sheet "Data" with the given header row and two fictional data rows.</summary>
    private static string Make(string dir, string name, object?[] headers)
    {
        var p = Path.Combine(dir, name + ".xlsx");
        using var w = XlsxWriter.Create(p);
        var s = w.AddSheet("Data");
        s.WriteRow(headers);
        var rows = new[] { new object?[] { "Asha", 3.0, "Pune", "first" }, new object?[] { "Ravi", 5.0, "Kochi", "second" } };
        foreach (var r in rows) s.WriteRow(r.Take(headers.Length).ToArray());
        w.Save();
        return p;
    }

    private static void Columns(string path, params string[] expected) => Names(path.ReadXlsxSheet(), expected);

    private static void Names(DataTable t, params string[] expected)
    {
        var actual = t.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
            throw new Exception($"columns: expected [{string.Join(" | ", expected)}] got [{string.Join(" | ", actual)}]");
        foreach (DataColumn c in t.Columns)
            if (c.DataType != typeof(object)) throw new Exception($"column {c.ColumnName}: type {c.DataType.Name}");
    }

    private static void Values(string path)
    {
        var t = path.ReadXlsxSheet();
        Eq("rows", 2, t.Rows.Count);
        Eq("row 1 Name", "Asha", t.Rows[0]["Name"]);
        Eq("row 2 Qty", 5.0, t.Rows[1]["Qty"]);
        Eq("row 2 Town", "Kochi", t.Rows[1]["Town"]);
    }

    private static void Eq(string what, object? expected, object? actual)
    {
        if (!Equals(expected, actual))
            throw new Exception($"{what}: expected <{expected}> got <{actual}>");
    }
}
