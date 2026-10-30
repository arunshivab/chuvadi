using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Chuvadi.Docs.Conversion;
using Chuvadi.Docs.Word;
using Chuvadi.Markup.Markdown;
using Paragraph = Chuvadi.Docs.Word.Paragraph;
using static Chuvadi.Docs.Conversion.ManualTests.Program;

namespace Chuvadi.Docs.Conversion.ManualTests;

/// <summary>Regression checks for the independent review of the Conversion library.</summary>
public static class ReviewFixesTests
{
    public static void Run(string outDir)
    {
        Check("raw inline HTML never swallows later text (void and unclosed dropped elements)", () =>
        {
            var a = MarkdownConversion.Import("- <input type=\"checkbox\"> buy milk\n- call mom\n\n# Next section\n\nBody text.");
            var ta = a.Blocks.OfType<Paragraph>().Select(p => p.GetText()).ToList();
            AssertTrue(ta.Contains("call mom") && ta.Contains("Next section") && ta.Contains("Body text.") && ta[0].Contains("buy milk", StringComparison.Ordinal), string.Join("|", ta));
            var b = MarkdownConversion.Import("Use the <script> tag carefully.\n\nSecond paragraph.\n\n# Heading\n\nThird `code` here.");
            var tb = b.Blocks.OfType<Paragraph>().Select(p => p.GetText()).ToList();
            AssertTrue(tb.Contains("Second paragraph.") && tb.Contains("Heading") && tb.Contains("Third code here."), string.Join("|", tb));
            AssertTrue(tb[0] == "Use the ", "script content dropped only to the end of its paragraph: " + tb[0]);
        });

        Check("raw tags cannot extend a link or close Markdown emphasis", () =>
        {
            var d = MarkdownConversion.Import("[a <b>x](https://e.com) after text.\n\n**bold </b> still?**");
            var p = d.Blocks.OfType<Paragraph>().ToList();
            AssertTrue(p[0].Runs.Single(r => r.TextContent.Contains("after", StringComparison.Ordinal)).HyperlinkUrl is null, "link ends with its text");
            AssertTrue(p[0].Runs.Single(r => r.TextContent == "x").Format.Bold && p[0].Runs.Single(r => r.TextContent == "x").HyperlinkUrl == "https://e.com", "inner bold kept");
            AssertTrue(p[1].Runs.Where(r => r.TextContent.Length > 0).All(r => r.Format.Bold), "stray </b> leaves ** bold alone");
        });

        Check("table budgets: empty rows skipped, cell budget, 63-column cap for Markdown", () =>
        {
            var html = "<table><tr><td colspan=63>x</td></tr>" + string.Concat(Enumerable.Repeat("<tr>", 100_000)) + "</table>";
            var sw = Stopwatch.StartNew();
            var r = HtmlConversion.ImportDetailed(html);
            AssertTrue(r.Document.Blocks.OfType<DocTable>().Single().Rows.Count == 1 && sw.ElapsedMilliseconds < 10000, $"rows {r.Document.Blocks.OfType<DocTable>().Single().Rows.Count}, {sw.ElapsedMilliseconds} ms");
            var md = "|" + string.Concat(Enumerable.Repeat(" h |", 1000)) + "\n|" + string.Concat(Enumerable.Repeat("---|", 1000)) + "\n" + string.Concat(Enumerable.Repeat("x\n", 5000));
            sw.Restart();
            var m = MarkdownConversion.ImportDetailed(md);
            var t = m.Document.Blocks.OfType<DocTable>().Single();
            AssertTrue(t.Columns == 63 && t.Rows.Count * 63 <= 100_000 && m.Warnings.Count >= 1 && sw.ElapsedMilliseconds < 15000, $"{t.Columns} cols, {t.Rows.Count} rows, {sw.ElapsedMilliseconds} ms");
            var small = MarkdownConversion.ImportDetailed("| a | b |\n|---|---|\n| 1 | 2 |", null, new MarkdownImportOptions { MaxTableCells = 2 });
            AssertTrue(small.Document.Blocks.OfType<DocTable>().Single().Rows.Count == 1 && small.Warnings.Any(), "custom budget");
        });

        Check("markdown export and import: unsafe, UNC and scheme-relative links are text only", () =>
        {
            var d = new Document();
            d.AddParagraph().Hyperlink("click", "javascript:alert(1)").Text(" ").Hyperlink("d", "data:text/html,<script>x</script>").Text(" ").Hyperlink("v", "vbscript:msgbox").Text(" ").Hyperlink("ok", "https://ok.example");
            d.AddParagraph().Field("HYPERLINK \"javascript:alert(2)\"", "js");
            var emf = ImageSpec.Inline(new byte[] { 1, 0, 0, 0 }, "image/x-emf", 10, 10);
            emf.AltText = "chart";
            d.AddParagraph().Image(emf);
            var md = MarkdownConversion.Export(d, new MarkdownExportOptions { ImageUrlResolver = i => "javascript:x" });
            foreach (var bad in new[] { "javascript", "vbscript", "data:text", "x-emf" }) AssertNotContains(md, bad, "markdown export");
            AssertContains(md, "click", "link text kept");
            AssertContains(md, "[ok](<https://ok.example>)", "safe link kept");
            AssertContains(md, "chart", "picture as alt text");
            var imp = MarkdownConversion.ImportDetailed("[a](<\\\\evil.example\\share>) [b](//evil.example/x) [c](file:///etc/passwd)");
            var runs = imp.Document.Blocks.OfType<Paragraph>().Single().Runs;
            AssertTrue(runs.All(r => r.HyperlinkUrl is null), "no link kept: " + string.Join(",", runs.Select(r => r.HyperlinkUrl)));
        });

        Check("hostile picture headers are rejected; nested tables are flattened past the cap", () =>
        {
            var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 8, 2, 0, 0, 0, 0, 0, 0, 0 };
            var r = MarkdownConversion.ImportDetailed($"![huge](data:image/png;base64,{Convert.ToBase64String(png)})");
            AssertTrue(!r.Document.Blocks.OfType<Paragraph>().Single().Runs.Any(x => x.Image is not null) && r.Warnings.Any(), "rejected with a warning");
            var path = Path.Combine(outDir, "review-hostile-png.docx");
            r.Document.SaveTo(path);
            AssertThrows<InvalidOperationException>(() => ImageSpec.FromBytes(png, "image/png"), "Docs ImageSpec.FromBytes refuses it too");
            var nested = string.Concat(Enumerable.Repeat("<table><tr><td>", 2000)) + "deep";
            var doc = HtmlConversion.Import(nested);
            var npath = Path.Combine(outDir, "review-nested-tables.docx");
            doc.SaveTo(npath);
            var back = Document.Load(npath);
            AssertTrue(back.GetText().Contains("deep", StringComparison.Ordinal), "reloads with its text");
        });

