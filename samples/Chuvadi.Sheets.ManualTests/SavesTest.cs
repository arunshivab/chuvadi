using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Chuvadi.Internal.Crypto;
using Chuvadi.Sheets.Model;
using static Chuvadi.Sheets.ManualTests.TestGroup;

namespace Chuvadi.Sheets.ManualTests;

/// <summary>
/// 2.1.0 items A, B and C (D-298, D-299, D-306):
/// A: Workbook.SaveTo(Stream, EncryptionOptions);
/// B: XlsxWriterOptions on every save (Workbook.SaveTo, ToXlsx for sequences and DataTables, XlsxExport), with
///    OverloadResolutionPriority keeping a literal null where it bound before;
/// C: XlsxWriterOptions.TempStorage = Memory: no file in the temp folder, TempDirectory ignored, the same file.
/// Encrypted files are opened from memory (D-44). Data is fictional (D-60).
/// </summary>
internal static class SavesTest
{
    private const string Password = "fictional-pass";
    private const int Spin = 1_000;   // a low spin count keeps the encrypted checks quick

    private sealed class Row
    {
        public string Name { get; set; } = "";
        public int Qty { get; set; }
        public DateTime When { get; set; }
    }

    private static readonly Row[] Rows =
    {
        new() { Name = "Fictional A", Qty = 3, When = new DateTime(2024, 10, 2) },
        new() { Name = "Fictional B", Qty = 7, When = new DateTime(2024, 10, 3, 9, 30, 0) },
    };

