using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using Chuvadi.Docs.Model;
using static Chuvadi.Docs.ManualTests.Program;

namespace Chuvadi.Docs.ManualTests;

/// <summary>
/// D-194 (Docs 2.0): DocxReader reads a header or footer that is not well-formed XML as blank,
/// like Document.Load (5b, B′); a broken body still throws; GetActiveContent and the template
/// methods stay strict; every part the reader skips is listed in DocxReader.SkippedParts (D).
/// A corrupt main part is a DocxFormatException at every entry point (5e).
/// </summary>
public static class SkippedPartsTests
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static void Run(string outDir)
    {
        var clean = Sample();
        var header = Part(clean, "word/header");
        var footer = Part(clean, "word/footer");
        var media = Part(clean, "word/media/");

        Check("5b: a header that is not well-formed XML reads as blank in ExtractText, Paragraphs, Tables and Images, like Load, and is listed once", () =>
        {
            var bad = ErrorContractMatrix.Replace(clean, header, "<w:hdr><w:p>");
            AssertTrue(Document.Load(new MemoryStream(bad)).Header.Count == 0, "Load: blank header");
            using var r = DocxReader.Open(new MemoryStream(bad));
            var text = r.ExtractText();
            AssertTrue(text.Contains("Body text") && text.Contains("Footer text") && !text.Contains("Header text"), "ExtractText: " + text);
            var paragraphs = r.Paragraphs().Select(p => p.Text).ToList();
            AssertTrue(paragraphs.Contains("Body text") && paragraphs.Contains("Footer text"), "Paragraphs");
            AssertTrue(r.Tables().Count() == 1, "Tables: the body table");
            AssertTrue(r.Images().Count == 0, "Images: the header's picture goes with the header");
            AssertTrue(r.SkippedParts.Count == 1, "listed once, however many readers met it: " + string.Join("; ", r.SkippedParts));
            var s = r.SkippedParts[0];
            AssertTrue(s.PartName == "/" + header && s.Reason == DocxSkippedPartReason.NotWellFormed && s.Error is System.Xml.XmlException, s.ToString());
        });

        Check("5b: all or nothing: a header that breaks after good paragraphs contributes none of them", () =>
        {
            var bad = ErrorContractMatrix.Replace(clean, header,
                $"<w:hdr xmlns:w=\"{W}\"><w:p><w:r><w:t>First good</w:t></w:r></w:p><w:p><w:r><w:t>Second good</w:t></w:r></w:p><w:p>");
            using var r = DocxReader.Open(new MemoryStream(bad));
            var paragraphs = r.Paragraphs().Select(p => p.Text).ToList();
            AssertTrue(!paragraphs.Any(p => p.Contains("good")), "Paragraphs: " + string.Join("|", paragraphs));
            AssertTrue(!r.ExtractText().Contains("good"), "ExtractText");
            AssertTrue(r.SkippedParts.Single().Reason == DocxSkippedPartReason.NotWellFormed, "listed");
        });

        Check("5b: a footer is treated the same way", () =>
        {
            var bad = ErrorContractMatrix.Replace(clean, footer, "<w:ftr><w:p>");
            using var r = DocxReader.Open(new MemoryStream(bad));
            var text = r.ExtractText();
            AssertTrue(text.Contains("Body text") && text.Contains("Header text") && !text.Contains("Footer text"), text);
            AssertTrue(r.SkippedParts.Single().PartName == "/" + footer, string.Join("; ", r.SkippedParts));
        });

        Check("5b: a broken body still throws DocxFormatException and is never listed as skipped", () =>
        {
            var bad = ErrorContractMatrix.Replace(clean, "word/document.xml", $"<w:document xmlns:w=\"{W}\"><w:body><w:p>");
            using var r = DocxReader.Open(new MemoryStream(bad));
            AssertThrows<DocxFormatException>(() => r.ExtractText(), "ExtractText");
            AssertThrows<DocxFormatException>(() => r.Paragraphs().ToList(), "Paragraphs");
            AssertThrows<DocxFormatException>(() => r.Tables().ToList(), "Tables");
            AssertTrue(r.SkippedParts.Count == 0, "nothing listed");
        });

        Check("5b: GetActiveContent and the template methods stay strict on a header that is not well-formed", () =>
        {
            var bad = ErrorContractMatrix.Replace(clean, header, "<w:hdr><w:p>");
            using var r = DocxReader.Open(new MemoryStream(bad));
            AssertThrows<DocxFormatException>(() => r.GetActiveContent(), "GetActiveContent");
            AssertThrows<DocxFormatException>(() => DocxTemplate.GetPlaceholders(new MemoryStream(bad)), "GetPlaceholders");
            AssertThrows<DocxFormatException>(() => DocxTemplate.CheckTemplate(new MemoryStream(bad)), "CheckTemplate");
            var output = new MemoryStream();
            AssertThrows<DocxFormatException>(() => DocxTemplate.Fill(new MemoryStream(bad), output, new TemplateData()), "Fill");
            AssertTrue(output.Length == 0, "Fill wrote nothing");
        });

        Check("D: skips that were silent before are listed: a missing header, a header whose zip entry cannot be opened, a missing picture", () =>
        {
            using (var r = DocxReader.Open(new MemoryStream(ErrorContractMatrix.Remove(clean, header))))
            {
                AssertTrue(r.ExtractText().Contains("Body text"), "reads");
                AssertTrue(r.SkippedParts.Single() is { Reason: DocxSkippedPartReason.Missing, Error: null } m && m.PartName == "/" + header, string.Join("; ", r.SkippedParts));
            }
            using (var r = DocxReader.Open(new MemoryStream(CorruptLocalHeader(clean, header))))
            {
                AssertTrue(!r.ExtractText().Contains("Header text"), "blank, as before and as Load");
                AssertTrue(r.SkippedParts.Single() is { Reason: DocxSkippedPartReason.Unreadable, Error: InvalidDataException }, string.Join("; ", r.SkippedParts));
            }
            using (var r = DocxReader.Open(new MemoryStream(ErrorContractMatrix.Remove(clean, media))))
            {
                AssertTrue(r.Images().Count == 0, "picture left out, as before");
                AssertTrue(r.SkippedParts.Single() is { Reason: DocxSkippedPartReason.Missing } p && p.PartName == "/" + media, string.Join("; ", r.SkippedParts));
            }
            using (var r = DocxReader.Open(new MemoryStream(ErrorContractMatrix.Remove(clean, header))))
            {
                r.GetActiveContent();
                AssertTrue(r.SkippedParts.Single().Reason == DocxSkippedPartReason.Missing, "GetActiveContent's skip is listed too");
            }
        });

        Check("D: a clean document lists nothing; IncludeHeadersAndFooters = false never reads, so never lists, a broken header", () =>
        {
            using (var r = DocxReader.Open(new MemoryStream(clean)))
            {
                r.ExtractText(); r.Paragraphs().ToList(); r.Tables().ToList(); r.Images(); r.GetActiveContent();
                AssertTrue(r.SkippedParts.Count == 0, string.Join("; ", r.SkippedParts));
            }
            var bad = ErrorContractMatrix.Replace(clean, header, "<w:hdr><w:p>");
            using (var r = DocxReader.Open(new MemoryStream(bad), null, new DocxReaderOptions { IncludeHeadersAndFooters = false }))
            {
                AssertTrue(r.ExtractText().Contains("Body text"), "reads");
                AssertTrue(r.SkippedParts.Count == 0, string.Join("; ", r.SkippedParts));
            }
        });

        Check("unchanged: corrupt compressed data in a header still throws, as Load does; limits and cancellation are never skipped", () =>
        {
            var corrupt = CorruptData(clean, header);
            AssertThrows<DocxFormatException>(() => Document.Load(new MemoryStream(corrupt)), "Load");
            using (var r = DocxReader.Open(new MemoryStream(corrupt)))
                AssertThrows<DocxFormatException>(() => r.ExtractText(), "ExtractText");
            var deep = ErrorContractMatrix.Replace(clean, header,
                $"<w:hdr xmlns:w=\"{W}\">" + string.Concat(Enumerable.Repeat("<w:p>", 40)) + string.Concat(Enumerable.Repeat("</w:p>", 40)) + "</w:hdr>");
            using (var r = DocxReader.Open(new MemoryStream(deep), null, new DocxReaderOptions { MaxXmlDepth = 10 }))
            {
                var ex = Catch(() => r.ExtractText());
                AssertTrue(ex is DocxSecurityLimitException, "depth limit in a header: " + ex?.GetType().Name);
                AssertTrue(r.SkippedParts.Count == 0, "a limit is not a skip");
            }
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            using (var r = DocxReader.Open(new MemoryStream(ErrorContractMatrix.Replace(clean, header, "<w:hdr><w:p>"))))
                AssertTrue(Catch(() => r.ExtractText(cts.Token)) is OperationCanceledException, "cancellation");
        });

        Check("5e + D-206: damaged compressed data in the main part is a DocxFormatException at every entry point; Open decompresses the main part once", () =>
        {
            var bad = CorruptData(clean, "word/document.xml");
            const string Message = "The main document part '/word/document.xml' is corrupt and cannot be read.";
            var ex = Catch(() => DocxReader.Open(new MemoryStream(bad)).Dispose());
            AssertTrue(ex is DocxFormatException { InnerException: InvalidDataException } && ex.Message == Message, "Open: " + ex?.GetType().Name + ": " + ex?.Message);
            var dir = Path.Combine(outDir, "skipped");
            Directory.CreateDirectory(dir);
            foreach (var (entry, run) in ErrorContractMatrix.EntryPoints(dir))
            {
                var e = Catch(() => run(bad));
                AssertTrue(e is DocxFormatException and not DocxSecurityLimitException, $"{entry}: {e?.GetType().Name}: {e?.Message}");
            }
        });

        Check("5e: a main part whose zip entry cannot be opened is a DocxFormatException at every entry point, with the original inside", () =>
        {
            var bad = CorruptLocalHeader(clean, "word/document.xml");
            const string Message = "The main document part '/word/document.xml' is corrupt and cannot be read.";
            var dir = Path.Combine(outDir, "skipped");
            Directory.CreateDirectory(dir);   // this group must not depend on another group's folder
            foreach (var (entry, run) in ErrorContractMatrix.EntryPoints(dir))
            {
                var ex = Catch(() => run(bad));
                AssertTrue(ex is not null && ex.GetType() == typeof(DocxFormatException) && ex.Message == Message && ex.InnerException is InvalidDataException,
                    $"{entry}: {ex?.GetType().Name}: {ex?.Message}");
            }
        });
    }

    // ---- helpers ------------------------------------------------------------------------

    /// <summary>Body text and a table, a header with text and a picture, a footer.</summary>
    private static byte[] Sample()
    {
        var d = new Document();
        d.AddParagraph("Body text");
        d.AddTable(1).AddRow("cell");
        d.Header.Add(new Paragraph("Header text"));
        d.Header.Add(new Paragraph().Image(ImageSpec.Inline(ErrorContractMatrix.Png1x1, "image/png", 10, 10)));
        d.Footer.Add(new Paragraph("Footer text"));
        var ms = new MemoryStream();
        d.SaveTo(ms);
        return ms.ToArray();
    }

    private static string Part(byte[] package, string prefix)
    {
        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        return zip.Entries.Select(e => e.FullName).First(n => n.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static Exception? Catch(Action action)
    {
        try { action(); return null; }
        catch (Exception ex) { return ex; }
    }

    private static int LocalHeader(byte[] package, string entryName)
    {
        var name = Encoding.ASCII.GetBytes(entryName);
        for (int i = 0; i + 30 < package.Length; i++)
        {
            if (package[i] != 0x50 || package[i + 1] != 0x4B || package[i + 2] != 3 || package[i + 3] != 4) continue;
            int n = BitConverter.ToUInt16(package, i + 26);
            if (n == name.Length && package.AsSpan(i + 30, n).SequenceEqual(name)) return i;
        }
        throw new InvalidOperationException("No local header for " + entryName);
    }

    /// <summary>Breaks the entry's local-header signature (the central directory still lists it).</summary>
    private static byte[] CorruptLocalHeader(byte[] package, string entryName)
    {
        var bad = (byte[])package.Clone();
        bad[LocalHeader(bad, entryName)] = (byte)'X';
        return bad;
    }

    /// <summary>Damages the first bytes of the entry's compressed data; the entry itself still opens.</summary>
    private static byte[] CorruptData(byte[] package, string entryName)
    {
        var bad = (byte[])package.Clone();
        int at = LocalHeader(bad, entryName);
        int start = at + 30 + BitConverter.ToUInt16(bad, at + 26) + BitConverter.ToUInt16(bad, at + 28);
        for (int k = 0; k < 8; k++) bad[start + k] ^= 0xFF;
        return bad;
    }
}
