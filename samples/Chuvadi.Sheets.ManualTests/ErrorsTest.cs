using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Chuvadi.Sheets.Model;

namespace Chuvadi.Sheets.ManualTests;

/// <summary>
/// Sheets 2.0.0: one documented exception for a malformed file. Every malformed-input error is
/// <see cref="XlsxFormatException"/> with the original error as InnerException, from every read
/// entry point; limits, passwords and cancellation keep their own types. All fixtures are built
/// and read in memory (encrypted ones included, so no antivirus scan is involved).
/// </summary>
internal static class ErrorsTest
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Pkg = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static void Run(string outputDir)
    {
        Console.WriteLine("[ErrorsTest] 2.0.0 exceptions for malformed input...");
        Console.WriteLine();
        int pass = 0, fail = 0;

        void Check(string label, Action body)
        {
            try { body(); Console.WriteLine($"    PASS: {label}"); pass++; }
            catch (Exception ex) { Console.WriteLine($"    FAIL: {label}"); Console.WriteLine($"          {ex.GetType().Name}: {ex.Message}"); fail++; }
        }

        static T Throws<T>(Action a, string what) where T : Exception
        {
            try { a(); }
            catch (T ex) { return ex; }
            catch (Exception ex) { throw new Exception($"{what}: expected {typeof(T).Name}, got {ex.GetType().Name}: {ex.Message}"); }
            throw new Exception($"{what}: expected {typeof(T).Name}, nothing thrown");
        }

        static void Format<TInner>(Action a, string what) where TInner : Exception
        {
            var ex = Throws<XlsxFormatException>(a, what);
            if (ex.InnerException is not TInner)
                throw new Exception($"{what}: inner exception {ex.InnerException?.GetType().Name ?? "none"}, expected {typeof(TInner).Name}");
        }

        static MemoryStream Mem(byte[] b) => new(b, writable: false);

        static void ReadAll(byte[] b, string? password = null, XlsxReaderOptions? o = null)
        {
            using var r = XlsxReader.Open(Mem(b), password, o ?? new XlsxReaderOptions { TreatFirstRowAsHeaders = false });
            foreach (var info in r.Sheets)
                foreach (var row in r.Sheet(info.Name).Rows) { }
        }

        var good = Package(SheetXml("<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>"));

        Check("not a zip: Workbook.Load and XlsxReader.Open throw XlsxFormatException (InvalidDataException inside)", () =>
        {
            var junk = Encoding.ASCII.GetBytes("this is not a workbook at all, just text");
            Format<InvalidDataException>(() => Workbook.Load(Mem(junk)), "Workbook.Load");
            Format<InvalidDataException>(() => XlsxReader.Open(Mem(junk)).Dispose(), "XlsxReader.Open");
        });

        Check("truncated zip: XlsxFormatException", () =>
        {
            var cut = good[..(good.Length / 2)];
            Throws<XlsxFormatException>(() => Workbook.Load(Mem(cut)), "half a file");
        });

        Check("malformed sheet XML: Workbook.Load and row enumeration throw XlsxFormatException (XmlException inside)", () =>
        {
            var broken = Package("<worksheet xmlns=\"" + Main + "\"><sheetData><row r=\"1\"><c r=\"A1\"><v>1</v></c>");
            Format<XmlException>(() => Workbook.Load(Mem(broken)), "Workbook.Load");
            Format<XmlException>(() => ReadAll(broken), "rows");
        });

        Check("missing sheet part and missing workbook relationship: XlsxFormatException", () =>
        {
            var noSheet = Zip(new Dictionary<string, string>
            {
                ["[Content_Types].xml"] = ContentTypes(),
                ["_rels/.rels"] = RootRels(),
                ["xl/workbook.xml"] = WorkbookXml(),
                ["xl/_rels/workbook.xml.rels"] = WorkbookRels(),
            });
            Throws<XlsxFormatException>(() => Workbook.Load(Mem(noSheet)), "sheet part missing");
            var noRoot = Zip(new Dictionary<string, string> { ["[Content_Types].xml"] = ContentTypes(), ["xl/workbook.xml"] = WorkbookXml() });
            Throws<XlsxFormatException>(() => Workbook.Load(Mem(noRoot)), "no officeDocument relationship");
        });

        Check("damaged relationship entry (empty Id): XlsxFormatException (ArgumentException inside)", () =>
        {
            var bad = Zip(new Dictionary<string, string>
            {
                ["[Content_Types].xml"] = ContentTypes(),
                ["_rels/.rels"] = RootRels(),
                ["xl/workbook.xml"] = WorkbookXml(),
                ["xl/_rels/workbook.xml.rels"] = WorkbookRels().Replace("Id=\"rId1\"", "Id=\"\""),
                ["xl/worksheets/sheet1.xml"] = SheetXml(""),
            });
            Format<ArgumentException>(() => Workbook.Load(Mem(bad)), "empty relationship id");
        });

        Check("lazy part readers: a malformed comments part throws XlsxFormatException from ReadComments", () =>
        {
            var withComments = Zip(new Dictionary<string, string>
            {
                ["[Content_Types].xml"] = ContentTypes("<Override PartName=\"/xl/comments1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.comments+xml\"/>"),
                ["_rels/.rels"] = RootRels(),
                ["xl/workbook.xml"] = WorkbookXml(),
                ["xl/_rels/workbook.xml.rels"] = WorkbookRels(),
                ["xl/worksheets/sheet1.xml"] = SheetXml("<row r=\"1\"><c r=\"A1\"><v>1</v></c></row>"),
                ["xl/worksheets/_rels/sheet1.xml.rels"] = $"<Relationships xmlns=\"{Pkg}\"><Relationship Id=\"rId1\" Type=\"{R}/comments\" Target=\"../comments1.xml\"/></Relationships>",
                ["xl/comments1.xml"] = $"<comments xmlns=\"{Main}\"><authors><author>Alex",
            });
            using var r = XlsxReader.Open(Mem(withComments));
            Format<XmlException>(() => r.Sheet(1).ReadComments(), "ReadComments");
        });

        Check("unchanged: StrictXml off still skips a malformed theme; MaxPartBytes is still InvalidDataException", () =>
        {
            var badTheme = Zip(new Dictionary<string, string>
            {
                ["[Content_Types].xml"] = ContentTypes("<Override PartName=\"/xl/theme/theme1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.theme+xml\"/>"),
                ["_rels/.rels"] = RootRels(),
                ["xl/workbook.xml"] = WorkbookXml(),
                ["xl/_rels/workbook.xml.rels"] = WorkbookRels($"<Relationship Id=\"rId9\" Type=\"{R}/theme\" Target=\"theme/theme1.xml\"/>"),
                ["xl/worksheets/sheet1.xml"] = SheetXml(""),
                ["xl/theme/theme1.xml"] = "<a:theme xmlns:a=\"x\"><broken>",
            });
            if (Workbook.Load(Mem(badTheme)).StyleTable.Theme.IsPresent) throw new Exception("theme should be skipped");
            var ex = Throws<InvalidDataException>(() => ReadAll(good, null, new XlsxReaderOptions { MaxPartBytes = 50, TreatFirstRowAsHeaders = false }), "MaxPartBytes");
            if (ex is null) throw new Exception("no exception");
        });

        var encrypted = Encrypted("pw");

        Check("encrypted: no password and wrong password are still XlsxPasswordRequiredException", () =>
        {
            Throws<XlsxPasswordRequiredException>(() => Workbook.Load(Mem(encrypted), null, null), "no password");
            Throws<XlsxPasswordRequiredException>(() => Workbook.Load(Mem(encrypted), "wrong", null), "wrong password");
            if (!Equals(1.0, Workbook.Load(Mem(encrypted), "pw", null)["S"].Cell("A1").Value)) throw new Exception("right password");
        });

        Check("encrypted, password given: a damaged container is XlsxFormatException, not a password error (1.x bug)", () =>
        {
            var cut = encrypted[..2048];   // the CFB header survives, the container does not
            Throws<XlsxFormatException>(() => Workbook.Load(Mem(cut), "pw", null), "truncated container");
            Throws<XlsxPasswordRequiredException>(() => Workbook.Load(Mem(cut), null, null), "no password: the container is not read");
        });

        Check("encrypted, password given: a failed integrity check is XlsxFormatException (InvalidDataException inside)", () =>
        {
            // Find the start of the EncryptedPackage stream's ciphertext in the file and flip one byte.
            var streams = Chuvadi.Internal.Crypto.CfbContainer.Read(Mem(encrypted));
            var slice = streams["EncryptedPackage"].AsSpan(16, 32);   // past the 8-byte size; inside one 64-byte mini sector
            int at = encrypted.AsSpan().IndexOf(slice);
            if (at < 0) throw new Exception("ciphertext not found in the container");
            var tampered = (byte[])encrypted.Clone();
            tampered[at + 8] ^= 0xFF;
            var ex = Throws<XlsxFormatException>(() => Workbook.Load(Mem(tampered), "pw", null), "tampered");
            if (ex.InnerException is not InvalidDataException) throw new Exception($"inner {ex.InnerException?.GetType().Name}");
        });

        Console.WriteLine();
        Console.WriteLine($"  ErrorsTest: {pass} passed, {fail} failed.");
        Console.WriteLine();
        if (fail > 0) throw new Exception($"ErrorsTest: {fail} check(s) failed.");
    }

    // ---- Fixtures (in memory) -------------------------------------------------------------

    private static byte[] Encrypted(string password)
    {
        var ms = new MemoryStream();
        using (var w = XlsxWriter.Create(ms, new XlsxWriterOptions { Encryption = new EncryptionOptions { Password = password, SpinCount = 1_000 } }))
        {
            var s = w.AddSheet("S");
            s.WriteRow(1);
            for (int i = 2; i < 200; i++) s.WriteRow("row " + i);   // several encryption segments
            w.Save();
        }
        return ms.ToArray();
    }

    private static string ContentTypes(string extra = "") =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
        "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
        extra + "</Types>";

    private static string RootRels() =>
        $"<Relationships xmlns=\"{Pkg}\"><Relationship Id=\"rId1\" Type=\"{R}/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>";

    private static string WorkbookXml() =>
        $"<workbook xmlns=\"{Main}\" xmlns:r=\"{R}\"><sheets><sheet name=\"S\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>";

    private static string WorkbookRels(string extra = "") =>
        $"<Relationships xmlns=\"{Pkg}\"><Relationship Id=\"rId1\" Type=\"{R}/worksheet\" Target=\"worksheets/sheet1.xml\"/>{extra}</Relationships>";

    private static string SheetXml(string rows) => $"<worksheet xmlns=\"{Main}\"><sheetData>{rows}</sheetData></worksheet>";

    private static byte[] Package(string sheetXml) => Zip(new Dictionary<string, string>
    {
        ["[Content_Types].xml"] = ContentTypes(),
        ["_rels/.rels"] = RootRels(),
        ["xl/workbook.xml"] = WorkbookXml(),
        ["xl/_rels/workbook.xml.rels"] = WorkbookRels(),
        ["xl/worksheets/sheet1.xml"] = sheetXml,
    });

    private static byte[] Zip(Dictionary<string, string> entries)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in entries)
            {
                using var s = zip.CreateEntry(name).Open();
                var b = new UTF8Encoding(false).GetBytes(content);
                s.Write(b, 0, b.Length);
            }
        return ms.ToArray();
    }
}