        Check("adjacent separate lists stay separate in HTML and Markdown", () =>
        {
            var d = HtmlConversion.Import("<ol><li>a</li><li>b</li></ol><ol><li>c</li><li>d</li></ol><ul><li>x</li></ul><p>p</p>");
            var html = HtmlConversion.Export(d, new HtmlExportOptions { FullDocument = false });
            AssertEqual(2, html.Split("<ol").Length - 1, "two ol: " + html);
            var md = MarkdownConversion.Export(d);
            var ast = MarkdownParser.Parse(md);
            var lists = ast.Children.OfType<ListBlock>().ToList();
            AssertTrue(lists.Count == 3 && lists[1].Start == 1 && lists[1].Children.Count == 2, "markdown keeps the lists apart:\n" + md);
        });

        Check("markdown export re-parses: adjacent and punctuation-edged emphasis", () =>
        {
            var d = new Document();
            d.AddParagraph().Text("Hello", TextFormat.BoldText).Text("World", TextFormat.BoldText);
            d.AddParagraph().Text("Note:", TextFormat.BoldText).Text("text");
            d.AddParagraph().Text("中文。", TextFormat.BoldText).Text("中文");
            d.AddParagraph().Text("a").Text("(b)", TextFormat.ItalicText).Text("c");
            d.AddParagraph().Text("x", TextFormat.BoldText).Text("y", new TextFormat { Bold = true, Italic = true });
            var md = MarkdownConversion.Export(d);
            var html = MarkdownConverter.ToHtml(md, null, new MarkdownHtmlOptions { RawHtml = RawHtmlMode.Passthrough });
            AssertContains(html, "<strong>HelloWorld</strong>", "merged");
            AssertContains(html, "<strong>Note:</strong>text", "punctuation end");
            AssertContains(html, "<strong>中文。</strong>中文", "CJK punctuation");
            AssertContains(html, "a<em>(b)</em>c", "punctuation start");
            AssertNotContains(html, "**", "no literal delimiters");
            var back = MarkdownConversion.Import(md);
            var bold = back.Blocks.OfType<Paragraph>().ElementAt(1).Runs.Single(r => r.TextContent == "Note:");
            AssertTrue(bold.Format.Bold, "raw tags import back as formatting");
        });

        Check("list item starting with a block keeps its number first; hidden links do not leak", () =>
        {
            var d = MarkdownConversion.Import("- - x\n  - z\n- y");
            var paras = d.Blocks.OfType<Paragraph>().ToList();
            AssertTrue(paras[0].GetText() == "" && paras[0].ListLevel == 0 && paras[1].GetText() == "x" && paras[1].ListLevel == 1, string.Join("|", paras.Select(p => p.GetText() + "@" + p.ListLevel)));
            var h = new Document();
            h.AddParagraph().Text("see ").Hyperlink("secret", "https://example.com/track?id=1", new TextFormat { Hidden = true }).Text(" end");
            var html = HtmlConversion.Export(h, new HtmlExportOptions { FullDocument = false });
            AssertNotContains(html, "track", "hidden link not written");
            AssertNotContains(MarkdownConversion.Export(h), "track", "markdown");
        });

