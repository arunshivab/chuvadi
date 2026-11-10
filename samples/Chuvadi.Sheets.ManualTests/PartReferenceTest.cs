using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Chuvadi.Sheets.Model;

namespace Chuvadi.Sheets.ManualTests;

/// <summary>
/// Sheets 2.0.2 (D-211, D-219): how the reader follows part references.
/// <list type="bullet">
/// <item>G1: relationship targets with dot segments ("./", "../", also in absolute targets) resolve as
/// RFC 3986 says, at the root, workbook and sheet level. Excel opens such files.</item>
/// <item>G2: a workbook or worksheet part that is neither in the zip nor declared is a malformed file
/// (XlsxFormatException with the FileNotFoundException inside), at every read entry point.</item>
/// <item>F3: a text cell whose shared-string index is missing, negative, out of range or not a number
/// makes the file a damaged file (XlsxFormatException); an empty value stays an empty cell.</item>
/// </list>
/// Every file is a Chuvadi-written workbook with one reference edited in memory. Data is fictional (D-60).
/// </summary>
internal static class PartReferenceTest
{
    public static void Run(string outputDir)
    {
        Console.WriteLine("[PartReferenceTest] 2.0.2 relationship targets, missing parts, shared-string references...");
        Console.WriteLine();
        int pass = 0, fail = 0;
        var failed = new FailedChecks();
        string dir = Path.Combine(outputDir, "partrefs");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);

        void Check(string label, Action body)
        {
            try { body(); Console.WriteLine($"    PASS: {label}"); pass++; }
            catch (Exception ex) { Console.WriteLine($"    FAIL: {label}"); Console.WriteLine($"          {ex.GetType().Name}: {ex.Message}"); fail++; failed.Add(label, ex); }
        }

        byte[] baseFile = BaseWorkbook();

        // ---- G1: dot segments ------------------------------------------------------------------------

        var targets = new (string Label, string RelsPart, string From, string To)[]
        {
            ("workbook rels \"./worksheets/sheet1.xml\"", "xl/_rels/workbook.xml.rels", "worksheets/sheet1.xml", "./worksheets/sheet1.xml"),
            ("workbook rels \"/xl/../xl/worksheets/sheet1.xml\"", "xl/_rels/workbook.xml.rels", "worksheets/sheet1.xml", "/xl/../xl/worksheets/sheet1.xml"),
            ("workbook rels \"/xl/./worksheets/sheet1.xml\"", "xl/_rels/workbook.xml.rels", "worksheets/sheet1.xml", "/xl/./worksheets/sheet1.xml"),
            ("workbook rels \"../xl/worksheets/sheet1.xml\" (worked before)", "xl/_rels/workbook.xml.rels", "worksheets/sheet1.xml", "../xl/worksheets/sheet1.xml"),
            ("workbook rels \"..\" above the root stops at the root", "xl/_rels/workbook.xml.rels", "worksheets/sheet1.xml", "../../../xl/worksheets/sheet1.xml"),
            ("workbook rels shared strings \"./sharedStrings.xml\"", "xl/_rels/workbook.xml.rels", "sharedStrings.xml", "./sharedStrings.xml"),
            ("root rels \"./xl/workbook.xml\"", "_rels/.rels", "xl/workbook.xml", "./xl/workbook.xml"),
            ("root rels \"/xl/../xl/workbook.xml\"", "_rels/.rels", "xl/workbook.xml", "/xl/../xl/workbook.xml"),
            ("sheet rels comments \"/xl/./comments1.xml\"", "xl/worksheets/_rels/sheet1.xml.rels", "../../xl/comments1.xml", "/xl/./comments1.xml"),
            ("sheet rels comments \"/xl/worksheets/../comments1.xml\"", "xl/worksheets/_rels/sheet1.xml.rels", "../../xl/comments1.xml", "/xl/worksheets/../comments1.xml"),
        };
        foreach (var t in targets)
            Check("G1: " + t.Label + " opens like Excel", () =>
            {
                byte[] file = EditEntry(baseFile, t.RelsPart, x => ReplaceTarget(x, t.From, t.To));
                CheckReadsBase(file);
            });

        Check("G1: targets without dot segments resolve exactly as before (the unedited file)", () => CheckReadsBase(baseFile));

        // ---- G2: missing required parts ----------------------------------------------------------------

        byte[] noSheet = RemoveUndeclared(baseFile, "xl/worksheets/sheet1.xml");
        byte[] noWorkbook = RemoveUndeclared(baseFile, "xl/workbook.xml");

