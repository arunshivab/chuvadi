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
/// Docs 2.1.0 limits (D-281, D-284): <see cref="DocxReaderOptions.MaxElements"/> counted per operation,
/// the <see cref="DocxReaderOptions.ForUntrustedInput"/> preset, and the token given to
/// <see cref="DocxReader.Open(Stream, string?, DocxReaderOptions?, CancellationToken)"/> covering the reader's
/// later reads (both tokens on the overloads that take their own).
/// </summary>
public static class UntrustedInputTests
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static void Run(string outDir)
    {
        var dir = Path.Combine(outDir, "untrusted");
        Directory.CreateDirectory(dir);
        Preset();
        ElementLimit(dir);
        ElementBomb();
        OpenToken(dir);
    }

    // ---- The preset -----------------------------------------------------------------

    private static void Preset()
    {
        Check("ForUntrustedInput: the D-284 values, a new instance on every call; MaxElements is off by default and in CreateUnrestricted", () =>
        {
            var p = DocxReaderOptions.ForUntrustedInput();
            AssertTrue(p.MaxPackageBytes == 50L * 1024 * 1024, "package 50 MB");
            AssertTrue(p.MaxTotalUncompressedBytes == 256L * 1024 * 1024, "total 256 MB");
            AssertTrue(p.MaxPartBytes == 64L * 1024 * 1024, "part 64 MB");
            AssertTrue(p.MaxCompressionRatio == 200, "ratio 200");
            AssertTrue(p.MaxEntries == 10_000, "entries 10,000");
            AssertTrue(p.MaxXmlDepth == 128, "depth 128");
            AssertTrue(p.MaxSpinCount == 500_000, "spin count 500,000");
            AssertTrue(p.MaxElements == 1_000_000, "elements 1,000,000");
            AssertTrue(!ReferenceEquals(p, DocxReaderOptions.ForUntrustedInput()), "a new instance on every call");
            AssertTrue(new DocxReaderOptions().MaxElements is null, "MaxElements off by default");
            AssertTrue(DocxReaderOptions.CreateUnrestricted().MaxElements is null, "off in CreateUnrestricted");
        });

        Check("ForUntrustedInput: a long Word-like document (5,000 paragraphs with styles and run formatting) loads, reads and fills", () =>
        {
            var bytes = WordLike(5000);
            var p = DocxReaderOptions.ForUntrustedInput();
            var d = Document.Load(new MemoryStream(bytes), null, p);
            AssertTrue(d.Blocks.Count == 5000, "5,000 paragraphs: " + d.Blocks.Count);
            using var r = DocxReader.Open(new MemoryStream(bytes), null, p);
            AssertTrue(r.Paragraphs().Count() == 5000, "streams 5,000 paragraphs");
            var output = new MemoryStream();
            DocxTemplate.Fill(new MemoryStream(bytes), output, new TemplateData(), new TemplateOptions { ReadOptions = p });
            AssertTrue(output.Length > 0, "fills");
        });
    }

    // ---- MaxElements ----------------------------------------------------------------

    private static void ElementLimit(string dir)
    {
        var plain = Package(Body(50));                 // body only: no styles, numbering or settings parts
        var withHeader = WithHeader(50, 40);

        Check("MaxElements in Document.Load: exactly at the limit it loads, one below it throws DocxSecurityLimitException (Elements, Allowed, part name)", () =>
        {
            int min = MinimalLimit(l => Document.Load(new MemoryStream(plain), null, new DocxReaderOptions { MaxElements = l }));
            AssertTrue(min == CountElements(plain, "word/document.xml"), $"the limit that loads ({min}) is the main part's element count ({CountElements(plain, "word/document.xml")})");
            var ex = Catch(() => Document.Load(new MemoryStream(plain), null, new DocxReaderOptions { MaxElements = min - 1 }));
            AssertTrue(ex is DocxSecurityLimitException { Limit: DocxLimit.Elements } s && s.Allowed == min - 1 && s.Actual == min && s.PartName == "/word/document.xml",
                "the Elements limit, unwrapped: " + ex?.GetType().Name + " " + ex?.Message);
            AssertContains(ex!.Message, $"more than {min - 1:N0} XML elements", "message");
        });

        Check("MaxElements counts every part of one Load together: a header's elements add to the body's", () =>
        {
            int body = MinimalLimit(l => Document.Load(new MemoryStream(Package(Body(50))), null, new DocxReaderOptions { MaxElements = l }));
            int both = MinimalLimit(l => Document.Load(new MemoryStream(withHeader), null, new DocxReaderOptions { MaxElements = l }));
            AssertTrue(both >= body + CountElements(withHeader, "word/header1.xml"), $"body {body}, with the header {both}");
        });

        Check("MaxElements in the template methods (Fill, GetPlaceholders, CheckTemplate): at the limit they work; one below they throw, and Fill writes nothing", () =>
        {
            int min = MinimalLimit(l => DocxTemplate.Fill(new MemoryStream(withHeader), new MemoryStream(), new TemplateData(),
                new TemplateOptions { ReadOptions = new DocxReaderOptions { MaxElements = l } }));
            var output = new MemoryStream();
            var ex = Catch(() => DocxTemplate.Fill(new MemoryStream(withHeader), output, new TemplateData(),
                new TemplateOptions { ReadOptions = new DocxReaderOptions { MaxElements = min - 1 } }));
            AssertTrue(ex is DocxSecurityLimitException { Limit: DocxLimit.Elements }, "Fill: " + ex?.Message);
            AssertTrue(output.Length == 0, "Fill wrote nothing");
            int ph = MinimalLimit(l => DocxTemplate.GetPlaceholders(new MemoryStream(withHeader), null, new DocxReaderOptions { MaxElements = l }));
            AssertTrue(Catch(() => DocxTemplate.GetPlaceholders(new MemoryStream(withHeader), null, new DocxReaderOptions { MaxElements = ph - 1 })) is DocxSecurityLimitException { Limit: DocxLimit.Elements }, "GetPlaceholders");
            int ck = MinimalLimit(l => DocxTemplate.CheckTemplate(new MemoryStream(withHeader), null, new DocxReaderOptions { MaxElements = l }));
            AssertTrue(Catch(() => DocxTemplate.CheckTemplate(new MemoryStream(withHeader), null, new DocxReaderOptions { MaxElements = ck - 1 })) is DocxSecurityLimitException { Limit: DocxLimit.Elements }, "CheckTemplate");
        });

        Check("MaxElements in every DocxReader read method, counted per call: at the limit each call works again and again; one below it throws", () =>
        {
            var reads = new (string Name, Action<DocxReader> Read)[]
            {
                ("Paragraphs", r => r.Paragraphs().Count()),
                ("Tables", r => r.Tables().Count()),
                ("ExtractText", r => r.ExtractText()),
                ("Images", r => r.Images()),
                ("SaveImages", r => r.SaveImages(Path.Combine(dir, "save-" + Guid.NewGuid().ToString("N")))),
                ("GetActiveContent", r => r.GetActiveContent()),
            };
            var bytes = WithHeader(50, 40, picture: true);
            foreach (var (name, read) in reads)
            {
                int min = MinimalLimit(l => { using var r = DocxReader.Open(new MemoryStream(bytes), null, new DocxReaderOptions { MaxElements = l }); read(r); });
                using (var r = DocxReader.Open(new MemoryStream(bytes), null, new DocxReaderOptions { MaxElements = min }))
                {
                    read(r); read(r); read(r);   // per call, not cumulative
                }
                using (var r = DocxReader.Open(new MemoryStream(bytes), null, new DocxReaderOptions { MaxElements = min - 1 }))
                {
                    var ex = Catch(() => read(r));
                    AssertTrue(ex is DocxSecurityLimitException { Limit: DocxLimit.Elements }, name + ": " + ex?.GetType().Name + " " + ex?.Message);
                }
            }
        });

        Check("MaxElements: one options object shared by two loads is never written to (each Load counts on its own copy)", () =>
        {
            var shared = new DocxReaderOptions { MaxElements = CountElements(plain, "word/document.xml") };
            for (int i = 0; i < 3; i++) Document.Load(new MemoryStream(plain), null, shared);
            AssertTrue(shared.MaxElements == CountElements(plain, "word/document.xml"), "unchanged");
        });

        Check("MaxElements null: the same outputs as before (a document saved, loaded and saved again is unchanged)", () =>
        {
            var d = Document.Load(new MemoryStream(withHeader));
            var a = new MemoryStream(); d.SaveTo(a);
            var e = Document.Load(new MemoryStream(withHeader), null, new DocxReaderOptions { MaxElements = 10_000_000 });
            var b = new MemoryStream(); e.SaveTo(b);
            AssertTrue(EncryptedSaveTests.SamePackageExceptTimes(a.ToArray(), b.ToArray()), "a high limit changes nothing");
        });
    }

    /// <summary>
    /// The bomb that no byte limit stops: 1.2 M empty paragraphs with random revision ids, which compress only
    /// about 4:1, so it passes the 200:1 ratio guard and the 64 MB part limit of the preset.
    /// </summary>
    private static void ElementBomb()
    {
        var rnd = new Random(7);
        var sb = new StringBuilder(1_200_000 * 26);
        for (int i = 0; i < 1_200_000; i++) sb.Append("<w:p w:rsidR=\"").Append(rnd.Next().ToString("X8")).Append("\"/>");
        var bomb = Package(sb.ToString());

        Check("ForUntrustedInput refuses an element bomb that passes every byte and ratio limit (1.2 M paragraphs, about 4:1): Load, DocxReader and Fill", () =>
        {
            var p = DocxReaderOptions.ForUntrustedInput();
            var load = Catch(() => Document.Load(new MemoryStream(bomb), null, p));
            AssertTrue(load is DocxSecurityLimitException { Limit: DocxLimit.Elements, Allowed: 1_000_000 }, "Load: " + load?.Message);
            using var r = DocxReader.Open(new MemoryStream(bomb), null, p);
            var read = Catch(() => r.ExtractText());
            AssertTrue(read is DocxSecurityLimitException { Limit: DocxLimit.Elements }, "ExtractText: " + read?.Message);
            var fill = Catch(() => DocxTemplate.Fill(new MemoryStream(bomb), new MemoryStream(), new TemplateData(), new TemplateOptions { ReadOptions = p }));
            AssertTrue(fill is DocxSecurityLimitException { Limit: DocxLimit.Elements }, "Fill: " + fill?.Message);
        });
    }

    // ---- The Open token (R3) --------------------------------------------------------

    private static void OpenToken(string dir)
    {
        var bytes = WithHeader(20, 10, picture: true);
        var reads = new (string Name, Action<DocxReader> Read, Action<DocxReader, CancellationToken> ReadWith)[]
        {
            ("Paragraphs", r => r.Paragraphs().Count(), (r, t) => r.Paragraphs(t).Count()),
            ("Tables", r => r.Tables().Count(), (r, t) => r.Tables(t).Count()),
            ("ExtractText", r => r.ExtractText(), (r, t) => r.ExtractText(t)),
            ("Images", r => r.Images(), (r, t) => r.Images(t)),
            ("SaveImages", r => r.SaveImages(Path.Combine(dir, "tok-" + Guid.NewGuid().ToString("N"))), (r, t) => r.SaveImages(Path.Combine(dir, "tok-" + Guid.NewGuid().ToString("N")), t)),
            ("GetActiveContent", r => r.GetActiveContent(), (r, t) => r.GetActiveContent(t)),
        };

        Check("The token given to Open covers the read methods without a token: cancelled after Open, every read throws OperationCanceledException (unwrapped)", () =>
        {
            foreach (var (name, read, _) in reads)
            {
                using var cts = new CancellationTokenSource();
                using var r = DocxReader.Open(new MemoryStream(bytes), null, null, cts.Token);
                read(r);                                    // works while the token is live
                cts.Cancel();
                var ex = Catch(() => read(r));
                AssertTrue(ex is OperationCanceledException, name + ": " + ex?.GetType().Name);
            }
        });

        Check("The overloads with their own token watch both: either token cancelled stops the read; neither cancelled, it works", () =>
        {
            foreach (var (name, _, readWith) in reads)
            {
                using var open = new CancellationTokenSource();
                using var own = new CancellationTokenSource();
                using var r = DocxReader.Open(new MemoryStream(bytes), null, null, open.Token);
                readWith(r, own.Token);
                own.Cancel();
                AssertTrue(Catch(() => readWith(r, own.Token)) is OperationCanceledException, name + ": its own token");
                using var fresh = new CancellationTokenSource();
                readWith(r, fresh.Token);
                open.Cancel();
                AssertTrue(Catch(() => readWith(r, fresh.Token)) is OperationCanceledException, name + ": the Open token");
            }
        });

        Check("A reader opened without a token, or with one that is never cancelled, reads as before", () =>
        {
            using var none = DocxReader.Open(new MemoryStream(bytes));
            using var live = new CancellationTokenSource();
            using var withToken = DocxReader.Open(new MemoryStream(bytes), null, null, live.Token);
            foreach (var (name, read, _) in reads) { read(none); read(withToken); }
            AssertTrue(none.ExtractText() == withToken.ExtractText(), "the same text");
        });

        Check("A Paragraphs() enumeration started before the cancel stops at the next element after it", () =>
        {
            using var cts = new CancellationTokenSource();
            using var r = DocxReader.Open(new MemoryStream(Package(Body(2000))), null, null, cts.Token);
            int n = 0;
            var ex = Catch(() => { foreach (var _ in r.Paragraphs()) { if (++n == 10) cts.Cancel(); } });
            AssertTrue(ex is OperationCanceledException && n < 2000, $"stopped after {n} paragraphs: {ex?.GetType().Name}");
        });
    }

    // ---- Helpers --------------------------------------------------------------------

    /// <summary>The smallest MaxElements for which <paramref name="run"/> succeeds (binary search).</summary>
    private static int MinimalLimit(Action<int> run)
    {
        int lo = 1, hi = 1;
        while (!Succeeds(run, hi)) { lo = hi + 1; hi *= 2; if (hi > 50_000_000) throw new InvalidOperationException("no limit works"); }
        while (lo < hi) { int mid = lo + (hi - lo) / 2; if (Succeeds(run, mid)) hi = mid; else lo = mid + 1; }
        return lo;
    }

    private static bool Succeeds(Action<int> run, int limit)
    {
        try { run(limit); return true; }
        catch (DocxSecurityLimitException e) when (e.Limit == DocxLimit.Elements) { return false; }
    }

    private static Exception? Catch(Action a)
    {
        try { a(); return null; } catch (Exception e) { return e; }
    }

    private static int CountElements(byte[] package, string entry)
    {
        using var z = new ZipArchive(new MemoryStream(package));
        using var r = System.Xml.XmlReader.Create(z.GetEntry(entry)!.Open());
        int n = 0;
        while (r.Read()) if (r.NodeType == System.Xml.XmlNodeType.Element) n++;
        return n;
    }

    private static string Body(int paragraphs)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < paragraphs; i++) sb.Append("<w:p><w:r><w:t>Paragraph ").Append(i).Append("</w:t></w:r></w:p>");
        sb.Append("<w:tbl><w:tr><w:tc><w:p><w:r><w:t>cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl><w:p/>");
        return sb.ToString();
    }

    /// <summary>A minimal package: content types, the package relationships and the main part only.</summary>
    private static byte[] Package(string body, Action<ZipArchive>? more = null, string documentRels = "", string bodyEnd = "")
    {
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Entry(z, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                + "<Default Extension=\"png\" ContentType=\"image/png\"/>"
                + "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>"
                + "<Override PartName=\"/word/header1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml\"/></Types>");
            Entry(z, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            Entry(z, "word/document.xml",
                $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"{W}\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" "
                + "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" "
                + "xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><w:body>" + body + bodyEnd + "</w:body></w:document>");
            if (documentRels.Length > 0)
                Entry(z, "word/_rels/document.xml.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" + documentRels + "</Relationships>");
            more?.Invoke(z);
        }
        return ms.ToArray();
    }

    /// <summary>A body plus one default header (and optionally a picture in the body).</summary>
    private static byte[] WithHeader(int bodyParagraphs, int headerParagraphs, bool picture = false)
    {
        var header = new StringBuilder();
        for (int i = 0; i < headerParagraphs; i++) header.Append("<w:p><w:r><w:t>Header ").Append(i).Append("</w:t></w:r></w:p>");
        string rels = "<Relationship Id=\"rIdH\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/header\" Target=\"header1.xml\"/>"
            + (picture ? "<Relationship Id=\"rIdP\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/image1.png\"/>" : "");
        string pic = picture
            ? "<w:p><w:r><w:drawing><wp:inline><wp:extent cx=\"762000\" cy=\"381000\"/><wp:docPr id=\"1\" name=\"Picture 1\"/><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">"
              + "<pic:pic><pic:nvPicPr><pic:cNvPr id=\"1\" name=\"image1.png\"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed=\"rIdP\"/></pic:blipFill><pic:spPr/></pic:pic>"
              + "</a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>"
            : "";
        return Package(Body(bodyParagraphs) + pic, z =>
        {
            Entry(z, "word/header1.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:hdr xmlns:w=\"{W}\">" + header + "</w:hdr>");
            if (picture)
            {
                var e = z.CreateEntry("word/media/image1.png");
                using var s = e.Open();
                s.Write(ImageTests.SamplePng80x40());
            }
        }, rels, "<w:sectPr><w:headerReference w:type=\"default\" r:id=\"rIdH\"/></w:sectPr>");
    }

    /// <summary>A Word-like document: paragraph and run properties, bookmarks, proofing marks, four runs a paragraph.</summary>
    private static byte[] WordLike(int paragraphs)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < paragraphs; i++)
        {
            sb.Append("<w:p w:rsidR=\"00112233\"><w:pPr><w:pStyle w:val=\"Normal\"/><w:spacing w:after=\"120\" w:line=\"276\" w:lineRule=\"auto\"/><w:jc w:val=\"both\"/></w:pPr>");
            sb.Append("<w:bookmarkStart w:id=\"").Append(i).Append("\" w:name=\"_Toc").Append(i).Append("\"/><w:bookmarkEnd w:id=\"").Append(i).Append("\"/>");
            for (int k = 0; k < 4; k++)
                sb.Append("<w:r><w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\" w:cs=\"Latha\"/><w:b/><w:sz w:val=\"22\"/><w:lang w:val=\"en-IN\"/></w:rPr><w:t xml:space=\"preserve\">The plan of care was discussed today. </w:t></w:r>");
            sb.Append("</w:p>");
        }
        return Package(sb.ToString());
    }

    private static void Entry(ZipArchive z, string name, string content)
    {
        var e = z.CreateEntry(name);
        using var s = e.Open();
        var b = Encoding.UTF8.GetBytes(content);
        s.Write(b, 0, b.Length);
    }
}
