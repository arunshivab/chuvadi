using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Chuvadi.Docs.Model;
using static Chuvadi.Docs.ManualTests.Program;

namespace Chuvadi.Docs.ManualTests;

/// <summary>
/// Docs 2.3.0, S1 (D-371): a new document starts with Word's paragraph spacing for a new document
/// (8pt after, 1.08 lines), so fonts whose marks reach below their line (Latha's vowel signs) are not cut by the
/// next line in Word. One line gets the old empty defaults back; loaded documents keep the file's own.
/// </summary>
public static class DefaultSpacingTests
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string WordLine = "<w:pPrDefault><w:pPr><w:spacing w:after=\"160\" w:line=\"259\" w:lineRule=\"auto\" /></w:pPr></w:pPrDefault>";

    public static void Run(string outDir)
    {
        Check("a new document writes Word's new-document spacing: w:after=160, w:line=259, w:lineRule=auto in w:pPrDefault", () =>
        {
            var d = new Document();
            d.AddParagraph("தமிழ் எழுத்துரு சோதனை").Configure(f => { });
            var bytes = Save(d);
            File.WriteAllBytes(Path.Combine(outDir, "default-spacing.docx"), bytes);
            var styles = Part(bytes, "word/styles.xml");
            AssertTrue(styles.Contains(WordLine), "pPrDefault: " + Between(styles, "<w:pPrDefault", "</w:pPrDefault>"));
            var pf = d.Defaults.ParagraphFormat;
            AssertTrue(pf.SpaceAfterPt == 8 && pf.LineSpacing is { } ls && Math.Abs(ls.Value - 259 / 240.0) < 1e-9, "the model values");
        });

        Check("StandardParagraphFormat is a fresh instance each time; changing a document's defaults changes no other document", () =>
        {
            var a = DocumentDefaults.StandardParagraphFormat;
            var b = DocumentDefaults.StandardParagraphFormat;
            AssertTrue(!ReferenceEquals(a, b) && a.SpaceAfterPt == 8, "fresh");
            var d1 = new Document();
            d1.Defaults.ParagraphFormat.SpaceAfterPt = 20;
            AssertTrue(new Document().Defaults.ParagraphFormat.SpaceAfterPt == 8, "not shared");
        });

        Check("the way back is one line: Defaults.ParagraphFormat = new ParagraphFormat() writes the empty w:pPrDefault of 2.2.0; null is rejected", () =>
        {
            var d = new Document();
            d.Defaults.ParagraphFormat = new ParagraphFormat();
            d.AddParagraph("x");
            var styles = Part(Save(d), "word/styles.xml");
            AssertTrue(styles.Contains("<w:pPrDefault />"), "empty: " + Between(styles, "<w:pPrDefault", "</w:docDefaults>"));
            AssertThrows<ArgumentNullException>(() => d.Defaults.ParagraphFormat = null!, "null");
        });

        Check("DocumentPreset: new Document() is Word; Minimal writes the empty w:pPrDefault, has no Header or Footer style and is otherwise the same document; an undefined value throws", () =>
        {
            AssertTrue(Part(Save(new Document(DocumentPreset.Word)), "word/styles.xml") == Part(Save(new Document()), "word/styles.xml"), "Word = the default");
            var minimal = new Document(DocumentPreset.Minimal);
            AssertTrue(minimal.Defaults.ParagraphFormat.SpaceAfterPt is null && minimal.Defaults.ParagraphFormat.LineSpacing is null, "Minimal: no spacing in the model");
            AssertTrue(minimal.Defaults.HeaderParagraphStyleId is null && minimal.Defaults.FooterParagraphStyleId is null
                && !minimal.Styles.Contains("Header") && !minimal.Styles.Contains("Footer"), "Minimal: no Header or Footer style (D-374)");
            var byLine = new Document();
            byLine.Defaults.ParagraphFormat = new ParagraphFormat();
            byLine.Styles.Remove("Header");
            byLine.Styles.Remove("Footer");
            string a = Part(Save(minimal), "word/styles.xml"), b = Part(Save(byLine), "word/styles.xml");
            AssertTrue(a == b && a.Contains("<w:pPrDefault />"), "Minimal = Word with the spacing way back and without the Header and Footer styles");
            AssertTrue(minimal.Styles.Count + 2 == new Document().Styles.Count && minimal.Compatibility.Mode == new Document().Compatibility.Mode, "the same styles but Header and Footer, the same compatibility mode");
            AssertTrue(Part(Save(minimal), "word/settings.xml") == Part(Save(new Document()), "word/settings.xml"), "same settings.xml");
            AssertThrows<ArgumentOutOfRangeException>(() => new Document((DocumentPreset)7), "undefined");
            File.WriteAllBytes(Path.Combine(outDir, "default-spacing-minimal.docx"), Save(minimal));
        });

        Check("the effective paragraph format of a new document's paragraph has the spacing; Word's note text keeps its own single line and 0pt after", () =>
        {
            var d = new Document();
            var p = d.AddParagraph("Body");
            var eff = d.GetEffectiveParagraphFormat(p);
            AssertTrue(eff.SpaceAfterPt == 8 && eff.LineSpacing is { } ls && Math.Abs(ls.Value - 259 / 240.0) < 1e-9, "body");
            d.AddParagraph("With a note").Footnote(d, "Note text");
            var note = d.Footnotes.First().Paragraphs[0];
            var en = d.GetEffectiveParagraphFormat(note);
            AssertTrue(en.SpaceAfterPt == 0 && en.LineSpacing is { } nl && nl.Value == 1, "note text: " + en.SpaceAfterPt + " " + en.LineSpacing?.Value);
        });

        Check("loaded documents keep the file's defaults: none stays none, Word's stay Word's, a file without styles has none (Load and FormattedParagraphs)", () =>
        {
            var none = Package($"<w:styles xmlns:w=\"{W}\"><w:docDefaults><w:rPrDefault/><w:pPrDefault/></w:docDefaults></w:styles>");
            var loaded = Document.Load(new MemoryStream(none));
            AssertTrue(loaded.Defaults.ParagraphFormat.SpaceAfterPt is null && loaded.Defaults.ParagraphFormat.LineSpacing is null, "none loads as none");
            AssertTrue(Part(Save(loaded), "word/styles.xml").Contains("<w:pPrDefault />"), "none saves as none");

            var word = Package($"<w:styles xmlns:w=\"{W}\"><w:docDefaults><w:rPrDefault/>{WordLine.Replace(" />", "/>")}</w:docDefaults></w:styles>");
            AssertTrue(Part(Save(Document.Load(new MemoryStream(word))), "word/styles.xml").Contains(WordLine), "Word's defaults kept");

            var noStyles = Package(null);
            var bare = Document.Load(new MemoryStream(noStyles));
            AssertTrue(bare.Defaults.ParagraphFormat.SpaceAfterPt is null, "no styles part: no spacing");
            using var r = DocxReader.Open(new MemoryStream(noStyles));
            var item = r.FormattedParagraphs().First();
            AssertTrue(item.ParagraphFormat.SpaceAfterPt != 8, "FormattedParagraphs: no new-document spacing: " + item.ParagraphFormat.SpaceAfterPt);
            AssertTrue(FormattedParagraphsTests.Describe(item.ParagraphFormat) == FormattedParagraphsTests.Describe(bare.GetEffectiveParagraphFormat(bare.Blocks.OfType<Paragraph>().First())), "the same as Load");
        });
    }

    private static byte[] Save(Document d)
    {
        var ms = new MemoryStream();
        d.SaveTo(ms);
        return ms.ToArray();
    }

    private static string Part(byte[] package, string name)
    {
        using var z = new ZipArchive(new MemoryStream(package));
        using var r = new StreamReader(z.GetEntry(name)!.Open());
        return r.ReadToEnd();
    }

    private static string Between(string s, string start, string end)
    {
        int i = s.IndexOf(start, StringComparison.Ordinal);
        if (i < 0) return "(none)";
        int j = s.IndexOf(end, i, StringComparison.Ordinal);
        return j < 0 ? s[i..] : s[i..(j + end.Length)];
    }

    private static byte[] Package(string? styles)
    {
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string content)
            {
                using var w = new StreamWriter(z.CreateEntry(name).Open(), new UTF8Encoding(false));
                w.Write(content);
            }
            Add("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>"
                + (styles is null ? "" : "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>") + "</Types>");
            Add("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            Add("word/document.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"{W}\"><w:body><w:p><w:r><w:t>Body</w:t></w:r></w:p></w:body></w:document>");
            if (styles is not null)
            {
                Add("word/_rels/document.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rIdS\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
                Add("word/styles.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + styles);
            }
        }
        return ms.ToArray();
    }
}
