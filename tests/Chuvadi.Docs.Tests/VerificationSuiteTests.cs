using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chuvadi.Docs.ManualTests;
using Chuvadi.Docs.Word;

namespace Chuvadi.Docs.Tests;

/// <summary>
/// CI-facing test suite. The "Suite_" facts wrap the manual verification groups (one fact
/// per group so failures localize); the rest are focused unit tests for behaviors that are
/// easiest to pin down in isolation.
/// </summary>
public class VerificationSuiteTests
{
    private static string FreshOutDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "chuvadi-docs-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact] public void Suite_MinimalDocx() => MinimalDocxTest.Run(FreshOutDir());
    [Fact] public void Suite_Formatting() => FormattingTest.Run(FreshOutDir());
    [Fact] public void Suite_Tables() => TablesTest.Run(FreshOutDir());
    [Fact] public void Suite_HeaderFooterPage() => HeaderFooterPageTest.Run(FreshOutDir());
    [Fact] public void Suite_ReaderRoundTrip() => ReaderRoundTripTest.Run(FreshOutDir());
    [Fact] public void Suite_Template() => TemplateTest.Run(FreshOutDir());
    [Fact] public void Suite_ProtectionEncryption() => ProtectionEncryptionTest.Run(FreshOutDir());

    // ---- Focused tests ---------------------------------------------------------------

    [Fact]
    public void EncryptedRoundTrip_StreamsAndPaths()
    {
        var dir = FreshOutDir();
        var path = Path.Combine(dir, "enc.docx");
        var doc = new Document();
        doc.AddParagraph("stream secret");
        doc.SaveTo(path, new EncryptionOptions { Password = "p@ss", SpinCount = 50_000 });

        // Stream-based load.
        using var fs = File.OpenRead(path);
        var loaded = Document.Load(fs, "p@ss");
        Assert.Contains("stream secret", loaded.GetText());
    }

    [Fact]
    public void TamperedEncryptedFile_FailsIntegrity()
    {
        var dir = FreshOutDir();
        var path = Path.Combine(dir, "tamper.docx");
        var doc = new Document();
        doc.AddParagraph("integrity matters");
        doc.SaveTo(path, new EncryptionOptions { Password = "p" });

        // Corrupt a wide central region: for any CFB layout this hits the EncryptedPackage
        // ciphertext or container structures (a single byte near EOF can land in harmless
        // sector padding). HMAC verification or container parsing must reject the file.
        var bytes = File.ReadAllBytes(path);
        for (int i = (int)(bytes.Length * 0.4); i < (int)(bytes.Length * 0.6); i++)
            bytes[i] ^= 0xFF;
        var tampered = Path.Combine(dir, "tampered.docx");
        File.WriteAllBytes(tampered, bytes);

        Assert.ThrowsAny<System.Exception>(() => Document.Load(tampered, "p"));
    }

    [Fact]
    public void EmptyDocument_SavesAndLoads()
    {
        var dir = FreshOutDir();
        var path = Path.Combine(dir, "empty.docx");
        new Document().SaveTo(path);
        var loaded = Document.Load(path);
        Assert.NotNull(loaded);
        using var r = DocxReader.Open(path);
        Assert.Equal(string.Empty, r.ExtractText().Trim());
    }

    [Fact]
    public void XmlSpecialCharacters_SurviveRoundTrip()
    {
        var dir = FreshOutDir();
        var path = Path.Combine(dir, "special.docx");
        const string tricky = "a < b && \"c\" > 'd' — ₹ café 中文 \t tab";
        var doc = new Document();
        doc.AddParagraph(tricky);
        doc.SaveTo(path);

        using var r = DocxReader.Open(path);
        var read = r.Paragraphs().First().Text;
        Assert.Equal(tricky, read);
    }

