using System;
using System.IO;
using System.Linq;
using Chuvadi.Docs.Model;
using static Chuvadi.Docs.ManualTests.Program;

namespace Chuvadi.Docs.ManualTests;

/// <summary>
/// Encrypted files of 6, 8, 12 and 50 MB round-trip (D-185). A compound file needs extra DIFAT sectors
/// once it has more than 109 FAT sectors (about 7.1 MB with 512-byte sectors, MS-CFB §2.5); before
/// Chuvadi.Sheets 2.0.1's shared writer wrote them, every encrypted save above that size was
/// unreadable. 6 MB stays under the boundary; 8 and 12 MB need one DIFAT sector, 50 MB needs six. Each size is saved through all three encrypted
/// writers (SaveTo(path, ...), SaveTo(Stream, ...) and DocxTemplate.Fill with OutputEncryption)
/// and read back through Document.Load and DocxReader.Open.
/// </summary>
public static class EncryptedLargeTests
{
    private const string Pw = "S3cret!";
    private const long DifatBoundary = 109L * 128 * 512;   // bytes of sectors the header alone can map

    /// <summary>The manual tests: 6, 8, 12 and 50 MB (D-204).</summary>
    public static void Run(string outDir) => Run(outDir, new[] { 6, 8, 12, 50 });

    /// <summary>The unit tests (CI): 6, 8 and 12 MB; the 50 MB case runs in the manual tests only (D-204, DOC-7).</summary>
    public static void RunCi(string outDir) => Run(outDir, new[] { 6, 8, 12 });

    private static void Run(string outDir, int[] sizes)
    {
        var dir = Path.Combine(outDir, "enclarge");
        Directory.CreateDirectory(dir);
        foreach (int mb in sizes)
            Size(dir, mb);
    }

    /// <summary>A document whose package is about <paramref name="mb"/> MB: random text does not compress.</summary>
    private static (Document Doc, string Marker) Sample(int mb)
    {
        var rnd = new Random(mb);
        var noise = new byte[mb * 1024 * 1024];   // as base64 text, deflate brings it back to about these many bytes
        rnd.NextBytes(noise);
        string marker = "end-of-" + mb + "-MB";
        var d = new Document();
        d.Properties.Created = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        d.Properties.Modified = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        d.AddHeading("Large " + mb + " MB {{Name}}", 1);
        d.AddParagraph(Convert.ToBase64String(noise));
        d.AddParagraph(marker);
        return (d, marker);
    }

    private static void Size(string dir, int mb)
    {
        var (doc, marker) = Sample(mb);
        var plainStream = new MemoryStream();
        doc.SaveTo(plainStream);
        var plain = plainStream.ToArray();
        var enc = new EncryptionOptions { Password = Pw, SpinCount = 1000 };

        Check($"{mb} MB: SaveTo(Stream, EncryptionOptions) round-trips through Load and DocxReader (package {plain.Length / 1048576.0:F1} MB)", () =>
        {
            var output = new MemoryStream();
            doc.SaveTo(output, enc);
            var bytes = output.ToArray();
            AssertSize(mb, bytes.Length);
            EncryptedSaveTests.AssertSamePackage(Chuvadi.Docs.Internal.PackageGuard.Decrypt(new MemoryStream(bytes), Pw, new DocxReaderOptions(), default), plain, "decrypted package is the plain package");
            AssertTrue(Document.Load(new MemoryStream(bytes), Pw).GetText().Contains(marker), "Load reads to the end");
            using var r = DocxReader.Open(new MemoryStream(bytes), Pw);
            AssertTrue(r.ExtractText().Contains(marker), "DocxReader reads to the end");
            OutsideTools.CheckWithMsoffcrypto(bytes, Pw, plain, $"{mb} MB");
        });

        Check($"{mb} MB: SaveTo(path, EncryptionOptions) round-trips from the file", () =>
        {
            var path = Path.Combine(dir, $"enc-{mb}mb.docx");
            doc.SaveTo(path, enc);
            AssertSize(mb, new FileInfo(path).Length);
            AssertTrue(Document.Load(path, Pw).GetText().Contains(marker), "Load reads to the end");
            using var r = DocxReader.Open(path, Pw);
            AssertTrue(r.ExtractText().Contains(marker), "DocxReader reads to the end");
        });

        Check($"{mb} MB: DocxTemplate.Fill with OutputEncryption round-trips", () =>
        {
            var output = new MemoryStream();
            DocxTemplate.Fill(new MemoryStream(plain), output, new TemplateData().Set("Name", "Ada"),
                new TemplateOptions { OutputEncryption = enc });
            var bytes = output.ToArray();
            AssertSize(mb, bytes.Length);
            var text = Document.Load(new MemoryStream(bytes), Pw).GetText();
            AssertTrue(text.Contains("Large " + mb + " MB Ada") && text.Contains(marker), "filled and complete");
        });
    }

    /// <summary>The file is on the intended side of the DIFAT boundary, so the test really tests it.</summary>
    private static void AssertSize(int mb, long length)
    {
        bool over = length > DifatBoundary;
        AssertTrue(mb < 7 ? !over : over, $"{mb} MB case is {length:N0} bytes; boundary {DifatBoundary:N0}");
    }
}
