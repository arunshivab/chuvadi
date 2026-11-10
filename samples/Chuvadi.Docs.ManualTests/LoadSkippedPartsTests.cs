using System;
using System.IO;
using System.Linq;
using Chuvadi.Docs.Model;
using static Chuvadi.Docs.ManualTests.Program;

namespace Chuvadi.Docs.ManualTests;

/// <summary>
/// Docs 2.1.0, D-310 (Layout's B11): <see cref="Document.SkippedParts"/> lists every optional part
/// <see cref="Document.Load(Stream, string?)"/> left out (missing, unreadable or not well-formed), once each, with
/// the same types as <see cref="DocxReader.SkippedParts"/>. Load itself behaves as before.
/// </summary>
public static class LoadSkippedPartsTests
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static byte[] Sample()
    {
        var d = new Document();
        d.AddParagraph("Body text").Image(ImageSpec.Inline(ErrorContractMatrix.Png1x1, "image/png", 10, 10));
        d.AddParagraph("Same picture again").Image(ImageSpec.Inline(ErrorContractMatrix.Png1x1, "image/png", 10, 10));
        d.AddParagraph("Second paragraph").Footnote(d, "A footnote");
        d.Header.Add(new Paragraph("Header text"));
        d.Footer.Add(new Paragraph("Footer text"));
        var ms = new MemoryStream();
        d.SaveTo(ms);
        return ms.ToArray();
    }

    private static string Part(byte[] package, string prefix)
    {
        using var z = new System.IO.Compression.ZipArchive(new MemoryStream(package));
        return z.Entries.First(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal)).FullName;
    }

    private static string List(Document d) => string.Join("; ", d.SkippedParts);

    public static void Run(string outDir)
    {
        var clean = Sample();
        var header = Part(clean, "word/header");
        var media = Part(clean, "word/media/");

        Check("a clean document and a new document list nothing", () =>
        {
            AssertTrue(Document.Load(new MemoryStream(clean)).SkippedParts.Count == 0, "clean");
            AssertTrue(new Document().SkippedParts.Count == 0, "new");
        });

        Check("a header that is not well-formed XML loads blank, as before, and is listed as NotWellFormed with its XmlException", () =>
        {
            var d = Document.Load(new MemoryStream(ErrorContractMatrix.Replace(clean, header, "<w:hdr><w:p>")));
            AssertTrue(d.Header.Count == 0, "header blank: " + d.Header.Count);
            AssertTrue(d.GetText().Contains("Body text"), "body: " + d.GetText());
            var s = d.SkippedParts.Single();
            AssertTrue(s.PartName == "/" + header && s.Reason == DocxSkippedPartReason.NotWellFormed && s.Error is System.Xml.XmlException, s.ToString());
        });

        Check("a missing header part (still referenced) is listed as Missing with no error, and the same as DocxReader lists", () =>
        {
            var bad = ErrorContractMatrix.Remove(clean, header);
            var d = Document.Load(new MemoryStream(bad));
            var s = d.SkippedParts.Single();
            AssertTrue(s.PartName == "/" + header && s.Reason == DocxSkippedPartReason.Missing && s.Error is null, s.ToString());
            using var r = DocxReader.Open(new MemoryStream(bad));
            r.ExtractText();
            AssertTrue(r.SkippedParts.Single().PartName == s.PartName && r.SkippedParts.Single().Reason == s.Reason, string.Join("; ", r.SkippedParts));
        });

        Check("a missing picture used twice is listed once as Missing; the paragraphs still load", () =>
        {
            var d = Document.Load(new MemoryStream(ErrorContractMatrix.Remove(clean, media)));
            AssertTrue(d.GetText().Contains("Body text"), "body");
            AssertTrue(d.SkippedParts.Count == 1 && d.SkippedParts[0].PartName == "/" + media && d.SkippedParts[0].Reason == DocxSkippedPartReason.Missing, List(d));
        });

        Check("settings, styles and footnotes: not well-formed settings and footnotes are listed; a missing styles part is listed as Missing", () =>
        {
            var bad = ErrorContractMatrix.Replace(clean, "word/settings.xml", $"<w:settings xmlns:w=\"{W}\"><w:compat>");
            bad = ErrorContractMatrix.Replace(bad, "word/footnotes.xml", $"<w:footnotes xmlns:w=\"{W}\"><w:footnote>");
            bad = ErrorContractMatrix.Remove(bad, "word/styles.xml");
            var d = Document.Load(new MemoryStream(bad));
            AssertTrue(d.GetText().Contains("Body text"), "body");
            var got = string.Join(",", d.SkippedParts.Select(s => s.PartName + ":" + s.Reason));
            AssertTrue(got == "/word/styles.xml:Missing,/word/settings.xml:NotWellFormed,/word/footnotes.xml:NotWellFormed", got);
        });

        Check("edits do not change the list, and saving does not use it", () =>
        {
            var d = Document.Load(new MemoryStream(ErrorContractMatrix.Replace(clean, header, "<w:hdr><w:p>")));
            int before = d.SkippedParts.Count;
            d.AddParagraph("edited");
            var saved = new MemoryStream();
            d.SaveTo(saved);
            AssertTrue(d.SkippedParts.Count == before && Document.Load(new MemoryStream(saved.ToArray())).SkippedParts.Count == 0, "unchanged by edits; the saved file has no skips");
        });

        Check("the main part is never listed: a broken body is still a DocxFormatException", () =>
        {
            var bad = ErrorContractMatrix.Replace(clean, "word/document.xml", $"<w:document xmlns:w=\"{W}\"><w:body><w:p>");
            AssertThrows<DocxFormatException>(() => Document.Load(new MemoryStream(bad)), "Load");
        });
    }
}
