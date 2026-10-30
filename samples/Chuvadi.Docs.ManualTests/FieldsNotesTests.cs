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
/// Phase 8 — bookmarks, internal links, fields (DATE, REF, PAGEREF, SEQ, TOC, any instruction),
/// footnotes and endnotes with their numbering, symbols, document properties and Document.Append.
/// </summary>
public static class FieldsNotesTests
{
    private static readonly XNamespace Wn = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static void Run(string outDir)
    {
        BookmarksAndLinks(outDir);
        Fields(outDir);
        Notes(outDir);
        PropertiesRoundTrip(outDir);
        Append(outDir);
        LegacyUnchanged(outDir);
        FloatingTables(outDir);
        EmbeddedFonts(outDir);
        TextBoxes(outDir);
        ReviewRegressions(outDir);
    }

    private static string Save(string outDir, string name, Document d)
    {
        var p = Path.Combine(outDir, name + ".docx");
        d.SaveTo(p);
        return p;
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

    private static void BookmarksAndLinks(string outDir)
    {
        Check("bookmarks (also spanning paragraphs) and internal links are written, loaded and round-trip", () =>
        {
            var d = new Document();
            d.AddParagraph("See ").InternalLink("the results", "Results").Text(".");
            var span = new Bookmark("Span");
            d.AddParagraph().BookmarkStart(span).Text("first");
            d.AddParagraph("second").BookmarkEnd(span);
            d.AddHeading("Results", 1).Bookmarked("Results", "");
            var path = Save(outDir, "p8-bookmarks", d);
            var x = XElement.Parse(ReadPartXml(path, "word/document.xml"));
            AssertTrue(x.Descendants(Wn + "hyperlink").Single().Attribute(Wn + "anchor")!.Value == "Results", "anchor written");
            var starts = x.Descendants(Wn + "bookmarkStart").ToList();
            AssertTrue(starts.Count == 2 && starts.Select(s => s.Attribute(Wn + "id")!.Value).Distinct().Count() == 2, "two bookmarks");

            var loaded = Document.Load(path);
            var names = loaded.GetBookmarks().Select(b => b.Name).ToList();
            AssertTrue(names.SequenceEqual(new[] { "Span", "Results" }), string.Join(",", names));
            var link = loaded.Blocks.OfType<Paragraph>().First().Runs.Single(r => r.IsLink);
            AssertTrue(link.HyperlinkAnchor == "Results" && link.HyperlinkUrl is null && link.TextContent == "the results", "internal link loaded");
            var paras = loaded.Blocks.OfType<Paragraph>().ToList();
            AssertTrue(paras[1].Runs[0].Bookmark is { IsStart: true } s0 && paras[2].Runs.Last().Bookmark is { IsStart: false } e0
                       && ReferenceEquals(s0.Bookmark, e0.Bookmark), "span across paragraphs");
            AssertTrue(paras[1].GetText() == "first", "markers carry no text");
            var again = Path.Combine(outDir, "p8-bookmarks-again.docx");
            loaded.SaveTo(again);
            AssertTrue(ReadPartXml(again, "word/document.xml") == ReadPartXml(path, "word/document.xml"), "load → save reproduces document.xml");
        });
        Check("body-level and table-level bookmarks load onto the next paragraph; a link tooltip loads", () =>
        {
            var d = new Document();
            d.AddParagraph("before");
            var t = d.AddTable(1);
            t.AddRow("cell");
            d.AddParagraph("after");
            var path = Save(outDir, "p8-bodymarks", d);
            EditPart(path, "word/document.xml", xml =>
            {
                var x = XElement.Parse(xml);
                var tbl = x.Descendants(Wn + "tbl").Single();
                tbl.AddBeforeSelf(new XElement(Wn + "bookmarkStart", new XAttribute(Wn + "id", "3"), new XAttribute(Wn + "name", "TableMark")));
                tbl.AddAfterSelf(new XElement(Wn + "bookmarkEnd", new XAttribute(Wn + "id", "3")));
                tbl.Element(Wn + "tr")!.AddFirst(new XElement(Wn + "bookmarkStart", new XAttribute(Wn + "id", "4"), new XAttribute(Wn + "name", "RowMark")),
                    new XElement(Wn + "bookmarkEnd", new XAttribute(Wn + "id", "4")));
                var p0 = x.Descendants(Wn + "p").First();
                var r0 = p0.Element(Wn + "r")!;
                r0.ReplaceWith(new XElement(Wn + "hyperlink", new XAttribute(Wn + "anchor", "TableMark"), new XAttribute(Wn + "tooltip", "Go to table"), new XElement(r0)));
                return x.ToString(SaveOptions.DisableFormatting);
            });
            var loaded = Document.Load(path);
            AssertTrue(loaded.GetBookmarks().Select(b => b.Name).SequenceEqual(new[] { "TableMark", "RowMark" }), string.Join(",", loaded.GetBookmarks()));
            var link = loaded.Blocks.OfType<Paragraph>().First().Runs.Single();
            AssertTrue(link.HyperlinkTooltip == "Go to table" && link.HyperlinkAnchor == "TableMark", "tooltip");
            var cellPara = loaded.Blocks.OfType<DocTable>().Single().Rows[0].Cells[0].Paragraphs[0];
            AssertTrue(cellPara.Runs.Count(r => r.Bookmark is not null) == 3 && cellPara.GetText() == "cell", "markers at the cell paragraph start");
            var again = Path.Combine(outDir, "p8-bodymarks-again.docx");
            loaded.SaveTo(again);
            AssertTrue(Document.Load(again).GetBookmarks().Count == 2, "round trip");
        });
        Check("bookmark names follow Word's rules", () =>
        {
            AssertThrows<ArgumentException>(() => new Bookmark("has space"), "space");
            AssertThrows<ArgumentException>(() => new Bookmark(new string('a', 41)), "too long");
            AssertTrue(new Bookmark("_Toc1").IsHidden, "hidden");
        });
    }

    private static void Fields(string outDir)
    {
        Check("fields: markers with instruction and cached result; DATE / REF / PAGEREF / SEQ helpers; TOC; nested and multi-paragraph fields load", () =>
        {
            var d = new Document();
            d.AddParagraph("Total: ").Bookmarked("Total", "1,234");
            d.AddParagraph("See ").Ref("Total", "1,234").Text(" on page ").PageRef("Total");
            d.AddParagraph("Figure ").Seq("Figure", "1");
            d.AddParagraph("Printed ").Date("dd/MM/yyyy");
            d.AddParagraph().Field("MERGEFIELD Name", "«Name»", new TextFormat { Bold = true });
            d.AddTableOfContents(1, 2);
            var path = Save(outDir, "p8-fields", d);
            var x = XElement.Parse(ReadPartXml(path, "word/document.xml"));
            var instr = x.Descendants(Wn + "instrText").Select(e => e.Value.Trim()).ToList();
            AssertTrue(instr.Contains("REF Total \\h") && instr.Contains("PAGEREF Total \\h") && instr.Contains("SEQ Figure \\* ARABIC")
                       && instr.Any(i => i.StartsWith("DATE \\@ \"dd/MM/yyyy\"")) && instr.Contains("TOC \\o \"1-2\" \\h \\z \\u"), string.Join(" | ", instr));
            AssertTrue(x.Descendants(Wn + "fldChar").Any(f => (string?)f.Attribute(Wn + "dirty") == "1"), "dirty field");
            AssertContains(ReadPartXml(path, "word/settings.xml"), "updateFields", "update on open");
            var dateText = d.Blocks.OfType<Paragraph>().ElementAt(3).GetText();
            AssertTrue(dateText == "Printed " + DateTime.Now.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture), dateText);

            var loaded = Document.Load(path);
            var refPara = loaded.Blocks.OfType<Paragraph>().ElementAt(1);
            var begins = refPara.Runs.Where(r => r.FieldMark is { Type: FieldMarkType.Begin }).Select(r => r.FieldMark!.Field).ToList();
            AssertTrue(begins.Count == 2 && begins[0].FieldType == "REF" && begins[0].Argument == "Total" && begins[1].FieldType == "PAGEREF", "fields loaded");
            AssertTrue(refPara.GetText() == "See 1,234 on page ", refPara.GetText());
            var merge = loaded.Blocks.OfType<Paragraph>().ElementAt(4);
            AssertTrue(merge.Runs.First().FieldMark!.Field.Argument == "Name" && merge.GetText() == "«Name»", "merge field");
            var again = Path.Combine(outDir, "p8-fields-again.docx");
            loaded.SaveTo(again);
            AssertTrue(ReadPartXml(again, "word/document.xml") == ReadPartXml(path, "word/document.xml"), "load → save reproduces document.xml");
            AssertTrue(loaded.UpdateFieldsOnOpen, "updateFields loaded");
        });
        Check("a field spanning paragraphs, a nested field in an instruction, simple fields and PAGE fields load as expected", () =>
        {
            var d = new Document();
            d.AddParagraph("x");
            var path = Save(outDir, "p8-fieldshapes", d);
            EditPart(path, "word/document.xml", xml =>
            {
                string R(string inner) => "<w:r>" + inner + "</w:r>";
                var body = "<w:p>" + R("<w:fldChar w:fldCharType=\"begin\"/>") + R("<w:instrText xml:space=\"preserve\"> TOC \\o \"1-3\" </w:instrText>")
                    + R("<w:fldChar w:fldCharType=\"separate\"/>") + R("<w:t>Entry one</w:t>") + "</w:p>"
                    + "<w:p>" + R("<w:t>Entry two</w:t>") + R("<w:fldChar w:fldCharType=\"end\"/>") + "</w:p>"
                    + "<w:p>" + R("<w:fldChar w:fldCharType=\"begin\"/>") + R("<w:instrText xml:space=\"preserve\"> IF </w:instrText>")
                    + R("<w:fldChar w:fldCharType=\"begin\"/>") + R("<w:instrText> MERGEFIELD Sex </w:instrText>") + R("<w:fldChar w:fldCharType=\"separate\"/>")
                    + R("<w:t>F</w:t>") + R("<w:fldChar w:fldCharType=\"end\"/>") + R("<w:instrText xml:space=\"preserve\"> = \"F\" \"Ms\" \"Mr\" </w:instrText>")
                    + R("<w:fldChar w:fldCharType=\"separate\"/>") + R("<w:t>Ms</w:t>") + R("<w:fldChar w:fldCharType=\"end\"/>") + "</w:p>"
                    + "<w:p><w:fldSimple w:instr=\" AUTHOR \">" + R("<w:t>Arun</w:t>") + "</w:fldSimple>"
                    + "<w:fldSimple w:instr=\" PAGE \">" + R("<w:t>7</w:t>") + "</w:fldSimple></w:p>";
                return xml.Replace("<w:body>", "<w:body>" + body);
            });
            var paras = Document.Load(path).Blocks.OfType<Paragraph>().ToList();
            var b = paras[0].Runs[0].FieldMark;
            AssertTrue(b is { Type: FieldMarkType.Begin } && b.Field.FieldType == "TOC" && paras[0].GetText() == "Entry one", "toc begins");
            AssertTrue(paras[1].Runs.Last().FieldMark is { Type: FieldMarkType.End } en && ReferenceEquals(en.Field, b!.Field), "ends in the next paragraph");
            var iff = paras[2].Runs[0].FieldMark!.Field;
            AssertTrue(iff.FieldType == "IF" && iff.Instruction.Contains("F = \"F\"") && paras[2].GetText() == "Ms", iff.Instruction);
            AssertTrue(paras[3].Runs[0].FieldMark!.Field.FieldType == "AUTHOR" && paras[3].GetText().StartsWith("Arun"), "simple field");
            AssertTrue(paras[3].Runs.Any(r => r.FieldInstruction == " PAGE " && r.TextContent == "7"), "PAGE stays a field run");
            var again = Path.Combine(outDir, "p8-fieldshapes-again.docx");
            Document.Load(path).SaveTo(again);
            var x = XElement.Parse(ReadPartXml(again, "word/document.xml"));
            AssertTrue(x.Descendants(Wn + "fldSimple").Count() == 2, "simple fields written back as simple fields");
            AssertTrue(Document.Load(again).Blocks.OfType<Paragraph>().First().Runs[0].FieldMark!.Field.FieldType == "TOC", "reloads");
        });
    }