        Check("gridBefore keeps columns aligned; ids are unique and safe; list types follow the format", () =>
        {
            var d = new Document();
            var t = d.AddTable(2);
            t.AddRow("A", "B");
            var r = t.AddRow("", "under B");
            r.GridBefore = 1;
            var html = HtmlConversion.Export(d, new HtmlExportOptions { FullDocument = false, Formatting = HtmlFormatting.Semantic });
            AssertContains(html, "<tr><td></td><td>under B</td></tr>", "placeholder cell: " + html);
            var text = TextConversion.Export(d);
            AssertContains(text, "\tunder B", "text keeps the column");

            var ids = HtmlConversion.Import("<p id=\"a b\">one</p><p id=\"a_b\">two</p><p><a href=\"#a_b\">to two</a></p>");
            var marks = ids.GetBookmarks().Select(b => b.Name).ToList();
            AssertTrue(marks.Count == 2 && marks.Distinct().Count() == 2, string.Join(",", marks));
            var link = ids.Blocks.OfType<Paragraph>().Last().Runs.Single(x => x.IsLink).HyperlinkAnchor;
            AssertEqual(marks[1], link, "link goes to the second id");

            var target = new Document();
            var tt = target.AddTable(1);
            tt.AddRow().Cells[0].Paragraphs[0].Bookmarked("intro", "in table");
            HtmlConversion.Import("<p id=\"intro\">appended</p>", target);
            AssertEqual(2, target.GetBookmarks().Select(b => b.Name).Distinct().Count(), "no duplicate name when appending");

            var e = new Document();
            e.AddParagraph().Bookmarked("fn-1", "clash").Text(" ").Bookmarked("sec+1", "plus");
            e.AddParagraph("x").Footnote(e, "note");
            e.AddParagraph().InternalLink("go", "fn-1");
            var eh = HtmlConversion.Export(e, new HtmlExportOptions { FullDocument = false });
            AssertContains(eh, "id=\"bm-fn-1\"", "bookmark kept apart from note ids");
            AssertContains(eh, "href=\"#bm-fn-1\"", "link follows");
            AssertContains(eh, "<span id=\"sec_1\"></span>", "second bookmark as an anchor, safe id");

            var l = new Document();
            var list = l.Numbering.AddNumberedList(NumberFormat.LowerLetter, "{0}.");
            l.AddParagraph("c-item").SetList(list);
            AssertContains(HtmlConversion.Export(l, new HtmlExportOptions { FullDocument = false }), "type=\"a\"", "letter list");
        });