        Check("G2: missing, undeclared workbook part: Workbook.Load, XlsxReader.Open, ReadXlsx are XlsxFormatException", () =>
        {
            string path = Path.Combine(dir, "no-workbook.xlsx");
            File.WriteAllBytes(path, noWorkbook);
            Malformed(() => Workbook.Load(new MemoryStream(noWorkbook)), "Workbook.Load(stream)", "/xl/workbook.xml");
            Malformed(() => Workbook.Load(path), "Workbook.Load(path)", "/xl/workbook.xml");
            Malformed(() => XlsxReader.Open(new MemoryStream(noWorkbook)).Dispose(), "XlsxReader.Open", "/xl/workbook.xml");
            Malformed(() => path.ReadXlsx(), "ReadXlsx(path)", "/xl/workbook.xml");
        });

        Check("G2: missing, undeclared sheet part: Workbook.Load, every SheetReader read, ReadXlsx are XlsxFormatException", () =>
        {
            string path = Path.Combine(dir, "no-sheet.xlsx");
            File.WriteAllBytes(path, noSheet);
            Malformed(() => Workbook.Load(new MemoryStream(noSheet)), "Workbook.Load(stream)", "/xl/worksheets/sheet1.xml");
            Malformed(() => Workbook.Load(path), "Workbook.Load(path)", "/xl/worksheets/sheet1.xml");
            Malformed(() => path.ReadXlsx(), "ReadXlsx(path)", "/xl/worksheets/sheet1.xml");
            using var r = XlsxReader.Open(new MemoryStream(noSheet));   // the sheet is read lazily: Open succeeds, as before
            Malformed(() => r.Sheet("Data").Rows.Count(), "Rows", "/xl/worksheets/sheet1.xml");
            Malformed(() => r.Sheet("Data").ReadLayout(), "ReadLayout", "/xl/worksheets/sheet1.xml");
            Malformed(() => r.Sheet("Data").ReadDrawings(), "ReadDrawings", "/xl/worksheets/sheet1.xml");
            Malformed(() => r.Sheet("Data").ReadComments(), "ReadComments", "/xl/worksheets/sheet1.xml");
            Malformed(() => r.Sheet("Data").ReadHeaderFooterPictures(), "ReadHeaderFooterPictures", "/xl/worksheets/sheet1.xml");
        });

        Check("G2: a path that does not exist is still FileNotFoundException (not a malformed file)", () =>
        {
            string missing = Path.Combine(dir, "no-such-file.xlsx");
            Expect<FileNotFoundException>(() => Workbook.Load(missing), "Workbook.Load(path)");
            Expect<FileNotFoundException>(() => XlsxReader.Open(missing).Dispose(), "XlsxReader.Open(path)");
            Expect<FileNotFoundException>(() => missing.ReadXlsx(), "ReadXlsx(path)");
        });

        Check("G2: a part declared but missing is XlsxFormatException with InvalidDataException inside, as before", () =>
        {
            byte[] declared = RemoveEntryOnly(baseFile, "xl/worksheets/sheet1.xml");
            var ex = Expect<XlsxFormatException>(() => Workbook.Load(new MemoryStream(declared)), "Workbook.Load");
            Assert(ex.InnerException is InvalidDataException, $"inner {ex.InnerException?.GetType().Name}");
        });

        Check("G2: optional parts missing and undeclared are still skipped (comments)", () =>
        {
            byte[] noComments = RemoveUndeclared(baseFile, "xl/comments1.xml");
            var wb = Workbook.Load(new MemoryStream(noComments));
            Assert(wb["Data"].Cell("A1").Value as string == "Fictional name", "A1");
        });

        // ---- F3: shared-string references -----------------------------------------------------------------

        var damaged = new (string Label, string Value, string Message)[]
        {
            ("an index past the end of the table", "<v>99</v>", "refers to shared string 99, but the workbook has"),
            ("a negative index", "<v>-1</v>", "refers to shared string -1, but the workbook has"),
            ("an index that is not a number", "<v>abc</v>", "has a shared-string reference that is not a number."),
        };
        foreach (var d in damaged)
            Check($"F3: {d.Label} is a damaged file at Workbook.Load, XlsxReader rows and ReadXlsx; earlier rows still read", () =>
            {
                byte[] file = EditEntry(baseFile, "xl/worksheets/sheet1.xml", x => SetCellValue(x, "A3", d.Value));
                string path = Path.Combine(dir, "f3-" + d.Value.Replace("<v>", "").Replace("</v>", "") + ".xlsx");
                File.WriteAllBytes(path, file);
                var e1 = Expect<XlsxFormatException>(() => Workbook.Load(new MemoryStream(file)), "Workbook.Load");
                Assert(e1.Message.Contains("cell A3 on sheet 'Data' " + d.Message, StringComparison.Ordinal), e1.Message);
                Assert(!e1.Message.Contains("abc", StringComparison.Ordinal), "the message repeats cell content");
                Expect<XlsxFormatException>(() => Workbook.Load(path), "Workbook.Load(path)");
                Expect<XlsxFormatException>(() => path.ReadXlsx(), "ReadXlsx(path)");
                using var r = XlsxReader.Open(new MemoryStream(file), new XlsxReaderOptions { TreatFirstRowAsHeaders = false });
                var seen = new List<string?>();
                Expect<XlsxFormatException>(() => { foreach (var row in r.Sheet("Data").Rows) seen.Add(row[0] as string); }, "Rows");
                Assert(seen.SequenceEqual(new[] { "Fictional name", "Fictional A" }), $"rows before the damaged one: {string.Join("|", seen)}");
            });

