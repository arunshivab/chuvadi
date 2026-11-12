using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Chuvadi.Docs.Internal;
using EncryptedSpool = Chuvadi.Internal.Crypto.EncryptedSpool;
using Chuvadi.Docs.Model;
using static Chuvadi.Docs.ManualTests.Program;

namespace Chuvadi.Docs.ManualTests;

/// <summary>
/// Docs 2.0 encrypted saving (D-47 option A, D-155): the new SaveTo(Stream, EncryptionOptions);
/// both encrypted overloads spool through a temporary file encrypted with a one-time AES key held
/// only in memory, created owner-only and deleted on close; the path overload replaces the target
/// only when the save succeeded. EncryptionOptions.TempDirectory chooses the spool's folder (D-217).
/// </summary>
public static class EncryptedSaveTests
{
    private const string Pw = "S3cret!";

    public static void Run(string outDir)
    {
        var dir = Path.Combine(outDir, "encsave");
        Directory.CreateDirectory(dir);
        StreamOverload(dir);
        PathOverload(dir);
        Spool(dir);
        TempFolder(dir);
        MemoryStorage(dir);
    }

    private static Document Sample()
    {
        var d = new Document();
        d.Properties.Created = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        d.Properties.Modified = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        d.AddHeading("Encrypted", 1);
        for (int i = 0; i < 200; i++) d.AddParagraph("Paragraph " + i + " " + new string('x', i % 50));
        d.AddParagraph().Image(ImageSpec.Inline(ErrorContractMatrix.Png1x1, "image/png", 10, 10));
        return d;
    }

    private static byte[] Plain(Document d) { var ms = new MemoryStream(); d.SaveTo(ms); return ms.ToArray(); }

    private static byte[] Decrypt(byte[] encrypted, string pw)
        => PackageGuard.Decrypt(new MemoryStream(encrypted), pw, new DocxReaderOptions(), default);