    private static void Notes(string outDir)
    {
        Check("footnotes and endnotes: parts, styles, separators, references and numbering", () =>
        {
            var d = new Document();
            d.AddParagraph("Claim one").Footnote(d, "Source A.");
            d.AddParagraph("Claim two").Footnote(d, "Source B.").Text(" and ").Endnote(d, "Later note.");
            var custom = d.Footnotes.Add("Starred note.");
            d.AddParagraph("Starred").NoteReference(custom, "*");
            var t = d.AddTable(1);
            t.AddRow("in a cell").Cells[0].Paragraphs[0].Footnote(d, "Cell note.");
            d.FootnoteProperties.NumberFormat = NumberFormat.LowerLetter;
            var path = Save(outDir, "p8-notes", d);
            var fx = XElement.Parse(ReadPartXml(path, "word/footnotes.xml"));
            var notes = fx.Elements(Wn + "footnote").ToList();
            AssertTrue(notes.Count == 6 && (string?)notes[0].Attribute(Wn + "type") == "separator", $"{notes.Count} footnote elements");
            AssertTrue(notes.Skip(2).All(n => n.Descendants(Wn + "footnoteRef").Any()), "note marks");
            AssertContains(ReadPartXml(path, "word/styles.xml"), "FootnoteReference", "styles");
            var settings = ReadPartXml(path, "word/settings.xml");
            AssertContains(settings, "<w:footnotePr><w:numFmt w:val=\"lowerLetter\" /><w:footnote w:id=\"-1\" />", "settings footnotePr");

            var refs = d.GetNoteReferences();
            AssertTrue(refs.Select(r => r.Label).SequenceEqual(new[] { "a", "b", "i", "*", "c" }), string.Join(",", refs.Select(r => r.Label)));

            var loaded = Document.Load(path);
            AssertTrue(loaded.Footnotes.Count == 4 && loaded.Endnotes.Count == 1, "notes loaded");
            AssertTrue(loaded.Footnotes[1].GetText() == " Source B." && loaded.Footnotes[1].Paragraphs[0].Runs[0].IsNoteMark, loaded.Footnotes[1].GetText());
            AssertTrue(loaded.FootnoteProperties.NumberFormat == NumberFormat.LowerLetter, "props");
            var lrefs = loaded.GetNoteReferences();
            AssertTrue(lrefs.Select(r => r.Label).SequenceEqual(new[] { "a", "b", "i", "*", "c" }) && ReferenceEquals(lrefs[0].Note, loaded.Footnotes[0]), "loaded refs");
            var again = Path.Combine(outDir, "p8-notes-again.docx");
            loaded.SaveTo(again);
            foreach (var part in new[] { "word/document.xml", "word/footnotes.xml", "word/endnotes.xml", "word/settings.xml" })
                AssertTrue(ReadPartXml(again, part) == ReadPartXml(path, part), "load → save reproduces " + part);
        });
        Check("note numbering restarts per section; a removed note leaves no dangling reference; pictures in notes are placed", () =>
        {
            var d = new Document();
            d.AddParagraph("one").Footnote(d, "n1");
            d.AddParagraph("two").Footnote(d, "n2");
            var s2 = d.AddSectionBreak(SectionStart.NextPage);
            s2.FootnoteProperties.Restart = NoteRestart.EachSection;
            d.AddParagraph("three").Footnote(d, "n3");
            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
            d.Footnotes[2].Paragraphs[0].Image(new ImageSpec { Bytes = png, ContentType = "image/png", WidthPt = 5, HeightPt = 5 });
            AssertTrue(d.GetNoteReferences().Select(r => r.Number).SequenceEqual(new[] { 1, 2, 1 }), string.Join(",", d.GetNoteReferences().Select(r => r.Number)));
            var placed = d.GetImagePlacements().Single();
            AssertTrue(placed.Host == ImageHost.Footnote && ReferenceEquals(placed.Note, d.Footnotes[2]) && ReferenceEquals(placed.Section, s2), "placement");
            var path = Save(outDir, "p8-notes-sections", d);
            var loaded = Document.Load(path);
            AssertTrue(loaded.Sections[1].FootnoteProperties.Restart == NoteRestart.EachSection && loaded.GetImagePlacements().Single().Host == ImageHost.Footnote, "loaded");
            d.Footnotes.Remove(d.Footnotes[0]);
            var removed = Save(outDir, "p8-notes-removed", d);
            AssertTrue(XElement.Parse(ReadPartXml(removed, "word/document.xml")).Descendants(Wn + "footnoteReference").Count() == 2, "no dangling reference");
        });
        Check("symbols load and save as w:sym", () =>
        {
            var d = new Document();
            d.AddParagraph("Done ").Symbol("Wingdings", '');
            var path = Save(outDir, "p8-symbol", d);
            AssertContains(ReadPartXml(path, "word/document.xml"), "<w:sym w:font=\"Wingdings\" w:char=\"F0FC\" />", "written");
            var run = Document.Load(path).Blocks.OfType<Paragraph>().Single().Runs.Last();
            AssertTrue(run.Symbol == new SymbolChar("Wingdings", ''), "loaded");
        });
    }

