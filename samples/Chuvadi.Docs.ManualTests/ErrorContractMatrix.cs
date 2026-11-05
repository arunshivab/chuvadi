using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using Chuvadi.Docs.Model;

namespace Chuvadi.Docs.ManualTests;

/// <summary>
/// The malformed-input matrix behind the Docs 2.0 error contract: every reading entry point
/// against every kind of malformed package. Each cell records what the entry point did: "ok", or
/// the exception type (and its InnerException type). Public API only.
/// </summary>
public static class ErrorContractMatrix
{
    public const string Password = "pw";

    /// <summary>A clean document with every part the cases break: styles, numbering, header, footer, table, picture, a placeholder.</summary>
    public static byte[] BaseDocument()
    {
        var d = new Document();
        d.Properties.Created = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        d.Properties.Modified = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        d.AddHeading("Title {{Name}}", 1);
        d.AddParagraph("Body {{Name}}");
        var list = d.Numbering.AddBulletList();
        d.AddListItem("item", list);
        var t = d.AddTable(2);
        t.AddRow("a", "b");
        d.AddParagraph().Image(ImageSpec.Inline(Png1x1, "image/png", 10, 10));
        d.Header.Add(new Paragraph("Header {{Name}}"));
        d.Footer.Add(new Paragraph("Footer"));
        using var ms = new MemoryStream();
        d.SaveTo(ms);
        return ms.ToArray();
    }

    /// <summary>A 1x1 PNG.</summary>
    public static readonly byte[] Png1x1 = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    /// <summary>The malformed cases, each derived from <paramref name="clean"/>.</summary>
    public static IReadOnlyList<(string Name, byte[] Bytes)> Cases(byte[] clean)
    {
        var rnd = new Random(1234);
        var garbage = new byte[2048];
        rnd.NextBytes(garbage);
        return new List<(string, byte[])>
        {
            ("empty input", Array.Empty<byte>()),
            ("garbage bytes", garbage),
            ("truncated zip", clean.Take(clean.Length / 2).ToArray()),
            ("document.xml not well-formed", Replace(clean, "word/document.xml", "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p>")),
            ("styles.xml not well-formed", Replace(clean, "word/styles.xml", "<w:styles><w:style>")),
            ("numbering.xml not well-formed", Replace(clean, "word/numbering.xml", "<w:numbering><w:abstractNum>")),
            ("header not well-formed", Replace(clean, HeaderPart(clean), "<w:hdr><w:p>")),
            ("document.xml.rels not well-formed", Replace(clean, "word/_rels/document.xml.rels", "<Relationships><Relationship")),
            ("[Content_Types].xml not well-formed", Replace(clean, "[Content_Types].xml", "<Types><Default")),
            ("[Content_Types].xml without the main part", Edit(clean, "[Content_Types].xml", s => RemoveOverride(s, "/word/document.xml"))),
            ("[Content_Types].xml missing", Remove(clean, "[Content_Types].xml")),
            ("_rels/.rels missing", Remove(clean, "_rels/.rels")),
            ("_rels/.rels not well-formed", Replace(clean, "_rels/.rels", "<Relationships><Relationship")),
            ("main part missing", Remove(clean, "word/document.xml")),
        };
    }

    /// <summary>Every reading entry point, as an action on the package bytes (written to <paramref name="dir"/> when a path is needed).</summary>
    public static IReadOnlyList<(string Name, Action<byte[]> Run)> EntryPoints(string dir)
    {
        string ToFile(byte[] b) { var p = Path.Combine(dir, "m-" + Guid.NewGuid().ToString("N")[..8] + ".docx"); File.WriteAllBytes(p, b); return p; }
        var opts = new DocxReaderOptions { IncludeHeadersAndFooters = true };
        var data = new TemplateData().Set("Name", "N");
        var values = new Dictionary<string, string> { ["Name"] = "N" };

        void Reader(byte[] b, Action<DocxReader> use)
        {
            using var r = DocxReader.Open(new MemoryStream(b), null, opts);
            use(r);
        }

        void FillStream(byte[] b)
        {
            var output = new MemoryStream();
            try { DocxTemplate.Fill(new MemoryStream(b), output, data); }
            finally { if (output.Length != 0 && !IsZip(output.ToArray())) throw new InvalidOperationException("Fill wrote a partial output."); }
        }

        void FillPath(byte[] b, Action<string, string> fill)
        {
            var input = ToFile(b);
            var output = input + ".out.docx";
            try { fill(input, output); }
            catch
            {
                if (File.Exists(output)) throw new InvalidOperationException("Fill created the output file although it failed.");
                throw;
            }
        }

        return new List<(string, Action<byte[]>)>
        {
            ("Document.Load(stream)", b => Document.Load(new MemoryStream(b))),
            ("Document.Load(stream, password, options, ct)", b => Document.Load(new MemoryStream(b), null, new DocxReaderOptions(), CancellationToken.None)),
            ("Document.Load(path)", b => Document.Load(ToFile(b))),
            ("Document.Load(path, password)", b => Document.Load(ToFile(b), null)),
            ("Document.Load(path, password, options, ct)", b => Document.Load(ToFile(b), null, new DocxReaderOptions(), CancellationToken.None)),
            ("DocxReader.Open(stream)", b => DocxReader.Open(new MemoryStream(b), null, opts).Dispose()),
            ("DocxReader.Open(path)", b => DocxReader.Open(ToFile(b), null, opts).Dispose()),
            ("DocxReader.Paragraphs()", b => Reader(b, r => r.Paragraphs().ToList())),
            ("DocxReader.Tables()", b => Reader(b, r => r.Tables().ToList())),
            ("DocxReader.ExtractText()", b => Reader(b, r => r.ExtractText())),
            ("DocxReader.Images()", b => Reader(b, r => r.Images())),
            ("DocxReader.SaveImages(folder)", b => Reader(b, r => r.SaveImages(Path.Combine(dir, "img-" + Guid.NewGuid().ToString("N")[..8])))),
            ("DocxReader.GetActiveContent()", b => Reader(b, r => r.GetActiveContent())),
            ("DocxReader.HasMacros", b => Reader(b, r => _ = r.HasMacros)),
            ("DocxTemplate.Fill(stream, stream, data)", FillStream),
            ("DocxTemplate.Fill(path, path, data)", b => FillPath(b, (i, o) => DocxTemplate.Fill(i, o, data))),
            ("DocxTemplate.Fill(path, path, values)", b => FillPath(b, (i, o) => DocxTemplate.Fill(i, o, values))),
            ("DocxTemplate.Fill(path, path, text, images, ..., ct)", b => FillPath(b, (i, o) => DocxTemplate.Fill(i, o, values, null, null, null, null, CancellationToken.None))),
            ("DocxTemplate.GetPlaceholders(stream)", b => DocxTemplate.GetPlaceholders(new MemoryStream(b))),
            ("DocxTemplate.GetPlaceholders(path)", b => DocxTemplate.GetPlaceholders(ToFile(b))),
            ("DocxTemplate.CheckTemplate(stream)", b => DocxTemplate.CheckTemplate(new MemoryStream(b))),
            ("DocxTemplate.CheckTemplate(path)", b => DocxTemplate.CheckTemplate(ToFile(b))),
        };
    }

