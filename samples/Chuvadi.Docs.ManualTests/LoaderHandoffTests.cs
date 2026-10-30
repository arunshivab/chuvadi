using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Chuvadi.Docs.Word;
using static Chuvadi.Docs.ManualTests.Program;

namespace Chuvadi.Docs.ManualTests;

/// <summary>
/// Phase 7 loader additions requested by Chuvadi.Layout: hyperlink targets on loaded runs,
/// positional tabs, whether the page size was stated, Word-only main parts, and the default
/// tab stop.
/// </summary>
public static class LoaderHandoffTests
{
    private static readonly XNamespace Wn = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Rn = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Rels = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace Ct = "http://schemas.openxmlformats.org/package/2006/content-types";

    public static void Run(string outDir)
    {
        Hyperlinks(outDir);
        PositionalTabs(outDir);
        SizeStated(outDir);
        MainPart(outDir);
        DefaultTabStop(outDir);
    }

    private static void EditPart(string path, string part, Func<string, string> edit)
    {
        var bytes = File.ReadAllBytes(path);
        using var ms = new MemoryStream();
        ms.Write(bytes, 0, bytes.Length);
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            var e = zip.GetEntry(part)!;
            string xml;
            using (var r = new StreamReader(e.Open())) xml = r.ReadToEnd();
            e.Delete();
            var n = zip.CreateEntry(part);
            using var w = new StreamWriter(n.Open(), new UTF8Encoding(false));
            w.Write(edit(xml));
        }
        File.WriteAllBytes(path, ms.ToArray());
    }

    private static void Hyperlinks(string outDir)
    {
        Check("loaded runs carry the hyperlink target (body, table cells, headers); a fragment is appended; no style is implied", () =>
        {
            var d = new Document();
            d.AddParagraph("See ").Hyperlink("the site", "https://example.com/a");
            d.Header.Add(new Paragraph("H ").Hyperlink("head", "https://example.com/h"));
            var t = d.AddTable(1);
            t.AddRow("cell");
            t.Rows[0].Cells[0].Paragraphs[0].Hyperlink(" link", "https://example.com/c");
            d.AddParagraph("plain");
            var path = Path.Combine(outDir, "handoff-links.docx");
            d.SaveTo(path);
            // A link with no rStyle and an anchor fragment, as other producers write it.
            EditPart(path, "word/document.xml", xml =>
            {
                var x = XElement.Parse(xml);
                var plain = x.Descendants(Wn + "p").Single(p => string.Concat(p.Descendants(Wn + "t").Select(e => e.Value)) == "plain");
                var run = plain.Element(Wn + "r")!;
                var rid = x.Descendants(Wn + "hyperlink").First().Attribute(Rn + "id")!.Value;
                run.ReplaceWith(new XElement(Wn + "hyperlink", new XAttribute(Rn + "id", rid), new XAttribute(Wn + "anchor", "part2"), new XElement(run)));
                return x.ToString(SaveOptions.DisableFormatting);
            });
            var loaded = Document.Load(path);
            var paras = loaded.Blocks.OfType<Paragraph>().ToList();
            var link = paras[0].Runs.Single(r => r.TextContent == "the site");
            AssertTrue(link.HyperlinkUrl == "https://example.com/a" && link.CharacterStyleId == "Hyperlink", $"{link.HyperlinkUrl} {link.CharacterStyleId}");
            AssertTrue(paras[0].Runs.Single(r => r.TextContent == "See ").HyperlinkUrl is null, "plain run");
            var frag = paras.Last().Runs.Single();
            AssertTrue(frag.HyperlinkUrl == "https://example.com/a#part2" && frag.CharacterStyleId is null, $"{frag.HyperlinkUrl}");
            var eff = loaded.GetEffectiveRunFormat(paras.Last(), frag);
            AssertTrue(eff.ColorHex is null && eff.EffectiveUnderline is null, "no implied Hyperlink style on a loaded link");
            var cellRun = loaded.Blocks.OfType<DocTable>().Single().Rows[0].Cells[0].Paragraphs[0].Runs.Single(r => r.TextContent == " link");
            AssertTrue(cellRun.HyperlinkUrl == "https://example.com/c", "cell link");
            AssertTrue(loaded.Header.SelectMany(p => p.Runs).Single(r => r.TextContent == "head").HyperlinkUrl == "https://example.com/h", "header link");

            // Round trip: the loaded link is written as a link again, still without a style.
            var again = Path.Combine(outDir, "handoff-links-again.docx");
            loaded.SaveTo(again);
            var x2 = XElement.Parse(ReadPartXml(again, "word/document.xml"));
            AssertTrue(x2.Descendants(Wn + "hyperlink").Count() == 3, "three links written");
            var reloaded = Document.Load(again).Blocks.OfType<Paragraph>().Last().Runs.Single();
            AssertTrue(reloaded.HyperlinkUrl == "https://example.com/a#part2" && reloaded.CharacterStyleId is null, "round trip");
        });
    }

    private static void PositionalTabs(string outDir)
    {
        Check("positional tabs are runs with alignment, base and leader; written, loaded and read as a tab", () =>
        {
            var d = new Document();
            d.AddParagraph("Left").PositionalTab(PositionalTabAlignment.Right, PositionalTabBase.Margin, TabLeader.Dot).Text("Right");
            d.AddParagraph("A").PositionalTab(PositionalTabAlignment.Center, PositionalTabBase.Indent);
            var path = Path.Combine(outDir, "handoff-ptab.docx");
            d.SaveTo(path);
            var ptab = XElement.Parse(ReadPartXml(path, "word/document.xml")).Descendants(Wn + "ptab").First();
            AssertTrue((string)ptab.Attribute(Wn + "alignment")! == "right" && (string)ptab.Attribute(Wn + "relativeTo")! == "margin" && (string)ptab.Attribute(Wn + "leader")! == "dot", ptab.ToString());
            var loaded = Document.Load(path).Blocks.OfType<Paragraph>().ToList();
            AssertTrue(loaded[0].GetText() == "Left\tRight", loaded[0].GetText());
            var run = loaded[0].Runs[1];
            AssertTrue(run.PositionalTab == new PositionalTab(PositionalTabAlignment.Right, PositionalTabBase.Margin, TabLeader.Dot) && !run.IsTab, $"{run.PositionalTab}");
            AssertTrue(loaded[1].Runs[1].PositionalTab == new PositionalTab(PositionalTabAlignment.Center, PositionalTabBase.Indent, TabLeader.None), "second");
            using (var reader = DocxReader.Open(path))
                AssertTrue(reader.Paragraphs().First().Text == "Left\tRight", "DocxReader text");
            AssertThrows<ArgumentOutOfRangeException>(() => new Paragraph().PositionalTab(PositionalTabAlignment.Left, leader: TabLeader.Heavy), "heavy leader");
        });
    }

    private static void SizeStated(string outDir)
    {
        Check("PageSetup.SizeStated: true in code; false when a loaded section has no w:pgSz (Size stays A4, no pgSz is written back)", () =>
        {
            var d = new Document();
            AssertTrue(d.Page.SizeStated, "new document");
            d.AddParagraph("x");
            var path = Path.Combine(outDir, "handoff-nosize.docx");
            d.SaveTo(path);
            EditPart(path, "word/document.xml", xml =>
            {
                var x = XElement.Parse(xml);
                x.Descendants(Wn + "pgSz").Remove();
                return x.ToString(SaveOptions.DisableFormatting);
            });
            var loaded = Document.Load(path);
            AssertTrue(!loaded.Page.SizeStated && loaded.Page.Size == PageSize.A4, $"{loaded.Page.SizeStated} {loaded.Page.Size}");
            var again = Path.Combine(outDir, "handoff-nosize-again.docx");
            loaded.SaveTo(again);
            AssertTrue(!ReadPartXml(again, "word/document.xml").Contains("pgSz"), "nothing stated, nothing written");
            loaded.Page.Size = PageSize.Letter;
            AssertTrue(loaded.Page.SizeStated, "setting a size states it");

            // An unusable pgSz (orientation and code, no width/height) states nothing either.
            var bad = Path.Combine(outDir, "handoff-badsize.docx");
            d.SaveTo(bad);
            EditPart(bad, "word/document.xml", xml =>
            {
                var x = XElement.Parse(xml);
                foreach (var g in x.Descendants(Wn + "pgSz")) { g.RemoveAttributes(); g.SetAttributeValue(Wn + "orient", "landscape"); g.SetAttributeValue(Wn + "code", "9"); }
                return x.ToString(SaveOptions.DisableFormatting);
            });
            var badDoc = Document.Load(bad);
            AssertTrue(!badDoc.Page.SizeStated && badDoc.Page.Orientation == PageOrientation.Portrait && badDoc.Page.PaperCode is null, "nothing taken");
            var badAgain = Path.Combine(outDir, "handoff-badsize-again.docx");
            badDoc.SaveTo(badAgain);
            AssertTrue(!ReadPartXml(badAgain, "word/document.xml").Contains("pgSz"), "no invented pgSz");

            var letter = new Document();
            letter.Page.Size = PageSize.Letter;
            letter.Page.PaperCode = 1;
            letter.AddParagraph("x");
            var lp = Path.Combine(outDir, "handoff-letter.docx");
            letter.SaveTo(lp);
            var l2 = Document.Load(lp);
            AssertTrue(l2.Page.SizeStated && l2.Page.Size == PageSize.Letter && l2.Page.PaperCode == 1, $"{l2.Page.Size} {l2.Page.PaperCode}");
        });
    }

    private static void MainPart(string outDir)
    {
        Check("a package whose main part is not a Word document is rejected; templates and macro-enabled parts load", () =>
        {
            var d = new Document();
            d.AddParagraph("x");
            var good = Path.Combine(outDir, "handoff-main.docx");
            d.SaveTo(good);
            string WithType(string name, string type)
            {
                var p = Path.Combine(outDir, name);
                File.Copy(good, p, overwrite: true);
                EditPart(p, "[Content_Types].xml", xml =>
                {
                    var x = XElement.Parse(xml);
                    var o = x.Elements(Ct + "Override").Single(e => (string)e.Attribute("PartName")! == "/word/document.xml");
                    o.SetAttributeValue("ContentType", type);
                    return x.ToString(SaveOptions.DisableFormatting);
                });
                return p;
            }
            var sheet = WithType("handoff-main-xlsx.docx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
            AssertThrows<DocxFormatException>(() => Document.Load(sheet), "Document.Load");
            AssertThrows<DocxFormatException>(() => DocxReader.Open(sheet).Dispose(), "DocxReader.Open");
            AssertThrows<DocxFormatException>(() => Document.Load(WithType("handoff-main-xml.docx", "application/xml")), "plain xml");
            Document.Load(WithType("handoff-main-dotx.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.template.main+xml"));
            Document.Load(WithType("handoff-main-docm.docx", "application/vnd.ms-word.document.macroEnabled.main+xml"));
            Document.Load(WithType("handoff-main-dotm.docx", "application/vnd.ms-word.template.macroEnabledTemplate.main+xml"));
        });
    }

    private static void DefaultTabStop(string outDir)
    {
        Check("Document.DefaultTabStopPt: null unless stated (effective 36pt); written in settings order and loaded", () =>
        {
            var d = new Document();
            AssertTrue(d.DefaultTabStopPt is null && d.EffectiveDefaultTabStopPt == 36, "default");
            d.AddParagraph("x");
            var plain = Path.Combine(outDir, "handoff-tabs-none.docx");
            d.SaveTo(plain);
            AssertTrue(!ReadPartXml(plain, "word/settings.xml").Contains("defaultTabStop"), "not written when null");
            AssertTrue(Document.Load(plain).DefaultTabStopPt is null, "loads null");

            d.DefaultTabStopPt = 18;
            d.DifferentOddAndEvenPages = true;
            d.Protect("pw");
            var path = Path.Combine(outDir, "handoff-tabs.docx");
            d.SaveTo(path);
            var names = XElement.Parse(ReadPartXml(path, "word/settings.xml")).Elements().Select(e => e.Name.LocalName).ToList();
            AssertTrue(names.IndexOf("documentProtection") < names.IndexOf("defaultTabStop") && names.IndexOf("defaultTabStop") < names.IndexOf("evenAndOddHeaders"), string.Join(",", names));
            AssertTrue(XElement.Parse(ReadPartXml(path, "word/settings.xml")).Element(Wn + "defaultTabStop")!.Attribute(Wn + "val")!.Value == "360", "twips");
            var loaded = Document.Load(path);
            AssertTrue(loaded.DefaultTabStopPt == 18 && loaded.EffectiveDefaultTabStopPt == 18, $"{loaded.DefaultTabStopPt}");
            AssertThrows<ArgumentOutOfRangeException>(() => d.DefaultTabStopPt = 0, "zero");
        });
    }
}
