// ZipFormatException is checked here by reflection only; the obsolete Chuvadi.Sheets.Zip API is not used.
#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Chuvadi.Internal.Crypto;
using Chuvadi.Sheets.Internal;
using Chuvadi.Sheets.ManualTests.Reference;
using Chuvadi.Sheets.Model;

namespace Chuvadi.Sheets.ManualTests;

/// <summary>
/// Sheets 2.0.1 (D-183, D-185, D-187): saving safely.
/// <list type="bullet">
/// <item>S1: ZipFormatException is [Obsolete] like the rest of Chuvadi.Sheets.Zip.</item>
/// <item>S2: every stream save leaves the caller's stream open, at the end of the written bytes.</item>
/// <item>S5a/S5e: the shared CFB writer is byte-identical to the old one up to 109 FAT sectors (about 7 MB),
/// writes DIFAT sectors above that, never seeks, and works after a prefix and on a non-seekable stream.</item>
/// <item>Round trips of encrypted files of about 6, 8, 12 and 50 MB: our reader, and msoffcrypto-tool when it
/// is installed (test-only, D-164; reported SKIP otherwise).</item>
/// <item>S5b-d, D2, D4, D5: path saves replace the target only on success; temporary files are owner-only
/// (Unix), exclusive and deleted; an encrypted export spools only encrypted data.</item>
/// </list>
/// Encrypted files are only ever opened from memory (D-44). All data is fictional (D-60).
/// </summary>
internal static class SaveSafetyTest
{
    private const string Password = "fictional-pass-2.0.1";
    private const int Spin = 1_000;
    private const int SectorSize = 512;
    private const uint EndOfChain = 0xFFFFFFFE, Free = 0xFFFFFFFF, FatSect = 0xFFFFFFFD, DifSect = 0xFFFFFFFC;

    public static void Run(string outputDir)
    {
        Console.WriteLine("[SaveSafetyTest] 2.0.1 stream saves, safe replace, temporary files, CFB/DIFAT...");
        Console.WriteLine();
        int pass = 0, fail = 0, skip = 0;
        string dir = Path.Combine(outputDir, "savesafety");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);

        void Check(string label, Action body)
        {
            var sw = Stopwatch.StartNew();
            try { body(); Console.WriteLine($"    PASS: {label} ({sw.ElapsedMilliseconds} ms)"); pass++; }
            catch (SkipException ex) { Console.WriteLine($"    SKIP: {label}: {ex.Message}"); skip++; }
            catch (Exception ex) { Console.WriteLine($"    FAIL: {label}"); Console.WriteLine($"          {ex.GetType().Name}: {ex.Message}"); fail++; }
        }

        // ---- S1 -----------------------------------------------------------------------------------------

        Check("S1: ZipFormatException is [Obsolete] with the namespace's message", () =>
        {
            var asm = typeof(Workbook).Assembly;
            var zfe = asm.GetType("Chuvadi.Sheets.Zip.ZipFormatException", throwOnError: true)!;
            var other = asm.GetType("Chuvadi.Sheets.Zip.ZipEntryInfo", throwOnError: true)!;
            var a = zfe.GetCustomAttribute<ObsoleteAttribute>() ?? throw new Exception("no [Obsolete]");
            var b = other.GetCustomAttribute<ObsoleteAttribute>()!;
            Assert(a.Message == b.Message, $"message differs: '{a.Message}'");
            Assert(!a.IsError, "must be a warning, not an error");
        });

        // ---- S2: stream saves leave the stream open -----------------------------------------------------

        foreach (var (name, save) in StreamSaves())
            Check($"S2: {name} leaves the stream open, at the end, readable", () =>
            {
                var ms = new MemoryStream();
                save(ms);
                Assert(ms.CanWrite && ms.CanRead, "stream was closed");
                Assert(ms.Position == ms.Length && ms.Length > 0, $"position {ms.Position} of {ms.Length}");
                ms.Position = 0;
                var wb = Workbook.Load(ms);
                Assert(wb.Sheets.Count >= 1, "no sheets read back");
            });

        Check("S2: an unencrypted save to a non-seekable stream still works and leaves it open", () =>
        {
            var sink = new MemoryStream();
            var ns = new NonSeekableWriteStream(sink);
            SmallWorkbook().SaveTo(ns);
            Assert(!ns.Disposed, "stream was closed");
            sink.Position = 0;
            Assert(Workbook.Load(sink).Sheets[0].Cell("A1").Value as string == "Fictional", "value");
        });

        // ---- S5a: encrypted saves to a caller's stream ----------------------------------------------------