        Check("F3: a missing (undeclared) shared-string part with text cells is a damaged file", () =>
        {
            byte[] file = RemoveUndeclared(baseFile, "xl/sharedStrings.xml");
            var ex = Expect<XlsxFormatException>(() => Workbook.Load(new MemoryStream(file)), "Workbook.Load");
            Assert(ex.Message.Contains("refers to shared string 0, but the workbook has 0.", StringComparison.Ordinal), ex.Message);
        });

        Check("F3: a missing shared-string part without text cells still opens", () =>
        {
            var wb = new Workbook();
            wb.AddSheet("Numbers").Cell("A1").Value = 42.0;
            var ms = new MemoryStream();
            wb.SaveTo(ms);
            byte[] file = ms.ToArray();
            if (Entries(file).Contains("xl/sharedStrings.xml")) file = RemoveUndeclared(file, "xl/sharedStrings.xml");
            Assert(Workbook.Load(new MemoryStream(file))["Numbers"].Cell("A1").Value is 42.0, "A1");
        });

        Check("F3: no value (<c t=\"s\"/>) and an empty value (<v></v>) still read as empty cells (D-239 Q1)", () =>
        {
            byte[] noValue = EditEntry(baseFile, "xl/worksheets/sheet1.xml", x => SetCellElement(x, "A3", "<c r=\"A3\" t=\"s\"/>"));
            Assert(Workbook.Load(new MemoryStream(noValue))["Data"].Cell("A3").Value is null, "no value");
            byte[] empty = EditEntry(baseFile, "xl/worksheets/sheet1.xml", x => SetCellValue(x, "A3", "<v></v>"));
            Assert(Workbook.Load(new MemoryStream(empty))["Data"].Cell("A3").Value is null or "", "empty value");
        });

        Check("F3: valid indexes, also with surrounding spaces, read as before", () =>
        {
            byte[] spaced = EditEntry(baseFile, "xl/worksheets/sheet1.xml", x => SetCellValue(x, "A1", "<v> 0 </v>"));
            Assert(Workbook.Load(new MemoryStream(spaced))["Data"].Cell("A1").Value as string == "Fictional name", "A1");
        });

