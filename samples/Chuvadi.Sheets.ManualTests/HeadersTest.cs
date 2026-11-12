using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using Chuvadi.Sheets.Model;
using static Chuvadi.Sheets.ManualTests.TestGroup;

namespace Chuvadi.Sheets.ManualTests;

/// <summary>
/// 2.1.0 item D (SH-6, D-306 (a)): RowReader.HasColumn, RowReader.HeaderNames and SheetReader.HeaderNames on the
/// streaming reader, on the header rows the 2.0.3/2.0.4 datatables group uses (repeats, blanks, numbers, a
/// header-only sheet). Data is fictional (D-60).
/// </summary>
internal static class HeadersTest
{
    public static void Run(string outputDir)
    {
        var g = new TestGroup("HeadersTest", "2.1.0 D: HasColumn and HeaderNames on the streaming reader...");
        string dir = Path.Combine(outputDir, "headers");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);

        string mixed = Write(dir, "mixed.xlsx",
            new object?[] { "Name", "qty", null, 2024.0, "Qty", " Town ", "" , "Notes" },
            new object?[] { "Fictional A", 3.0, "x", 1.0, 9.0, "Fictional Town", "y", "z" });

        g.Check("HeaderNames: one entry per header cell by position, raw text, null where empty or not text", () =>
        {
            using var r = XlsxReader.Open(mixed);
            var sheet = r.Sheet("Fictional");
            True(sheet.HeaderNames is null, "SheetReader.HeaderNames is null before the header row is read");
            using var e = sheet.Rows.GetEnumerator();
            True(e.MoveNext(), "a data row");
            var expected = new string?[] { "Name", "qty", null, null, "Qty", " Town ", null, "Notes" };
            Eq(Show(expected), Show(sheet.HeaderNames!), "SheetReader.HeaderNames");
            Eq(Show(expected), Show(e.Current.HeaderNames), "RowReader.HeaderNames");
        });

        g.Check("HasColumn: the same lookup as row[name] (case ignored, text headers only, first of a repeat)", () =>
        {
            using var r = XlsxReader.Open(mixed);
            foreach (var row in r.Sheet("Fictional").Rows)
            {
                True(row.HasColumn("Name") && row.HasColumn("NAME"), "Name in any case");
                True(row.HasColumn("Qty") && row.HasColumn("qty"), "qty / Qty");
                Eq(3.0, row["QTY"], "row[\"QTY\"] is the first (leftmost) qty");
                True(row.HasColumn(" Town ") && !row.HasColumn("Town"), "headers are not trimmed");
                True(!row.HasColumn("2024"), "a number header is not a name");
                True(!row.HasColumn(""), "an empty name");
                True(!row.HasColumn("Missing") && row["Missing"] is null, "an unknown name: false, and row[name] null as before");
                Eq("name", Throws<ArgumentNullException>(() => row.HasColumn(null!), "null").ParamName, "param");
            }
        });

        g.Check("a header-only sheet: Rows yields nothing but SheetReader.HeaderNames is filled after MoveNext", () =>
        {
            string p = Write(dir, "header-only.xlsx", new object?[] { "Id", "Fictional Name" });
            using var r = XlsxReader.Open(p);
            var sheet = r.Sheet("Fictional");
            using var e = sheet.Rows.GetEnumerator();
            True(!e.MoveNext(), "no data rows");
            Eq("Id|Fictional Name", Show(sheet.HeaderNames!), "HeaderNames");
        });

        g.Check("without headers: HasColumn false, RowReader.HeaderNames empty, SheetReader.HeaderNames null", () =>
        {
            using var r = XlsxReader.Open(mixed, new XlsxReaderOptions { TreatFirstRowAsHeaders = false });
            var sheet = r.Sheet("Fictional");
            int n = 0;
            foreach (var row in sheet.Rows)
            {
                n++;
                True(!row.HasColumn("Name"), "HasColumn");
                Eq(0, row.HeaderNames.Count, "RowReader.HeaderNames");
                Throws<InvalidOperationException>(() => _ = row["Name"], "row[name] still throws without headers");
            }
            Eq(2, n, "both rows are data rows");
            True(sheet.HeaderNames is null, "SheetReader.HeaderNames");
        });

        g.Check("HeaderNames are the raw texts; ReadXlsxSheet's DataTable names stay unique (2.0.4)", () =>
        {
            DataTable t = mixed.ReadXlsxSheet();
            string names = string.Join("|", t.Columns.Cast<DataColumn>().Select(c => c.ColumnName));
            // 2.0.4's Excel naming, unchanged: a number header keeps its text, blanks count from Column1, repeats get 2.
            Eq("Name|qty|Column1|2024|Qty2| Town |Column3|Notes", names, "DataTable column names");
        });

        g.Finish();
    }

    private static string Write(string dir, string name, params object?[][] rows)
    {
        var p = Path.Combine(dir, name);
        using var w = XlsxWriter.Create(p);
        using (var s = w.AddSheet("Fictional"))
            foreach (var row in rows) s.WriteRow(row);
        w.Save();
        return p;
    }

    private static string Show(IEnumerable<string?> names) => string.Join("|", names.Select(n => n ?? "(null)"));
}