    /// <summary>Runs one cell: "ok", or "ExceptionType(InnerType)".</summary>
    public static string Outcome(Action<byte[]> run, byte[] bytes)
    {
        try { run(bytes); return "ok"; }
        catch (Exception ex)
        {
            var inner = ex.InnerException is null ? "" : "(" + ex.InnerException.GetType().Name + ")";
            return ex.GetType().Name + inner;
        }
    }

    /// <summary>The whole matrix, one line per cell: case | entry point | outcome.</summary>
    public static List<(string Case, string Entry, string Outcome)> Run(string dir)
    {
        var clean = BaseDocument();
        var result = new List<(string, string, string)>();
        foreach (var (entry, run) in EntryPoints(dir))
        {
            result.Add(("clean", entry, Outcome(run, clean)));
            foreach (var (name, bytes) in Cases(clean))
                result.Add((name, entry, Outcome(run, bytes)));
        }
        return result;
    }

    // ---- package editing ----------------------------------------------------------------

    public static bool IsZip(byte[] b) => b.Length > 4 && b[0] == 'P' && b[1] == 'K';

    public static string HeaderPart(byte[] package)
    {
        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        return zip.Entries.Select(e => e.FullName).First(n => n.StartsWith("word/header", StringComparison.Ordinal));
    }

    public static byte[] Edit(byte[] package, string part, Func<string, string> edit)
    {
        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        var e = zip.GetEntry(part) ?? throw new InvalidOperationException("No part " + part);
        using var r = new StreamReader(e.Open());
        return Replace(package, part, edit(r.ReadToEnd()));
    }

    public static byte[] Replace(byte[] package, string part, string content) => Rewrite(package, part, Encoding.UTF8.GetBytes(content));

    public static byte[] Remove(byte[] package, string part) => Rewrite(package, part, null);

    /// <summary>Copies the package entry by entry, replacing (or, with null, dropping) <paramref name="part"/>; entry order is kept.</summary>
    public static byte[] Rewrite(byte[] package, string part, byte[]? content)
    {
        using var src = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        if (src.GetEntry(part) is null) throw new InvalidOperationException("No part " + part);
        var ms = new MemoryStream();
        using (var dst = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var e in src.Entries)
            {
                byte[] bytes;
                if (e.FullName == part)
                {
                    if (content is null) continue;
                    bytes = content;
                }
                else
                {
                    using var s = e.Open();
                    using var copy = new MemoryStream();
                    s.CopyTo(copy);
                    bytes = copy.ToArray();
                }
                var n = dst.CreateEntry(e.FullName);
                using var w = n.Open();
                w.Write(bytes, 0, bytes.Length);
            }
        }
        return ms.ToArray();
    }

    private static string RemoveOverride(string contentTypes, string partName)
    {
        var key = "PartName=\"" + partName + "\"";
        int at = contentTypes.IndexOf(key, StringComparison.Ordinal);
        if (at < 0) throw new InvalidOperationException("No override for " + partName);
        int start = contentTypes.LastIndexOf("<Override", at, StringComparison.Ordinal);
        int end = contentTypes.IndexOf("/>", at, StringComparison.Ordinal) + 2;
        return contentTypes.Remove(start, end - start);
    }
}