    [Fact]
    public void LeadingTrailingWhitespace_Preserved()
    {
        var dir = FreshOutDir();
        var path = Path.Combine(dir, "ws.docx");
        var doc = new Document();
        doc.AddParagraph()
            .Text("  leading", TextFormat.BoldText)
            .Text("trailing  ");
        doc.SaveTo(path);

        using var r = DocxReader.Open(path);
        Assert.Equal("  leadingtrailing  ", r.Paragraphs().First().Text);
    }

    [Fact]
    public void MaxPartBytes_RejectsOversizedPart()
    {
        var dir = FreshOutDir();
        var path = Path.Combine(dir, "big.docx");
        var doc = new Document();
        for (int i = 0; i < 200; i++)
            doc.AddParagraph(new string('x', 1000));
        doc.SaveTo(path);

        var options = new DocxReaderOptions { MaxPartBytes = 10_000 };
        using var r = DocxReader.Open(path, options: options);
        Assert.ThrowsAny<System.Exception>(() => r.ExtractText());
    }

    [Fact]
    public void NotADocx_ThrowsDocxFormatException()
    {
        var dir = FreshOutDir();
        var path = Path.Combine(dir, "fake.docx");
        File.WriteAllText(path, "this is not a zip at all, just text long enough to sniff");
        Assert.Throws<DocxFormatException>(() => DocxReader.Open(path));
    }

    [Fact]
    public void Template_MissingValueDictionary_Throws()
    {
        Assert.ThrowsAny<System.ArgumentException>(() =>
            DocxTemplate.Fill("in.docx", "out.docx", null!));
    }

    [Fact]
    public void ColumnSpan_ClampsToTableWidth()
    {
        var dir = FreshOutDir();
        var path = Path.Combine(dir, "span.docx");
        var doc = new Document();
        var t = doc.AddTable(2);
        var row = t.AddRow("a", "b");
        row.Cell(0).ColumnSpan = 99; // absurd span must clamp, not corrupt
        doc.SaveTo(path);

        using var r = DocxReader.Open(path);
        var grid = r.Tables().First();
        Assert.Single(grid[0]); // one spanned cell
    }

    [Fact]
    public void HeadingLevel_OutOfRange_Throws()
    {
        var doc = new Document();
        Assert.Throws<System.ArgumentOutOfRangeException>(() => doc.AddHeading("x", 4));
    }

    [Fact]
    public void Protection_RequiresPassword()
    {
        var doc = new Document();
        Assert.Throws<System.ArgumentException>(() => doc.Protect(""));
    }

    [Fact]
    public void GetText_CoversParagraphsAndTables()
    {
        var doc = new Document();
        doc.AddParagraph("para");
        var t = doc.AddTable(1);
        t.AddRow("cell");
        var text = doc.GetText();
        Assert.Contains("para", text);
        Assert.Contains("cell", text);
    }

    [Fact]
    public void Template_ValuesInHeader_AndUnsuppliedKeptVerbatim()
    {
        var dir = FreshOutDir();
        var tpl = Path.Combine(dir, "tpl.docx");
        var outPath = Path.Combine(dir, "out.docx");

        var doc = new Document();
        doc.Header.SetText("Ref: {{Ref}}");
        doc.AddParagraph("Body {{Known}} and {{Unknown}}.");
        doc.SaveTo(tpl);

        DocxTemplate.Fill(tpl, outPath, new Dictionary<string, string>
        {
            ["Ref"] = "R-7",
            ["Known"] = "K",
        });

        using var r = DocxReader.Open(outPath);
        var body = r.ExtractText();
        Assert.Contains("Body K and {{Unknown}}.", body);

        var loaded = Document.Load(outPath);
        // Header text isn't traversed by ExtractText (v1 contract) — verify via raw part.
        using var zip = System.IO.Compression.ZipFile.OpenRead(outPath);
        using var s = zip.GetEntry("word/header1.xml")!.Open();
        using var sr = new StreamReader(s);
        Assert.Contains("Ref: R-7", sr.ReadToEnd());
    }
}