        Console.WriteLine();
        Console.WriteLine($"  {pass} passed, {fail} failed");
        Console.WriteLine();
        if (fail > 0) throw new Exception(failed.Summary($"PartReferenceTest: {fail} check(s) failed."));
    }

    // ---- the base workbook ---------------------------------------------------------------------------------

    private static byte[] BaseWorkbook()
    {
        var wb = new Workbook();
        var s = wb.AddSheet("Data");
        s.Cell("A1").Value = "Fictional name";
        s.Cell("B1").Value = 12.5;
        s.Cell("A2").Value = "Fictional A";
        s.Cell("A3").Value = "Fictional B";
        s.AddComment("A2", "Fictional author", "Fictional note");
        var ms = new MemoryStream();
        wb.SaveTo(ms);
        byte[] bytes = ms.ToArray();
        foreach (var part in new[] { "xl/sharedStrings.xml", "xl/comments1.xml", "xl/worksheets/_rels/sheet1.xml.rels" })
            if (!Entries(bytes).Contains(part)) throw new Exception($"base workbook has no {part}: {string.Join(", ", Entries(bytes))}");
        return bytes;
    }

    /// <summary>Every read of the base workbook gives its values and its comment.</summary>
    private static void CheckReadsBase(byte[] file)
    {
        var wb = Workbook.Load(new MemoryStream(file));
        Assert(wb["Data"].Cell("A1").Value as string == "Fictional name", "Workbook.Load A1");
        Assert(wb["Data"].Cell("B1").Value is 12.5, "Workbook.Load B1");
        Assert(wb["Data"].Cell("A3").Value as string == "Fictional B", "Workbook.Load A3");
        using var r = XlsxReader.Open(new MemoryStream(file), new XlsxReaderOptions { TreatFirstRowAsHeaders = false });
        var sheet = r.Sheet("Data");
        Assert(sheet.Rows.Count() == 3, "XlsxReader rows");
        Assert(sheet.ReadComments().Count == 1, "comments");
    }

    // ---- zip editing ---------------------------------------------------------------------------------------

    private static HashSet<string> Entries(byte[] zip)
    {
        using var z = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        return z.Entries.Select(e => e.FullName).ToHashSet(StringComparer.Ordinal);
    }

    private static byte[] Rewrite(byte[] zip, Func<string, byte[], byte[]?> map)
    {
        var output = new MemoryStream();
        using (var src = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
        using (var dst = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var e in src.Entries)
            {
                byte[] data;
                using (var s = e.Open()) { var m = new MemoryStream(); s.CopyTo(m); data = m.ToArray(); }
                var mapped = map(e.FullName, data);
                if (mapped is null) continue;
                var ne = dst.CreateEntry(e.FullName);
                using var w = ne.Open();
                w.Write(mapped);
            }
        }
        return output.ToArray();
    }

    private static byte[] EditEntry(byte[] zip, string name, Func<string, string> edit)
    {
        bool found = false;
        var result = Rewrite(zip, (n, d) =>
        {
            if (n != name) return d;
            found = true;
            return Encoding.UTF8.GetBytes(edit(Encoding.UTF8.GetString(d)));
        });
        if (!found) throw new Exception($"no entry {name}");
        return result;
    }

    /// <summary>Removes a part and its Override in [Content_Types].xml: neither in the zip nor declared.</summary>
    private static byte[] RemoveUndeclared(byte[] zip, string name)
        => Rewrite(RemoveEntryOnly(zip, name), (n, d) => n != "[Content_Types].xml" ? d
            : Encoding.UTF8.GetBytes(Regex.Replace(Encoding.UTF8.GetString(d), "<Override[^>]*PartName=\"/" + Regex.Escape(name) + "\"[^>]*/>", "")));

    /// <summary>Removes a part from the zip but leaves it declared.</summary>
    private static byte[] RemoveEntryOnly(byte[] zip, string name)
    {
        if (!Entries(zip).Contains(name)) throw new Exception($"no entry {name}");
        return Rewrite(zip, (n, d) => n == name ? null : d);
    }

    private static string ReplaceTarget(string rels, string from, string to)
    {
        string a = $"Target=\"{from}\"";
        if (!rels.Contains(a, StringComparison.Ordinal)) throw new Exception($"no {a} in {rels}");
        return rels.Replace(a, $"Target=\"{to}\"");
    }

    private static string SetCellValue(string sheetXml, string reference, string valueXml)
    {
        var m = Regex.Match(sheetXml, $"<c r=\"{reference}\"[^>]*t=\"s\"[^>]*><v>[^<]*</v></c>");
        if (!m.Success) throw new Exception($"no shared-string cell {reference}");
        string open = m.Value.Substring(0, m.Value.IndexOf("><v>", StringComparison.Ordinal) + 1);
        return sheetXml.Replace(m.Value, open + valueXml + "</c>");
    }

    private static string SetCellElement(string sheetXml, string reference, string cellXml)
    {
        var m = Regex.Match(sheetXml, $"<c r=\"{reference}\"[^>]*t=\"s\"[^>]*><v>[^<]*</v></c>");
        if (!m.Success) throw new Exception($"no shared-string cell {reference}");
        return sheetXml.Replace(m.Value, cellXml);
    }

    // ---- assertions -------------------------------------------------------------------------------------------

    private static void Malformed(Action a, string what, string partName)
    {
        var ex = Expect<XlsxFormatException>(a, what);
        Assert(ex.InnerException is FileNotFoundException, $"{what}: inner {ex.InnerException?.GetType().Name}");
        Assert(ex.Message.StartsWith("The file is not a valid xlsx workbook: ", StringComparison.Ordinal)
            && ex.Message.Contains($"'{partName}'", StringComparison.Ordinal), $"{what}: {ex.Message}");
    }

    private static void Malformed(Func<object?> f, string what, string partName) => Malformed(() => { f(); }, what, partName);

    private static T Expect<T>(Action a, string what) where T : Exception
    {
        try { a(); }
        catch (T ex) when (ex.GetType() == typeof(T)) { return ex; }
        catch (Exception ex) { throw new Exception($"{what}: expected {typeof(T).Name}, got {ex.GetType().Name}: {ex.Message}"); }
        throw new Exception($"{what}: expected {typeof(T).Name}, nothing thrown");
    }

    private static T Expect<T>(Func<object?> f, string what) where T : Exception => Expect<T>(() => { f(); }, what);

    private static void Assert(bool condition, string what)
    {
        if (!condition) throw new Exception(what);
    }
}