    private static void PropertiesRoundTrip(string outDir)
    {
        Check("document properties: core, app and custom, written and loaded", () =>
        {
            var d = new Document();
            d.AddParagraph("x");
            d.Properties.Title = "Discharge summary";
            d.Properties.Author = "Dr. Arun";
            d.Properties.Keywords = "oncology; summary";
            d.Properties.Company = "Apulki";
            d.Properties.Created = new DateTime(2026, 10, 1, 9, 30, 0, DateTimeKind.Utc);
            d.Properties.SetCustom("PatientId", "P-0042").SetCustom("Pages", 3).SetCustom("Urgent", true).SetCustom("Dose", 1.5);
            var path = Save(outDir, "p8-props", d);
            AssertContains(ReadPartXml(path, "docProps/core.xml"), "<dc:title>Discharge summary</dc:title>", "title");
            AssertContains(ReadPartXml(path, "docProps/custom.xml"), "<vt:lpwstr>P-0042</vt:lpwstr>", "custom");
            var p = Document.Load(path).Properties;
            AssertTrue(p.Title == "Discharge summary" && p.Author == "Dr. Arun" && p.Company == "Apulki" && p.Keywords == "oncology; summary", "core/app");
            AssertTrue(p.Created == new DateTime(2026, 10, 1, 9, 30, 0, DateTimeKind.Utc), $"{p.Created:o}");
            AssertTrue((string)p.GetCustom("PatientId")! == "P-0042" && (int)p.GetCustom("Pages")! == 3 && (bool)p.GetCustom("Urgent")! && (double)p.GetCustom("Dose")! == 1.5, "custom values");
            AssertThrows<ArgumentException>(() => p.SetCustom("Bad", new object()), "type check");
        });
    }

