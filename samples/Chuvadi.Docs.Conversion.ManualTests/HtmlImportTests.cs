using System;
using System.IO;
using System.Linq;
using System.Text;
using Chuvadi.Docs.Conversion;
using Chuvadi.Docs.Word;
using static Chuvadi.Docs.Conversion.ManualTests.Program;

namespace Chuvadi.Docs.Conversion.ManualTests;

public static class HtmlImportTests
{
    public static void Run(string outDir)
    {
        Check("html import: blocks, headings, inline formatting, inline styles, whitespace", () =>
        {
            var html = "<html><head><title> My page </title></head><body>"
                + "<h1>Head</h1><p style=\"text-align:center\">  Some   <b>bold</b>\n<em>em</em> <u>u</u> <s>s</s> H<sub>2</sub>O x<sup>2</sup> <code>code</code> <mark>hi</mark></p>"
                + "<p><span style=\"color:#ff0000;font-size:16px;font-family:'Georgia', serif;font-weight:700\">styled</span> <font color=\"blue\" size=\"5\">font</font></p>"
                + "<div>div text<p>inner</p>tail</div><center>c</center><p>line<br>break</p></body></html>";
            var doc = HtmlConversion.Import(html);
            AssertEqual("My page", doc.Properties.Title, "title");
            var p = doc.Blocks.OfType<Paragraph>().ToList();
            AssertEqual("Heading1", p[0].StyleId, "h1");
            AssertEqual("Some bold em u s H2O x2 code hi", p[1].GetText(), "whitespace collapsed");
            AssertEqual(ParagraphAlignment.Center, p[1].Alignment, "text-align");
            AssertTrue(p[1].Runs.Single(r => r.TextContent == "bold").Format.Bold && p[1].Runs.Single(r => r.TextContent == "em").Format.Italic, "b/em");
            AssertTrue(p[1].Runs.Single(r => r.TextContent == "u").Format.Underline && p[1].Runs.Single(r => r.TextContent == "s").Format.Strikethrough, "u/s");
            AssertEqual(VerticalTextPosition.Superscript, p[1].Runs.Single(r => r.TextContent == "2" && r.Format.VerticalPosition == VerticalTextPosition.Superscript).Format.VerticalPosition, "sup");
            AssertEqual("yellow", p[1].Runs.Single(r => r.TextContent == "hi").Format.Highlight, "mark");
            var styled = p[2].Runs.Single(r => r.TextContent == "styled").Format;
            AssertTrue(styled.ColorHex == "FF0000" && styled.SizePt == 12 && styled.Font == "Georgia" && styled.Bold, $"css: {styled.ColorHex} {styled.SizePt} {styled.Font}");
            var font = p[2].Runs.Single(r => r.TextContent == "font").Format;
            AssertTrue(font.ColorHex == "0000FF" && font.SizePt == 18, "font element");
            var texts = p.Select(q => q.GetText()).ToList();
            AssertTrue(texts.Contains("div text") && texts.Contains("inner") && texts.Contains("tail"), string.Join("|", texts));
            AssertEqual(ParagraphAlignment.Center, p.Single(q => q.GetText() == "c").Alignment, "center element");
            AssertTrue(p.Single(q => q.GetText().StartsWith("line", StringComparison.Ordinal)).Runs.Any(r => r.Break == BreakType.Line), "br");
            doc.SaveTo(Path.Combine(outDir, "html-import-inline.docx"));
        });

        Check("html import: lists (nested, start), quotes, pre, definition lists, rules", () =>
        {
            var html = "<ul><li>a<ul><li>a1</li></ul></li><li>b</li></ul><ol start=\"5\"><li>five</li><li>six<p>more</p></li></ol>"
                + "<blockquote><p>q</p><blockquote>qq</blockquote></blockquote><pre>  x = 1\n    y</pre><dl><dt>Term</dt><dd>Def</dd></dl><hr><p>end</p>";
            var doc = HtmlConversion.Import(html);
            var labels = doc.GetListLabels();
            var p = doc.Blocks.OfType<Paragraph>().ToList();
            var a1 = p.Single(q => q.GetText() == "a1");
            AssertTrue(a1.ListLevel == 1 && labels.ContainsKey(a1), "nested bullet");
            AssertEqual("5.", labels[p.Single(q => q.GetText() == "five")].Text, "start=5");
            AssertEqual("6.", labels[p.Single(q => q.GetText() == "six")].Text, "six");
            var more = p.Single(q => q.GetText() == "more");
            AssertTrue(!labels.ContainsKey(more) && (more.Format.IndentLeftPt ?? 0) > 0, "second paragraph of an item is indented, not numbered");
            AssertTrue(p.Single(q => q.GetText() == "qq").StyleId == "Quote" && (p.Single(q => q.GetText() == "qq").Format.IndentLeftPt ?? 0) == 36, "nested quote");
            AssertEqual("  x = 1\n    y", p.Single(q => q.StyleId == "Code").GetText(), "pre keeps whitespace");
            AssertTrue(p.Single(q => q.GetText() == "Term").Runs[0].Format.Bold && (p.Single(q => q.GetText() == "Def").Format.IndentLeftPt ?? 0) == 36, "dl");
            AssertTrue(p.Any(q => q.Format.Borders?.Bottom is not null), "hr");
            doc.SaveTo(Path.Combine(outDir, "html-import-blocks.docx"));
        });

        Check("html import: tables with header, colspan, rowspan, alignment, background, caption", () =>
        {
            var html = "<table><caption>Cap</caption><thead><tr><th>A</th><th colspan=\"2\">BC</th></tr></thead>"
                + "<tbody><tr><td rowspan=\"2\" style=\"background-color:#eeeeee\">tall</td><td align=\"right\">r</td><td>c</td></tr>"
                + "<tr><td>d</td><td valign=\"middle\">e</td></tr></tbody></table>";
            var doc = HtmlConversion.Import(html);
            var t = doc.Blocks.OfType<DocTable>().Single();
            AssertEqual(3, t.Columns, "grid columns");
            AssertTrue(t.Rows[0].IsHeader && t.Rows[0].Cells[1].ColumnSpan == 2, "header and colspan");
            AssertTrue(t.Rows[1].Cells[0].VerticalMerge == VerticalMerge.Restart && t.Rows[2].Cells[0].VerticalMerge == VerticalMerge.Continue, "rowspan");
            AssertEqual("EEEEEE", t.Rows[1].Cells[0].ShadeHex, "background");
            AssertEqual(ParagraphAlignment.Right, t.Rows[1].Cells[1].Paragraphs[0].Alignment, "align");
            AssertEqual("d", t.Rows[2].Cells[1].Paragraphs[0].GetText(), "rowspan pushes cells right");
            AssertEqual(CellVerticalAlignment.Center, t.Rows[2].Cells[2].VerticalAlignment, "valign");
            AssertTrue(doc.Blocks.OfType<Paragraph>().First().GetText() == "Cap", "caption");
            doc.SaveTo(Path.Combine(outDir, "html-import-table.docx"));
        });

        Check("html import: hostile input is sanitized before it reaches the document", () =>
        {
            var vectors = new[]
            {
                "<script>alert(1)</script>ok",
                "<img src=x onerror=alert(1)>",
                "<a href=\"javascript:alert(1)\">j</a>",
                "<a href=\"  JaVaScRiPt:alert(1)\">j2</a>",
                "<svg><script>alert(1)</script></svg>",
                "<iframe src=\"https://evil.example\"></iframe>",
                "<form action=\"https://evil.example\"><input name=p></form>",
                "<math><mtext><table><mglyph><style><img src=x onerror=alert(1)>",
                "<noscript><p title=\"</noscript><img src=x onerror=alert(1)>\">",
                "<a href=\"vbscript:msgbox(1)\">v</a><object data=\"x\"></object><embed src=\"x\">",
                "<div style=\"background:url(https://track.example/p.gif)\">bg</div>",
                "<img src=\"https://track.example/pixel.png\" alt=\"pixel\">",
            };
            // Each vector on its own (some swallow what follows them, as a browser would), then all together.
            int n = 0;
            foreach (var v in vectors.Append(string.Join("<br>", vectors)))
            {
                var r = HtmlConversion.ImportDetailed(v);
                var path = Path.Combine(outDir, $"html-import-hostile-{++n}.docx");
                r.Document.SaveTo(path);
                var xml = ReadPart(path, "word/document.xml");
                foreach (var bad in new[] { "alert", "javascript", "vbscript", "onerror", "msgbox", "evil.example", "track.example" })
                    AssertNotContains(xml, bad, "document.xml for " + v);
                var rels = ReadPart(path, "word/_rels/document.xml.rels");
                AssertNotContains(rels, "script", "relationships for " + v);
                AssertNotContains(rels, "evil", "relationships for " + v);
            }
            var tracked = HtmlConversion.ImportDetailed(vectors[10] + vectors[11] + vectors[0] + vectors[3]);
            AssertTrue(tracked.BlockedResources.Count(u => u.Contains("track.example", StringComparison.Ordinal)) == 2, "remote resources reported: " + string.Join(",", tracked.BlockedResources));
            var text = tracked.Document.GetText();
            AssertTrue(text.Contains("ok", StringComparison.Ordinal) && text.Contains("j2", StringComparison.Ordinal) && text.Contains("pixel", StringComparison.Ordinal) && text.Contains("bg", StringComparison.Ordinal), "text kept: " + text);
        });

        Check("html import: ids become bookmarks for internal links; data pictures; bytes with charset", () =>
        {
            var png = Convert.ToBase64String(Png());
            var html = $"<p><a href=\"#sec 2\">jump</a></p><h2 id=\"sec 2\">Two</h2><p><img src=\"data:image/png;base64,{png}\" alt=\"dot\" width=\"2\"></p>";
            var doc = HtmlConversion.Import(html);
            var p = doc.Blocks.OfType<Paragraph>().ToList();
            var link = p[0].Runs.Single(r => r.IsLink);
            var mark = p[1].Runs.First(r => r.Bookmark is not null).Bookmark!.Bookmark.Name;
            AssertEqual(link.HyperlinkAnchor, mark, "link targets the bookmark");
            AssertEqual("dot", p[2].Runs.Single(r => r.Image is not null).Image!.AltText, "picture");
            var bytes = Encoding.GetEncoding(28591).GetBytes("<meta charset=\"iso-8859-1\"><p>café</p>");
            var fromBytes = HtmlConversion.Import(bytes);
            AssertEqual("café", fromBytes.Blocks.OfType<Paragraph>().Single().GetText(), "charset sniffed");
            var path = Path.Combine(outDir, "html-import-links.docx");
            doc.SaveTo(path);
            var reloaded = Document.Load(path);
            AssertTrue(reloaded.GetBookmarks().Any(b => b.Name == mark), "bookmark saved");
        });

        Check("html import: pathological nesting and wide tables stay bounded", () =>
        {
            var deep = string.Concat(Enumerable.Repeat("<div><span>", 1000)) + "x";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var doc = HtmlConversion.Import(deep);
            AssertTrue(doc.GetText().Contains('x') && sw.ElapsedMilliseconds < 15000, sw.ElapsedMilliseconds + " ms");
            // Formatting elements nested this deep exceed the parser's work budget: the documented exception, quickly.
            sw.Restart();
            AssertThrows<Chuvadi.Markup.MarkupLimitExceededException>(() => HtmlConversion.Import(string.Concat(Enumerable.Repeat("<div><b>", 20000)) + "x"), "work budget");
            AssertTrue(sw.ElapsedMilliseconds < 15000, "budget stops it quickly: " + sw.ElapsedMilliseconds + " ms");
            var wide = "<table><tr>" + string.Concat(Enumerable.Repeat("<td>c</td>", 200)) + "</tr></table>";
            var r = HtmlConversion.ImportDetailed(wide);
            AssertTrue(r.Document.Blocks.OfType<DocTable>().Single().Columns == 63 && r.Warnings.Any(w => w.Contains("63", StringComparison.Ordinal)), "63-column cap");
        });
    }
}