        Check("S5a: an encrypted save after existing bytes keeps them and writes from the current position", () =>
        {
            var ms = new MemoryStream();
            var prefix = Encoding.ASCII.GetBytes("PREFIX-8");
            ms.Write(prefix);
            WriteEncryptedSmall(ms);
            Assert(ms.CanWrite, "stream was closed");
            var all = ms.ToArray();
            Assert(all.AsSpan(0, 8).SequenceEqual(prefix), "prefix overwritten");
            Assert(all.Length > 8 && (all.Length - 8) % SectorSize == 0, "container length");
            var wb = Workbook.Load(new MemoryStream(all, 8, all.Length - 8), Password, null);
            Assert(wb.Sheets[0].Cell("A1").Value as string == "Fictional", "value");
        });

        Check("S5a: an encrypted save to a non-seekable, write-only stream works and leaves it open", () =>
        {
            var sink = new MemoryStream();
            var ns = new NonSeekableWriteStream(sink);
            WriteEncryptedSmall(ns);
            Assert(!ns.Disposed, "stream was closed");
            var wb = Workbook.Load(new MemoryStream(sink.ToArray()), Password, null);
            Assert(wb.Sheets[0].Cell("A1").Value as string == "Fictional", "value");
        });

        // ---- S5a/S5e: the CFB writer against the old one ------------------------------------------------

        int boundary = LargestSizeWithoutDifat();
        Console.WriteLine($"    info: the largest EncryptedPackage that needs no DIFAT sector is {boundary:N0} bytes");

        Check("S5e: up to the 109-FAT-sector boundary the CFB writer is byte-identical to the old one", () =>
        {
            var sizes = new List<int> { 1, 100, 511, 512, 513, 4095, 4096, 4097, 65_536, 100_003, 1_000_000, 3_333_333,
                5_000_000, 6_000_000, 7_000_000, boundary - SectorSize, boundary - 1, boundary };
            foreach (int size in sizes)
            {
                byte[] data = Bytes(size, seed: size);
                byte[] old = LegacyWrite(data);
                byte[] now = NewWrite(data, prefix: null, nonSeekable: false);
                Assert(old.AsSpan().SequenceEqual(now), $"size {size:N0}: bytes differ");
                Assert(ReadUInt32(now, 72) == 0, $"size {size:N0}: unexpected DIFAT sector");

                byte[] prefix = Encoding.ASCII.GetBytes("ABC");
                byte[] after = NewWrite(data, prefix, nonSeekable: false);
                Assert(after.AsSpan(0, 3).SequenceEqual(prefix) && after.AsSpan(3).SequenceEqual(old), $"size {size:N0}: after a prefix");
                byte[] ns = NewWrite(data, prefix: null, nonSeekable: true);
                Assert(ns.AsSpan().SequenceEqual(old), $"size {size:N0}: non-seekable");
            }
            // Mini-stream-only and multi-stream trees too.
            foreach (var tree in new[] { 0, 1 })
            {
                byte[] old = LegacyWriteTree(tree), now = NewWriteTree(tree);
                Assert(old.AsSpan().SequenceEqual(now), $"tree {tree}: bytes differ");
            }
        });

        Check("S5e: the first size above the boundary gets one DIFAT sector and reads back", () =>
        {
            CheckDifat(Bytes(boundary + 1, seed: 7), expectDifat: true);
        });

        foreach (int mb in new[] { 8, 12, 50 })
            Check($"S5e: a {mb} MB container has a valid DIFAT chain and reads back unchanged", () =>
                CheckDifat(Bytes(mb * 1024 * 1024, seed: mb), expectDifat: true));

        // ---- Round trips (D-185) --------------------------------------------------------------------------

