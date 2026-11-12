using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Xml;
using Chuvadi.Docs.Internal;
using Chuvadi.Docs.Model;
using static Chuvadi.Docs.ManualTests.Program;

namespace Chuvadi.Docs.ManualTests;

/// <summary>
/// Docs 2.1.1 (DOC-10, N2): Load reads its body, table, row, cell, paragraph, run and property subtrees with
/// the internal <see cref="SubtreeReader"/> in place of <see cref="XmlReader.ReadSubtree"/>. These checks drive
/// both readers through the same random sequences of reads, skips, content reads, nested subtrees and early
/// disposals over the same XML (well-formed and truncated, through the guarded reader with an element limit),
/// and require the same observations at every step: node type, name, namespace, value, depth, emptiness,
/// end of file, read state, attributes by name, exceptions, and where the outer reader is left.
/// The one intended difference, the BCL reader's synthetic xmlns attributes, is not observed here: Load never
/// enumerates attributes or captures XML through these readers.
/// </summary>
public static class SubtreeReaderTests
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static void Run(string outDir)
    {
        var samples = Samples();

        Check($"the light subtree reader matches XmlReader.ReadSubtree step by step: {samples.Count} documents x 1,000 random walks", () =>
        {
            int steps = 0;
            foreach (var (name, xml) in samples)
                for (int seed = 0; seed < 1000; seed++)
                {
                    var bcl = Walk(xml, seed, light: false, maxElements: null);
                    var mine = Walk(xml, seed, light: true, maxElements: null);
                    Same(bcl, mine, $"{name}, seed {seed}");
                    steps += bcl.Count;
                }
            AssertTrue(steps > 150_000, "steps compared: " + steps);
        });

        Check("the same under an element limit: the limit is reached at the same element, with the same exception", () =>
        {
            foreach (var (name, xml) in samples)
                foreach (int limit in new[] { 1, 5, 17, 60, 250 })
                    for (int seed = 0; seed < 20; seed++)
                        Same(Walk(xml, seed, false, limit), Walk(xml, seed, true, limit), $"{name}, limit {limit}, seed {seed}");
        });

        Check("a truncated part: the error swallowed while disposing leaves the outer reader in the Error state, as with ReadSubtree", () =>
        {
            var bad = $"<w:document xmlns:w=\"{W}\"><w:body><w:p><w:r><w:t>cut";
            foreach (bool light in new[] { false, true })
            {
                var r = GuardedXmlReader.Create(new MemoryStream(Encoding.UTF8.GetBytes(bad)), 256, null, CancellationToken.None, "/word/document.xml");
                r.Read(); r.Read(); r.Read(); // w:p
                using (var s = light ? r.LightSubtree() : r.ReadSubtree()) s.Read();
                AssertTrue(r.ReadState == ReadState.Error, (light ? "light: " : "BCL: ") + r.ReadState);
            }
        });

        Check("Load reads the well-formed samples (through the light reader)", () =>
        {
            foreach (var (name, xml) in samples)
            {
                if (!name.StartsWith("doc", StringComparison.Ordinal)) continue;
                var d = Document.Load(new MemoryStream(Package(xml)));
                AssertTrue(d.GetText().Length > 0, name);
            }
        });
    }

    // ---- the walk -------------------------------------------------------------------------

    private static List<string> Walk(string xml, int seed, bool light, int? maxElements)
    {
        var log = new List<string>();
        var rng = new Random(seed);
        var budget = maxElements is int m ? new ElementBudget(m) : null;
        try
        {
            using var r = GuardedXmlReader.Create(new MemoryStream(Encoding.UTF8.GetBytes(xml)), 256, budget, CancellationToken.None, "/part.xml");
            // Walk the top level with plain reads, opening a subtree on some elements.
            while (r.Read())
            {
                Observe(log, "outer", r);
                if (r.NodeType == XmlNodeType.Element && rng.Next(3) > 0)
                {
                    using (var s = light ? r.LightSubtree() : r.ReadSubtree()) Drive(s, rng, light, log, 0);
                    Observe(log, "outer after dispose", r);
                }
            }
            Observe(log, "outer end", r);
        }
        catch (Exception e)
        {
            log.Add("EX " + e.GetType().FullName + ": " + e.Message);
        }
        return log;
    }

    private static void Drive(XmlReader s, Random rng, bool light, List<string> log, int level)
    {
        Observe(log, "initial", s);
        int ops = rng.Next(1, 60);
        for (int i = 0; i < ops; i++)
        {
            int op = rng.Next(100);
            string opName = op < 55 ? "Read" : op < 68 ? "Skip" : op < 76 ? "Content" : op < 82 ? "MoveToElement" : op < 86 ? "MoveToAttribute" : "Inner/stop";
            try
            {
                if (op < 55) log.Add("Read " + s.Read());
                else if (op < 68) { s.Skip(); log.Add("Skip"); }
                else if (op < 76) log.Add("Content " + s.ReadElementContentAsString());
                else if (op < 82) log.Add("MoveToElement " + s.MoveToElement());
                else if (op < 86) log.Add("MoveToAttribute " + s.MoveToAttribute("val", W) + " " + s.Value);
                else if (op < 96 && s.NodeType == XmlNodeType.Element && level < 6)
                {
                    using (var inner = light ? s.LightSubtree() : s.ReadSubtree()) Drive(inner, rng, light, log, level + 1);
                    log.Add("after inner");
                }
                else { log.Add("stop"); return; }
            }
            catch (Exception e) when (e is XmlException or InvalidOperationException)
            {
                log.Add("EX in " + opName + " " + e.GetType().FullName + ": " + e.Message);
                if (e is XmlException) return;
            }
            Observe(log, "step", s);
            if (s.EOF && rng.Next(4) == 0) { log.Add("read past end " + s.Read()); return; }
        }
    }

    private static void Observe(List<string> log, string at, XmlReader r)
        => log.Add($"{at}: {r.NodeType} {r.Name}|{r.LocalName}|{r.NamespaceURI}|{r.Prefix} d={r.Depth} empty={r.IsEmptyElement} " +
                   $"eof={r.EOF} state={r.ReadState} v=[{r.Value}] val=[{r.GetAttribute("val", W)}] id=[{r.GetAttribute("id")}]");

    private static void Same(List<string> a, List<string> b, string what)
    {
        for (int i = 0; i < Math.Min(a.Count, b.Count); i++)
            if (a[i] != b[i]) throw new InvalidOperationException($"{what}, step {i}:\n  before: {string.Join("\n          ", a.GetRange(Math.Max(0, i - 6), Math.Min(6, i)))}\n  ReadSubtree: {a[i]}\n  light:       {b[i]}");
        if (a.Count != b.Count) throw new InvalidOperationException($"{what}: {a.Count} steps against {b.Count}");
    }

    // ---- inputs -------------------------------------------------------------------------

    private static List<(string, string)> Samples()
    {
        var list = new List<(string, string)>();
        var sb = new StringBuilder($"<w:document xmlns:w=\"{W}\" xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\"><w:body>");
        for (int i = 0; i < 12; i++)
        {
            sb.Append($"<w:p w14:paraId=\"{i:X8}\" w:rsidR=\"00A1B2C3\"><w:pPr><w:pStyle w:val=\"Heading{i % 3}\"/><w:spacing w:after=\"0\"/></w:pPr>");
            sb.Append($"<w:r w:rsidR=\"00C0FFEE\"><w:rPr><w:b/><w:lang w:val=\"en-IN\"/></w:rPr><w:t xml:space=\"preserve\">Run {i} </w:t></w:r>");
            sb.Append("<w:proofErr w:type=\"spellStart\"/><w:r><w:t>wrd</w:t><w:tab/><w:t/></w:r><w:proofErr w:type=\"spellEnd\"/>");
            sb.Append("<mc:AlternateContent><mc:Choice Requires=\"w14\"><w:r><w:t>choice</w:t></w:r></mc:Choice><mc:Fallback><w:r><w:t>fallback</w:t></w:r></mc:Fallback></mc:AlternateContent>");
            sb.Append("<w:hyperlink w:anchor=\"a\"><w:r><w:t>link</w:t></w:r></w:hyperlink></w:p>");
            if (i % 4 == 1)
                sb.Append("<w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"100\"/><w:gridCol w:w=\"200\"/></w:tblGrid>" +
                          "<w:tr><w:trPr><w:cantSplit/></w:trPr><w:tc><w:tcPr><w:tcW w:w=\"100\" w:type=\"dxa\"/></w:tcPr><w:p><w:r><w:t>c1</w:t></w:r></w:p></w:tc>" +
                          "<w:tc><w:p/><w:tbl><w:tr><w:tc><w:p><w:r><w:t>nested</w:t></w:r></w:p></w:tc></w:tr></w:tbl><w:p/></w:tc></w:tr></w:tbl>");
        }
        sb.Append("<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/></w:sectPr></w:body></w:document>");
        list.Add(("doc-word-like", sb.ToString()));
        list.Add(("doc-empty-elements", $"<w:document xmlns:w=\"{W}\"><w:body><w:p/><w:p><w:r/></w:p><w:p><w:r><w:t/></w:r></w:p><w:tbl/><w:sectPr/></w:body></w:document>"));
        list.Add(("mixed-content", "<a><b>x<c/>y<!-- c --><?pi z?><![CDATA[q]]><d id=\"1\">t</d></b><e/>tail</a>"));
        list.Add(("default-and-prefixed-namespaces", $"<root xmlns=\"urn:d\" xmlns:w=\"{W}\"><w:x w:val=\"1\"><y xmlns=\"urn:e\"><w:z w:val=\"2\"/></y></w:x><w:x/></root>"));
        list.Add(("truncated", $"<w:document xmlns:w=\"{W}\"><w:body><w:p><w:r><w:t>one</w:t></w:r></w:p><w:p><w:r><w:t>tw"));
        list.Add(("mismatched", $"<w:document xmlns:w=\"{W}\"><w:body><w:p><w:r></w:p></w:r></w:body></w:document>"));
        return list;
    }

    private static byte[] Package(string documentXml)
    {
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string text)
            {
                using var w = new StreamWriter(z.CreateEntry(name).Open(), new UTF8Encoding(false));
                w.Write(text);
            }
            Add("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
            Add("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            Add("word/document.xml", documentXml);
        }
        return ms.ToArray();
    }
}
