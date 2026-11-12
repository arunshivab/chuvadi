using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Chuvadi.Docs.Internal;
using Chuvadi.Docs.Model;
using static Chuvadi.Docs.ManualTests.Program;

namespace Chuvadi.Docs.ManualTests;

/// <summary>
/// Docs 2.2.0, N4 (DOC-14 B, D-343): a run built in code with a complex-script font and complex-script text gets
/// <c>&lt;w:cs/&gt;</c> on save when <see cref="TextFormat.ComplexScript"/> is unset, because Word shows the
/// complex-script font only for runs marked so (Arun's Word check, 11 Nov 2026). Loaded documents never change.
/// </summary>
public static class ComplexScriptFlagTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string Tamil = "தமிழ் எழுத்துரு சோதனை";

    public static void Run(string outDir)
    {
        Check("a built run with a complex-script font and Tamil text gets <w:cs/>; the same run with English text does not", () =>
        {
            var d = new Document();
            d.AddParagraph("").Text(Tamil, new TextFormat { Font = "Nirmala UI", ComplexScriptFont = "Latha", SizePt = 24 });
            d.AddParagraph("").Text("English text", new TextFormat { Font = "Nirmala UI", ComplexScriptFont = "Latha" });
            var bytes = Save(d);
            File.WriteAllBytes(Path.Combine(outDir, "complex-script-flag.docx"), bytes);
            AssertTrue(Flag(bytes, Tamil) == "cs", "Tamil: " + Flag(bytes, Tamil));
            AssertTrue(Flag(bytes, "English text") == "none", "English: " + Flag(bytes, "English text"));
        });

        Check("a mixed Tamil and English run is flagged as a whole; explicit ComplexScript true or false wins", () =>
        {
            var d = new Document();
            d.AddParagraph("").Text("Name: " + Tamil, new TextFormat { ComplexScriptFont = "Latha" });
            d.AddParagraph("").Text("off " + Tamil, new TextFormat { ComplexScriptFont = "Latha", ComplexScript = false });
            d.AddParagraph("").Text("on English", new TextFormat { ComplexScriptFont = "Latha", ComplexScript = true });
            var bytes = Save(d);
            AssertTrue(Flag(bytes, "Name: " + Tamil) == "cs", "mixed");
            AssertTrue(Flag(bytes, "off " + Tamil) == "cs=0", "explicit false is written as before: " + Flag(bytes, "off " + Tamil));
            AssertTrue(Flag(bytes, "on English") == "cs", "explicit true");
        });

        Check("no complex-script font, or FontForAllScripts with English text: no flag; FontForAllScripts with Tamil text: flagged", () =>
        {
            var d = new Document();
            d.AddParagraph("").Text(Tamil + " 1", new TextFormat { Font = "Latha" });
            d.AddParagraph("").Text("All scripts English", new TextFormat { FontForAllScripts = "Arial" });
            d.AddParagraph("").Text(Tamil + " 2", new TextFormat { FontForAllScripts = "Latha" });
            var bytes = Save(d);
            AssertTrue(Flag(bytes, Tamil + " 1") == "none", "no complex-script font");
            AssertTrue(Flag(bytes, "All scripts English") == "none", "FontForAllScripts, English");
            AssertTrue(Flag(bytes, Tamil + " 2") == "cs", "FontForAllScripts, Tamil");
        });

        Check("loaded documents never change: a Word-style run with w:rFonts w:cs and Tamil text but no w:cs saves without it; a flagged run keeps one flag", () =>
        {
            var built = new Document();
            built.AddParagraph("").Text(Tamil, new TextFormat { ComplexScriptFont = "Latha" });
            var once = Save(built);
            var again = Save(Document.Load(new MemoryStream(once)));
            AssertTrue(Flag(again, Tamil) == "cs", "kept once on load and save");

            var raw = Package($"<w:p><w:r><w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\" w:cs=\"Latha\"/></w:rPr><w:t>{Tamil}</w:t></w:r></w:p>");
            var saved = Save(Document.Load(new MemoryStream(raw)));
            AssertTrue(Flag(saved, Tamil) == "none", "a loaded run is saved as it was: " + Flag(saved, Tamil));
        });

        Check("the block list: sorted, no overlaps; Tamil, Devanagari, Malayalam, Arabic, Hebrew, Thai and Tamil Supplement are in; Latin, Greek, Cyrillic, CJK and emoji are out", () =>
        {
            var blocks = ComplexScriptText.Blocks;
            for (int i = 1; i < blocks.Length; i++)
                AssertTrue(blocks[i].First > blocks[i - 1].Last, "order at " + blocks[i].Name);
            foreach (var s in new[] { "த", "क", "മ", "ع", "ש", "ก", char.ConvertFromUtf32(0x11FC0) })
                AssertTrue(ComplexScriptText.Contains("x" + s), "in: U+" + char.ConvertToUtf32(s, 0).ToString("X4"));
            foreach (var s in new[] { "Hello", "Ελληνικά", "Кириллица", "中文", "日本語", "😀", "" })
                AssertTrue(!ComplexScriptText.Contains(s), "out: " + s);
            AssertTrue(!ComplexScriptText.Contains(null), "null");
        });
    }

    private static byte[] Save(Document d)
    {
        var ms = new MemoryStream();
        d.SaveTo(ms);
        return ms.ToArray();
    }

    /// <summary>The w:cs state of the run holding <paramref name="text"/>: "cs", "cs=0" or "none".</summary>
    private static string Flag(byte[] package, string text)
    {
        using var z = new ZipArchive(new MemoryStream(package));
        using var s = z.GetEntry("word/document.xml")!.Open();
        var x = XDocument.Load(s);
        var run = x.Descendants(W + "r").First(r => string.Concat(r.Elements(W + "t").Select(t => t.Value)) == text);
        var cs = run.Element(W + "rPr")?.Element(W + "cs");
        if (cs is null) return "none";
        var val = (string?)cs.Attribute(W + "val");
        return val is null or "1" or "true" or "on" ? "cs" : "cs=0";
    }

    private static byte[] Package(string body)
    {
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string content)
            {
                using var w = new StreamWriter(z.CreateEntry(name).Open(), new UTF8Encoding(false));
                w.Write(content);
            }
            Add("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
            Add("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            Add("word/document.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"{W.NamespaceName}\"><w:body>{body}</w:body></w:document>");
        }
        return ms.ToArray();
    }
}