    public static void Run(string outputDir)
    {
        var g = new TestGroup("SavesTest", "2.1.0 A, B, C: encrypted stream save, writer options on every save, TempStorage...");
        string dir = Path.Combine(outputDir, "saves");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var enc = new EncryptionOptions { Password = Password, SpinCount = Spin };

        // ---- A ----------------------------------------------------------------------------------------------

        g.Check("A: SaveTo(Stream, EncryptionOptions) writes after the stream's bytes, leaves it open, and is the path save's file", () =>
        {
            var wb = Sample();
            var ms = new MemoryStream();
            ms.Write(new byte[] { 1, 2, 3 }, 0, 3);
            wb.SaveTo(ms, enc);
            True(ms.CanWrite, "the stream was closed");
            byte[] all = ms.ToArray();
            True(all[0] == 1 && all[1] == 2 && all[2] == 3, "the stream's first bytes were overwritten");
            byte[] file = all.AsSpan(3).ToArray();
            True(file.AsSpan(0, 4).SequenceEqual(new byte[] { 0xD0, 0xCF, 0x11, 0xE0 }), "not a compound (encrypted) file");
            var back = Workbook.Load(new MemoryStream(file, writable: false), Password, null);
            Eq("Fictional A", back.Sheets[0].Cell("A1").Value, "A1 after decryption");

            var p = Path.Combine(dir, "a-path.xlsx");
            wb.SaveTo(p, enc);
            SaveSafetyTest.SamePackage(
                EncryptedPackageReader.DecryptToPlaintextPackage(new MemoryStream(file, writable: false), Password),
                EncryptedPackageReader.DecryptToPlaintextPackage(new MemoryStream(File.ReadAllBytes(p), writable: false), Password));
        });

        g.Check("A: a non-seekable stream works; null encryption and null stream throw ArgumentNullException", () =>
        {
            var wb = Sample();
            var sink = new MemoryStream();
            wb.SaveTo(new ForwardOnlyStream(sink), enc);
            Eq("Fictional A", Workbook.Load(new MemoryStream(sink.ToArray(), writable: false), Password, null).Sheets[0].Cell("A1").Value, "A1");
            Eq("encryption", Throws<ArgumentNullException>(() => wb.SaveTo(new MemoryStream(), (EncryptionOptions)null!), "null encryption").ParamName, "param");
            Eq("output", Throws<ArgumentNullException>(() => wb.SaveTo((Stream)null!, enc), "null stream").ParamName, "param");
        });

        g.Check("B: a literal null still binds to the EncryptionOptions overloads (OverloadResolutionPriority)", () =>
        {
            var wb = Sample();
            // Both calls would be ambiguous (CS0121) without the attribute; they compile and throw as before 2.1.0.
            Eq("encryption", Throws<ArgumentNullException>(() => wb.SaveTo(Path.Combine(dir, "never.xlsx"), null!), "SaveTo(path, null)").ParamName, "path param");
            Eq("encryption", Throws<ArgumentNullException>(() => wb.SaveTo(new MemoryStream(), null!), "SaveTo(stream, null)").ParamName, "stream param");
            True(!File.Exists(Path.Combine(dir, "never.xlsx")), "a file was written");
        });

        // ---- B ----------------------------------------------------------------------------------------------

        g.Check("B: Workbook.SaveTo(path or stream, XlsxWriterOptions) applies the options; null options = SaveTo(path)", () =>
        {
            var wb = Sample();
            var p1 = Path.Combine(dir, "b-inline.xlsx");
            wb.SaveTo(p1, new XlsxWriterOptions { UseInlineStrings = true, FullCalculationOnLoad = true });
            string sheet = Part(File.ReadAllBytes(p1), "xl/worksheets/sheet1.xml");
            True(sheet.Contains("t=\"inlineStr\""), "inline strings not used");
            True(Part(File.ReadAllBytes(p1), "xl/workbook.xml").Contains("fullCalcOnLoad=\"1\""), "fullCalcOnLoad missing");
            True(!Entries(File.ReadAllBytes(p1)).Contains("xl/sharedStrings.xml"), "a shared strings part was written");

            var ms = new MemoryStream();
            wb.SaveTo(ms, new XlsxWriterOptions { Encryption = enc });
            Eq("Fictional B", Workbook.Load(new MemoryStream(ms.ToArray(), writable: false), Password, null).Sheets[0].Cell("A2").Value, "encrypted through options");

            var pDefault = Path.Combine(dir, "b-default.xlsx");
            var pNull = Path.Combine(dir, "b-null.xlsx");
            wb.SaveTo(pDefault);
            wb.SaveTo(pNull, (XlsxWriterOptions?)null);
            SaveSafetyTest.SamePackage(File.ReadAllBytes(pNull), File.ReadAllBytes(pDefault));
            Throws<ArgumentException>(() => wb.SaveTo(new MemoryStream(), new XlsxWriterOptions { Encryption = new EncryptionOptions { Password = "" } }), "empty password");

            // The Default…Format options format the model's unstyled dates, as on XlsxWriter (C1 is unstyled).
            var fmt = new MemoryStream();
            wb.SaveTo(fmt, new XlsxWriterOptions { DefaultDateFormat = "dd/mm/yyyy" });
            True(Part(fmt.ToArray(), "xl/styles.xml").Contains("formatCode=\"dd/mm/yyyy\""), "DefaultDateFormat not applied");
            Eq(new DateTime(2024, 10, 2), Workbook.Load(new MemoryStream(fmt.ToArray())).Sheets[0].Cell("C1").Value, "the date itself");
        });

        g.Check("B: ToXlsx<T> with options (path and stream); ToXlsx(path, null) still binds to the configure overload", () =>
        {
            var p = Path.Combine(dir, "b-list.xlsx");
            Rows.ToXlsx(p, new XlsxWriterOptions { Encryption = enc }, cfg => cfg.SheetName("Fictional"));
            var back = Workbook.Load(new MemoryStream(File.ReadAllBytes(p), writable: false), Password, null);
            Eq("Fictional", back.Sheets[0].Name, "sheet name from configure");
            Eq("Fictional B", back.Sheets[0].Cell("A3").Value, "A3");

            var ms = new MemoryStream();
            Rows.ToXlsx(ms, new XlsxWriterOptions { UseInlineStrings = true });
            True(Part(ms.ToArray(), "xl/worksheets/sheet1.xml").Contains("inlineStr"), "stream: inline strings");

            var pNull = Path.Combine(dir, "b-list-null.xlsx");
            Rows.ToXlsx(pNull, null);            // the configure overload, as before 2.1.0
            var ms2 = new MemoryStream();
            Rows.ToXlsx(ms2, null);
            Eq("Fictional A", Workbook.Load(pNull).Sheets[0].Cell("A2").Value, "path, null");
            Eq("Fictional A", Workbook.Load(new MemoryStream(ms2.ToArray())).Sheets[0].Cell("A2").Value, "stream, null");
        });

        g.Check("B: DataTable to a stream (new), and DataTable and XlsxExport with options", () =>
        {
            var table = new DataTable("Fictional");
            table.Columns.Add("Town", typeof(string));
            table.Columns.Add("Count", typeof(int));
            table.Rows.Add("Fictional Town", 4);

            var s1 = new MemoryStream();
            table.ToXlsx(s1);
            Eq("Fictional", Workbook.Load(new MemoryStream(s1.ToArray())).Sheets[0].Name, "ToXlsx(Stream): sheet named after the table");
            var s2 = new MemoryStream();
            table.ToXlsx(s2, cfg => cfg.SheetName("Renamed"));
            Eq("Renamed", Workbook.Load(new MemoryStream(s2.ToArray())).Sheets[0].Name, "ToXlsx(Stream, configure)");
            var s3 = new MemoryStream();
            table.ToXlsx(s3, null);              // the configure overload
            Eq("Fictional Town", Workbook.Load(new MemoryStream(s3.ToArray())).Sheets[0].Cell("A2").Value, "ToXlsx(Stream, null)");

            var p = Path.Combine(dir, "b-table.xlsx");
            table.ToXlsx(p, new XlsxWriterOptions { Encryption = enc });
            Eq(4.0, Workbook.Load(new MemoryStream(File.ReadAllBytes(p), writable: false), Password, null).Sheets[0].Cell("B2").Value, "path with options");
            var s4 = new MemoryStream();
            table.ToXlsx(s4, new XlsxWriterOptions { Encryption = enc }, cfg => cfg.SheetName("Locked"));
            Eq("Locked", Workbook.Load(new MemoryStream(s4.ToArray(), writable: false), Password, null).Sheets[0].Name, "stream with options and configure");

            var pe = Path.Combine(dir, "b-export.xlsx");
            new XlsxExport(pe, new XlsxWriterOptions { Encryption = enc }).AddSheet("Items", Rows).AddSheet("Towns", table).Save();
            var ex = Workbook.Load(new MemoryStream(File.ReadAllBytes(pe), writable: false), Password, null);
            Eq("Items,Towns", string.Join(",", ex.Sheets.Select(s => s.Name)), "XlsxExport(path, options) sheets");
            var se = new MemoryStream();
            new XlsxExport(se, new XlsxWriterOptions { UseInlineStrings = true }).AddSheet("Items", Rows).Save();
            True(Part(se.ToArray(), "xl/worksheets/sheet1.xml").Contains("inlineStr"), "XlsxExport(stream, options)");
        });

        // ---- C ----------------------------------------------------------------------------------------------

        g.Check("C: TempStorage defaults to File; an undefined value throws ArgumentOutOfRangeException", () =>
        {
            var o = new XlsxWriterOptions();
            Eq(TempStorage.File, o.TempStorage, "default");
            Eq(0, (int)TempStorage.File, "File = 0");
            Eq(1, (int)TempStorage.Memory, "Memory = 1");
            Throws<ArgumentOutOfRangeException>(() => o.TempStorage = (TempStorage)5, "(TempStorage)5");
            Eq(TempStorage.File, o.TempStorage, "unchanged after the error");
        });

        g.Check("C: with Memory, TempDirectory is ignored: a missing folder fails with File and works with Memory (every save)", () =>
        {
            string missing = Path.Combine(dir, "no-such-folder");
            var file = new XlsxWriterOptions { TempDirectory = missing };
            Throws<DirectoryNotFoundException>(() => Sample().SaveTo(new MemoryStream(), file), "File mode uses TempDirectory");

            var mem = new XlsxWriterOptions { TempDirectory = missing, TempStorage = TempStorage.Memory };
            var memEnc = new XlsxWriterOptions { TempDirectory = missing, TempStorage = TempStorage.Memory, Encryption = enc };
            var wb = Sample();
            wb.SaveTo(new MemoryStream(), mem);
            wb.SaveTo(new MemoryStream(), memEnc);
            wb.SaveTo(Path.Combine(dir, "c-mem.xlsx"), mem);
            wb.SaveTo(Path.Combine(dir, "c-mem-enc.xlsx"), memEnc);
            Rows.ToXlsx(new MemoryStream(), memEnc);
            using (var w = XlsxWriter.Create(new MemoryStream(), memEnc))
            {
                using (var s = w.AddSheet("Fictional")) s.WriteRow("Fictional A", 1);
                w.Save();
            }
            True(!Directory.Exists(missing), "the missing folder was created");
        });

        g.Check("C: no file in the temp folder during a Memory save; a path save leaves only the target", () =>
        {
            string spoolDir = Path.Combine(dir, "spool-folder");
            Directory.CreateDirectory(spoolDir);
            string outDir = Path.Combine(dir, "target-folder");
            Directory.CreateDirectory(outDir);
            var seen = new List<string>();
            using (var watcher = new FileSystemWatcher(spoolDir) { EnableRaisingEvents = true, IncludeSubdirectories = true })
            {
                watcher.Created += (_, e) => { lock (seen) seen.Add(e.Name ?? "?"); };
                var mem = new XlsxWriterOptions { TempDirectory = spoolDir, TempStorage = TempStorage.Memory, Encryption = enc };
                using (var w = XlsxWriter.Create(Path.Combine(outDir, "c.xlsx"), mem))
                {
                    using (var s = w.AddSheet("Fictional"))
                        for (int i = 0; i < 2000; i++) s.WriteRow("Fictional " + i, i);
                    True(Directory.GetFiles(spoolDir).Length == 0, "a spool file exists while the writer is open");
                    w.Save();
                }
                System.Threading.Thread.Sleep(200);
            }
            Eq(0, seen.Count + Directory.GetFiles(spoolDir).Length, "files created in the temp folder: " + string.Join(", ", seen));
            Eq("c.xlsx", string.Join(",", Directory.GetFiles(outDir).Select(Path.GetFileName)), "files in the target folder");

            // The same writer with File creates its spools there (so the check above can see them).
            var fileOpt = new XlsxWriterOptions { TempDirectory = spoolDir };
            using (var w = XlsxWriter.Create(new MemoryStream(), fileOpt))
            {
                using (var s = w.AddSheet("Fictional")) s.WriteRow("Fictional A", 1);
                True(Directory.GetFiles(spoolDir).Length == 1, "File mode: one spool in the folder while writing");
                w.Save();
            }
            Eq(0, Directory.GetFiles(spoolDir).Length, "File mode: spool deleted after Save");
        });

        g.Check("C: File and Memory give the same file (plain, and encrypted after decryption; times masked)", () =>
        {
            var wb = Sample();
            var a = new MemoryStream(); var b = new MemoryStream();
            wb.SaveTo(a, new XlsxWriterOptions { TempStorage = TempStorage.File });
            wb.SaveTo(b, new XlsxWriterOptions { TempStorage = TempStorage.Memory });
            SaveSafetyTest.SamePackage(b.ToArray(), a.ToArray());

            var c = new MemoryStream(); var d = new MemoryStream();
            wb.SaveTo(c, new XlsxWriterOptions { Encryption = enc });
            wb.SaveTo(d, new XlsxWriterOptions { Encryption = enc, TempStorage = TempStorage.Memory });
            SaveSafetyTest.SamePackage(
                EncryptedPackageReader.DecryptToPlaintextPackage(new MemoryStream(d.ToArray()), Password),
                EncryptedPackageReader.DecryptToPlaintextPackage(new MemoryStream(c.ToArray()), Password));
            SaveSafetyTest.SamePackage(
                EncryptedPackageReader.DecryptToPlaintextPackage(new MemoryStream(d.ToArray()), Password), a.ToArray());
        });

        g.Check("C: memory cost, File against Memory (info: about 0.2, 2.4 and 12 MB workbooks, encrypted)", () =>
        {
            foreach (int rows in new[] { 4_000, 50_000, 250_000 })
            {
                foreach (var storage in new[] { TempStorage.File, TempStorage.Memory })
                {
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    var sw = Stopwatch.StartNew();
                    var output = new MemoryStream();
                    using (var w = XlsxWriter.Create(output, new XlsxWriterOptions { TempStorage = storage, Encryption = enc }))
                    {
                        using (var s = w.AddSheet("Fictional"))
                            for (int i = 0; i < rows; i++) s.WriteRow("Fictional row " + i, i, i * 0.5, "Fictional town " + (i % 97));
                        w.Save();
                    }
                    sw.Stop();
                    double allocatedMb = (GC.GetAllocatedBytesForCurrentThread() - before) / 1048576.0;
                    Console.WriteLine($"          {rows,7:N0} rows, {output.Length / 1048576.0,5:F1} MB file, {storage,-6}: {sw.ElapsedMilliseconds,5} ms, {allocatedMb,6:F1} MB allocated");
                }
            }
        });

        g.Finish();
    }

    private static Workbook Sample()
    {
        var wb = new Workbook();
        var s = wb.AddSheet("Fictional");
        s.Cell("A1").Value = "Fictional A";
        s.Cell("A2").Value = "Fictional B";
        s.Cell("B1").Value = 3.0;
        s.Cell("C1").Value = new DateTime(2024, 10, 2);
        return wb;
    }

    private static string Part(byte[] zip, string name)
    {
        using var z = new ZipArchive(new MemoryStream(zip, writable: false), ZipArchiveMode.Read);
        using var r = new StreamReader(z.GetEntry(name)!.Open(), Encoding.UTF8);
        return r.ReadToEnd();
    }

    private static HashSet<string> Entries(byte[] zip)
    {
        using var z = new ZipArchive(new MemoryStream(zip, writable: false), ZipArchiveMode.Read);
        return z.Entries.Select(e => e.FullName).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>A write-only stream that cannot seek or report its length, like a network response.</summary>
    private sealed class ForwardOnlyStream : Stream
    {
        private readonly Stream _inner;
        public ForwardOnlyStream(Stream inner) { _inner = inner; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
    }
}