        bool? haveMsoffcrypto = null;
        foreach (int mb in new[] { 6, 8, 12, 50 })
            Check($"D-185: a {mb} MB encrypted xlsx round-trips (our reader; msoffcrypto-tool if installed)", () =>
            {
                int rows = RowsFor(mb);
                var plain = new MemoryStream();
                WriteLarge(plain, rows, encryption: null);
                var enc = new MemoryStream();
                WriteLarge(enc, rows, new EncryptionOptions { Password = Password, SpinCount = Spin });
                byte[] encBytes = enc.ToArray();
                double sizeMb = encBytes.Length / 1048576.0;
                Assert(Math.Abs(sizeMb - mb) < mb * 0.15 + 0.3, $"size {sizeMb:F2} MB is not about {mb} MB");
                bool difat = ReadUInt32(encBytes, 72) > 0;
                Assert(difat == (encBytes.Length > 7_143_424), $"DIFAT {difat} for {encBytes.Length:N0} bytes");
                Console.WriteLine($"          {encBytes.Length:N0} bytes, {ReadUInt32(encBytes, 44)} FAT sectors, {ReadUInt32(encBytes, 72)} DIFAT sectors, {rows:N0} rows");

                byte[] decrypted = EncryptedPackageReader.DecryptToPlaintextPackage(new MemoryStream(encBytes, writable: false), Password);
                SamePackage(decrypted, plain.ToArray());

                using (var r = XlsxReader.Open(new MemoryStream(encBytes, writable: false), Password, new XlsxReaderOptions { TreatFirstRowAsHeaders = false }))
                {
                    var gen = new RowGenerator();
                    int n = 0;
                    foreach (var row in r.Sheet("Data").Rows)
                    {
                        var expected = gen.Next(n);
                        Assert(Convert.ToInt64(row[0]) == n + 1, $"row {n + 1}: number");
                        for (int c = 0; c < expected.Length; c++)
                            if (row[c + 1] as string != expected[c]) throw new Exception($"row {n + 1}, column {c + 2}: value differs");
                        n++;
                    }
                    Assert(n == rows, $"{n} rows read, {rows} written");
                }
                if (mb <= 12)
                {
                    var wb = Workbook.Load(new MemoryStream(encBytes, writable: false), Password, null);
                    Assert(wb["Data"].Cell(rows, 1).Value is not null, "Workbook.Load: last row");
                }

                haveMsoffcrypto ??= Msoffcrypto.Available();
                if (haveMsoffcrypto == true)
                {
                    byte[] external = Msoffcrypto.Decrypt(encBytes, Password);
                    Assert(external.AsSpan().SequenceEqual(decrypted), "msoffcrypto-tool decrypts to different bytes");
                    Console.WriteLine("          msoffcrypto-tool: decrypted, identical package");
                }
                else Console.WriteLine("          msoffcrypto-tool: not installed, outside check skipped (pip install msoffcrypto-tool)");
            });

        // ---- D4: path saves replace the target only on success ---------------------------------------------

        Check("D4: Workbook.SaveTo(path) replaces an existing file and leaves no temporary file", () =>
        {
            string p = Old(dir, "replace.xlsx");
            SmallWorkbook().SaveTo(p);
            Assert(Workbook.Load(p).Sheets[0].Cell("A1").Value as string == "Fictional", "value");
            NoTempBeside(p);
        });

        Check("D4: XlsxWriter.Create(path) keeps the old file until Save; Dispose without Save keeps it", () =>
        {
            string p = Old(dir, "dispose.xlsx");
            using (var w = XlsxWriter.Create(p))
            {
                using (var s = w.AddSheet("Data")) s.WriteRow("Fictional");
                Assert(File.ReadAllText(p) == "OLD", "target changed before Save");
                Assert(TempsBeside(p).Length == 1, "no temporary file beside the target");
            }
            Assert(File.ReadAllText(p) == "OLD", "target changed by Dispose");
            NoTempBeside(p);
        });

        Check("D4: a cancelled SaveAsync keeps the old file and removes the temporary file", () =>
        {
            string p = Old(dir, "cancel.xlsx");
            var w = XlsxWriter.Create(p);
            using (var s = w.AddSheet("Data")) for (int i = 0; i < 1000; i++) s.WriteRow(i, "Fictional");
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Throws<OperationCanceledException>(() => w.SaveAsync(cts.Token).GetAwaiter().GetResult(), "SaveAsync");
            w.Dispose();
            Assert(File.ReadAllText(p) == "OLD", "target changed");
            NoTempBeside(p);
        });

        Check("D4: a failing encrypted Workbook.SaveTo(path, ...) keeps the old file (null password, as before: ArgumentNullException)", () =>
        {
            string p = Old(dir, "encfail.xlsx");
            Throws<ArgumentNullException>(() => SmallWorkbook().SaveTo(p, new EncryptionOptions { Password = null!, SpinCount = Spin }), "SaveTo");
            Assert(File.ReadAllText(p) == "OLD", "target changed");
            NoTempBeside(p);
        });

        Check("D4: an encrypted Workbook.SaveTo(path, ...) replaces the file; it reads back from memory", () =>
        {
            string p = Old(dir, "enc.xlsx");
            SmallWorkbook().SaveTo(p, new EncryptionOptions { Password = Password, SpinCount = Spin });
            NoTempBeside(p);
            var wb = Workbook.Load(new MemoryStream(File.ReadAllBytes(p)), Password, null);
            Assert(wb.Sheets[0].Cell("A1").Value as string == "Fictional", "value");
        });

        Check("D4: a missing folder still fails at Create with DirectoryNotFoundException naming the target", () =>
        {
            string p = Path.Combine(dir, "no-such-folder", "x.xlsx");
            var ex = Throws<DirectoryNotFoundException>(() => XlsxWriter.Create(p).Dispose(), "Create");
            Assert(ex.Message.Contains(p, StringComparison.Ordinal) && !ex.Message.Contains(".tmp", StringComparison.Ordinal), ex.Message);
        });