        Check("code blocks keep note references; options: quote style, wrapper allowlist, cancellation, overloads", () =>
        {
            var d = new Document();
            var code = d.AddParagraph("var x = 1;");
            code.StyleId = "Code";
            code.Footnote(d, "about x");
            var html = HtmlConversion.Export(d, new HtmlExportOptions { FullDocument = false });
            AssertContains(html, "href=\"#fn-1\"", "note ref in pre");
            AssertContains(MarkdownConversion.Export(d), "var x = 1;[1]", "note ref in fence");

            var q = MarkdownConversion.Import("> quoted", new MarkdownImportOptions { QuoteStyleId = "MyQuote" });
            AssertTrue(q.Styles.Contains("MyQuote") && q.Blocks.OfType<Paragraph>().Single().StyleId == "MyQuote", "custom quote style created");

            var w = HtmlConversion.Export(new Document(), new HtmlExportOptions { FullDocument = false, WrapperElement = "script" });
            AssertNotContains(w, "script", "wrapper not on the allowlist is not written");

            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var big = string.Join("\n", Enumerable.Repeat("line", 5000));
            AssertThrows<OperationCanceledException>(() => TextConversion.Import(big, null, cts.Token), "text import cancels");
            AssertThrows<OperationCanceledException>(() => MarkdownConversion.Import(big, null, cts.Token), "markdown import cancels");
            AssertThrows<OperationCanceledException>(() => HtmlConversion.Import("<p>" + big, null, cts.Token), "html import cancels");

            var res = TextConversion.ImportDetailed("a\nb");
            AssertEqual(2, res.Document.Blocks.Count, "text ImportDetailed");
            var fromStream = HtmlConversion.Import(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<p>s</p>")));
            AssertEqual("s", fromStream.GetText().Trim(), "stream overload");
            var md = MarkdownConversion.ImportDetailed(new StringReader("# r"));
            AssertEqual("Heading1", md.Document.Blocks.OfType<Paragraph>().Single().StyleId, "reader ImportDetailed");
            var end = MarkdownConversion.Import("a\n\n---", new MarkdownImportOptions { ThematicBreakAsBorder = false });
            AssertTrue(end.Blocks.OfType<Paragraph>().Last().PageBreakBefore, "final thematic break as a page break");
        });
        Check("round 2: id de-duplication is linear; table-first items; no empty tables; flanking-aware emphasis", () =>
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 20000; i++) sb.Append("<p id=\"").Append(new string('a', 40)).Append(i).Append("\">x</p>");
            var sw = Stopwatch.StartNew();
            var many = HtmlConversion.Import(sb.ToString());
            AssertTrue(sw.ElapsedMilliseconds < 8000 && many.GetBookmarks().Select(b => b.Name).Distinct().Count() == 20000, sw.ElapsedMilliseconds + " ms");
            sw.Restart();
            HtmlConversion.Export(many, new HtmlExportOptions { FullDocument = false });
            AssertTrue(sw.ElapsedMilliseconds < 8000, "export " + sw.ElapsedMilliseconds + " ms");

            var h = HtmlConversion.Import("<ol><li><table><tr><td>cell</td></tr></table></li><li>two</li></ol>");
            var labels = h.GetListLabels();
            var first = h.Blocks.First();
            AssertTrue(first is Paragraph fp && labels.TryGetValue(fp, out var l1) && l1.Text == "1." && h.Blocks[1] is DocTable, "number before the table");
            var m = MarkdownConversion.Import("1. | a |\n   |---|\n   | b |\n2. two");
            AssertTrue(m.Blocks.First() is Paragraph mp && m.GetListLabels().ContainsKey(mp) && m.Blocks[1] is DocTable, "markdown: number before the table");

            var budget = MarkdownConversion.ImportDetailed("| a | b |\n|---|---|\n\n| c |\n|---|", null, new MarkdownImportOptions { MaxTableCells = 2 });
            AssertTrue(budget.Document.Blocks.OfType<DocTable>().All(t => t.Rows.Count > 0), "no table without rows");

            var d = new Document();
            d.AddParagraph().Text("Note: ", TextFormat.BoldText).Text("text");
            d.AddParagraph().Text("see ").Text("(fig. 1)", TextFormat.ItalicText).Text(" here");
            d.AddParagraph().Text("snake").Text("case", TextFormat.ItalicText).Text("word");
            var md = MarkdownConversion.Export(d, new MarkdownExportOptions { Writer = new MarkdownWriterOptions { EmphasisChar = '_' } });
            AssertContains(md, "**Note:** text", "plain Markdown where it works");
            AssertContains(md, "see _(fig. 1)_ here", "punctuation with spaces outside");
            AssertContains(md, "snake<em>case</em>word", "_ inside a word falls back");
            var plain = MarkdownConversion.Export(d, new MarkdownExportOptions { EmphasisFallback = MarkdownEmphasisFallback.PlainText, Writer = new MarkdownWriterOptions { EmphasisChar = '_' } });
            AssertContains(plain, "snakecaseword", "plain-text fallback");
            var back = MarkdownConversion.Import(md).Blocks.OfType<Paragraph>().ElementAt(2);
            AssertTrue(back.Runs.Single(r => r.TextContent == "case").Format.Italic, "re-imports");
        });

        Check("round 2: preview block cap reaches into tables; stream limit; anchors encoded", () =>
        {
            var d = new Document();
            var t = d.AddTable(1);
            for (int i = 0; i < 5000; i++) t.AddRow("r" + i);
            var html = HtmlConversion.Export(d, new HtmlExportOptions { FullDocument = false, MaxBlocks = 100 });
            AssertTrue(html.Split("<tr>").Length - 1 == 100, "rows capped");
            var o = new HtmlImportOptions();
            o.Policy.Limits.MaxInputChars = 1000;
            AssertThrows<Chuvadi.Markup.MarkupLimitExceededException>(() => HtmlConversion.Import(new MemoryStream(new byte[100_000]), null, o), "stream capped");
            AssertThrows<Chuvadi.Markup.MarkupLimitExceededException>(() => HtmlConversion.Import(new StringReader(new string('x', 5000)), o), "reader capped");
            var a = new Document();
            a.AddParagraph().Field("HYPERLINK \\l \"sec 1\"", "jump");
            var md = MarkdownConversion.Export(a);
            AssertContains(md, "#sec%201", "anchor encoded");
            AssertEqual("sec_1", MarkdownConversion.Import(md).Blocks.OfType<Paragraph>().Single().Runs.Single(r => r.IsLink).HyperlinkAnchor, "decoded on import, as a valid bookmark name");
        });
    }
}