    private static void Append(string outDir)
    {
        Check("Append copies content, lists, notes and missing styles; renames clashing bookmarks with their links and REF fields", () =>
        {
            var a = new Document();
            a.AddParagraph("A1").Bookmarked("Intro", "");
            var la = a.Numbering.AddNumberedList();
            a.AddListItem("a-one", la);

            var b = new Document();
            var style = b.Styles.Add(new StyleDefinition("Remark", StyleType.Paragraph) { Name = "Remark", BasedOn = "Normal", RunFormat = new TextFormat { Italic = true } });
            b.AddParagraph(new Paragraph("B remark") { StyleId = "Remark" });
            b.AddParagraph("B1").Bookmarked("Intro", "");
            b.AddParagraph("go ").InternalLink("intro", "Intro").Text(" ").Ref("Intro", "B1");
            var lb = b.Numbering.AddNumberedList();
            b.AddListItem("b-one", lb);
            b.AddListItem("b-two", lb);
            b.AddParagraph("noted").Footnote(b, "B's note");

            a.Append(b, new AppendOptions { StartSection = SectionStart.NextPage });
            AssertTrue(b.Blocks.Count == 6 && b.Footnotes.Count == 1, "source unchanged");
            AssertTrue(a.Styles.Contains("Remark") && a.Footnotes.Count == 1 && a.Sections.Count == 2, "styles, notes, section");
            var names = a.GetBookmarks().Select(x => x.Name).ToList();
            AssertTrue(names.SequenceEqual(new[] { "Intro", "Intro_2" }), string.Join(",", names));
            var go = a.Blocks.OfType<Paragraph>().Single(p => p.GetText().StartsWith("go "));
            AssertTrue(go.Runs.Single(r => r.IsLink).HyperlinkAnchor == "Intro_2", "link renamed");
            AssertTrue(go.Runs.First(r => r.FieldMark is { Type: FieldMarkType.Begin }).FieldMark!.Field.Argument == "Intro_2", "REF renamed");
            var labels = a.GetListLabels();
            var texts = a.Blocks.OfType<Paragraph>().Where(p => labels.ContainsKey(p)).Select(p => labels[p].Text + p.GetText()).ToList();
            AssertTrue(texts.SequenceEqual(new[] { "1.a-one", "1.b-one", "2.b-two" }), string.Join(" | ", texts));
            var path = Save(outDir, "p8-append", a);
            var loaded = Document.Load(path);
            AssertTrue(loaded.Footnotes.Single().GetText().EndsWith("B's note") && loaded.GetNoteReferences().Count == 1, "notes survive save");

            var c = new Document();
            c.Styles.Add(new StyleDefinition("Remark", StyleType.Paragraph) { Name = "Remark", RunFormat = new TextFormat { Bold = true } });
            c.AddParagraph(new Paragraph("C remark") { StyleId = "Remark" });
            c.Append(b, new AppendOptions { KeepSourceStyles = true });
            var renamed = c.Blocks.OfType<Paragraph>().Single(p => p.GetText() == "B remark").StyleId;
            AssertTrue(renamed == "Remark_2" && c.Styles["Remark_2"].RunFormat!.Italic && c.Styles["Remark"].RunFormat!.Bold, renamed);
        });
    }

