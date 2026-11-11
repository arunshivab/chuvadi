using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Sheets.Model;
using static Chuvadi.Sheets.ManualTests.TestGroup;

namespace Chuvadi.Sheets.ManualTests;

/// <summary>
/// 2.1.0 item G (PR-3): ReadXlsx (DataSet), ReadXlsxSheet (DataTable) and ReadXlsx&lt;T&gt; on a Stream give the
/// same results as the path versions and leave the stream open. Encrypted input is
/// read from memory (D-44). Data is fictional (D-60).
/// </summary>
internal static class StreamReadsTest
{
    private const string Password = "fictional-pass";

    private sealed class Item
    {
        public string? Name { get; set; }
        public double Qty { get; set; }
        public DateTime When { get; set; }
    }

    public static void Run(string outputDir)
    {
        var g = new TestGroup("StreamReadsTest", "2.1.0 G: the one-line readers on a Stream...");
        string dir = Path.Combine(outputDir, "streamreads");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);

        string plain = Path.Combine(dir, "two-sheets.xlsx");
        using (var w = XlsxWriter.Create(plain))
        {
            using (var s = w.AddSheet("Items"))
            {
                s.WriteRow("Name", "Qty", "When", "Qty");
                s.WriteRow("Fictional A", 3, new DateTime(2024, 10, 2), 9);
                s.WriteRow("Fictional B", 7.5, new DateTime(1900, 1, 1), null);
            }
            using (var s = w.AddSheet("Towns"))
            {
                s.WriteRow("Town", "");
                s.WriteRow("Fictional Town", 1);
            }
            w.Save();
        }
        byte[] plainBytes = File.ReadAllBytes(plain);
        var encBytes = new MemoryStream();
        Workbook.Load(plain).SaveTo(encBytes, new EncryptionOptions { Password = Password, SpinCount = 1_000 });

        g.Check("ReadXlsx(Stream) and (Stream, password, options) = the path version (every sheet, column and value)", () =>
        {
            string path = Dump(plain.ReadXlsx());
            Eq(path, Dump(new MemoryStream(plainBytes).ReadXlsx()), "DataSet");
            Eq(path, Dump(new MemoryStream(encBytes.ToArray()).ReadXlsx(Password, null)), "encrypted DataSet");
        });

        g.Check("ReadXlsxSheet(Stream), (Stream, name) and (Stream, name, password, options) = the path versions", () =>
        {
            Eq(Dump(plain.ReadXlsxSheet()), Dump(new MemoryStream(plainBytes).ReadXlsxSheet()), "first sheet");
            Eq(Dump(plain.ReadXlsxSheet("Towns")), Dump(new MemoryStream(plainBytes).ReadXlsxSheet("Towns")), "by name");
            Eq(Dump(plain.ReadXlsxSheet(null, null, null)), Dump(new MemoryStream(encBytes.ToArray()).ReadXlsxSheet(null, Password, null)), "encrypted, first sheet");
        });

        g.Check("ReadXlsx<T>(Stream), (Stream, name) and (Stream, name, password, options) = the path versions", () =>
        {
            Eq(Dump(plain.ReadXlsx<Item>()), Dump(new MemoryStream(plainBytes).ReadXlsx<Item>()), "first sheet");
            Eq(Dump(plain.ReadXlsx<Item>("Items")), Dump(new MemoryStream(plainBytes).ReadXlsx<Item>("Items")), "by name");
            var list = new MemoryStream(encBytes.ToArray()).ReadXlsx<Item>("Items", Password, null);
            Eq(Dump(plain.ReadXlsx<Item>()), Dump(list), "encrypted");
            Eq(new DateTime(1900, 1, 1), list[1].When, "1900-01-01 read back");
        });

        g.Check("the stream is left open, as Workbook.Load(Stream) leaves it", () =>
        {
            var ms = new MemoryStream(plainBytes);
            var t = ms.ReadXlsxSheet();
            True(ms.CanRead, "closed");
            Eq(2, t.Rows.Count, "rows");
            ms.Position = 0;
            Eq(2, ms.ReadXlsx().Tables.Count, "read again from the same stream");
        });

        g.Check("errors as the path versions: null stream, a missing sheet, a password needed, options kept", () =>
        {
            Eq("input", Throws<ArgumentNullException>(() => ((Stream)null!).ReadXlsx(), "null DataSet").ParamName, "param");
            Throws<ArgumentNullException>(() => ((Stream)null!).ReadXlsxSheet(), "null DataTable");
            Throws<ArgumentNullException>(() => ((Stream)null!).ReadXlsx<Item>(), "null list");
            var pathErr = Catch(() => plain.ReadXlsxSheet("Nope"));
            var streamErr = Catch(() => new MemoryStream(plainBytes).ReadXlsxSheet("Nope"));
            Eq(pathErr.GetType(), streamErr.GetType(), "missing sheet: same exception type");
            Eq(pathErr.Message, streamErr.Message, "missing sheet: same message");
            Throws<XlsxPasswordRequiredException>(() => new MemoryStream(encBytes.ToArray()).ReadXlsx(), "encrypted without password");
            var lim = Catch(() => new MemoryStream(plainBytes).ReadXlsx(null, new XlsxReaderOptions { MaxRowsPerSheet = 1 }));
            True(lim is XlsxLimitExceededException, "options are passed on (MaxRowsPerSheet): " + lim.GetType().Name);
        });

        g.Finish();
    }

    private static Exception Catch(Action a)
    {
        try { a(); } catch (Exception ex) { return ex; }
        throw new Exception("nothing was thrown");
    }

    private static string Dump(DataSet ds) => string.Join("\n#\n", ds.Tables.Cast<DataTable>().Select(Dump));

    private static string Dump(DataTable t)
    {
        var sb = new StringBuilder(t.TableName).Append('\n');
        sb.Append(string.Join("|", t.Columns.Cast<DataColumn>().Select(c => c.ColumnName + ":" + c.DataType.Name))).Append('\n');
        foreach (DataRow r in t.Rows)
            sb.Append(string.Join("|", r.ItemArray.Select(v => v is DBNull ? "(null)" : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)))).Append('\n');
        return sb.ToString();
    }

    private static string Dump(IEnumerable<Item> items)
        => string.Join("\n", items.Select(i => $"{i.Name}|{i.Qty.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}|{i.When:O}"));
}
