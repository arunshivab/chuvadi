using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Chuvadi.Docs.Model;
using static Chuvadi.Docs.ManualTests.Program;

namespace Chuvadi.Docs.ManualTests;

/// <summary>
/// Docs 2.3.0, D-374: a <see cref="DocumentPreset.Word"/> document has Word's Header and Footer styles (no space
/// after, single lines, a centre and a right tab), and its header and footer paragraphs in Normal use them, so they
/// do not take the body's new 8pt after and 1.08 lines. Minimal documents and loaded documents are unchanged.
/// </summary>
public static class HeaderFooterStyleTests
{
    private static readonly XNamespace Wx = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static void Run(string outDir)
    {
        Check("a Word-preset document has Word's Header and Footer styles: based on Normal, priority 99, centre 4513 and right 9026 tabs, 0 after and single lines", () =>
        {
            var d = new Document();
            d.Header.SetText("Header text");
            d.Footer.Add(new Paragraph { Alignment = ParagraphAlignment.Center }.PageNumber());
            d.AddParagraph("தமிழ் எழுத்துரு சோதனை");
            d.AddParagraph("Body text");
            var bytes = Save(d);
            File.WriteAllBytes(Path.Combine(outDir, "header-footer-styles.docx"), bytes);
            var styles = Part(bytes, "word/styles.xml");
            foreach (var (id, name) in new[] { ("Header", "header"), ("Footer", "footer") })
            {
                var s = StyleXml(styles, id);
                AssertTrue(s.Contains($"<w:name w:val=\"{name}\" />") && s.Contains("<w:basedOn w:val=\"Normal\" />")
                    && s.Contains("<w:uiPriority w:val=\"99\" />") && s.Contains("<w:unhideWhenUsed />") && !s.Contains("qFormat"), id + " head: " + s);
                AssertTrue(s.Contains("<w:tabs><w:tab w:val=\"center\" w:pos=\"4513\" /><w:tab w:val=\"right\" w:pos=\"9026\" /></w:tabs>"), id + " tabs: " + s);
                AssertTrue(s.Contains("<w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\" />"), id + " spacing: " + s);
            }
            AssertTrue(d.Defaults.HeaderParagraphStyleId == "Header" && d.Defaults.FooterParagraphStyleId == "Footer", "the ids");
        });

        Check("header and footer paragraphs in Normal are written in Header and Footer (added, SetText, table cells, the closing paragraph after a table); other styles and the body are unchanged", () =>
        {
            var d = new Document();
            d.Header.Add(new Paragraph("H added"));
            d.Header.Add(new Paragraph("H title") { Style = ParagraphStyle.Title });
            var box = new TextBox();
            var boxed = new Paragraph("H box");
            box.Paragraphs.Add(boxed);
            d.Header.Add(new Paragraph("H with box").TextBox(box));
            d.Header.AddTable(1).AddRow("H cell");
            d.FirstPageHeader.SetText("H first");
            d.Footer.Add(new Paragraph("F added"));
            d.AddParagraph("Body");
            var bytes = Save(d);
            AssertTrue(StyleOf(HeaderPartWith(bytes, "H added"), "H box") == "none" && d.GetEffectiveParagraphFormat(boxed).SpaceAfterPt == 8, "a text box in the header keeps Normal");
            var header = HeaderPartWith(bytes, "H added");
            AssertTrue(StyleOf(header, "H added") == "Header", "added: " + StyleOf(header, "H added"));
            AssertTrue(StyleOf(header, "H title") == "Title", "Title kept");
            AssertTrue(StyleOf(header, "H cell") == "Header", "table cell: " + StyleOf(header, "H cell"));
            var last = header.Root!.Elements(Wx + "p").Last();
            AssertTrue((string?)last.Element(Wx + "pPr")?.Element(Wx + "pStyle")?.Attribute(Wx + "val") == "Header" && !last.Descendants(Wx + "t").Any(), "closing paragraph after the table");
            AssertTrue(StyleOf(HeaderPartWith(bytes, "H first"), "H first") == "Header", "SetText (first page)");
            AssertTrue(StyleOf(HeaderPartWith(bytes, "F added"), "F added") == "Footer", "footer");
            var body = XDocument.Parse(Part(bytes, "word/document.xml"));
            AssertTrue(StyleOf(body, "Body") == "none", "body paragraph");
            AssertTrue(d.Header[0].StyleId == "Normal", "the model is not changed");
        });

        Check("the effective formats follow: header and footer paragraphs 0pt after, single lines, two tabs; the body 8pt; a header table cell 0pt; a Title in the header keeps Title", () =>
        {
            var d = new Document();
            var body = d.AddParagraph("Body");
            var h = new Paragraph("Header");
            d.Header.Add(h);
            var title = new Paragraph("Header title") { Style = ParagraphStyle.Title };
            d.Header.Add(title);
            var f = new Paragraph("Footer");
            d.Footer.Add(f);
            var t = d.Header.AddTable(1);
            t.AddRow("cell");
            var cp = t.Rows[0].Cells[0].Paragraphs[0];
            AssertTrue(Spacing(d.GetEffectiveParagraphFormat(body)) == "8|1.0792", "body: " + Spacing(d.GetEffectiveParagraphFormat(body)));
            var eh = d.GetEffectiveParagraphFormat(h);
            AssertTrue(Spacing(eh) == "0|1" && eh.TabStops.Count == 2 && eh.TabStops[0].Alignment == TabAlignment.Center, "header: " + Spacing(eh) + " tabs " + eh.TabStops.Count);
            AssertTrue(Spacing(d.GetEffectiveParagraphFormat(f)) == "0|1", "footer");
            AssertTrue(Spacing(d.GetEffectiveParagraphFormat(cp, new TableCellRef(t, 0, 0))) == "0|1", "header table cell");
            AssertTrue(d.GetEffectiveParagraphFormat(title).SpaceAfterPt == 6, "Title in the header");
            AssertTrue(d.GetEffectiveRunFormat(h, h.Runs[0]).SizePt == 11, "run format: Header adds no run formatting");
        });

        Check("changes after a lookup are seen: a paragraph added to the header later, and a new section's header", () =>
        {
            var d = new Document();
            var body = d.AddParagraph("Body");
            d.Header.Add(new Paragraph("first"));
            _ = d.GetEffectiveParagraphFormat(body);
            var later = new Paragraph("later");
            d.Header.Add(later);
            AssertTrue(Spacing(d.GetEffectiveParagraphFormat(later)) == "0|1", "added later");
            d.AddSectionBreak();
            var second = new Paragraph("second section");
            d.Header.Add(second);
            d.AddParagraph("Body 2");
            AssertTrue(Spacing(d.GetEffectiveParagraphFormat(second)) == "0|1", "new section");
            AssertTrue(Spacing(d.GetEffectiveParagraphFormat(body)) == "8|1.0792", "the body still 8pt");
            d.Header.Remove(second);
            AssertTrue(Spacing(d.GetEffectiveParagraphFormat(second)) == "8|1.0792", "removed from the header: Normal again");
            var bytes = Save(d);
            AssertTrue(StyleOf(HeaderPartWith(bytes, "first"), "first") == "Header", "section 1 written");
        });

        Check("the settings: null gives Normal, another paragraph style is used, an id that names no paragraph style is ignored", () =>
        {
            var d = new Document();
            d.Defaults.HeaderParagraphStyleId = null;
            var mine = new StyleDefinition("MyFooter", StyleType.Paragraph) { Name = "My Footer", BasedOn = "Normal" };
            mine.ParagraphFormat.SpaceAfterPt = 3;
            d.Styles.Add(mine);
            d.Defaults.FooterParagraphStyleId = "MyFooter";
            var h = new Paragraph("head");
            d.Header.Add(h);
            var f = new Paragraph("foot");
            d.Footer.Add(f);
            var bytes = Save(d);
            AssertTrue(StyleOf(HeaderPartWith(bytes, "head"), "head") == "none" && d.GetEffectiveParagraphFormat(h).SpaceAfterPt == 8, "null: Normal");
            AssertTrue(StyleOf(HeaderPartWith(bytes, "foot"), "foot") == "MyFooter" && d.GetEffectiveParagraphFormat(f).SpaceAfterPt == 3, "own style");
            d.Defaults.FooterParagraphStyleId = "Hyperlink"; // a character style
            AssertTrue(StyleOf(HeaderPartWith(Save(d), "foot"), "foot") == "none" && d.GetEffectiveParagraphFormat(f).SpaceAfterPt == 8, "a character style id is ignored");
            d.Defaults.FooterParagraphStyleId = "Missing";
            AssertTrue(StyleOf(HeaderPartWith(Save(d), "foot"), "foot") == "none" && d.GetEffectiveParagraphFormat(f).SpaceAfterPt == 8, "a missing id is ignored");
        });

        Check("Minimal: no Header or Footer style, header paragraphs written as before and resolved through Normal", () =>
        {
            var d = new Document(DocumentPreset.Minimal);
            var h = new Paragraph("head");
            d.Header.Add(h);
            d.AddParagraph("Body");
            var bytes = Save(d);
            AssertTrue(!Part(bytes, "word/styles.xml").Contains("w:styleId=\"Header\""), "no Header style");
            var header = HeaderPartWith(bytes, "head");
            AssertTrue(StyleOf(header, "head") == "none", "head: no pStyle");
            AssertTrue(!header.Root!.Descendants(Wx + "pStyle").Any(), "no pStyle anywhere in the header part");
            var e = d.GetEffectiveParagraphFormat(h);
            AssertTrue(e.SpaceAfterPt is null && e.LineSpacing is null && e.TabStops.Count == 0, "Normal's (empty) format");
            File.WriteAllBytes(Path.Combine(outDir, "header-footer-styles-minimal.docx"), bytes);
        });

        Check("loaded documents are unchanged: a file's header paragraph without pStyle stays Normal although the file has a Header style; a saved Word-preset header loads and saves in Header", () =>
        {
            var file = Package();
            var loaded = Document.Load(new MemoryStream(file));
            AssertTrue(loaded.Defaults.HeaderParagraphStyleId is null && loaded.Defaults.FooterParagraphStyleId is null, "no ids on load");
            var lh = loaded.Header[0];
            AssertTrue(loaded.GetEffectiveParagraphFormat(lh).SpaceAfterPt != 12, "resolved through Normal, not the file's Header (12pt after)");
            var saved = Save(loaded);
            AssertTrue(StyleOf(HeaderPartWith(saved, "Loaded header"), "Loaded header") == "none", "saved without pStyle");
            AssertTrue(!Part(saved, "word/styles.xml").Contains("w:pos=\"4513\""), "the file's own Header style, not Chuvadi's");

            var built = new Document();
            built.Header.Add(new Paragraph("round trip"));
            var once = Save(built);
            var reloaded = Document.Load(new MemoryStream(once));
            AssertTrue(reloaded.Header[0].StyleId == "Header", "loads with the Header style");
            AssertTrue(Spacing(reloaded.GetEffectiveParagraphFormat(reloaded.Header[0])) == "0|1", "and resolves it");
            var again = Save(reloaded);
            AssertTrue(HeaderPartWith(again, "round trip").ToString(SaveOptions.DisableFormatting) == HeaderPartWith(once, "round trip").ToString(SaveOptions.DisableFormatting), "the same header part");
        });
    }