        Check("D4: a target held open by someone else still fails at Create (IOException) and is unchanged", () =>
        {
            string p = Old(dir, "locked.xlsx");
            using (new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Throws<IOException>(() => XlsxWriter.Create(p).Dispose(), "Create");
                Throws<IOException>(() => SmallWorkbook().SaveTo(p), "SaveTo");
            }
            Assert(File.ReadAllText(p) == "OLD", "target changed");
            NoTempBeside(p);
        });

        Check("D4: a folder as the target still fails at Create (UnauthorizedAccessException)", () =>
        {
            string p = Fresh(dir, "a-folder.xlsx");
            Throws<UnauthorizedAccessException>(() => XlsxWriter.Create(p).Dispose(), "Create");
            NoTempBeside(p);
        });

        Check("D4: replacing a file keeps its permission bits (Unix) and a symbolic link stays a link", () =>
        {
            if (OperatingSystem.IsWindows()) throw new SkipException("Unix permission bits and links; File.Replace keeps attributes and security on Windows");
            string p = Old(dir, "mode.xlsx");
            var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;
            File.SetUnixFileMode(p, mode);
            SmallWorkbook().SaveTo(p);
            Assert(File.GetUnixFileMode(p) == mode, $"mode {File.GetUnixFileMode(p)}");

            string real = Old(dir, "real.xlsx"), link = Path.Combine(dir, "link.xlsx");
            if (File.Exists(link)) File.Delete(link);
            File.CreateSymbolicLink(link, real);
            SmallWorkbook().SaveTo(link);
            Assert(new FileInfo(link).LinkTarget is not null, "the link was replaced by a file");
            Assert(Workbook.Load(real).Sheets[0].Cell("A1").Value as string == "Fictional", "the linked file was not written");
            NoTempBeside(real);
        });

        // ---- D5, D2: temporary files ------------------------------------------------------------------------

        Check("D5: sheet spools are owner-only, exclusive and deleted after Save", () =>
        {
            string tmp = Fresh(dir, "tmp-plain");
            var target = new MemoryStream();
            using (var w = XlsxWriter.Create(target, new XlsxWriterOptions { TempDirectory = tmp }))
            {
                using (var s = w.AddSheet("Data")) s.WriteRow("Fictional");
                var files = Directory.GetFiles(tmp);
                Assert(files.Length == 1, $"{files.Length} spool files");
                CheckPrivate(files[0]);
                w.Save();
            }
            Assert(Directory.GetFiles(tmp).Length == 0, "spool left behind");
        });

        Check("D2, S5d: an encrypted export spools only encrypted data, owner-only, and deletes it", () =>
        {
            string tmp = Fresh(dir, "tmp-enc");
            const string marker = "FICTIONAL-MARKER-7Q2";
            var target = new MemoryStream();
            using (var w = XlsxWriter.Create(target, new XlsxWriterOptions
                   { TempDirectory = tmp, UseInlineStrings = true, Encryption = new EncryptionOptions { Password = Password, SpinCount = Spin } }))
            {
                var pkgSpool = typeof(XlsxWriter).GetField("_outputStream", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w);
                Assert(pkgSpool is EncryptedSpool, "the package spool is not encrypted");
                using (var s = w.AddSheet("Data"))
                {
                    for (int i = 0; i < 2000; i++) s.WriteRow(i, marker);
                    Assert(s.Spool is EncryptedSpool, "the sheet spool is not encrypted");
                    s.Spool.Flush();
                }
                var files = Directory.GetFiles(tmp);
                Assert(files.Length == 2, $"{files.Length} spool files (expected package + sheet)");
                foreach (var f in files)
                {
                    CheckPrivate(f);
                    if (TryReadDespiteLock(f) is { } bytes)
                    {
                        Assert(bytes.Length > 0 || f.Contains("_pkg_"), $"{Path.GetFileName(f)} empty");
                        Assert(!Contains(bytes, Encoding.UTF8.GetBytes(marker)), $"plaintext in {Path.GetFileName(f)}");
                        Assert(!Contains(bytes, Encoding.UTF8.GetBytes("<row")), $"sheet XML in {Path.GetFileName(f)}");
                    }
                }
                w.Save();
            }
            Assert(Directory.GetFiles(tmp).Length == 0, "spool left behind");
            Assert(Workbook.Load(new MemoryStream(target.ToArray()), Password, null)["Data"].Cell(2000, 2).Value as string == marker, "value");
        });

