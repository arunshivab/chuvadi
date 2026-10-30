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
/// Phase 9 — comments (ranges, anchors, replies, done) and tracked changes (insertions, deletions,
/// moves, run and paragraph formatting changes, paragraph marks), with accept / reject.
/// </summary>
public static class CommentsRevisionsTests
{
    private static readonly XNamespace Wn = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace W15 = "http://schemas.microsoft.com/office/word/2012/wordml";
    private static readonly DateTime When = new(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc);

    public static void Run(string outDir)
    {
        Comments(outDir);
        Revisions(outDir);
        AcceptReject(outDir);
        WordShapes(outDir);
        ReviewFixes(outDir);
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

    private static void Comments(string outDir)
    {
        Check("comments: range, anchor, content, replies and done flags are written and loaded", () =>
        {
            var d = new Document();
            var c = d.Comments.Add("Dr. Arun Shiva", "Please confirm the dose.", date: When);
            d.AddParagraph("The dose is ").Commented("2 Gy per fraction", c).Text(".");
            var reply = d.Comments.Reply(c, "Physicist", "Confirmed against the plan.", date: When);
            reply.Done = true;
            var path = Save(outDir, "p9-comments", d);
            var cx = XElement.Parse(ReadPartXml(path, "word/comments.xml"));
            var comments = cx.Elements(Wn + "comment").ToList();
            AssertTrue(comments.Count == 2 && (string)comments[0].Attribute(Wn + "author")! == "Dr. Arun Shiva" && (string)comments[0].Attribute(Wn + "initials")! == "DAS", "comments part");
            AssertTrue(comments[0].Descendants(Wn + "annotationRef").Any(), "annotation mark");
            var ex = XElement.Parse(ReadPartXml(path, "word/commentsExtended.xml"));
            var exs = ex.Elements(W15 + "commentEx").ToList();
            AssertTrue(exs.Count == 2 && exs[1].Attribute(W15 + "paraIdParent")!.Value == exs[0].Attribute(W15 + "paraId")!.Value && (string)exs[1].Attribute(W15 + "done")! == "1", "extended");
            var body = XElement.Parse(ReadPartXml(path, "word/document.xml"));
            AssertTrue(body.Descendants(Wn + "commentRangeStart").Count() == 2 && body.Descendants(Wn + "commentReference").Count() == 2, "reply anchored with its comment");

            var loaded = Document.Load(path);
            AssertTrue(loaded.Comments.Count == 2 && loaded.Comments[1].Parent == loaded.Comments[0] && loaded.Comments[1].Done && !loaded.Comments[0].Done, "loaded threads");
            AssertTrue(loaded.Comments[0].GetText() == "Please confirm the dose." && loaded.Comments[0].Date == When, loaded.Comments[0].GetText());
            var p = loaded.Blocks.OfType<Paragraph>().Single();
            AssertTrue(p.GetText() == "The dose is 2 Gy per fraction." && p.Runs.Count(r => r.CommentRange is not null) == 4, "markers loaded");
            var again = Path.Combine(outDir, "p9-comments-again.docx");
            loaded.SaveTo(again);
            foreach (var part in new[] { "word/document.xml", "word/comments.xml", "word/commentsExtended.xml" })
                AssertTrue(ReadPartXml(again, part) == ReadPartXml(path, part), "load → save reproduces " + part);
            loaded.Comments.Remove(loaded.Comments[0]);
            var removed = Path.Combine(outDir, "p9-comments-removed.docx");
            loaded.SaveTo(removed);
            AssertTrue(!ReadPartXml(removed, "word/document.xml").Contains("comment"), "removed comment leaves no markers");
        });
    }

    private static void Revisions(string outDir)
    {
        Check("tracked changes: insertions, deletions, format changes and paragraph marks round-trip", () =>
        {
            var d = new Document();
            d.TrackRevisions = true;
            d.AddParagraph("Dose: ").TrackedDelete("2 Gy", "Reviewer", date: When).TrackedInsert("1.8 Gy", "Reviewer", date: When).Text(" per fraction");
            d.AddParagraph().TrackedFormat("Important", TextFormat.BoldText, TextFormat.None, "Reviewer", When);
            var moved = d.AddParagraph("Merged line one");
            moved.MarkRevision = RevisionMark.Delete("Reviewer", When);
            d.AddParagraph("line two");
            var styled = d.AddParagraph("Centred now");
            styled.Alignment = ParagraphAlignment.Center;
            styled.FormatChange = new ParagraphFormatChange(RevisionMark.Format("Reviewer", When), new ParagraphFormat { Alignment = ParagraphAlignment.Left }, null);
            var path = Save(outDir, "p9-revisions", d);
            var x = XElement.Parse(ReadPartXml(path, "word/document.xml"));
            AssertTrue(x.Descendants(Wn + "ins").Count() == 1 && x.Descendants(Wn + "delText").Single().Value == "2 Gy", "ins/del");
            AssertTrue(x.Descendants(Wn + "rPrChange").Any() && x.Descendants(Wn + "pPrChange").Any(), "format changes");
            AssertTrue(x.Descendants(Wn + "pPr").Any(pp => pp.Element(Wn + "rPr")?.Element(Wn + "del") is not null), "deleted paragraph mark");
            var ids = x.Descendants().Where(e => e.Name.LocalName is "ins" or "del" or "rPrChange" or "pPrChange").Select(e => (string)e.Attribute(Wn + "id")!).ToList();
            AssertTrue(ids.Distinct().Count() == ids.Count, "unique revision ids");
            AssertContains(ReadPartXml(path, "word/settings.xml"), "<w:trackRevisions />", "setting");
            AssertTrue(d.Blocks.OfType<Paragraph>().First().GetText() == "Dose: 1.8 Gy per fraction", "GetText leaves deletions out");

            var loaded = Document.Load(path);
            var revs = loaded.GetRevisions();
            AssertTrue(revs.Count(r => r.Mark.Type == RevisionType.Delete) == 2 && revs.Count(r => r.Mark.Type == RevisionType.Insert) == 1
                       && revs.Count(r => r.Mark.Type == RevisionType.FormatChange) == 2, string.Join(",", revs.Select(r => r.Mark.Type)));
            var first = loaded.Blocks.OfType<Paragraph>().First();
            AssertTrue(first.Runs.Single(r => r.IsDeleted).TextContent == "2 Gy" && first.Runs.Single(r => r.Revision?.Type == RevisionType.Insert).Revision!.Author == "Reviewer", "runs");
            var fc = loaded.Blocks.OfType<Paragraph>().ElementAt(1).Runs[0].FormatChange!;
            AssertTrue(fc.OldFormat.IsDefault && fc.Mark.Date == When && loaded.TrackRevisions, "run format change");
            var again = Path.Combine(outDir, "p9-revisions-again.docx");
            loaded.SaveTo(again);
            AssertTrue(ReadPartXml(again, "word/document.xml") == ReadPartXml(path, "word/document.xml"), "load → save reproduces document.xml");
        });
    }

    private static Document Sample()
    {
        var d = new Document();
        d.AddParagraph("Dose: ").TrackedDelete("2 Gy", "R", date: When).TrackedInsert("1.8 Gy", "R", date: When).Text(".");
        d.AddParagraph().TrackedFormat("Bold now", TextFormat.BoldText, TextFormat.ItalicText, "R", When);
        var a = d.AddParagraph("Part A ");
        a.MarkRevision = RevisionMark.Delete("R", When);
        d.AddParagraph("part B");
        var ins = d.AddParagraph("Inserted mark ");
        ins.MarkRevision = RevisionMark.Insert("R", When);
        d.AddParagraph("next");
        var c = d.AddParagraph("Centred");
        c.Alignment = ParagraphAlignment.Center;
        c.FormatChange = new ParagraphFormatChange(RevisionMark.Format("R", When), new ParagraphFormat { Alignment = ParagraphAlignment.Right }, null);
        return d;
    }

    private static void AcceptReject(string outDir)
    {
        Check("accept all: insertions stay, deletions go, new formatting kept, deleted marks join paragraphs", () =>
        {
            var d = Sample();
            d.AcceptAllRevisions();
            var paras = d.Blocks.OfType<Paragraph>().ToList();
            var texts = paras.Select(p => p.GetText()).ToList();
            AssertTrue(texts.SequenceEqual(new[] { "Dose: 1.8 Gy.", "Bold now", "Part A part B", "Inserted mark ", "next", "Centred" }), string.Join(" | ", texts));
            AssertTrue(paras[1].Runs[0].Format.Bold && paras[1].Runs[0].FormatChange is null && paras[5].Alignment == ParagraphAlignment.Center, "formats");
            AssertTrue(d.GetRevisions().Count == 0, "no revisions left");
            Save(outDir, "p9-accepted", d);
        });
        Check("reject all: insertions go, deletions stay, old formatting back, inserted marks join paragraphs", () =>
        {
            var d = Sample();
            d.RejectAllRevisions();
            var paras = d.Blocks.OfType<Paragraph>().ToList();
            var texts = paras.Select(p => p.GetText()).ToList();
            AssertTrue(texts.SequenceEqual(new[] { "Dose: 2 Gy.", "Bold now", "Part A ", "part B", "Inserted mark next", "Centred" }), string.Join(" | ", texts));
            AssertTrue(paras[1].Runs[0].Format.Italic && !paras[1].Runs[0].Format.Bold && paras[5].Alignment == ParagraphAlignment.Right, "formats restored");
            AssertTrue(d.GetRevisions().Count == 0, "no revisions left");
        });
    }

    private static void WordShapes(string outDir)
    {
        Check("Word-style markup: moves, a deleted field, insertions inside links and in tables load as tracked changes", () =>
        {
            var d = new Document();
            d.AddParagraph("x");
            var t = d.AddTable(1);
            t.AddRow("cell");
            var path = Save(outDir, "p9-wordshapes", d);
            EditPart(path, "word/document.xml", xml =>
            {
                string R(string inner) => "<w:r>" + inner + "</w:r>";
                var body = "<w:p><w:moveFrom w:id=\"1\" w:author=\"A\" w:date=\"2026-10-28T09:00:00Z\">" + R("<w:t>moved away</w:t>") + "</w:moveFrom>"
                    + "<w:moveTo w:id=\"2\" w:author=\"A\" w:date=\"2026-10-28T09:00:00Z\">" + R("<w:t>moved here</w:t>") + "</w:moveTo></w:p>"
                    + "<w:p><w:del w:id=\"3\" w:author=\"B\">" + R("<w:fldChar w:fldCharType=\"begin\"/>") + R("<w:delInstrText> DATE </w:delInstrText>")
                    + R("<w:fldChar w:fldCharType=\"separate\"/>") + R("<w:delText>today</w:delText>") + R("<w:fldChar w:fldCharType=\"end\"/>") + "</w:del>"
                    + "<w:hyperlink w:anchor=\"top\"><w:ins w:id=\"4\" w:author=\"C\">" + R("<w:t>new link</w:t>") + "</w:ins></w:hyperlink></w:p>";
                return xml.Replace("<w:body>", "<w:body>" + body).Replace("<w:t>cell</w:t></w:r>", "<w:t>cell</w:t></w:r><w:ins w:id=\"5\" w:author=\"D\"><w:r><w:t xml:space=\"preserve\"> added</w:t></w:r></w:ins>");
            });
            var loaded = Document.Load(path);
            var paras = loaded.Blocks.OfType<Paragraph>().ToList();
            AssertTrue(paras[0].Runs[0].Revision is { Type: RevisionType.Delete, IsMove: true } && paras[0].Runs[1].Revision is { Type: RevisionType.Insert, IsMove: true }, "moves");
            AssertTrue(paras[0].GetText() == "moved here", paras[0].GetText());
            var field = paras[1].Runs.First(r => r.FieldMark is not null);
            AssertTrue(field.FieldMark!.Field.FieldType == "DATE" && field.IsDeleted && paras[1].Runs.Single(r => r.TextContent == "today").IsDeleted, "deleted field");
            var link = paras[1].Runs.Single(r => r.IsLink);
            AssertTrue(link.HyperlinkAnchor == "top" && link.Revision?.Author == "C", "insertion inside a link");
            var cell = loaded.Blocks.OfType<DocTable>().Single().Rows[0].Cells[0].Paragraphs[0];
            AssertTrue(cell.Runs.Last().Revision?.Author == "D" && cell.GetText() == "cell added", "in a table");
            var again = Path.Combine(outDir, "p9-wordshapes-again.docx");
            loaded.SaveTo(again);
            var x = XElement.Parse(ReadPartXml(again, "word/document.xml"));
            AssertTrue(x.Descendants(Wn + "delInstrText").Any() && x.Descendants(Wn + "hyperlink").Single().Element(Wn + "ins") is not null, "written back as Word does");
            var reloaded = Document.Load(again);
            AssertTrue(reloaded.GetRevisions().Count == loaded.GetRevisions().Count, "same revisions after a round trip");
        });
    }

    private static XElement Body(string path) => XElement.Parse(ReadPartXml(path, "word/document.xml"));

    private static string Inject(string outDir, string name, string bodyXml)
    {
        var d = new Document();
        d.AddParagraph("x");
        var path = Save(outDir, name, d);
        EditPart(path, "word/document.xml", xml => xml.Replace("<w:body>", "<w:body>" + bodyXml));
        return path;
    }

    private static void ReviewFixes(string outDir)
    {
        Check("tracked PAGE field: the change sits inside w:fldSimple and survives a round trip", () =>
        {
            var path = Inject(outDir, "p9-field-rev",
                "<w:p><w:fldSimple w:instr=\" PAGE \"><w:ins w:id=\"9\" w:author=\"E\"><w:r><w:t>1</w:t></w:r></w:ins></w:fldSimple></w:p>"
                + "<w:p><w:del w:id=\"10\" w:author=\"F\"><w:fldSimple w:instr=\" NUMPAGES \"><w:r><w:delText>3</w:delText></w:r></w:fldSimple></w:del></w:p>");
            var loaded = Document.Load(path);
            var paras = loaded.Blocks.OfType<Paragraph>().ToList();
            AssertTrue(paras[0].Runs.Single().FieldInstruction is not null && paras[0].Runs.Single().Revision?.Author == "E", "insertion inside the field");
            AssertTrue(paras[1].Runs.Single().Revision is { Type: RevisionType.Delete, Author: "F" } && paras[1].Runs.Single().TextContent == "3", "deletion around the field");
            var again = Path.Combine(outDir, "p9-field-rev-again.docx");
            loaded.SaveTo(again);
            var fields = Body(again).Descendants(Wn + "fldSimple").ToList();
            AssertTrue(fields.Count == 2 && fields[0].Element(Wn + "ins")?.Element(Wn + "r") is not null
                && fields[1].Element(Wn + "del")?.Element(Wn + "r")?.Element(Wn + "delText") is not null, "w:fldSimple > w:ins|w:del > w:r");
            AssertTrue(!Body(again).Elements(Wn + "body").Elements(Wn + "p").Elements(Wn + "ins").Any(), "no w:ins holding a w:fldSimple");
            AssertTrue(Document.Load(again).GetRevisions().Count == 2, "reloaded");
        });

        Check("accept/reject in cells, notes and headers: marks join only adjacent paragraphs, never across a table", () =>
        {
            var d = new Document();
            var t = d.AddTable(1);
            var cell = t.AddRow("first").Cells[0];
            cell.Paragraphs[0].MarkRevision = RevisionMark.Delete("R", When);
            cell.AddTable(1).AddRow("nested");
            cell.Paragraphs.Add(new Paragraph("after table"));
            var p3 = new Paragraph("joins ");
            p3.MarkRevision = RevisionMark.Delete("R", When);
            cell.Paragraphs.Add(p3);
            cell.Paragraphs.Add(new Paragraph("next"));
            var note = d.Footnotes.Add("note one ");
            note.Paragraphs[0].MarkRevision = RevisionMark.Delete("R", When);
            note.Paragraphs.Add(new Paragraph("note two"));
            d.AddParagraph("ref").NoteReference(note);
            d.Header.Add(new Paragraph("head one "));
            d.Header[0].MarkRevision = RevisionMark.Delete("R", When);
            d.Header.Add(new Paragraph("head two"));
            d.AcceptAllRevisions();
            var texts = cell.Paragraphs.Select(p => p.GetText()).ToList();
            AssertTrue(texts.SequenceEqual(new[] { "first", "after table", "joins next" }), string.Join(" | ", texts));
            AssertTrue(cell.Blocks.Count == 4 && cell.Blocks[1] is DocTable, "nested table stays after its paragraph");
            AssertTrue(note.Paragraphs.Count == 1 && note.GetText().EndsWith("note one note two"), note.GetText());
            AssertTrue(d.Header.Count == 1 && d.Header[0].GetText() == "head one head two", "header joined");
            AssertTrue(d.GetRevisions().Count == 0, "none left");
        });

        Check("comment anchors are written in footnotes; never in headers", () =>
        {
            var d = new Document();
            var c = d.Comments.Add("R", "on the note", date: When);
            var note = d.Footnotes.Add("noted ");
            note.Paragraphs[0].Commented("here", c);
            d.AddParagraph("body").NoteReference(note);
            var c2 = d.Comments.Add("R", "in header", date: When);
            d.Header.Add(new Paragraph("head").CommentReference(c2));
            var path = Save(outDir, "p9-comment-note", d);
            AssertTrue(ReadPartXml(path, "word/footnotes.xml").Contains("commentReference"), "anchor in the footnote");
            var headerPart = ZipNames(path).First(n => n.StartsWith("word/header"));
            AssertTrue(!ReadPartXml(path, headerPart).Contains("commentReference"), "none in the header");
            var loaded = Document.Load(path);
            AssertTrue(loaded.Footnotes.Single().Paragraphs[0].Runs.Any(r => r.CommentReference is not null), "loaded from the note");
        });

        Check("table rows: tracked insertion/deletion in w:trPr round-trips; accept/reject removes rows and empty tables", () =>
        {
            var d = new Document();
            var t = d.AddTable(2);
            t.AddRow("keep", "1");
            t.AddRow("deleted", "2").Revision = RevisionMark.Delete("R", When);
            t.AddRow("inserted", "3").Revision = RevisionMark.Insert("R", When);
            var gone = d.AddTable(1);
            gone.AddRow("all deleted").Revision = RevisionMark.Delete("R", When);
            d.AddParagraph("end");
            var path = Save(outDir, "p9-rows", d);
            var trPrs = Body(path).Descendants(Wn + "trPr").ToList();
            AssertTrue(trPrs.Count(e => e.Element(Wn + "del") is not null) == 2 && trPrs.Count(e => e.Element(Wn + "ins") is not null) == 1, "written in trPr");
            var loaded = Document.Load(path);
            var lt = loaded.Blocks.OfType<DocTable>().First();
            AssertTrue(lt.Rows[1].Revision is { Type: RevisionType.Delete, Author: "R" } && lt.Rows[2].Revision?.Type == RevisionType.Insert, "loaded");
            var again = Path.Combine(outDir, "p9-rows-again.docx");
            loaded.SaveTo(again);
            AssertTrue(ReadPartXml(again, "word/document.xml") == ReadPartXml(path, "word/document.xml"), "load → save reproduces");
            loaded.AcceptAllRevisions();
            var tables = loaded.Blocks.OfType<DocTable>().ToList();
            AssertTrue(tables.Count == 1 && tables[0].Rows.Count == 2 && tables[0].Rows[1].Cells[0].Paragraphs[0].GetText() == "inserted" && tables[0].Rows[1].Revision is null, "accepted");
            var rejected = Document.Load(path);
            rejected.RejectAllRevisions();
            var rt = rejected.Blocks.OfType<DocTable>().ToList();
            AssertTrue(rt.Count == 2 && rt[0].Rows.Count == 2 && rt[0].Rows[1].Cells[0].Paragraphs[0].GetText() == "deleted", "rejected");
        });

        Check("deletion inside an insertion keeps both; reject removes the text, accept removes it too", () =>
        {
            var path = Inject(outDir, "p9-insdel",
                "<w:p><w:ins w:id=\"1\" w:author=\"A\"><w:r><w:t xml:space=\"preserve\">added </w:t></w:r><w:del w:id=\"2\" w:author=\"B\"><w:r><w:delText>then cut</w:delText></w:r></w:del></w:ins></w:p>");
            var loaded = Document.Load(path);
            var p = loaded.Blocks.OfType<Paragraph>().First();
            var cut = p.Runs.Single(r => r.TextContent == "then cut");
            AssertTrue(cut.Revision is { Type: RevisionType.Delete, Author: "B" } && cut.InsertedRevision?.Author == "A", "both kept");
            AssertTrue(loaded.GetRevisions().Count == 3, "GetRevisions lists the insertion under the deletion");
            var again = Path.Combine(outDir, "p9-insdel-again.docx");
            loaded.SaveTo(again);
            var ins = Body(again).Descendants(Wn + "ins").ToList();
            AssertTrue(ins.Any(i => i.Element(Wn + "del")?.Element(Wn + "r") is not null), "w:ins > w:del > w:r");
            var rl = Document.Load(again);
            AssertTrue(rl.Blocks.OfType<Paragraph>().First().Runs.Single(r => r.TextContent == "then cut").InsertedRevision is not null, "reloaded");
            rl.RejectAllRevisions();
            AssertTrue(rl.Blocks.OfType<Paragraph>().First().GetText() == "" && rl.Blocks.OfType<Paragraph>().First().Runs.Count == 0, "reject removes both");
            var acc = Document.Load(again);
            acc.AcceptAllRevisions();
            AssertTrue(acc.Blocks.OfType<Paragraph>().First().GetText() == "added ", "accept keeps the insertion only");
        });

        Check("paragraph format change keeps old numbering and the mark's rPrChange; reject keeps mark formatting", () =>
        {
            var d = new Document();
            var list = d.Numbering.AddNumberedList();
            d.AddParagraph("item").ListInstance = list;
            var path = Save(outDir, "p9-pprchange", d);
            var numId = Body(path).Descendants(Wn + "numId").First().Attribute(Wn + "val")!.Value;
            EditPart(path, "word/document.xml", xml => xml.Replace("<w:body>", "<w:body><w:p><w:pPr><w:jc w:val=\"center\"/>"
                + "<w:rPr><w:b/><w:rPrChange w:id=\"7\" w:author=\"M\"><w:rPr><w:i/></w:rPr></w:rPrChange></w:rPr>"
                + "<w:pPrChange w:id=\"8\" w:author=\"P\"><w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"" + numId + "\"/></w:numPr><w:jc w:val=\"right\"/></w:pPr></w:pPrChange></w:pPr>"
                + "<w:r><w:t>changed</w:t></w:r></w:p>"));
            var loaded = Document.Load(path);
            var again = Path.Combine(outDir, "p9-pprchange-again.docx");
            loaded.SaveTo(again);
            var ppr = Body(again).Descendants(Wn + "pPr").First();
            AssertTrue(ppr.Element(Wn + "pPrChange")?.Descendants(Wn + "numId").Single().Attribute(Wn + "val")?.Value == numId, "old numbering written back");
            AssertTrue(ppr.Element(Wn + "rPr")?.Element(Wn + "rPrChange")?.Descendants(Wn + "i").Any() == true, "mark rPrChange written back");
            var first = loaded.Blocks.OfType<Paragraph>().First();
            var format = first.Format;
            loaded.RejectAllRevisions();
            AssertTrue(ReferenceEquals(first.Format, format) && first.Alignment == ParagraphAlignment.Right, "values copied into the same format");
            AssertTrue(first.Format.MarkFormat is { Italic: true, Bold: false }, "mark formatting restored from its own change");
            var acc = Document.Load(path);
            acc.AcceptAllRevisions();
            var fa = acc.Blocks.OfType<Paragraph>().First();
            AssertTrue(fa.Alignment == ParagraphAlignment.Center && fa.Format.MarkFormat is { Bold: true }, "accept keeps new values");
        });

        Check("accepting a deleted mark that ends a section merges the sections (the next section's layout applies)", () =>
        {
            var d = new Document();
            d.AddParagraph("first section ");
            var ended = d.Blocks.OfType<Paragraph>().Last();
            d.AddSectionBreak();
            d.Section.Page.Orientation = PageOrientation.Landscape;
            d.AddParagraph("second");
            ended.MarkRevision = RevisionMark.Delete("R", When);
            AssertTrue(d.Sections.Count == 2, "two sections before");
            d.AcceptAllRevisions();
            var paras = d.Blocks.OfType<Paragraph>().ToList();
            AssertTrue(paras.Count == 1 && paras[0].GetText() == "first section second" && paras[0].SectionProperties is null && d.Sections.Count == 1, string.Join("|", paras.Select(p => p.GetText())));
        });

        Check("comment ending with a table: the extended paraId is on the trailing paragraph; empty comment is valid", () =>
        {
            var d = new Document();
            var c = d.Comments.Add("R", "see table", date: When);
            c.AddTable(1).AddRow("t");
            c.Done = true;
            var empty = d.Comments.Add("R", "x", date: When);
            empty.Paragraphs.Clear();
            d.AddParagraph().Commented("a", c).Commented("b", empty);
            var path = Save(outDir, "p9-comment-table", d);
            var cx = XElement.Parse(ReadPartXml(path, "word/comments.xml"));
            var W14 = (XNamespace)"http://schemas.microsoft.com/office/word/2010/wordml";
            var lasts = cx.Elements(Wn + "comment").Select(e => e.Elements().Last()).ToList();
            AssertTrue(lasts.All(e => e.Name == Wn + "p" && e.Attribute(W14 + "paraId") is not null), "each comment ends with a paragraph carrying the id");
            var ex = XElement.Parse(ReadPartXml(path, "word/commentsExtended.xml"));
            var ids = ex.Elements(W15 + "commentEx").Select(e => e.Attribute(W15 + "paraId")!.Value).ToList();
            AssertTrue(ids.SequenceEqual(lasts.Select(e => e.Attribute(W14 + "paraId")!.Value)), "ids match");
            AssertTrue(empty.Paragraphs.Count == 0, "model unchanged by saving");
            var loaded = Document.Load(path);
            AssertTrue(loaded.Comments.Count == 2, "loads");
        });

        Check("accepting a deletion that holds a comment anchor removes the comment", () =>
        {
            var path = Inject(outDir, "p9-del-comment",
                "<w:p><w:commentRangeStart w:id=\"0\"/><w:r><w:t>kept</w:t></w:r><w:commentRangeEnd w:id=\"0\"/>"
                + "<w:del w:id=\"5\" w:author=\"R\"><w:r><w:commentReference w:id=\"0\"/></w:r></w:del></w:p>");
            EditPart(path, "[Content_Types].xml", xml => xml.Replace("</Types>",
                "<Override PartName=\"/word/comments.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml\"/></Types>"));
            EditPart(path, "word/_rels/document.xml.rels", xml => xml.Replace("</Relationships>",
                "<Relationship Id=\"rIdC9\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments\" Target=\"comments.xml\"/></Relationships>"));
            AddPart(path, "word/comments.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:comments xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">"
                + "<w:comment w:id=\"0\" w:author=\"\"><w:p><w:r><w:t>gone</w:t></w:r></w:p></w:comment></w:comments>");
            var loaded = Document.Load(path);
            AssertTrue(loaded.Comments.Count == 1 && loaded.Comments[0].Author == "", "loaded with its empty author kept");
            AssertTrue(loaded.GetRevisions().Single().Mark.Author == "R", "deleted anchor");
            loaded.AcceptAllRevisions();
            AssertTrue(loaded.Comments.Count == 0, "comment removed");
            var after = Path.Combine(outDir, "p9-del-comment-accepted.docx");
            loaded.SaveTo(after);
            AssertTrue(!ReadPartXml(after, "word/document.xml").Contains("comment"), "no markers left");
        });

        Check("re-review: long join chains are linear; merges, emptied headers and GetRevisions cover rows", () =>
        {
            var d = new Document();
            for (int i = 0; i < 30000; i++)
            {
                var p = d.AddParagraph("p" + i + " ");
                if (i < 29999) p.MarkRevision = RevisionMark.Delete("R", When);
            }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            d.AcceptAllRevisions();
            AssertTrue(d.Blocks.Count == 1 && d.Blocks.OfType<Paragraph>().Single().Runs.Count == 30000 && sw.ElapsedMilliseconds < 5000, sw.ElapsedMilliseconds + " ms");

            var m = new Document();
            var t = m.AddTable(1);
            var r0 = t.AddRow("top");
            r0.Cells[0].VerticalMerge = VerticalMerge.Restart;
            r0.Revision = RevisionMark.Delete("R", When);
            t.AddRow("").Cells[0].VerticalMerge = VerticalMerge.Continue;
            var cell = t.AddRow("c").Cells[0];
            cell.Paragraphs[0].MarkRevision = RevisionMark.Delete("R", When);
            cell.AddTable(1).AddRow("gone").Revision = RevisionMark.Delete("R", When);
            cell.Paragraphs.Add(new Paragraph("d"));
            m.Header.AddTable(1).AddRow("only").Revision = RevisionMark.Delete("R", When);
            var revs = m.GetRevisions();
            AssertTrue(revs.Count(x => x.Row is not null) == 3 && revs.Count(x => x.Paragraph.MarkRevision is not null && x.Run is null) == 1, "rows listed: " + revs.Count);
            m.AcceptAllRevisions();
            AssertTrue(t.Rows.Count == 2 && t.Rows[0].Cells[0].VerticalMerge == VerticalMerge.Restart, "merge start promoted");
            AssertTrue(cell.Paragraphs.Count == 1 && cell.Paragraphs[0].GetText() == "cd" && cell.Blocks.Count == 1, "joined once the table went: " + cell.Paragraphs.Count);
            AssertTrue(m.Header.Blocks.Count == 0 && !m.Header.LinkToPrevious, "emptied header stays blank");
            AssertTrue(m.GetRevisions().Count == 0, "none left");
        });

        Check("revision types are validated: a format-change mark cannot be an insertion/deletion", () =>
        {
            var p = new Paragraph("x");
            AssertThrows<ArgumentException>(() => p.MarkRevision = RevisionMark.Format("R"), "mark revision");
            AssertThrows<ArgumentException>(() => new DocTable(1).AddRow("a").Revision = RevisionMark.Format("R"), "row revision");
            var row = new DocTable(1).AddRow("a");
            row.Revision = RevisionMark.Insert("R");
            AssertTrue(row.Revision is not null, "insert allowed");
        });
    }

    private static string[] ZipNames(string path)
    {
        using var z = ZipFile.OpenRead(path);
        return z.Entries.Select(e => e.FullName).ToArray();
    }

    private static void AddPart(string path, string part, string xml)
    {
        var bytes = File.ReadAllBytes(path);
        using var ms = new MemoryStream();
        ms.Write(bytes, 0, bytes.Length);
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            var n = zip.CreateEntry(part);
            using var w = new StreamWriter(n.Open(), new UTF8Encoding(false));
            w.Write(xml);
        }
        File.WriteAllBytes(path, ms.ToArray());
    }
}