    private static void StreamOverload(string dir)
    {
        Check("SaveTo(Stream, EncryptionOptions) writes an encrypted package that opens with the password and holds exactly the plain package", () =>
        {
            var d = Sample();
            var output = new MemoryStream();
            d.SaveTo(output, new EncryptionOptions { Password = Pw, SpinCount = 1000 });
            var bytes = output.ToArray();
            AssertTrue(bytes.Length > 8 && bytes[0] == 0xD0 && bytes[1] == 0xCF, "a compound file (encrypted), not a zip");
            AssertSamePackage(Decrypt(bytes, Pw), Plain(d), "decrypted package is byte-identical to SaveTo(Stream) (zip entry times aside)");
            AssertTrue(Document.Load(new MemoryStream(bytes), Pw).GetText() == d.GetText(), "text round-trips");
            OutsideTools.CheckWithMsoffcrypto(bytes, Pw, Plain(d), "stream save");
            AssertThrows<DocxPasswordRequiredException>(() => Document.Load(new MemoryStream(bytes)), "needs the password");
        });
        Check("SaveTo(Stream, EncryptionOptions) leaves the stream open, writes from its current position, and matches the path overload", () =>
        {
            var d = Sample();
            var output = new MemoryStream();
            output.Write(new byte[] { 1, 2, 3 });
            d.SaveTo(output, new EncryptionOptions { Password = Pw, SpinCount = 1000 });
            output.WriteByte(9);   // still open
            var all = output.ToArray();
            AssertTrue(all[0] == 1 && all[1] == 2 && all[2] == 3 && all[^1] == 9, "prefix kept, still writable");
            var viaStream = Decrypt(all[3..^1], Pw);
            var path = Path.Combine(dir, "enc-path.docx");
            d.SaveTo(path, new EncryptionOptions { Password = Pw, SpinCount = 1000 });
            AssertSamePackage(Decrypt(File.ReadAllBytes(path), Pw), viaStream, "same package as the path overload");
        });
        Check("the package comparison ignores the zip entry times: a changed byte or data descriptors are a difference", () =>
        {
            var a = Plain(Sample());
            var b = Plain(Sample());
            AssertSamePackage(a, b, "two saves of one document");
            var flipped = (byte[])a.Clone();
            flipped[flipped.Length / 2] ^= 0x01;
            AssertTrue(!SamePackageExceptTimes(a, flipped), "one changed byte");
            var timeOnly = (byte[])a.Clone();
            timeOnly[10] ^= 0x01;   // the first entry's local-header time field
            AssertSamePackage(a, timeOnly, "a time field only");
            var described = new MemoryStream();
            Sample().SaveTo(new WriteOnlyStream(described));   // a non-seekable stream makes the zip writer use data descriptors
            AssertTrue(!SamePackageExceptTimes(a, described.ToArray()), "data descriptors");
        });
        Check("the package comparison ignores the save clock in docProps/core.xml (D-321): two saves either side of a second boundary are the same package", () =>
        {
            var d = new Document();   // no dates set: each save writes the current time to the second
            d.AddParagraph("Saved twice, a second apart");
            var first = Plain(d);
            var start = DateTime.UtcNow;
            while (DateTime.UtcNow.Second == start.Second) System.Threading.Thread.Sleep(5);
            var second = Plain(d);
            AssertTrue(CoreXml(first) != CoreXml(second), "the two core.xml differ (the clock moved on): " + CoreXml(second));
            AssertSamePackage(first, second, "the same package apart from the dates");
        });
        Check("the package comparison still catches every other difference and names the part: a changed byte, data descriptors, a changed dcterms:title", () =>
        {
            var a = Plain(Sample());
            var flipped = (byte[])a.Clone();
            int at = DataOffset(a, "word/document.xml") + 7;
            flipped[at] ^= 0x01;
            var why = PackageCompare.Difference(a, flipped) ?? "";
            AssertTrue(why.StartsWith("differs in word/document.xml at byte 7 of its data", StringComparison.Ordinal), "a changed byte: " + why);

            var described = new MemoryStream();
            Sample().SaveTo(new WriteOnlyStream(described));
            why = PackageCompare.Difference(a, described.ToArray()) ?? "";
            AssertTrue(why.Contains("[Content_Types].xml") || why.Contains("local header"), "data descriptors: " + why);

            var titled = Sample();
            titled.Properties.Title = "A";
            var other = Sample();
            other.Properties.Title = "B";
            why = PackageCompare.Difference(Plain(titled), Plain(other)) ?? "";
            AssertTrue(why.StartsWith("differs in docProps/core.xml (inflated, the two dates masked)", StringComparison.Ordinal), "a changed title: " + why);

            why = PackageCompare.Difference(a, a.AsSpan(0, a.Length - 1).ToArray()) ?? "";
            AssertTrue(why.Length > 0, "a truncated package: " + why);
        });
        Check("SaveTo(Stream, EncryptionOptions) argument checks: null stream, null options, empty password", () =>
        {
            var d = Sample();
            AssertThrows<ArgumentNullException>(() => d.SaveTo((Stream)null!, new EncryptionOptions { Password = Pw }), "null stream");
            AssertThrows<ArgumentNullException>(() => d.SaveTo(new MemoryStream(), (EncryptionOptions)null!), "null options");
            var empty = new MemoryStream();
            AssertThrows<ArgumentException>(() => d.SaveTo(empty, new EncryptionOptions { Password = "" }), "empty password");
            AssertTrue(empty.Length == 0, "nothing written");
            AssertThrows<ArgumentException>(() => d.SaveTo(null!, new EncryptionOptions { Password = Pw }), "SaveTo(null, options) binds to the path overload");
        });
        Check("SaveTo(Stream, EncryptionOptions) works on a write-only, non-seekable output stream", () =>
        {
            var d = Sample();
            var sink = new MemoryStream();
            d.SaveTo(new WriteOnlyStream(sink), new EncryptionOptions { Password = Pw, SpinCount = 1000 });
            AssertSamePackage(Decrypt(sink.ToArray(), Pw), Plain(d), "same package");
        });
    }