    private static void LegacyUnchanged(string outDir)
    {
        Check("documents without Phase 8 content save as before (no notes parts, same settings)", () =>
        {
            var d = new Document();
            d.AddHeading("Title", 1);
            d.AddParagraph("Body ").Hyperlink("link", "https://example.com");
            var path = Save(outDir, "p8-legacy", d);
            using var z = ZipFile.OpenRead(path);
            AssertTrue(!z.Entries.Any(e => e.FullName.Contains("footnotes") || e.FullName.Contains("custom.xml")), "no new parts");
            AssertTrue(ReadPartXml(path, "word/settings.xml").EndsWith("<w:settings xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" />")
                       || !ReadPartXml(path, "word/settings.xml").Contains("footnotePr"), "settings unchanged");
        });
    }

    private static void FloatingTables(string outDir)
    {
        Check("floating tables: w:tblpPr and w:tblOverlap are typed, written in schema order and loaded", () =>
        {
            var d = new Document();
            var t = d.AddTable(2);
            t.AddRow("a", "b");
            t.Format.FloatingPosition = new TableFloatingPosition
            {
                HorizontalAnchor = TableFloatAnchor.Page, VerticalAnchor = TableFloatAnchor.Text, XAlign = TableFloatXAlign.Right,
                YPt = 12, LeftFromTextPt = 9, RightFromTextPt = 9,
            };
            t.Format.AllowOverlap = false;
            var path = Save(outDir, "p8-floattable", d);
            var x = XElement.Parse(ReadPartXml(path, "word/document.xml"));
            var tblPr = x.Descendants(Wn + "tblPr").Single();
            var names = tblPr.Elements().Select(e => e.Name.LocalName).ToList();
            AssertTrue(names.IndexOf("tblpPr") < names.IndexOf("tblOverlap") && names.IndexOf("tblOverlap") < names.IndexOf("tblW"), string.Join(",", names));
            var pp = tblPr.Element(Wn + "tblpPr")!;
            AssertTrue((string)pp.Attribute(Wn + "horzAnchor")! == "page" && (string)pp.Attribute(Wn + "tblpXSpec")! == "right" && (string)pp.Attribute(Wn + "tblpY")! == "240", pp.ToString());
            var lt = Document.Load(path).Blocks.OfType<DocTable>().Single();
            var fp = lt.Format.FloatingPosition!;
            AssertTrue(fp.HorizontalAnchor == TableFloatAnchor.Page && fp.XAlign == TableFloatXAlign.Right && fp.YPt == 12 && fp.LeftFromTextPt == 9 && lt.Format.AllowOverlap == false, "loaded");
            AssertTrue(Document.Load(path).GetEffectiveTableFormat(lt).FloatingPosition is not null, "effective");
            var again = Path.Combine(outDir, "p8-floattable-again.docx");
            Document.Load(path).SaveTo(again);
            AssertTrue(ReadPartXml(again, "word/document.xml") == ReadPartXml(path, "word/document.xml"), "round trip");
        });
    }

    private static void EmbeddedFonts(string outDir)
    {
        Check("embedded fonts: obfuscated .odttf parts with a font key; loaded back as the plain font bytes", () =>
        {
            var font = new byte[256];
            for (int i = 0; i < font.Length; i++) font[i] = (byte)(i * 7 + 3);
            var d = new Document();
            d.AddParagraph().Text("Styled", new TextFormat { Font = "Chuvadi Test Sans" });
            var info = d.EmbedFont("Chuvadi Test Sans", font);
            info.Embedded.Add(new EmbeddedFont(EmbeddedFontStyle.Bold, font) { Subsetted = true });
            info.Charset = "00";
            info.Family = "swiss";
            var path = Save(outDir, "p8-fonts", d);
            var ft = XElement.Parse(ReadPartXml(path, "word/fontTable.xml"));
            var embeds = ft.Descendants().Where(e => e.Name.LocalName.StartsWith("embed")).ToList();
            AssertTrue(embeds.Count == 2 && embeds.All(e => Guid.TryParse((string?)e.Attribute(Wn + "fontKey"), out _)), "embeds with keys");
            using (var z = ZipFile.OpenRead(path))
            {
                var part = z.Entries.Single(e => e.FullName == "word/fonts/font1.odttf");
                using var ms = new MemoryStream();
                using (var st = part.Open()) st.CopyTo(ms);
                var stored = ms.ToArray();
                AssertTrue(!stored.Take(32).SequenceEqual(font.Take(32)) && stored.Skip(32).SequenceEqual(font.Skip(32)), "first 32 bytes obfuscated only");
            }
            AssertContains(ReadPartXml(path, "word/settings.xml"), "embedTrueTypeFonts", "setting");
            var loaded = Document.Load(path);
            var li = loaded.Fonts.Single();
            AssertTrue(li.Name == "Chuvadi Test Sans" && li.Family == "swiss" && li.Embedded.Count == 2 && li.Embedded.All(e => e.Data.SequenceEqual(font)), "plain bytes back");
            AssertTrue(li.Embedded.Single(e => e.Style == EmbeddedFontStyle.Bold).Subsetted && loaded.EmbedTrueTypeFonts, "flags");
            var again = Path.Combine(outDir, "p8-fonts-again.docx");
            loaded.SaveTo(again);
            AssertTrue(ReadPartXml(again, "word/fontTable.xml") == ReadPartXml(path, "word/fontTable.xml"), "same keys, same table");
        });
    }

    private static void TextBoxes(string outDir)
    {
        Check("text boxes: geometry, insets, fill and outline, content with paragraphs; written as wps shapes and loaded", () =>
        {
            var d = new Document();
            var box = new TextBox
            {
                WidthPt = 200, HeightPt = 80, FillHex = "FFF2CC", LineHex = "BF9000", LineWidthPt = 1.5,
                Insets = new ImageEffectExtent(10, 5, 10, 5), VerticalAnchor = TextBoxVerticalAnchor.Center, Name = "Callout",
                Position = new FloatingPosition { HorizontalAnchor = HorizontalAnchor.Margin, HAlign = HorizontalAlignment.Right, VerticalAnchor = VerticalAnchor.Paragraph, VerticalOffsetPt = 6, Wrap = TextWrap.Square },
            };
            box.Paragraphs.Add(new Paragraph("Important:", TextFormat.BoldText));
            box.Paragraphs.Add(new Paragraph("Fasting from midnight."));
            d.AddParagraph("Instructions follow.").TextBox(box);
            d.AddParagraph("After the box.");
            var path = Save(outDir, "p8-textbox", d);
            var xml = ReadPartXml(path, "word/document.xml");
            AssertContains(xml, "Requires=\"wps\"", "AlternateContent");
            AssertContains(xml, "<w:txbxContent>", "content");
            var loaded = Document.Load(path);
            var lb = loaded.Blocks.OfType<Paragraph>().First().Runs.Single(r => r.TextBox is not null).TextBox!;
            AssertTrue(lb.GetText() == "Important:\nFasting from midnight." && lb.WidthPt == 200 && lb.HeightPt == 80, lb.GetText());
            AssertTrue(lb.FillHex == "FFF2CC" && lb.LineHex == "BF9000" && lb.LineWidthPt == 1.5 && lb.VerticalAnchor == TextBoxVerticalAnchor.Center, "shape");
            AssertTrue(lb.Insets == new ImageEffectExtent(10, 5, 10, 5) && lb.Position!.HAlign == HorizontalAlignment.Right && lb.Name == "Callout", "geometry");
            AssertTrue(loaded.Blocks.OfType<Paragraph>().First().GetText() == "Instructions follow.", "box text is not the paragraph's");
            var again = Path.Combine(outDir, "p8-textbox-again.docx");
            loaded.SaveTo(again);
            AssertTrue(ReadPartXml(again, "word/document.xml") == xml, "round trip");
        });
    }

    private static string Inject(string outDir, string name, string bodyXml, Func<string, string>? editNotes = null)
    {
        var d = new Document();
        d.AddParagraph("x");
        if (editNotes is not null) d.AddParagraph("y").Footnote(d, "seed");
        var path = Save(outDir, name, d);
        EditPart(path, "word/document.xml", xml =>
        {
            var start = xml.IndexOf("<w:body>", StringComparison.Ordinal) + "<w:body>".Length;
            var end = xml.IndexOf("<w:sectPr", StringComparison.Ordinal);
            return xml[..start] + bodyXml + xml[end..];
        });
        if (editNotes is not null) EditPart(path, "word/footnotes.xml", editNotes);
        return path;
    }

    private static string R(string inner) => "<w:r>" + inner + "</w:r>";

    private static void ReviewRegressions(string outDir)
    {
        Check("an unclosed field begin does not swallow the following paragraphs", () =>
        {
            var path = Inject(outDir, "p8r-unclosed", "<w:p>" + R("<w:fldChar w:fldCharType=\"begin\"/>") + R("<w:instrText>REF x</w:instrText>") + R("<w:t>A</w:t>") + "</w:p>"
                + "<w:p>" + R("<w:t>B important</w:t>") + "</w:p><w:p>" + R("<w:t>C important</w:t>") + "</w:p>");
            var texts = Document.Load(path).Blocks.OfType<Paragraph>().Select(p => p.GetText()).ToList();
            AssertTrue(texts.SequenceEqual(new[] { "", "B important", "C important" }), string.Join(" | ", texts));
        });
        Check("many open fields or bookmarks followed by page breaks load in linear time", () =>
        {
            var sb = new StringBuilder("<w:p>");
            for (int i = 0; i < 20_000; i++) sb.Append(R("<w:fldChar w:fldCharType=\"begin\"/>")).Append(R("<w:instrText>X</w:instrText>")).Append(R("<w:fldChar w:fldCharType=\"separate\"/>"));
            sb.Append("</w:p>");
            for (int i = 0; i < 20_000; i++) sb.Append("<w:p>").Append(R("<w:t>t</w:t>")).Append("</w:p>");
            var p1 = Inject(outDir, "p8r-fields", sb.ToString());
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Document.Load(p1);
            AssertTrue(sw.Elapsed.TotalSeconds < 5, $"fields {sw.Elapsed.TotalSeconds:F1}s");
            var sb2 = new StringBuilder("<w:p>");
            for (int i = 0; i < 20_000; i++) sb2.Append($"<w:bookmarkStart w:id=\"{i}\" w:name=\"b{i}\"/>");
            for (int i = 0; i < 20_000; i++) sb2.Append(R("<w:br w:type=\"page\"/>"));
            sb2.Append("</w:p>");
            var p2 = Inject(outDir, "p8r-breaks", sb2.ToString());
            sw.Restart();
            Document.Load(p2);
            AssertTrue(sw.Elapsed.TotalSeconds < 5, $"breaks {sw.Elapsed.TotalSeconds:F1}s");
        });
        Check("Append renames a clashing bookmark whose loaded name breaks Word's rules, without throwing", () =>
        {
            var a = Document.Load(Inject(outDir, "p8r-badname-a", "<w:p><w:bookmarkStart w:id=\"1\" w:name=\"My Mark\"/>" + R("<w:t>a</w:t>") + "<w:bookmarkEnd w:id=\"1\"/></w:p>"));
            var b = Document.Load(Inject(outDir, "p8r-badname-b", "<w:p><w:bookmarkStart w:id=\"1\" w:name=\"My Mark\"/>" + R("<w:t>b</w:t>") + "<w:bookmarkEnd w:id=\"1\"/></w:p>"));
            a.Append(b);
            AssertTrue(a.GetBookmarks().Select(x => x.Name).SequenceEqual(new[] { "My Mark", "My Mark_2" }), string.Join(",", a.GetBookmarks()));
        });
        Check("a malformed footnotes part loses its notes, not the document", () =>
        {
            var path = Inject(outDir, "p8r-badnotes", "<w:p>" + R("<w:t>body</w:t>") + R("<w:footnoteReference w:id=\"1\"/>") + "</w:p>",
                xml => xml.Replace("</w:footnotes>", "<w:footnote w:id=\"9\"><w:p><w:r><w:t>broken</w:r></w:p></w:footnote></w:footnotes>"));
            var d = Document.Load(path);
            AssertTrue(d.Footnotes.Count == 0 && d.Blocks.OfType<Paragraph>().Single().GetText() == "body", "loaded without notes");
        });
        Check("a field nested in an instruction round-trips as a nested field; form-field data is kept", () =>
        {
            var body = "<w:p>" + R("<w:fldChar w:fldCharType=\"begin\"/>") + R("<w:instrText xml:space=\"preserve\"> IF </w:instrText>")
                + R("<w:fldChar w:fldCharType=\"begin\"/>") + R("<w:instrText> MERGEFIELD Sex </w:instrText>") + R("<w:fldChar w:fldCharType=\"separate\"/>")
                + R("<w:t>F</w:t>") + R("<w:fldChar w:fldCharType=\"end\"/>") + R("<w:instrText xml:space=\"preserve\"> = \"F\" \"Ms\" \"Mr\" </w:instrText>")
                + R("<w:fldChar w:fldCharType=\"separate\"/>") + R("<w:t>Ms</w:t>") + R("<w:fldChar w:fldCharType=\"end\"/>") + "</w:p>"
                + "<w:p>" + R("<w:fldChar w:fldCharType=\"begin\"><w:ffData><w:name w:val=\"Text1\"/><w:enabled/><w:textInput/></w:ffData></w:fldChar>")
                + R("<w:instrText xml:space=\"preserve\"> FORMTEXT </w:instrText>") + R("<w:fldChar w:fldCharType=\"separate\"/>") + R("<w:t>typed</w:t>") + R("<w:fldChar w:fldCharType=\"end\"/>") + "</w:p>";
            var path = Inject(outDir, "p8r-nested", body);
            var d = Document.Load(path);
            var iff = d.Blocks.OfType<Paragraph>().First().Runs[0].FieldMark!.Field;
            AssertTrue(iff.InstructionParts.Count == 3 && iff.InstructionParts[1] is Field { FieldType: "MERGEFIELD", CachedResult: "F" }, string.Join(" | ", iff.InstructionParts));
            var again = Path.Combine(outDir, "p8r-nested-again.docx");
            d.SaveTo(again);
            var x = ReadPartXml(again, "word/document.xml");
            AssertContains(x, "MERGEFIELD Sex", "nested instruction written");
            AssertContains(x, "<w:ffData><w:name w:val=\"Text1\" /><w:enabled /><w:textInput /></w:ffData>", "ffData kept");
            var re = Document.Load(again).Blocks.OfType<Paragraph>().First().Runs[0].FieldMark!.Field;
            AssertTrue(re.InstructionParts.Count == 3 && re.Instruction == iff.Instruction, re.Instruction);
        });
        Check("one bookmark object used twice gets two ids; two bookmarks with one name are written with unique names", () =>
        {
            var d = new Document();
            var b = new Bookmark("Twice");
            d.AddParagraph().BookmarkStart(b).Text("one").BookmarkEnd(b);
            d.AddParagraph().BookmarkStart(b).Text("two").BookmarkEnd(b);
            d.AddParagraph().Bookmarked("Twice", "three");
            var x = XElement.Parse(ReadPartXml(Save(outDir, "p8r-dupbm", d), "word/document.xml"));
            var starts = x.Descendants(Wn + "bookmarkStart").ToList();
            AssertTrue(starts.Select(s => s.Attribute(Wn + "id")!.Value).Distinct().Count() == 3, "ids");
            AssertTrue(starts.Select(s => s.Attribute(Wn + "name")!.Value).Distinct().Count() == 2 + 0 || starts.Select(s => s.Attribute(Wn + "name")!.Value).Count(n => n.StartsWith("Twice_")) == 1, "names");
            var ends = x.Descendants(Wn + "bookmarkEnd").Select(e => e.Attribute(Wn + "id")!.Value).ToList();
            AssertTrue(ends.SequenceEqual(starts.Select(s => s.Attribute(Wn + "id")!.Value)), "ends pair");
        });
        Check("text boxes keep rotation and theme fills / style through load → save", () =>
        {
            const string wps = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
            var box = "<w:p><w:r><w:drawing><wp:inline xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\"><wp:extent cx=\"1270000\" cy=\"635000\"/><wp:docPr id=\"5\" name=\"Box\"/>"
                + "<a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><a:graphicData uri=\"" + wps + "\"><wps:wsp xmlns:wps=\"" + wps + "\"><wps:cNvSpPr txBox=\"1\"/>"
                + "<wps:spPr><a:xfrm rot=\"1800000\"><a:off x=\"0\" y=\"0\"/><a:ext cx=\"1270000\" cy=\"635000\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom><a:solidFill><a:schemeClr val=\"lt1\"/></a:solidFill><a:ln w=\"6350\"><a:solidFill><a:prstClr val=\"black\"/></a:solidFill></a:ln></wps:spPr>"
                + "<wps:style><a:lnRef idx=\"0\"><a:scrgbClr r=\"0\" g=\"0\" b=\"0\"/></a:lnRef><a:fillRef idx=\"0\"><a:scrgbClr r=\"0\" g=\"0\" b=\"0\"/></a:fillRef><a:effectRef idx=\"0\"><a:scrgbClr r=\"0\" g=\"0\" b=\"0\"/></a:effectRef><a:fontRef idx=\"minor\"><a:schemeClr val=\"dk1\"/></a:fontRef></wps:style>"
                + "<wps:txbx><w:txbxContent><w:p><w:r><w:t>Theme box</w:t></w:r></w:p></w:txbxContent></wps:txbx><wps:bodyPr rot=\"0\" vert=\"vert270\" wrap=\"square\"><a:spAutoFit/></wps:bodyPr></wps:wsp></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>";
            var path = Inject(outDir, "p8r-themebox", box);
            var d = Document.Load(path);
            var tb = d.Blocks.OfType<Paragraph>().First().Runs.Single(r => r.TextBox is not null).TextBox!;
            AssertTrue(Math.Abs(tb.RotationDegrees - 30) < 1e-9 && tb.AutoFit && tb.GetText() == "Theme box", $"{tb.RotationDegrees}");
            var again = Path.Combine(outDir, "p8r-themebox-again.docx");
            d.SaveTo(again);
            var x = ReadPartXml(again, "word/document.xml");
            AssertContains(x, "<a:schemeClr val=\"lt1\" />", "theme fill kept");
            AssertContains(x, "<wps:style>", "style kept");
            AssertContains(x, "vert=\"vert270\"", "bodyPr kept");
            tb.FillHex = "FF0000";
            d.SaveTo(again);
            AssertTrue(!ReadPartXml(again, "word/document.xml").Contains("schemeClr val=\"lt1\""), "a changed fill is written from the model");
        });
        Check("endnote positions follow the schema; references inside notes and headers are not written", () =>
        {
            var d = new Document();
            d.EndnoteProperties.Position = NotePosition.PageBottom;
            var n1 = d.Footnotes.Add("first");
            d.AddParagraph("a").NoteReference(n1);
            d.Footnotes[0].Paragraphs[0].NoteReference(n1);
            d.Header.Add(new Paragraph("h").NoteReference(n1));
            d.AddParagraph("e").Endnote(d, "end");
            var path = Save(outDir, "p8r-notepos", d);
            AssertTrue(!ReadPartXml(path, "word/settings.xml").Contains("pageBottom"), "endnote pageBottom not written");
            AssertTrue(!ReadPartXml(path, "word/footnotes.xml").Contains("footnoteReference"), "no reference in a note");
            using var z = ZipFile.OpenRead(path);
            foreach (var e in z.Entries.Where(e => e.FullName.StartsWith("word/header")))
                using (var r = new StreamReader(e.Open())) AssertTrue(!r.ReadToEnd().Contains("footnoteReference"), "no reference in a header");
            AssertThrows<ArgumentOutOfRangeException>(() => d.FootnoteProperties.StartAt = -5, "negative start");
        });
        Check("a custom note number format keeps its pattern; a simple field written in complex form gets its separator", () =>
        {
            var path = Inject(outDir, "p8r-customfmt", "<w:p>" + R("<w:t>x</w:t>") + "</w:p>");
            EditPart(path, "word/document.xml", xml => xml.Replace("<w:sectPr>", "<w:sectPr><w:footnotePr><w:numFmt w:val=\"custom\" w:format=\"001, 002\"/></w:footnotePr>"));
            var again = Path.Combine(outDir, "p8r-customfmt-again.docx");
            Document.Load(path).SaveTo(again);
            AssertContains(ReadPartXml(again, "word/document.xml"), "w:format=\"001, 002\"", "custom pattern kept");

            var d = new Document();
            var a = new Field(" AUTHOR ");
            var p = d.AddParagraph();
            p.FieldMarker(a, FieldMarkType.Begin).Text("Arun").FieldMarker(a, FieldMarkType.End);
            typeof(Field).GetProperty("Simple", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(a, true);
            var b = new Field(" TITLE ");
            typeof(Field).GetProperty("Simple", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(b, true);
            var q = d.AddParagraph();
            q.FieldMarker(a, FieldMarkType.Begin).FieldMarker(b, FieldMarkType.Begin).Text("z").FieldMarker(a, FieldMarkType.End).FieldMarker(b, FieldMarkType.End);
            var x = XElement.Parse(ReadPartXml(Save(outDir, "p8r-simplecomplex", d), "word/document.xml"));
            var second = x.Descendants(Wn + "p").ElementAt(1);
            var types = second.Descendants(Wn + "fldChar").Select(f => (string)f.Attribute(Wn + "fldCharType")!).ToList();
            AssertTrue(types.Count(t => t == "separate") == types.Count(t => t == "begin"), string.Join(",", types));
        });
        Check("dates without a kind are written as UTC; a loaded modified date is refreshed on save", () =>
        {
            var d = new Document();
            d.AddParagraph("x");
            d.Properties.Created = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Unspecified);
            d.Properties.Modified = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Unspecified);
            var path = Save(outDir, "p8r-dates", d);
            AssertContains(ReadPartXml(path, "docProps/core.xml"), "2026-01-02T03:04:05Z", "no offset shift");
            var loaded = Document.Load(path);
            var again = Path.Combine(outDir, "p8r-dates-again.docx");
            loaded.SaveTo(again);
            var core = ReadPartXml(again, "docProps/core.xml");
            AssertTrue(core.Contains("<dcterms:created xsi:type=\"dcterms:W3CDTF\">2026-01-02T03:04:05Z") && !core.Contains("<dcterms:modified xsi:type=\"dcterms:W3CDTF\">2026-01-02"), core);
        });
    }
}