        Check("D5: Dispose without Save deletes every spool", () =>
        {
            string tmp = Fresh(dir, "tmp-dispose");
            var w = XlsxWriter.Create(new MemoryStream(), new XlsxWriterOptions { TempDirectory = tmp, Encryption = new EncryptionOptions { Password = Password, SpinCount = Spin } });
            using (var s = w.AddSheet("A")) s.WriteRow("Fictional");
            using (var s = w.AddSheet("B")) s.WriteRow("Fictional");
            Assert(Directory.GetFiles(tmp).Length == 3, "expected package + 2 sheet spools");
            w.Dispose();
            Assert(Directory.GetFiles(tmp).Length == 0, "spool left behind");
        });

        Console.WriteLine();
        Console.WriteLine($"  {pass} passed, {fail} failed, {skip} skipped");
        Console.WriteLine();
        if (fail > 0) throw new Exception($"SaveSafetyTest: {fail} check(s) failed.");
    }

    // ---- stream save entry points ------------------------------------------------------------------------

    private static IEnumerable<(string Name, Action<Stream> Save)> StreamSaves()
    {
        yield return ("Workbook.SaveTo(Stream)", s => SmallWorkbook().SaveTo(s));
        yield return ("XlsxWriter.Create(Stream) + Save", s =>
        {
            using var w = XlsxWriter.Create(s);
            using (var sh = w.AddSheet("Data")) sh.WriteRow("Fictional");
            w.Save();
        });
        yield return ("XlsxWriter.Create(Stream) + SaveAsync", s =>
        {
            using var w = XlsxWriter.Create(s);
            using (var sh = w.AddSheet("Data")) sh.WriteRow("Fictional");
            w.SaveAsync().GetAwaiter().GetResult();
        });
        yield return ("IEnumerable<T>.ToXlsx(Stream)", s => new[] { new Person("Fictional", 1) }.ToXlsx(s));
        yield return ("new XlsxExport(Stream).Save", s =>
        {
            var t = new DataTable("People");
            t.Columns.Add("Name", typeof(string));
            t.Rows.Add("Fictional");
            new XlsxExport(s).AddSheet("People", new[] { new Person("Fictional", 1) }).AddSheet("Table", t).Save();
        });
    }

    private sealed record Person(string Name, int Number);

    private static Workbook SmallWorkbook()
    {
        var wb = new Workbook();
        wb.AddSheet("Data").Cell("A1").Value = "Fictional";
        return wb;
    }

    private static void WriteEncryptedSmall(Stream output)
    {
        using var w = XlsxWriter.Create(output, new XlsxWriterOptions { Encryption = new EncryptionOptions { Password = Password, SpinCount = Spin } });
        using (var s = w.AddSheet("Data")) s.WriteRow("Fictional");
        w.Save();
    }

    // ---- large files -------------------------------------------------------------------------------------

    /// <summary>Rows that give an encrypted file of about <paramref name="mb"/> MB (measured: about 64 bytes per row).</summary>
    private static int RowsFor(int mb) => (int)(mb * 1048576L / 64);

    private static void WriteLarge(Stream output, int rows, EncryptionOptions? encryption)
    {
        using var w = XlsxWriter.Create(output, new XlsxWriterOptions { UseInlineStrings = true, Encryption = encryption });
        using (var s = w.AddSheet("Data"))
        {
            var gen = new RowGenerator();
            var values = new object?[5];
            for (int i = 0; i < rows; i++)
            {
                var row = gen.Next(i);
                values[0] = i + 1;
                for (int c = 0; c < row.Length; c++) values[c + 1] = row[c];
                s.WriteRow(values);
            }
        }
        w.Save();
    }

    /// <summary>Deterministic fictional codes: four random 12-character strings per row, so the package barely compresses.</summary>
    private sealed class RowGenerator
    {
        private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        private readonly Random _random = new(2_0_1);
        private readonly char[] _chars = new char[12];
        private int _next;

        public string[] Next(int index)
        {
            if (index != _next) throw new InvalidOperationException("rows must be generated in order");
            _next++;
            var row = new string[4];
            for (int c = 0; c < row.Length; c++)
            {
                for (int k = 0; k < _chars.Length; k++) _chars[k] = Alphabet[_random.Next(Alphabet.Length)];
                row[c] = new string(_chars);
            }
            return row;
        }
    }

    /// <summary>
    /// The decrypted package holds the same parts, with the same bytes, as the unencrypted save. Only the save
    /// time differs: the zip entry times, and the created/modified dates in docProps/core.xml.
    /// </summary>
    private static void SamePackage(byte[] decrypted, byte[] plain)
    {
        static SortedDictionary<string, byte[]> Parts(byte[] zip)
        {
            var parts = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            using var z = new System.IO.Compression.ZipArchive(new MemoryStream(zip, writable: false), System.IO.Compression.ZipArchiveMode.Read);
            foreach (var e in z.Entries)
            {
                using var s = e.Open();
                var ms = new MemoryStream();
                s.CopyTo(ms);
                byte[] b = ms.ToArray();
                if (e.FullName == "docProps/core.xml")
                    b = Encoding.UTF8.GetBytes(System.Text.RegularExpressions.Regex.Replace(Encoding.UTF8.GetString(b), @"\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ", "(time)"));
                parts[e.FullName] = b;
            }
            return parts;
        }
        var a = Parts(decrypted);
        var b = Parts(plain);
        Assert(a.Keys.SequenceEqual(b.Keys), "the decrypted package has different parts from the unencrypted save");
        foreach (var (name, bytes) in a)
            Assert(bytes.AsSpan().SequenceEqual(b[name]), $"part {name} differs from the unencrypted save");
    }

    // ---- CFB ---------------------------------------------------------------------------------------------

    private static byte[] Bytes(int size, int seed)
    {
        var b = new byte[size];
        new Random(seed).NextBytes(b);
        return b;
    }

    /// <summary>The node tree EncryptedPackageWriter builds, with fixed contents.</summary>
    private static List<CfbContainer.Node> NewTree(byte[] package) => new()
    {
        new() { Name = "EncryptedPackage", Type = CfbContainer.TypeStream, Data = package, Color = CfbContainer.ColorRed },
        new()
        {
            Name = "\u0006DataSpaces", Type = CfbContainer.TypeStorage, Color = CfbContainer.ColorRed,
            Children =
            {
                new() { Name = "Version", Type = CfbContainer.TypeStream, Data = DataSpacesBuilder.BuildVersionStream(), Color = CfbContainer.ColorBlack },
                new() { Name = "DataSpaceMap", Type = CfbContainer.TypeStream, Data = DataSpacesBuilder.BuildDataSpaceMap(), Color = CfbContainer.ColorBlack },
                new()
                {
                    Name = "DataSpaceInfo", Type = CfbContainer.TypeStorage, Color = CfbContainer.ColorBlack,
                    Children = { new() { Name = "StrongEncryptionDataSpace", Type = CfbContainer.TypeStream, Data = DataSpacesBuilder.BuildStrongEncryptionDataSpace(), Color = CfbContainer.ColorBlack } },
                },
                new()
                {
                    Name = "TransformInfo", Type = CfbContainer.TypeStorage, Color = CfbContainer.ColorRed,
                    Children =
                    {
                        new()
                        {
                            Name = "StrongEncryptionTransform", Type = CfbContainer.TypeStorage, Color = CfbContainer.ColorBlack,
                            Children = { new() { Name = "\u0006Primary", Type = CfbContainer.TypeStream, Data = DataSpacesBuilder.BuildPrimary(), Color = CfbContainer.ColorBlack } },
                        },
                    },
                },
            },
        },
        new() { Name = "EncryptionInfo", Type = CfbContainer.TypeStream, Data = Bytes(1_234, seed: 1), Color = CfbContainer.ColorBlack },
    };

    private static List<LegacyCfbWriter.Node> ToLegacy(IEnumerable<CfbContainer.Node> nodes)
        => nodes.Select(n => new LegacyCfbWriter.Node
        {
            Name = n.Name, Type = n.Type, Data = n.Data, Color = n.Color, Children = ToLegacy(n.Children),
        }).ToList();

    private static byte[] LegacyWrite(byte[] package)
    {
        var ms = new MemoryStream();
        LegacyCfbWriter.Write(ms, ToLegacy(NewTree(package)));
        return ms.ToArray();
    }

    private static byte[] NewWrite(byte[] package, byte[]? prefix, bool nonSeekable)
    {
        var ms = new MemoryStream();
        if (prefix is not null) ms.Write(prefix);
        Stream target = nonSeekable ? new NonSeekableWriteStream(ms) : ms;
        CfbContainer.Write(target, NewTree(package));
        return ms.ToArray();
    }

    /// <summary>Tree 0: only mini streams; tree 1: several regular streams of odd sizes.</summary>
    private static List<CfbContainer.Node> OtherTree(int tree) => tree == 0
        ? new() { new() { Name = "A", Type = CfbContainer.TypeStream, Data = Bytes(10, 2) }, new() { Name = "B", Type = CfbContainer.TypeStream, Data = Bytes(4000, 3) } }
        : new()
        {
            new() { Name = "One", Type = CfbContainer.TypeStream, Data = Bytes(4097, 4) },
            new() { Name = "Two", Type = CfbContainer.TypeStream, Data = Bytes(70_001, 5) },
            new() { Name = "S", Type = CfbContainer.TypeStorage, Children = { new() { Name = "Three", Type = CfbContainer.TypeStream, Data = Bytes(9, 6) } } },
        };

    private static byte[] LegacyWriteTree(int tree)
    {
        var ms = new MemoryStream();
        LegacyCfbWriter.Write(ms, ToLegacy(OtherTree(tree)));
        return ms.ToArray();
    }

    private static byte[] NewWriteTree(int tree)
    {
        var ms = new MemoryStream();
        CfbContainer.Write(ms, OtherTree(tree));
        return ms.ToArray();
    }

    /// <summary>The largest EncryptedPackage (in the tree above) that needs 109 FAT sectors or fewer: found by bisection.</summary>
    private static int LargestSizeWithoutDifat()
    {
        int lo = 6_000_000, hi = 7_200_000;
        while (hi - lo > 1)
        {
            int mid = lo + (hi - lo) / 2;
            if (ReadUInt32(NewWrite(new byte[mid], null, false), 72) == 0) lo = mid; else hi = mid;
        }
        return lo;
    }

    /// <summary>Checks the header, FAT and DIFAT of a container written for <paramref name="package"/>, and reads it back.</summary>
    private static void CheckDifat(byte[] package, bool expectDifat)
    {
        byte[] file = NewWrite(package, null, false);
        Assert(file.Length % SectorSize == 0, "length not a whole number of sectors");
        int totalSectors = file.Length / SectorSize - 1;
        uint fatCount = ReadUInt32(file, 44), difatStart = ReadUInt32(file, 68), difatCount = ReadUInt32(file, 72);
        uint expectedDifat = fatCount > 109 ? (fatCount - 109 + 126) / 127 : 0;
        Assert(difatCount == expectedDifat && (difatCount > 0) == expectDifat, $"{fatCount} FAT sectors, {difatCount} DIFAT sectors");
        Assert(fatCount * 128 >= totalSectors && (fatCount - 1) * 128 < totalSectors, $"{fatCount} FAT sectors for {totalSectors} sectors");

        // Every FAT sector number: 109 from the header, the rest along the DIFAT chain.
        var fatSectors = new List<uint>();
        for (int i = 0; i < 109; i++) { uint v = ReadUInt32(file, 76 + 4 * i); if (v != Free) fatSectors.Add(v); }
        var difatSectors = new List<uint>();
        uint next = difatStart;
        for (uint d = 0; d < difatCount; d++)
        {
            Assert(next < totalSectors, $"DIFAT pointer {next} out of range");
            difatSectors.Add(next);
            int off = SectorSize * (int)(next + 1);
            for (int i = 0; i < 127; i++) { uint v = ReadUInt32(file, off + 4 * i); if (v != Free) fatSectors.Add(v); }
            next = ReadUInt32(file, off + 508);
        }
        Assert(next == (difatCount > 0 ? EndOfChain : EndOfChain), "DIFAT chain does not end with end of chain");
        Assert(fatSectors.Count == fatCount, $"{fatSectors.Count} FAT sectors listed, {fatCount} in the header");
        for (int i = 1; i < fatSectors.Count; i++) Assert(fatSectors[i] == fatSectors[0] + i, "FAT sectors not contiguous");

        uint Fat(uint sector) => ReadUInt32(file, SectorSize * (int)(fatSectors[(int)(sector / 128)] + 1) + 4 * (int)(sector % 128));
        foreach (var s in fatSectors) Assert(Fat(s) == FatSect, $"FAT sector {s} not marked FATSECT");
        foreach (var s in difatSectors) Assert(Fat(s) == DifSect, $"DIFAT sector {s} not marked DIFSECT");
        Assert(difatSectors.Count == 0 || difatSectors[^1] == totalSectors - 1, "DIFAT sectors are not at the end");

        var streams = CfbContainer.Read(new MemoryStream(file, writable: false));
        Assert(streams["EncryptedPackage"].AsSpan().SequenceEqual(package), "EncryptedPackage differs");
        Assert(streams["EncryptionInfo"].AsSpan().SequenceEqual(Bytes(1_234, seed: 1)), "EncryptionInfo differs");
        Assert(streams["\u0006Primary"].AsSpan().SequenceEqual(DataSpacesBuilder.BuildPrimary()), "\\x06Primary differs");

        byte[] prefixed = NewWrite(package, Encoding.ASCII.GetBytes("XY"), nonSeekable: true);
        Assert(prefixed.AsSpan(2).SequenceEqual(file), "different bytes after a prefix on a non-seekable stream");
    }

    // ---- files -------------------------------------------------------------------------------------------

    private static string Old(string dir, string name)
    {
        string p = Path.Combine(dir, name);
        File.WriteAllText(p, "OLD");
        return p;
    }

    private static string Fresh(string dir, string name)
    {
        string p = Path.Combine(dir, name);
        Directory.CreateDirectory(p);
        foreach (var f in Directory.GetFiles(p)) File.Delete(f);
        return p;
    }

    private static string[] TempsBeside(string target)
        => Directory.GetFiles(Path.GetDirectoryName(target)!, "." + Path.GetFileName(target) + ".*.tmp");

    private static void NoTempBeside(string target)
        => Assert(TempsBeside(target).Length == 0, "temporary file left beside the target");

    /// <summary>Owner-only (Unix: 600) and opened exclusively (another open fails).</summary>
    private static void CheckPrivate(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(path);
            Assert(mode == (UnixFileMode.UserRead | UnixFileMode.UserWrite), $"{Path.GetFileName(path)} mode {mode}");
        }
        try
        {
            using var other = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            throw new Exception($"{Path.GetFileName(path)} could be opened by someone else");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// The bytes of a spool that is open exclusively: on Linux and macOS through `cat` (the .NET lock is advisory,
    /// so another process can still read it), so the content can be checked for plaintext; null on Windows, where
    /// the open-exclusive check above already proves no one else can read it.
    /// </summary>
    private static byte[]? TryReadDespiteLock(string path)
    {
        if (OperatingSystem.IsWindows()) return null;
        var psi = new ProcessStartInfo("cat") { RedirectStandardOutput = true, UseShellExecute = false };
        psi.ArgumentList.Add(path);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("cat did not start");
        var ms = new MemoryStream();
        p.StandardOutput.BaseStream.CopyTo(ms);
        p.WaitForExit();
        if (p.ExitCode != 0) throw new Exception($"cat {Path.GetFileName(path)} failed (exit {p.ExitCode})");
        return ms.ToArray();
    }

    private static bool Contains(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle) >= 0;

    // ---- msoffcrypto-tool (test-only, D-164) ------------------------------------------------------------

    private static class Msoffcrypto
    {
        private const string Script =
            "import sys,io,msoffcrypto\n" +
            "f=msoffcrypto.OfficeFile(io.BytesIO(sys.stdin.buffer.read()))\n" +
            "f.load_key(password=sys.argv[1])\n" +
            "o=io.BytesIO()\n" +
            "f.decrypt(o)\n" +
            "sys.stdout.buffer.write(o.getvalue())\n";

        private static string? _python;

        public static bool Available()
        {
            foreach (var candidate in new[] { "python3", "python", "py" })
            {
                try
                {
                    var (code, _) = Run(candidate, new[] { "-I", "-c", "import msoffcrypto" }, Array.Empty<byte>());
                    if (code == 0) { _python = candidate; return true; }
                }
                catch (System.ComponentModel.Win32Exception) { }
                catch (InvalidOperationException) { }
            }
            return false;
        }

        /// <summary>Decrypts in memory: the file goes in on stdin and the package comes back on stdout (D-44).</summary>
        public static byte[] Decrypt(byte[] file, string password)
        {
            var (code, output) = Run(_python!, new[] { "-I", "-c", Script, password }, file);
            if (code != 0) throw new Exception($"msoffcrypto-tool failed (exit {code})");
            return output;
        }

        private static (int Code, byte[] Output) Run(string exe, string[] args, byte[] input)
        {
            var psi = new ProcessStartInfo(exe)
            {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("no process");
            var stdout = new MemoryStream();
            var copy = p.StandardOutput.BaseStream.CopyToAsync(stdout);
            var err = p.StandardError.ReadToEndAsync();
            p.StandardInput.BaseStream.Write(input);
            p.StandardInput.Close();
            copy.GetAwaiter().GetResult();
            err.GetAwaiter().GetResult();
            p.WaitForExit();
            return (p.ExitCode, stdout.ToArray());
        }
    }

    // ---- helpers -----------------------------------------------------------------------------------------

    private sealed class SkipException : Exception { public SkipException(string m) : base(m) { } }

    private static void Assert(bool condition, string what)
    {
        if (!condition) throw new Exception(what);
    }

    private static T Throws<T>(Action a, string what) where T : Exception
    {
        try { a(); }
        catch (T ex) { return ex; }
        catch (Exception ex) { throw new Exception($"{what}: expected {typeof(T).Name}, got {ex.GetType().Name}: {ex.Message}"); }
        throw new Exception($"{what}: expected {typeof(T).Name}, nothing thrown");
    }

    private static uint ReadUInt32(byte[] b, int off) => BitConverter.ToUInt32(b, off);

    /// <summary>A write-only, non-seekable stream over a sink, like an HTTP response body.</summary>
    private sealed class NonSeekableWriteStream : Stream
    {
        private readonly Stream _sink;
        public bool Disposed { get; private set; }
        public NonSeekableWriteStream(Stream sink) { _sink = sink; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !Disposed;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { if (Disposed) throw new ObjectDisposedException(nameof(NonSeekableWriteStream)); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (Disposed) throw new ObjectDisposedException(nameof(NonSeekableWriteStream));
            _sink.Write(buffer, offset, count);
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