    private static void PathOverload(string dir)
    {
        Check("bug fix: a failing encrypted path save leaves the existing file untouched and no temp file", () =>
        {
            var target = Path.Combine(dir, "enc-atomic.docx");
            Sample().SaveTo(target, new EncryptionOptions { Password = Pw, SpinCount = 1000 });
            var before = File.ReadAllBytes(target);
            var bad = new Document();
            bad.AddParagraph().Image(ImageSpec.Inline(ErrorContractMatrix.Png1x1, "image/png", double.NaN, 10));
            AssertThrows<ArgumentOutOfRangeException>(() => bad.SaveTo(target, new EncryptionOptions { Password = Pw, SpinCount = 1000 }), "NaN size");
            AssertTrue(File.ReadAllBytes(target).SequenceEqual(before), "original intact");
            AssertTrue(!Directory.GetFiles(dir, ".enc-atomic.docx.*").Any(), "no temp file beside the target");
        });
        Check("the encrypted path save creates a new file and overwrites an existing one", () =>
        {
            var target = Path.Combine(dir, "enc-new.docx");
            if (File.Exists(target)) File.Delete(target);
            Sample().SaveTo(target, new EncryptionOptions { Password = Pw, SpinCount = 1000 });
            AssertTrue(Document.Load(target, Pw).GetText().Contains("Paragraph 199"), "created");
            var other = new Document();
            other.AddParagraph("replaced");
            other.SaveTo(target, new EncryptionOptions { Password = Pw, SpinCount = 1000 });
            AssertTrue(Document.Load(target, Pw).GetText().Trim() == "replaced", "overwritten");
            AssertTrue(!Directory.GetFiles(dir, ".enc-new.docx.*").Any(), "no temp file");
        });
    }

