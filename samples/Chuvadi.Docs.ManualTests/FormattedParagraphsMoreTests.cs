using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Chuvadi.Docs.Internal;
using Chuvadi.Docs.Model;
using static Chuvadi.Docs.ManualTests.Program;

namespace Chuvadi.Docs.ManualTests;

/// <summary>
/// Docs 2.4.0 (D-387): <see cref="DocxReader.FormattedParagraphs()"/> with footnotes and endnotes
/// (<see cref="DocxReaderOptions.IncludeNotes"/>), comments (<see cref="DocxReaderOptions.IncludeComments"/>), text boxes
/// (<see cref="DocxReaderOptions.IncludeTextBoxes"/>) and list labels (<see cref="DocxFormattedParagraph.ListLabel"/>),
/// each checked against <see cref="Document.Load(Stream, string?)"/> on the same file.
/// </summary>
public static class FormattedParagraphsMoreTests
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static DocxReaderOptions All() => new() { IncludeNotes = true, IncludeComments = true, IncludeTextBoxes = true };

    public static void Run(string outDir)
    {
        var rich = Rich();
        File.WriteAllBytes(Path.Combine(outDir, "formatted-paragraphs-more.docx"), rich);

        Check("off by default: no note, comment or text-box items, and no reference runs (the 2.3.0 stream); labels are always there", () =>
        {
            var defaults = new DocxReaderOptions();
            AssertTrue(!defaults.IncludeNotes && !defaults.IncludeComments && !defaults.IncludeTextBoxes, "the defaults");
            using var r = DocxReader.Open(new MemoryStream(rich));
            var items = r.FormattedParagraphs().ToList();
            AssertTrue(items.All(i => i.Note is null && i.Comment is null && i.TextBox is null && i.Host is null), "no extra items");
            AssertTrue(items.SelectMany(i => i.Paragraph.Runs).All(x => x.NoteReference is null && x.CommentReference is null && x.CommentRange is null), "no reference runs");
            AssertTrue(items.Count(i => i.ListLabel is not null) >= 4, "labels without any option");
            AssertTrue(r.SkippedParts.Count == 0, "nothing skipped");
        });

        Check("everything on: the same paragraphs, runs, formats and labels as Load, in the order body, headers, footers, footnotes, endnotes, comments", () =>
        {
            int n = CompareAll(rich, "rich");
            AssertTrue(n >= 20, "items compared: " + n);
            using var r = DocxReader.Open(new MemoryStream(rich), options: All());
            var kinds = r.FormattedParagraphs().Where(i => i.TextBox is null).Select(Kind).ToList();
            var order = kinds.Distinct().ToList();
            AssertTrue(string.Join(",", order) == "body,header,footer,footnote,endnote,comment", "order: " + string.Join(",", order));
            for (int i = 1; i < kinds.Count; i++)
                AssertTrue(order.IndexOf(kinds[i]) >= order.IndexOf(kinds[i - 1]), "kinds stay grouped at " + i);
        });

        Check("IncludeNotes: the body keeps its reference runs; note items carry their Note, kind and part; a list inside the notes is one story across footnotes", () =>
        {
            using var r = DocxReader.Open(new MemoryStream(rich), options: new DocxReaderOptions { IncludeNotes = true });
            var items = r.FormattedParagraphs().ToList();
            var body = items.Where(i => i.Note is null && i.Part == "/word/document.xml").ToList();
            AssertTrue(body.SelectMany(i => i.Paragraph.Runs).Count(x => x.NoteReference is not null) == 4, "four references in the body (three footnotes, one endnote)");
            AssertTrue(body.SelectMany(i => i.Paragraph.Runs).All(x => x.CommentReference is null), "no comment references without IncludeComments");
            var foot = items.Where(i => i.Note?.Kind == NoteKind.Footnote).ToList();
            var end = items.Where(i => i.Note?.Kind == NoteKind.Endnote).ToList();
            AssertTrue(foot.Count >= 3 && end.Count >= 1, $"footnote items {foot.Count}, endnote items {end.Count}");
            AssertTrue(foot.All(i => i.Part == "/word/footnotes.xml") && end.All(i => i.Part == "/word/endnotes.xml"), "parts: " + foot[0].Part + " " + end[0].Part);
            AssertTrue(items.IndexOf(end[0]) > items.IndexOf(foot[^1]), "endnotes after footnotes");
            var noteLabels = foot.Where(i => i.ListLabel is not null).Select(i => i.ListLabel!.Text).ToList();
            AssertTrue(string.Join("|", noteLabels) == "1.|2.", "the footnote list counts across notes: " + string.Join("|", noteLabels));
            AssertTrue(items.All(i => i.Comment is null), "no comments");
        });

        Check("IncludeComments: reference and range runs in the body; comment items carry author, initials, date, done and the reply parent, after the notes", () =>
        {
            using var r = DocxReader.Open(new MemoryStream(rich), options: new DocxReaderOptions { IncludeComments = true });
            var items = r.FormattedParagraphs().ToList();
            var runs = items.Where(i => i.Comment is null).SelectMany(i => i.Paragraph.Runs).ToList();
            AssertTrue(runs.Count(x => x.CommentReference is not null) >= 1 && runs.Any(x => x.CommentRange is not null), "references and ranges");
            AssertTrue(runs.All(x => x.NoteReference is null), "no note references without IncludeNotes");
            var comments = items.Where(i => i.Comment is not null).ToList();
            AssertTrue(comments.Count >= 2, "comment items: " + comments.Count);
            var first = comments[0].Comment!;
            AssertTrue(first.Author == "Dr. Jane Example" && first.Initials == "JE" && first.Date == new DateTime(2026, 11, 15, 9, 30, 0), $"first: {first.Author} {first.Initials} {first.Date}");
            var reply = comments.Select(i => i.Comment!).First(c => c.Parent is not null);
            AssertTrue(reply.Parent!.Author == "Dr. Jane Example" && reply.Author == "Reviewer", "the reply");
            AssertTrue(comments.All(i => i.ListLabel is null && i.Part == "/word/comments.xml"), "no labels, the comments part");
        });

        Check("IncludeTextBoxes: box paragraphs follow their host in order, a table in a box carries its cell; no labels in boxes", () =>
        {
            using var r = DocxReader.Open(new MemoryStream(rich), options: new DocxReaderOptions { IncludeTextBoxes = true });
            var items = r.FormattedParagraphs().ToList();
            int host = items.FindIndex(i => i.Text.StartsWith("Host paragraph", StringComparison.Ordinal));
            AssertTrue(host >= 0, "the host");
            var texts = items.Skip(host + 1).TakeWhile(i => i.TextBox is not null).Select(i => i.Text).ToList();
            // The box ends with a table, so the file has the empty paragraph Word needs after it.
            AssertTrue(string.Join("|", texts) == "Box line 1|Box line 2|Box cell|", "after the host: " + string.Join("|", texts));
            var box = items[host + 1];
            AssertTrue(texts.Count == 4 && items.Skip(host + 1).Take(4).All(i => i.Host == items[host].Paragraph && i.TextBox == box.TextBox), "Host and TextBox on each");
            var cell = items.First(i => i.Text == "Box cell");
            AssertTrue(cell.Cell is not null && cell.TextBox == box.TextBox, "a table in the box: " + cell.Cell);
            AssertTrue(items.Where(i => i.TextBox is not null).All(i => i.ListLabel is null), "no labels in boxes, as GetListLabel");
            using var off = DocxReader.Open(new MemoryStream(rich));
            AssertTrue(off.FormattedParagraphs().Count() == items.Count - 4, "without the option, the four box items are not yielded");
            AssertTrue(off.FormattedParagraphs().First(i => i.Text.StartsWith("Host paragraph", StringComparison.Ordinal)).Paragraph.Runs.Any(x => x.TextBox is not null), "the box stays in its host's runs");
        });

        Check("list labels match Document.GetListLabel: body, nested levels, table cells, a restart, a header counted on its own", () =>
        {
            var doc = Document.Load(new MemoryStream(rich));
            using var r = DocxReader.Open(new MemoryStream(rich));
            var items = r.FormattedParagraphs().ToList();
            var labelled = items.Where(i => i.ListLabel is not null).Select(i => i.Text + "=" + i.ListLabel!.Text).ToList();
            AssertTrue(labelled.Contains("First item=1.") && labelled.Contains("Second item=2.") && labelled.Contains("Sub item=1.") /* AddNumberedList: "{0}." at every level */
                && labelled.Contains("Third item=3.") && labelled.Contains("Cell item=4.") && labelled.Contains("Restarted=1.")
                && labelled.Contains("Header item=1."), "labels: " + string.Join(", ", labelled));
            var want = doc.GetListLabels();
            AssertTrue(items.Count(i => i.Part == "/word/document.xml" && i.ListLabel is not null) == doc.Blocks.OfType<Paragraph>().Count(p => want.ContainsKey(p))
                + doc.Blocks.OfType<DocTable>().SelectMany(t => t.Rows).SelectMany(row => row.Cells).SelectMany(c => c.Paragraphs).Count(p => want.ContainsKey(p)), "the same labelled paragraphs as Load");
        });

        Check("limits, errors and cancellation with the options on: the notes count towards MaxElements; a broken notes part is listed and its references dropped; the token stops the notes", () =>
        {
            // One limit: enough for the body and the formatting parts, not for the notes and comments as well.
            int baseElements = CountElements(rich, "word/document.xml") + CountElements(rich, "word/styles.xml")
                + CountElements(rich, "word/numbering.xml") + CountElements(rich, "word/settings.xml");
            int storyElements = CountElements(rich, "word/footnotes.xml") + CountElements(rich, "word/endnotes.xml")
                + CountElements(rich, "word/comments.xml");
            AssertTrue(storyElements > 20, "the stories have elements: " + storyElements);
            int limit = baseElements + storyElements / 2;
            using (var r = DocxReader.Open(new MemoryStream(rich), options: new DocxReaderOptions { MaxElements = limit, IncludeHeadersAndFooters = false }))
                AssertTrue(r.FormattedParagraphs().Any(), "passes without the notes and comments");
            using (var r = DocxReader.Open(new MemoryStream(rich), options: new DocxReaderOptions { MaxElements = limit, IncludeHeadersAndFooters = false, IncludeNotes = true, IncludeComments = true }))
                AssertThrows<DocxSecurityLimitException>(() => r.FormattedParagraphs().ToList(), "the notes and comments count");

            var broken = ErrorContractMatrix.Replace(rich, "word/footnotes.xml", $"<w:footnotes xmlns:w=\"{W}\"><w:footnote w:id=\"1\"><w:p>");
            using (var r = DocxReader.Open(new MemoryStream(broken), options: new DocxReaderOptions { IncludeNotes = true }))
            {
                var items = r.FormattedParagraphs().ToList();
                AssertTrue(r.SkippedParts.Any(s => s.PartName == "/word/footnotes.xml" && s.Reason == DocxSkippedPartReason.NotWellFormed), "listed");
                AssertTrue(items.All(i => i.Note?.Kind != NoteKind.Footnote), "no footnote items");
                AssertTrue(items.SelectMany(i => i.Paragraph.Runs).Count(x => x.NoteReference is not null) == 1, "only the endnote's reference is left");
                var loaded = Document.Load(new MemoryStream(broken));
                AssertTrue(loaded.Footnotes.Count == 0 && loaded.SkippedParts.Any(s => s.PartName == "/word/footnotes.xml"), "as Load");
            }

            using var cts = new CancellationTokenSource();
            using (var r = DocxReader.Open(new MemoryStream(rich), options: All()))
            {
                int seen = 0;
                AssertThrows<OperationCanceledException>(() =>
                {
                    foreach (var item in r.FormattedParagraphs(cts.Token))
                        if (item.Note is not null && ++seen == 1) cts.Cancel();
                }, "cancelled during the notes");
            }
        });

        Check("every .docx written by the earlier groups, with every option on: the same items as Load", () =>
        {
            int files = 0, items = 0;
            foreach (var path in Directory.EnumerateFiles(outDir, "*.docx", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
            {
                var bytes = File.ReadAllBytes(path);
                try { Document.Load(new MemoryStream(bytes)); }
                catch (Exception e) when (e is DocxFormatException or DocxPasswordRequiredException) { continue; }
                items += CompareAll(bytes, Path.GetFileName(path));
                files++;
            }
            bool alone = files < 20;
            AssertTrue(alone ? files >= 1 : files >= 20 && items >= 200, $"{files} files, {items} items");
            Console.WriteLine($"        ({files} files, {items} items compared)");
        });
    }

    private static string Kind(DocxFormattedParagraph i)
        => i.Note is { } n ? (n.Kind == NoteKind.Footnote ? "footnote" : "endnote")
         : i.Comment is not null ? "comment"
         : i.Part.Contains("header", StringComparison.Ordinal) ? "header"
         : i.Part.Contains("footer", StringComparison.Ordinal) ? "footer" : "body";

    // ---- equivalence with Load, every option on -----------------------------------------------

    /// <summary>
    /// The reader's body, footnote, endnote, comment and text-box items against Load's model: paragraphs, every run
    /// (references included), effective run and paragraph formats and list labels. Returns the items compared.
    /// </summary>
    private static int CompareAll(byte[] package, string name)
    {
        var doc = Document.Load(new MemoryStream(package));
        var labels = doc.GetListLabels();
        using var reader = DocxReader.Open(new MemoryStream(package), options: All());
        var got = reader.FormattedParagraphs().ToList();
        string main = got.Count > 0 ? got[0].Part : "/word/document.xml";

        var expected = new List<(Paragraph P, TableCellRef? Cell, string Kind)>();
        void Story(IEnumerable<object> blocks, string kind)
        {
            foreach (var b in blocks)
            {
                if (b is Paragraph p) { expected.Add((p, null, kind)); Boxes(p, kind); }
                else if (b is DocTable t) Table(t, kind);
            }
        }
        void Table(DocTable t, string kind)
        {
            for (int r = 0; r < t.Rows.Count; r++)
                for (int c = 0; c < t.Rows[r].Cells.Count; c++)
                    foreach (var b in t.Rows[r].Cells[c].Blocks)
                    {
                        if (b is Paragraph p) { expected.Add((p, new TableCellRef(t, r, c), kind)); Boxes(p, kind); }
                        else if (b is DocTable n) Table(n, kind);
                    }
        }
        void Boxes(Paragraph host, string kind, int depth = 0)
        {
            if (depth > 8) return;
            foreach (var run in host.Runs)
            {
                if (run.TextBox is not { } box) continue;
                foreach (var b in box.Blocks)
                {
                    if (b is Paragraph bp) { expected.Add((bp, null, "box")); Boxes(bp, kind, depth + 1); }
                    else if (b is DocTable bt)
                    {
                        var start = expected.Count;
                        Table(bt, "box");
                    }
                }
            }
        }
        Story(doc.Blocks, "body");
        int bodyCount = expected.Count;
        foreach (var note in doc.Footnotes) Story(note.Blocks, "footnote");
        foreach (var note in doc.Endnotes) Story(note.Blocks, "endnote");
        foreach (var comment in doc.Comments) Story(comment.Blocks, "comment");

        var actual = got.Where(i => i.Part == main || i.Note is not null || i.Comment is not null).ToList();
        if (actual.Count != expected.Count)
            throw new InvalidOperationException($"{name}: {actual.Count} items against Load's {expected.Count} (body and boxes {bodyCount})");
        for (int i = 0; i < actual.Count; i++)
        {
            var (p, cell, kind) = expected[i];
            var item = actual[i];
            string where = $"{name}, item {i} ({kind}) \"{(p.GetText().Length > 30 ? p.GetText()[..30] : p.GetText())}\"";
            if ((kind == "box") != (item.TextBox is not null)) throw new InvalidOperationException(where + ": text-box flag");
            if (p.GetText() != item.Text) throw new InvalidOperationException($"{where}: text \"{item.Text}\"");
            if ((cell is null) != (item.Cell is null) || (cell is { } c && (c.Row != item.Cell!.Value.Row || c.Column != item.Cell.Value.Column)))
                throw new InvalidOperationException(where + ": cell");
            if (p.Runs.Count != item.Paragraph.Runs.Count)
                throw new InvalidOperationException($"{where}: {item.Paragraph.Runs.Count} runs against Load's {p.Runs.Count}");
            for (int k = 0; k < p.Runs.Count; k++)
            {
                var want = cell is { } cr ? doc.GetEffectiveRunFormat(p, p.Runs[k], cr) : doc.GetEffectiveRunFormat(p, p.Runs[k]);
                if ((p.Runs[k].NoteReference is null) != (item.Paragraph.Runs[k].NoteReference is null)
                    || (p.Runs[k].CommentReference is null) != (item.Paragraph.Runs[k].CommentReference is null))
                    throw new InvalidOperationException($"{where}, run {k}: reference");
                string a = FormattedParagraphsTests.Describe(want), b = FormattedParagraphsTests.Describe(item.RunFormats[k]);
                if (a != b) throw new InvalidOperationException($"{where}, run {k}: format\n  reader: {b}\n  Load:   {a}");
            }
            var wantP = cell is { } cp ? doc.GetEffectiveParagraphFormat(p, cp) : doc.GetEffectiveParagraphFormat(p);
            if (FormattedParagraphsTests.Describe(wantP) != FormattedParagraphsTests.Describe(item.ParagraphFormat))
                throw new InvalidOperationException($"{where}: paragraph format");
            labels.TryGetValue(p, out var wantLabel);
            if (kind is "box" or "comment") wantLabel = null; // not counted by GetListLabels either
            if (Label(wantLabel) != Label(item.ListLabel))
                throw new InvalidOperationException($"{where}: label {Label(item.ListLabel)} against Load's {Label(wantLabel)}");
        }
        return actual.Count;
    }

    private static string Label(ListLabel? l)
        => l is null ? "none" : $"{l.Text}/{l.Number}/{l.Level}/{l.Suffix}/{FormattedParagraphsTests.Describe(l.Format)}";

    private static int CountElements(byte[] package, string part)
    {
        using var z = new System.IO.Compression.ZipArchive(new MemoryStream(package));
        var e = z.GetEntry(part);
        if (e is null) return 0;
        int n = 0;
        using var x = System.Xml.XmlReader.Create(e.Open());
        while (x.Read()) if (x.NodeType == System.Xml.XmlNodeType.Element) n++;
        return n;
    }

    // ---- input ----------------------------------------------------------------------------------

    /// <summary>Lists (nested, in a table, restarted, in a header and in footnotes), notes, comments with a reply, and text boxes.</summary>
    private static byte[] Rich()
    {
        var d = new Document();
        var list = d.Numbering.AddNumberedList();
        d.AddParagraph("Title of the test", ParagraphStyle.Heading1);
        d.AddParagraph("").Text("First item").SetList(list);
        d.AddParagraph("").Text("Second item").Footnote(d, "Footnote one").SetList(list);
        d.AddParagraph("").Text("Sub item").SetList(list, 1);
        d.AddParagraph("").Text("Third item").SetList(list);
        var t = d.AddTable(2);
        t.AddRow("Cell item", "plain cell");
        t.Rows[0].Cells[0].Paragraphs[0].SetList(list);
        var again = d.Numbering.Restart(list);
        d.AddParagraph("").Text("Restarted").SetList(again);

        var comment = d.Comments.Add("Dr. Jane Example", "Please check this.", "JE", new DateTime(2026, 11, 15, 9, 30, 0));
        d.Comments.Reply(comment, "Reviewer", "Checked.");
        d.AddParagraph("").Text("Before ").Commented("commented words", comment).Text(" after.").Footnote(d, "Footnote two");

        var box = new TextBox();
        box.Paragraphs.Add(new Paragraph("Box line 1"));
        box.Paragraphs.Add(new Paragraph("Box line 2"));
        box.AddTable(1).AddRow("Box cell");
        d.AddParagraph("").Text("Host paragraph ").TextBox(box).Endnote(d, "Endnote one");
        d.AddParagraph("Last body paragraph");

        // A list inside the footnotes: one count across all footnotes.
        var noteList = d.Numbering.AddNumberedList();
        d.Footnotes[0].Paragraphs.Add(new Paragraph("").Text("Note list one").SetList(noteList));
        var third = d.AddParagraph("").Text("Third reference");
        third.Footnote(d, "Footnote three");
        d.Footnotes[2].Paragraphs.Add(new Paragraph("").Text("Note list two").SetList(noteList));

        var headerList = d.Numbering.AddNumberedList();
        d.Header.Add(new Paragraph("").Text("Header item").SetList(headerList));
        d.Footer.Add(new Paragraph("Footer text"));

        var ms = new MemoryStream();
        d.SaveTo(ms);
        return ms.ToArray();
    }
}
