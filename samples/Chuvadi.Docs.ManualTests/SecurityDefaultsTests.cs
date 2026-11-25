using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using Chuvadi.Docs.Internal;
using Chuvadi.Docs.Model;
using static Chuvadi.Docs.ManualTests.Program;

namespace Chuvadi.Docs.ManualTests;

/// <summary>
/// Docs 2.4.1 (D-406, D-410, SEC-02): safe limits on by default (compression ratio 200:1, 2,000,000 XML elements,
/// content nesting 64), the trusted opt-out <see cref="DocxReaderOptions.ForTrustedInput"/>, the nesting guard
/// (<see cref="DocxReaderOptions.MaxNestingDepth"/>) checked on a 256 KB thread stack, nested light subtree readers
/// that no longer form a chain, and out of memory / stack exhaustion passing through unwrapped.
/// </summary>
public static class SecurityDefaultsTests
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string Wps = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
    private const int SmallStack = 256 * 1024;

    public static void Run(string outDir)
    {
        Presets();
        RatioDefault();
        ElementDefault();
        NestingLimit();
        NestingOnSmallStack();
        NoLimitNoCrash();
        FatalPassThrough();
    }

    // ---- R1, R1b, R2: the defaults and the presets -------------------------------------------------

    private static void Presets()
    {
        Check("2.4.1 defaults: ratio 200:1, 2,000,000 elements, nesting 64; the other limits as in 2.4.0", () =>
        {
            var d = new DocxReaderOptions();
            AssertTrue(d.MaxCompressionRatio == 200 && DocxReaderOptions.DefaultMaxCompressionRatio == 200, "ratio 200");
            AssertTrue(d.MaxElements == 2_000_000 && DocxReaderOptions.DefaultMaxElements == 2_000_000, "elements 2,000,000");
            AssertTrue(d.MaxNestingDepth == 64 && DocxReaderOptions.DefaultMaxNestingDepth == 64, "nesting 64");
            AssertTrue(d.MaxPackageBytes == 1L << 30 && d.MaxTotalUncompressedBytes == 1L << 30 && d.MaxPartBytes is null, "byte limits unchanged");
            AssertTrue(d.MaxEntries == 10_000 && d.MaxXmlDepth == 256 && d.MaxSpinCount == 500_000, "entries, depth, spin count unchanged");
        });

        Check("ForTrustedInput: exactly the 2.4.0 defaults (ratio 1,000, no element limit) plus nesting 64, a new instance on every call", () =>
        {
            var t = DocxReaderOptions.ForTrustedInput();
            AssertTrue(t.MaxPackageBytes == 1L << 30 && t.MaxTotalUncompressedBytes == 1L << 30 && t.MaxPartBytes is null, "byte limits");
            AssertTrue(t.MaxCompressionRatio == 1_000 && t.CompressionRatioThresholdBytes == 1L << 20, "ratio 1,000 for entries of 1 MB or more");
            AssertTrue(t.MaxEntries == 10_000 && t.MaxXmlDepth == 256 && t.MaxSpinCount == 500_000, "entries, depth, spin count");
            AssertTrue(t.MaxElements is null, "no element limit");
            AssertTrue(t.MaxNestingDepth == 64, "nesting 64");
            AssertTrue(!ReferenceEquals(t, DocxReaderOptions.ForTrustedInput()), "a new instance on every call");
        });

        Check("ForUntrustedInput sets nesting 32; CreateUnrestricted turns nesting off with every other limit", () =>
        {
            AssertTrue(DocxReaderOptions.ForUntrustedInput().MaxNestingDepth == 32, "untrusted 32");
            var u = DocxReaderOptions.CreateUnrestricted();
            AssertTrue(u.MaxNestingDepth is null && u.MaxElements is null && u.MaxCompressionRatio is null, "unrestricted: off");
        });
    }

    // ---- R1: compression ratio 200 by default -------------------------------------------------------

    private static void RatioDefault()
    {
        var bytes = Package(RatioBody(), out double ratio);

        Check("a 1.8 MB part compressed between 200:1 and 1,000:1 is refused by default, naming ForTrustedInput, and loads with it", () =>
        {
            AssertTrue(ratio > 220 && ratio < 900, "the part's ratio is in range: " + ratio.ToString("F0"));
            var ex = Catch(() => Document.Load(new MemoryStream(bytes)));
            AssertTrue(ex is DocxSecurityLimitException { Limit: DocxLimit.CompressionRatio, Allowed: 200 }, "refused by the ratio: " + ex?.GetType().Name + " " + ex?.Message);
            AssertContains(ex!.Message, "DocxReaderOptions.ForTrustedInput()", "the message names the way out");
            var ex2 = Catch(() => { using var r = DocxReader.Open(new MemoryStream(bytes)); r.ExtractText(); });
            AssertTrue(ex2 is DocxSecurityLimitException { Limit: DocxLimit.CompressionRatio }, "the streaming reader too (R1b): " + ex2?.GetType().Name);
            var d = Document.Load(new MemoryStream(bytes), null, DocxReaderOptions.ForTrustedInput());
            AssertTrue(d.Blocks.Count == 600 * 501, "loads with ForTrustedInput: " + d.Blocks.Count);
        });
    }

    // ---- R1, R1b: 2,000,000 elements by default ----------------------------------------------------

    private static void ElementDefault()
    {
        Check("the element default: the streaming reader stops after 2,000,000 elements, naming ForTrustedInput; one fewer element reads", () =>
        {
            // The ratio limit is lifted so the element count is what is tested (empty paragraphs compress about 680:1).
            var over = Package(Repeat("<w:p/>", 2_000_000), out _);
            var ex = Catch(() =>
            {
                using var r = DocxReader.Open(new MemoryStream(over), null, new DocxReaderOptions { MaxCompressionRatio = null });
                r.Paragraphs().Count();
            });
            AssertTrue(ex is DocxSecurityLimitException { Limit: DocxLimit.Elements, Allowed: 2_000_000 } s && s.PartName == "/word/document.xml",
                "the Elements limit: " + ex?.GetType().Name + " " + ex?.Message);
            AssertContains(ex!.Message, "DocxReaderOptions.ForTrustedInput()", "the message names the way out");
            // document + body + 1,999,998 paragraphs = 2,000,000 elements in the main part (the package parts are not XML read).
            var at = Package(Repeat("<w:p/>", 1_999_998), out _);
            using var ok = DocxReader.Open(new MemoryStream(at), null, new DocxReaderOptions { MaxCompressionRatio = null });
            AssertTrue(ok.Paragraphs().Count() == 1_999_998, "exactly at the limit it reads");
        });
    }

    // ---- R3: MaxNestingDepth -----------------------------------------------------------------------

    private static readonly (string Name, Func<int, string> Body)[] Shapes =
    {
        ("hyperlinks", n => Nest(n, "<w:hyperlink w:anchor=\"a\">", "</w:hyperlink>", inline: true)),
        ("insertions", n => Nest(n, i => $"<w:ins w:id=\"{i}\" w:author=\"a\">", _ => "</w:ins>", inline: true)),
        ("mixed tracked changes and simple fields", n => Nest(n, i => (i % 5) switch
        {
            0 => $"<w:del w:id=\"{i}\" w:author=\"a\">",
            1 => $"<w:moveTo w:id=\"{i}\" w:author=\"a\">",
            2 => $"<w:moveFrom w:id=\"{i}\" w:author=\"a\">",
            3 => "<w:fldSimple w:instr=\" MERGEFIELD x \">",
            _ => $"<w:ins w:id=\"{i}\" w:author=\"a\">",
        }, i => (i % 5) switch { 0 => "</w:del>", 1 => "</w:moveTo>", 2 => "</w:moveFrom>", 3 => "</w:fldSimple>", _ => "</w:ins>" }, inline: true)),
        ("tables", n => Tables(n, "<w:p><w:r><w:t>deep</w:t></w:r></w:p>")),
        // The table is one level; the outermost wrapper is read by the table itself, each wrapper inside it is one more.
        ("rows in nested custom XML", n => "<w:tbl><w:tblGrid><w:gridCol w:w=\"100\"/></w:tblGrid>" + Repeat("<w:customXml w:element=\"x\">", n)
            + "<w:tr><w:tc><w:p><w:r><w:t>deep</w:t></w:r></w:p></w:tc></w:tr>" + Repeat("</w:customXml>", n) + "</w:tbl><w:p/>"),
        ("tables with hyperlinks inside", n => Tables(n / 2, Nest(n - n / 2, "<w:hyperlink w:anchor=\"a\">", "</w:hyperlink>", inline: true))),
    };

    private static void NestingLimit()
    {
        foreach (var (name, body) in Shapes)
            Check($"MaxNestingDepth: {name} 64 deep load by default, 65 deep are refused (NestingDepth, Actual 65, Allowed 64, part name)", () =>
            {
                var d = Document.Load(new MemoryStream(Package(body(64), out _)));
                AssertContains(Text(d), "deep", "64 deep loads with its text");
                var ex = Catch(() => Document.Load(new MemoryStream(Package(body(65), out _))));
                AssertTrue(ex is DocxSecurityLimitException { Limit: DocxLimit.NestingDepth, Actual: 65, Allowed: 64 } s && s.PartName == "/word/document.xml",
                    "65 deep refused: " + ex?.GetType().Name + " " + ex?.Message);
                AssertContains(ex!.Message, "nested 65 levels deep", "message");
            });

        Check("MaxNestingDepth: ForUntrustedInput refuses 33 levels and loads 32; a custom limit is honoured; FormattedParagraphs counts too", () =>
        {
            var body = Shapes[0].Body;
            Document.Load(new MemoryStream(Package(body(32), out _)), null, DocxReaderOptions.ForUntrustedInput());
            var ex = Catch(() => Document.Load(new MemoryStream(Package(body(33), out _)), null, DocxReaderOptions.ForUntrustedInput()));
            AssertTrue(ex is DocxSecurityLimitException { Limit: DocxLimit.NestingDepth, Allowed: 32 }, "untrusted 33: " + ex?.Message);
            var custom = new DocxReaderOptions { MaxNestingDepth = 5 };
            Document.Load(new MemoryStream(Package(Shapes[3].Body(5), out _)), null, custom);
            ex = Catch(() => Document.Load(new MemoryStream(Package(Shapes[3].Body(6), out _)), null, custom));
            AssertTrue(ex is DocxSecurityLimitException { Limit: DocxLimit.NestingDepth, Allowed: 5 }, "custom 5: " + ex?.Message);
            ex = Catch(() =>
            {
                using var r = DocxReader.Open(new MemoryStream(Package(body(65), out _)));
                r.FormattedParagraphs().Count();
            });
            AssertTrue(ex is DocxSecurityLimitException { Limit: DocxLimit.NestingDepth }, "FormattedParagraphs refuses 65: " + ex?.GetType().Name);
        });

        Check("MaxNestingDepth: a text box counts four levels: 16 nested text boxes load by default, 17 are refused (Actual 68)", () =>
        {
            var d = Document.Load(new MemoryStream(Package(TextBoxes(16), out _)));
            AssertTrue(BoxLevels(d, out var inner) == 16 && inner == "deep", $"16 nested text boxes load: {BoxLevels(d, out _)} levels");
            var ex = Catch(() => Document.Load(new MemoryStream(Package(TextBoxes(17), out _))));
            AssertTrue(ex is DocxSecurityLimitException { Limit: DocxLimit.NestingDepth, Actual: 68, Allowed: 64 }, "17 refused: " + ex?.GetType().Name + " " + ex?.Message);
            // Mixed: 15 text boxes (60 levels) with 4 nested hyperlinks inside reach 64 and load; 5 hyperlinks do not.
            Document.Load(new MemoryStream(Package(TextBoxes(15, Nest(4, "<w:hyperlink w:anchor=\"a\">", "</w:hyperlink>", inline: true)), out _)));
            ex = Catch(() => Document.Load(new MemoryStream(Package(TextBoxes(15, Nest(5, "<w:hyperlink w:anchor=\"a\">", "</w:hyperlink>", inline: true)), out _))));
            AssertTrue(ex is DocxSecurityLimitException { Limit: DocxLimit.NestingDepth, Actual: 65 }, "15 boxes + 5 links refused: " + ex?.Message);
        });

        Check("smart tags, run-level content controls and custom XML are read flat: 1,000 deep load with the XML depth limit off", () =>
        {
            var noDepth = new DocxReaderOptions { MaxXmlDepth = null };
            foreach (var (open, close) in new[] { ("<w:smartTag w:element=\"x\">", "</w:smartTag>"), ("<w:sdt><w:sdtContent>", "</w:sdtContent></w:sdt>"), ("<w:customXml w:element=\"x\">", "</w:customXml>") })
            {
                var d = Document.Load(new MemoryStream(Package(Nest(1000, open, close, inline: true), out _)), null, noDepth);
                AssertContains(d.GetText(), "deep", open);
            }
        });
    }

    private static void NestingOnSmallStack()
    {
        foreach (var (name, body) in Shapes)
            Check($"256 KB thread stack: {name} 64 deep through Load, save and reload, list labels, text, the streaming readers and a template fill", () =>
            {
                var bytes = Package(body(64), out _);
                var result = OnThread(SmallStack, () =>
                {
                    var d = Document.Load(new MemoryStream(bytes));
                    var saved = new MemoryStream();
                    d.SaveTo(saved);
                    var again = Document.Load(new MemoryStream(saved.ToArray()));
                    d.GetListLabels();
                    var text = Text(again);
                    using (var r = DocxReader.Open(new MemoryStream(bytes)))
                    {
                        r.Paragraphs().Count();
                        r.Tables().Count();
                        r.ExtractText();
                    }
                    using (var r = DocxReader.Open(new MemoryStream(bytes), null, new DocxReaderOptions { IncludeTextBoxes = true, IncludeNotes = true, IncludeComments = true }))
                        r.FormattedParagraphs().Count();
                    DocxTemplate.Fill(new MemoryStream(bytes), new MemoryStream(), new TemplateData());
                    return text;
                });
                AssertContains(result, "deep", "the text survives");
            });

        Check("256 KB thread stack: 16 nested text boxes (the default's most) load, save and read as formatted paragraphs", () =>
        {
            var bytes = Package(TextBoxes(16), out _);
            var noDepth = new DocxReaderOptions { IncludeTextBoxes = true };
            var text = OnThread(SmallStack, () =>
            {
                var d = Document.Load(new MemoryStream(bytes), null, noDepth);
                d.SaveTo(new MemoryStream());
                using (var r = DocxReader.Open(new MemoryStream(bytes), null, noDepth)) r.FormattedParagraphs().Count();
                return BoxLevels(d, out var inner) + " " + inner;
            });
            AssertTrue(text == "16 deep", "16 levels: " + text);
        });

        Check("256 KB thread stack: a part at the default XML depth (250 nested smart tags) loads, saves, fills as a template and streams", () =>
        {
            var bytes = Package(Nest(250, "<w:smartTag w:element=\"x\">", "</w:smartTag>", inline: true), out _);
            var text = OnThread(SmallStack, () =>
            {
                var d = Document.Load(new MemoryStream(bytes));
                d.SaveTo(new MemoryStream());
                DocxTemplate.Fill(new MemoryStream(bytes), new MemoryStream(), new TemplateData());
                DocxTemplate.CheckTemplate(new MemoryStream(bytes));
                using (var r = DocxReader.Open(new MemoryStream(bytes))) { r.Paragraphs().Count(); r.FormattedParagraphs().Count(); return r.ExtractText(); }
            });
            AssertContains(text, "deep", "text");
        });

        Check("256 KB thread stack: 65 deep is refused there too, as a catchable DocxSecurityLimitException", () =>
        {
            foreach (var (name, body) in Shapes)
            {
                var bytes = Package(body(65), out _);
                var ex = OnThread(SmallStack, () => Catch(() => Document.Load(new MemoryStream(bytes))));
                AssertTrue(ex is DocxSecurityLimitException { Limit: DocxLimit.NestingDepth }, name + ": " + ex?.GetType().Name);
            }
        });
    }

    // ---- R3 with the limit off, and 2b: no chain of subtree readers ---------------------------------

    private static void NoLimitNoCrash()
    {
        Check("CreateUnrestricted: 20,000 nested hyperlinks on a 1 MB thread end in InsufficientExecutionStackException, not a process crash", () =>
        {
            var bytes = Package(Shapes[0].Body(20_000), out _);
            var ex = OnThread(1024 * 1024, () => Catch(() => Document.Load(new MemoryStream(bytes), null, DocxReaderOptions.CreateUnrestricted())));
            AssertTrue(ex is InsufficientExecutionStackException, "unwrapped stack exhaustion: " + ex?.GetType().Name + " " + ex?.Message);
            var tables = Package(Shapes[3].Body(20_000), out _);
            ex = OnThread(1024 * 1024, () => Catch(() => Document.Load(new MemoryStream(tables), null, DocxReaderOptions.CreateUnrestricted())));
            AssertTrue(ex is InsufficientExecutionStackException, "tables: " + ex?.GetType().Name + " " + ex?.Message);
        });

        Check("2b: a light subtree opened on a light subtree reads the outer reader directly: 100,000 nested subtrees read on a 256 KB thread", () =>
        {
            // Before 2.4.1 nested subtree readers formed a chain and every Read went down all of it: here that is
            // 100,000 frames per Read, a stack overflow (which ends the process) long before the end.
            const int levels = 100_000;
            var xml = Repeat("<a>", levels) + "x" + Repeat("</a>", levels);
            var (texts, depth) = OnThread(SmallStack, () =>
            {
                using var outer = GuardedXmlReader.Create(new MemoryStream(Encoding.UTF8.GetBytes(xml)), null, null, CancellationToken.None, "/part.xml");
                outer.Read();
                var readers = new List<System.Xml.XmlReader>();
                System.Xml.XmlReader s = outer;
                for (int i = 0; i < levels - 1; i++)
                {
                    s = s.LightSubtree();
                    readers.Add(s);
                    s.Read(); // its own root element
                    s.Read(); // the next <a>
                }
                int n = 0;
                int innermostDepth = s.Depth;
                while (s.Read()) if (s.NodeType == System.Xml.XmlNodeType.Text) n++;
                for (int i = readers.Count - 1; i >= 0; i--) readers[i].Dispose();
                return (n, innermostDepth);
            });
            AssertTrue(texts == 1 && depth == 1, $"the innermost subtree sees its text at its own depth: {texts}, {depth}");
        });

        Check("2b: 1,000 nested hyperlinks load with no limits in well under a second per thousand levels", () =>
        {
            var bytes = Package(Shapes[0].Body(1_000), out _);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var text = OnThread(16 * 1024 * 1024, () => Document.Load(new MemoryStream(bytes), null, DocxReaderOptions.CreateUnrestricted()).GetText());
            AssertContains(text, "deep", "text");
            AssertTrue(sw.Elapsed.TotalSeconds < 10, $"took {sw.Elapsed.TotalSeconds:F1}s");
        });
    }

    // ---- R4 / 2a: fatal exceptions pass through ----------------------------------------------------

    private static void FatalPassThrough()
    {
        Check("R4: out of memory and stack exhaustion are never wrapped as DocxFormatException (the exception contract)", () =>
        {
            AssertTrue(DocxErrors.IsContract(new OutOfMemoryException()), "OutOfMemoryException passes through");
            AssertTrue(DocxErrors.IsContract(new InsufficientMemoryException()), "InsufficientMemoryException passes through");
            AssertTrue(DocxErrors.IsContract(new InsufficientExecutionStackException()), "InsufficientExecutionStackException passes through");
            AssertTrue(!DocxErrors.IsContract(new InvalidDataException()), "an ordinary read failure is still wrapped");
        });

        Check("R4: stack exhaustion from FormattedParagraphs and from a header comes out unwrapped too", () =>
        {
            var bytes = Package(Shapes[0].Body(20_000), out _);
            var ex = OnThread(1024 * 1024, () => Catch(() =>
            {
                using var r = DocxReader.Open(new MemoryStream(bytes), null, DocxReaderOptions.CreateUnrestricted());
                r.FormattedParagraphs().Count();
            }));
            AssertTrue(ex is InsufficientExecutionStackException, "FormattedParagraphs: " + ex?.GetType().Name);
            var header = PackageWithHeader("<w:p/>", Shapes[0].Body(20_000));
            ex = OnThread(1024 * 1024, () => Catch(() => Document.Load(new MemoryStream(header), null, DocxReaderOptions.CreateUnrestricted())));
            AssertTrue(ex is InsufficientExecutionStackException, "header: " + ex?.GetType().Name);
        });
    }

    // ---- helpers --------------------------------------------------------------------------------------

    /// <summary>The text, with the text of tracked deletions too (GetText leaves deleted text out).</summary>
    private static string Text(Document d)
    {
        var t = d.GetText();
        if (t.Contains("deep", StringComparison.Ordinal)) return t;
        return string.Concat(d.Blocks.OfType<Paragraph>().SelectMany(p => p.Runs).Select(r => r.TextContent));
    }

    /// <summary>How many text boxes are nested in the first paragraph, and the innermost box's text.</summary>
    private static int BoxLevels(Document d, out string innermost)
    {
        int levels = 0;
        innermost = "";
        var p = d.Blocks.OfType<Paragraph>().FirstOrDefault();
        while (p?.Runs.FirstOrDefault(r => r.TextBox is not null)?.TextBox is { } box)
        {
            levels++;
            innermost = box.GetText().Trim();
            p = box.Paragraphs.FirstOrDefault();
        }
        return levels;
    }

    private static T OnThread<T>(int stackBytes, Func<T> work)
    {
        T result = default!;
        Exception? failure = null;
        var t = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception e) { failure = e; }
        }, stackBytes);
        t.Start();
        t.Join();
        if (failure is not null) throw new InvalidOperationException("on the thread: " + failure.GetType().Name + ": " + failure.Message, failure);
        return result;
    }

    private static Exception? Catch(Action a)
    {
        try { a(); return null; }
        catch (Exception e) { return e; }
    }

    private static string Repeat(string s, int n)
    {
        var sb = new StringBuilder(s.Length * Math.Max(0, n));
        for (int i = 0; i < n; i++) sb.Append(s);
        return sb.ToString();
    }

    private static string Nest(int n, string open, string close, bool inline) => Nest(n, _ => open, _ => close, inline);

    private static string Nest(int n, Func<int, string> open, Func<int, string> close, bool inline)
    {
        var sb = new StringBuilder();
        if (inline) sb.Append("<w:p>");
        for (int i = 0; i < n; i++) sb.Append(open(i));
        sb.Append("<w:r><w:t>deep</w:t></w:r>");
        for (int i = n - 1; i >= 0; i--) sb.Append(close(i));
        if (inline) sb.Append("</w:p>");
        return sb.ToString();
    }

    private static string Tables(int n, string inner)
    {
        if (n <= 0) return inner;
        return Repeat("<w:tbl><w:tblGrid><w:gridCol w:w=\"100\"/></w:tblGrid><w:tr><w:tc>", n) + inner + Repeat("</w:tc></w:tr></w:tbl><w:p/>", n);
    }

    private static string TextBoxes(int n, string? inner = null)
    {
        const string open = "<w:p><w:r><w:drawing><wp:inline xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\"><wp:extent cx=\"635000\" cy=\"635000\"/><wp:docPr id=\"1\" name=\"Box\"/>"
            + "<a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><a:graphicData uri=\"" + Wps + "\"><wps:wsp xmlns:wps=\"" + Wps + "\"><wps:cNvSpPr txBox=\"1\"/>"
            + "<wps:txbx><w:txbxContent>";
        const string close = "</w:txbxContent></wps:txbx><wps:bodyPr/></wps:wsp></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>";
        return Repeat(open, n) + (inner ?? "<w:p><w:r><w:t>deep</w:t></w:r></w:p>") + Repeat(close, n);
    }

    /// <summary>600 groups of 500 empty paragraphs and one paragraph of four random hex digits: about 1.8 MB of XML.</summary>
    private static string RatioBody()
    {
        var rng = new Random(1);
        var sb = new StringBuilder();
        for (int i = 0; i < 600; i++)
        {
            for (int k = 0; k < 500; k++) sb.Append("<w:p/>");
            sb.Append("<w:p><w:r><w:t>");
            for (int k = 0; k < 4; k++) sb.Append("0123456789abcdef"[rng.Next(16)]);
            sb.Append("</w:t></w:r></w:p>");
        }
        return sb.ToString();
    }

    private static byte[] Package(string body, out double documentRatio)
    {
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(z, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
            Add(z, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            Add(z, "word/document.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"{W}\"><w:body>{body}</w:body></w:document>");
        }
        var bytes = ms.ToArray();
        using (var z = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
        {
            var e = z.GetEntry("word/document.xml")!;
            documentRatio = e.CompressedLength == 0 ? 0 : (double)e.Length / e.CompressedLength;
        }
        return bytes;
    }

    private static byte[] PackageWithHeader(string body, string headerBody)
    {
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(z, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/><Override PartName=\"/word/header1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml\"/></Types>");
            Add(z, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            Add(z, "word/_rels/document.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rIdH\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/header\" Target=\"header1.xml\"/></Relationships>");
            Add(z, "word/document.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"{W}\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><w:body>{body}<w:sectPr><w:headerReference w:type=\"default\" r:id=\"rIdH\"/></w:sectPr></w:body></w:document>");
            Add(z, "word/header1.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:hdr xmlns:w=\"{W}\">{headerBody}</w:hdr>");
        }
        return ms.ToArray();
    }

    private static void Add(ZipArchive z, string name, string text)
    {
        using var w = new StreamWriter(z.CreateEntry(name, CompressionLevel.SmallestSize).Open(), new UTF8Encoding(false));
        w.Write(text);
    }
}