    private static void Spool(string dir)
    {
        Check("the spool never writes the plaintext package to disk: its file holds AES ciphertext only", () =>
        {
            var plain = Plain(Sample());
            string? spoolPath;
            byte[] onDisk;
            using (var spool = new EncryptedSpool())
            {
                spool.Write(plain);
                spool.Flush();
                spoolPath = SpoolFile(spool).Name;
                onDisk = RawBytes(spool);
                AssertTrue(onDisk.Length == plain.Length, "same length (counter mode)");
                AssertTrue(!(onDisk[0] == 'P' && onDisk[1] == 'K'), "not a zip on disk");
                AssertTrue(IndexOf(onDisk, Encoding.ASCII.GetBytes("word/document.xml")) < 0, "no part names on disk");
                AssertTrue(IndexOf(onDisk, Encoding.ASCII.GetBytes("[Content_Types].xml")) < 0, "no content types on disk");
                spool.Position = 0;
                var back = new MemoryStream();
                spool.CopyTo(back);
                AssertTrue(back.ToArray().SequenceEqual(plain), "reads back the plaintext");
            }
            AssertTrue(!File.Exists(spoolPath), "deleted on close");
        });
        Check("the spool is seekable: overwriting and reading at any offset round-trip (as the zip writer needs)", () =>
        {
            var rnd = new Random(7);
            var expected = new byte[300_000];
            rnd.NextBytes(expected);
            using var spool = new EncryptedSpool();
            spool.Write(expected);
            foreach (var (at, len) in new[] { (0, 1), (15, 2), (16, 16), (17, 33), (65_530, 20), (131_071, 70_000), (299_990, 10) })
            {
                var patch = new byte[len];
                rnd.NextBytes(patch);
                spool.Position = at;
                spool.Write(patch);
                Array.Copy(patch, 0, expected, at, len);
            }
            AssertTrue(spool.Length == expected.Length, "length");
            foreach (var (at, len) in new[] { (0, 300_000), (3, 5), (16, 1), (65_535, 2), (131_000, 100_000) })
            {
                spool.Position = at;
                var got = new byte[len];
                spool.ReadExactly(got);
                AssertTrue(got.AsSpan().SequenceEqual(expected.AsSpan(at, len)), $"read {at}+{len}");
            }
            spool.Seek(-1, SeekOrigin.End);
            AssertTrue(spool.ReadByte() == expected[^1] && spool.ReadByte() == -1, "byte reads and end of stream");
        });
        Check("the spool file is created owner-only (Unix mode 600) and exclusively; a second key gives different ciphertext", () =>
        {
            using var a = new EncryptedSpool();
            using var b = new EncryptedSpool();
            var data = new byte[4096];
            a.Write(data);
            b.Write(data);
            a.Flush();
            b.Flush();
            var pa = SpoolFile(a).Name;
            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(pa);
                AssertTrue(mode == (UnixFileMode.UserRead | UnixFileMode.UserWrite), "mode " + mode);
            }
            // Opened with FileShare.None: no other handle can open it.
            AssertThrows<IOException>(() => new FileStream(pa, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete).Dispose(), "exclusive");
            AssertTrue(!RawBytes(a).SequenceEqual(RawBytes(b)), "independent keys");
        });
    }

    // ---- helpers ------------------------------------------------------------------------

    /// <summary>
    /// Two zip packages are the same apart from the save clocks: the entries' last-modified times and the
    /// <c>dcterms:created</c>/<c>dcterms:modified</c> dates in <c>docProps/core.xml</c>. Everything else is
    /// compared byte for byte (see <see cref="PackageCompare"/>, D-321).
    /// </summary>
    internal static bool SamePackageExceptTimes(byte[] a, byte[] b) => PackageCompare.Difference(a, b) is null;

    private static string CoreXml(byte[] package)
    {
        using var z = new ZipArchive(new MemoryStream(package));
        using var r = new StreamReader(z.GetEntry("docProps/core.xml")!.Open());
        return r.ReadToEnd();
    }

    /// <summary>Where an entry's compressed data starts, from its local header.</summary>
    private static int DataOffset(byte[] package, string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name);
        for (int i = 0; i + 30 + bytes.Length <= package.Length; i++)
            if (BitConverter.ToUInt32(package, i) == 0x04034b50 && BitConverter.ToUInt16(package, i + 26) == bytes.Length
                && package.AsSpan(i + 30, bytes.Length).SequenceEqual(bytes))
                return i + 30 + bytes.Length + BitConverter.ToUInt16(package, i + 28);
        throw new InvalidOperationException(name + " not found");
    }

    /// <summary>Asserts <see cref="SamePackageExceptTimes"/>; the failure names the first differing part and offset.</summary>
    internal static void AssertSamePackage(byte[] a, byte[] b, string message)
    {
        var difference = PackageCompare.Difference(a, b);
        AssertTrue(difference is null, message + ": " + difference);
    }

    /// <summary>
    /// EncryptionOptions.TempDirectory (D-217, P1): where the encrypted saves spool. The output stream
    /// below lists the folders while the encrypted file is written, which is when the spool exists,
    /// so the checks don't depend on timing.
    /// </summary>
    private static void TempFolder(string dir)
    {
        Check("EncryptionOptions.TempDirectory: the stream save spools in that folder, which is empty again afterwards; the file opens with the password", () =>
        {
            var folder = FreshFolder(dir, "tempdir-used");
            var d = Sample();
            var output = new ObservingStream(folder, "chuvadi_docs_*.tmp");
            d.SaveTo(output, new EncryptionOptions { Password = Pw, SpinCount = 1000, TempDirectory = folder });
            AssertTrue(output.Seen.Count == 1 && output.Seen[0].StartsWith("chuvadi_docs_") && output.Seen[0].EndsWith(".tmp"),
                "one spool file in the folder while writing: " + string.Join(", ", output.Seen));
            AssertTrue(Directory.GetFileSystemEntries(folder).Length == 0, "the folder is empty after the save");
            AssertSamePackage(Decrypt(output.ToArray(), Pw), Plain(d), "decrypted package is the plain package");
        });

        Check("EncryptionOptions.TempDirectory null (the default): the spool goes to the system temp folder, as before", () =>
        {
            var unused = FreshFolder(dir, "tempdir-default");
            var output = new ObservingStream(Path.GetTempPath(), "chuvadi_docs_*.tmp", unused);
            Sample().SaveTo(output, new EncryptionOptions { Password = Pw, SpinCount = 1000 });
            AssertTrue(output.Seen.Count >= 1, "a spool file in " + Path.GetTempPath() + " while writing");
            AssertTrue(output.SeenOther.Count == 0, "nothing in any other folder");
            AssertTrue(Document.Load(new MemoryStream(output.ToArray()), Pw).GetText() == Sample().GetText(), "opens");
        });

        Check("EncryptionOptions.TempDirectory missing: both encrypted saves throw DirectoryNotFoundException and write nothing (stream untouched; path target unchanged, no temporary file beside it)", () =>
        {
            var missing = Path.Combine(dir, "tempdir-missing-" + Guid.NewGuid().ToString("N"));
            var enc = new EncryptionOptions { Password = Pw, SpinCount = 1000, TempDirectory = missing };
            var output = new MemoryStream();
            output.Write(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            Exception? ex = null;
            try { Sample().SaveTo(output, enc); } catch (Exception e) { ex = e; }
            AssertTrue(ex is DirectoryNotFoundException && ex.Message.Contains(missing), "stream save: " + ex?.GetType().Name + ": " + ex?.Message);
            AssertTrue(output.Length == 8 && output.Position == 8, "the stream is untouched");

            var target = Path.Combine(dir, "tempdir-missing.docx");
            File.WriteAllText(target, "old");
            ex = null;
            try { Sample().SaveTo(target, enc); } catch (Exception e) { ex = e; }
            AssertTrue(ex is DirectoryNotFoundException && ex.Message.Contains(missing), "path save: " + ex?.GetType().Name + ": " + ex?.Message);
            AssertTrue(File.ReadAllText(target) == "old", "the existing file is unchanged");
            AssertTrue(Directory.GetFiles(dir, ".tempdir-missing.docx.*.tmp").Length == 0, "no temporary file beside the target");
            AssertTrue(!Directory.Exists(missing), "the folder is not created");
        });

        Check("EncryptionOptions.TempDirectory naming a file, not a folder: the save throws an IOException and writes nothing", () =>
        {
            var file = Path.Combine(dir, "tempdir-is-a-file.txt");
            File.WriteAllText(file, "not a folder");
            var output = new MemoryStream();
            Exception? ex = null;
            try { Sample().SaveTo(output, new EncryptionOptions { Password = Pw, SpinCount = 1000, TempDirectory = file }); }
            catch (Exception e) { ex = e; }
            AssertTrue(ex is IOException, ex?.GetType().Name + ": " + ex?.Message);
            AssertTrue(output.Length == 0, "nothing written");
            AssertTrue(File.ReadAllText(file) == "not a folder", "the file is unchanged");
        });

        Check("DocxTemplate.Fill with OutputEncryption encrypts in memory: TempDirectory is not used (a missing folder is fine)", () =>
        {
            var missing = Path.Combine(dir, "tempdir-fill-" + Guid.NewGuid().ToString("N"));
            var template = new Document();
            template.AddParagraph("Hello {{Name}}");
            var output = new MemoryStream();
            DocxTemplate.Fill(new MemoryStream(Plain(template)), output, new TemplateData().Set("Name", "Ada"),
                new TemplateOptions { OutputEncryption = new EncryptionOptions { Password = Pw, SpinCount = 1000, TempDirectory = missing } });
            AssertTrue(Document.Load(new MemoryStream(output.ToArray()), Pw).GetText().Contains("Hello Ada"), "filled and encrypted");
            AssertTrue(!Directory.Exists(missing), "no folder created");
        });
    }

    /// <summary>EncryptionOptions.TempStorage (D-296 R3): the spool in process memory.</summary>
    private static void MemoryStorage(string dir)
    {
        Check("EncryptionOptions.TempStorage is File by default; an undefined value is rejected", () =>
        {
            AssertTrue(new EncryptionOptions { Password = Pw }.TempStorage == TempStorage.File, "default File");
            AssertThrows<ArgumentOutOfRangeException>(() => _ = new EncryptionOptions { Password = Pw, TempStorage = (TempStorage)7 }, "undefined");
        });

        Check("TempStorage.Memory: the stream save creates no file in TempDirectory while writing, and decrypts to exactly the File route's package", () =>
        {
            var folder = FreshFolder(dir, "tempstorage-memory");
            var d = Sample();
            var output = new ObservingStream(folder, "*");
            d.SaveTo(output, new EncryptionOptions { Password = Pw, SpinCount = 1000, TempDirectory = folder, TempStorage = TempStorage.Memory });
            AssertTrue(output.Seen.Count == 0 && Directory.GetFileSystemEntries(folder).Length == 0, "no file in the folder: " + string.Join(", ", output.Seen));
            var viaFile = new MemoryStream();
            d.SaveTo(viaFile, new EncryptionOptions { Password = Pw, SpinCount = 1000, TempDirectory = folder });
            var mem = Decrypt(output.ToArray(), Pw);
            AssertSamePackage(mem, Decrypt(viaFile.ToArray(), Pw), "same package as the File route");
            AssertSamePackage(mem, Plain(d), "and as the plain save");
            OutsideTools.CheckWithMsoffcrypto(output.ToArray(), Pw, Plain(d), "memory spool");
        });

        Check("TempStorage.Memory: a missing TempDirectory is not used, so both encrypted saves still work (stream and path)", () =>
        {
            var missing = Path.Combine(dir, "tempstorage-missing-" + Guid.NewGuid().ToString("N"));
            var enc = new EncryptionOptions { Password = Pw, SpinCount = 1000, TempDirectory = missing, TempStorage = TempStorage.Memory };
            var d = Sample();
            var output = new MemoryStream();
            d.SaveTo(output, enc);
            var path = Path.Combine(dir, "enc-memory.docx");
            d.SaveTo(path, enc);
            AssertTrue(!Directory.Exists(missing), "the folder was not created");
            AssertTrue(Document.Load(new MemoryStream(output.ToArray()), Pw).GetText() == d.GetText() && Document.Load(path, Pw).GetText() == d.GetText(), "both open with the password");
            AssertSamePackage(Decrypt(File.ReadAllBytes(path), Pw), Plain(d), "the path save holds the plain package");
        });
    }

    private static string FreshFolder(string dir, string name)
    {
        var folder = Path.Combine(dir, name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>A write-only output that, on each write, lists the spool files in one folder (and any file in the others).</summary>
    private sealed class ObservingStream : Stream
    {
        private readonly MemoryStream _inner = new();
        private readonly string _folder, _pattern;
        private readonly string[] _others;
        public readonly System.Collections.Generic.List<string> Seen = new();
        public readonly System.Collections.Generic.List<string> SeenOther = new();

        public ObservingStream(string folder, string pattern, params string[] others) { _folder = folder; _pattern = pattern; _others = others; }

        public byte[] ToArray() => _inner.ToArray();
        public override void Write(byte[] buffer, int offset, int count)
        {
            foreach (var f in Directory.GetFiles(_folder, _pattern))
                if (!Seen.Contains(Path.GetFileName(f))) Seen.Add(Path.GetFileName(f));
            foreach (var o in _others)
                foreach (var f in Directory.GetFileSystemEntries(o))
                    if (!SeenOther.Contains(f)) SeenOther.Add(f);
            _inner.Write(buffer, offset, count);
        }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    /// <summary>The spool's backing file stream, found by reflection (test only).</summary>
    private static FileStream SpoolFile(EncryptedSpool spool)
    {
        var f = typeof(EncryptedSpool).GetField("_file", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (FileStream)f.GetValue(spool)!;
    }

    /// <summary>The bytes on disk, read through the spool's own (exclusive) file handle.</summary>
    private static byte[] RawBytes(EncryptedSpool spool)
    {
        var file = SpoolFile(spool);
        file.Flush();
        long keep = file.Position;
        file.Position = 0;
        var bytes = new byte[file.Length];
        file.ReadExactly(bytes);
        file.Position = keep;
        return bytes;
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        return -1;
    }

    private sealed class WriteOnlyStream : Stream
    {
        private readonly Stream _inner;
        public WriteOnlyStream(Stream inner) => _inner = inner;
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