    private static string Spacing(ParagraphFormat f)
        => (f.SpaceAfterPt?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-") + "|"
           + (f.LineSpacing is { } ls ? Math.Round(ls.Value, 4).ToString(System.Globalization.CultureInfo.InvariantCulture) : "-");

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

    private static string StyleXml(string styles, string id)
    {
        int i = styles.IndexOf($"w:styleId=\"{id}\"", StringComparison.Ordinal);
        if (i < 0) return "(none)";
        int start = styles.LastIndexOf("<w:style ", i, StringComparison.Ordinal);
        int end = styles.IndexOf("</w:style>", i, StringComparison.Ordinal);
        return styles[start..(end + 10)];
    }

    /// <summary>The header or footer part holding a paragraph with <paramref name="text"/>.</summary>
    private static XDocument HeaderPartWith(byte[] package, string text)
    {
        using var z = new ZipArchive(new MemoryStream(package));
        foreach (var e in z.Entries.Where(e => e.FullName.StartsWith("word/header") || e.FullName.StartsWith("word/footer")))
        {
            using var s = e.Open();
            var x = XDocument.Load(s);
            if (x.Descendants(Wx + "p").Any(p => Text(p) == text)) return x;
        }
        throw new InvalidOperationException("No header or footer part has the text " + text);
    }

    private static string Text(XElement p) => string.Concat(p.Descendants(Wx + "t").Select(t => t.Value));

    /// <summary>The pStyle of the paragraph holding <paramref name="text"/>, or "none".</summary>
    private static string StyleOf(XDocument part, string text)
    {
        var p = part.Descendants(Wx + "p").First(q => Text(q) == text);
        return (string?)p.Element(Wx + "pPr")?.Element(Wx + "pStyle")?.Attribute(Wx + "val") ?? "none";
    }

    /// <summary>A Word-like file whose styles have a Header style (12pt after) and whose header paragraph has no pStyle.</summary>
    private static byte[] Package()
    {
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string content)
            {
                using var w = new StreamWriter(z.CreateEntry(name).Open(), new UTF8Encoding(false));
                w.Write(content);
            }
            const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            Add("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                + "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>"
                + "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>"
                + "<Override PartName=\"/word/header1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml\"/></Types>");
            Add("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            Add("word/_rels/document.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + $"<Relationship Id=\"rIdS\" Type=\"{R}/styles\" Target=\"styles.xml\"/><Relationship Id=\"rIdH\" Type=\"{R}/header\" Target=\"header1.xml\"/></Relationships>");
            Add("word/styles.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:styles xmlns:w=\"{W}\"><w:docDefaults><w:rPrDefault/><w:pPrDefault/></w:docDefaults>"
                + "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>"
                + "<w:style w:type=\"paragraph\" w:styleId=\"Header\"><w:name w:val=\"header\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:spacing w:after=\"240\"/></w:pPr></w:style></w:styles>");
            Add("word/header1.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:hdr xmlns:w=\"{W}\"><w:p><w:r><w:t>Loaded header</w:t></w:r></w:p></w:hdr>");
            Add("word/document.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"{W}\" xmlns:r=\"{R}\"><w:body><w:p><w:r><w:t>Body</w:t></w:r></w:p>"
                + "<w:sectPr><w:headerReference w:type=\"default\" r:id=\"rIdH\"/></w:sectPr></w:body></w:document>");
        }
        return ms.ToArray();
    }
}
