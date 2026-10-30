using System;
using System.IO;
using System.Linq;
using Chuvadi.Docs.Conversion;
using Chuvadi.Docs.Word;
using static Chuvadi.Docs.Conversion.ManualTests.Program;

namespace Chuvadi.Docs.Conversion.ManualTests;

public static class MarkdownImportTests
{
    public static void Run(string outDir)
    {
        Check("markdown import: headings, emphasis, strong, strike, code spans, links, breaks", () =>
        {
            var doc = MarkdownConversion.Import("# Title\n\n## Sub *it*\n\nSome **bold *and it*** ~~gone~~ `code` [link](https://example.com \"t\") <https://auto.example>  \nnext\nsoft");
            var p = doc.Blocks.OfType<Paragraph>().ToList();
            AssertEqual("Heading1", p[0].StyleId, "h1");
            AssertEqual("Heading2", p[1].StyleId, "h2");
            AssertTrue(p[1].Runs.Single(r => r.TextContent == "it").Format.Italic, "italic in heading");
            var body = p[2];
            AssertTrue(body.Runs.Single(r => r.TextContent == "bold ").Format.Bold, "bold");
            var both = body.Runs.Single(r => r.TextContent == "and it");
            AssertTrue(both.Format.Bold && both.Format.Italic, "nested emphasis");
            AssertTrue(body.Runs.Single(r => r.TextContent == "gone").Format.Strikethrough, "strike");
            AssertEqual("Consolas", body.Runs.Single(r => r.TextContent == "code").Format.Font, "code font");
            AssertEqual("https://example.com", body.Runs.Single(r => r.TextContent == "link").HyperlinkUrl, "link");
            AssertEqual("https://auto.example", body.Runs.Single(r => r.TextContent == "https://auto.example").HyperlinkUrl, "autolink");
            AssertTrue(body.Runs.Any(r => r.Break == BreakType.Line), "hard break");
            AssertTrue(body.GetText().EndsWith("next soft", StringComparison.Ordinal), "soft break is a space: " + body.GetText());
            var lb = MarkdownConversion.Import("a\nb", new MarkdownImportOptions { SoftBreaks = SoftBreakImport.LineBreak });
            AssertTrue(lb.Blocks.OfType<Paragraph>().Single().Runs.Any(r => r.Break == BreakType.Line), "soft break as line break");
        });

        Check("markdown import: lists (nested, ordered start, tasks), quotes (nested), code blocks, thematic break", () =>
        {
            var md = "- a\n- b\n  1. x\n  2. y\n- [x] done\n- [ ] todo\n\n3. three\n4. four\n\n> quote\n>\n> > deeper\n\n```cs\nvar x = 1;\n  indented\n```\n\n---\n\nend";
            var doc = MarkdownConversion.Import(md);
            var labels = doc.GetListLabels();
            var p = doc.Blocks.OfType<Paragraph>().ToList();
            var a = p.Single(q => q.GetText() == "a");
            var x = p.Single(q => q.GetText() == "x");
            AssertTrue(labels[a].LevelDefinition.Format == NumberFormat.Bullet && a.ListLevel == 0, "bullet level 0");
            AssertTrue(labels[x].Text == "1." && x.ListLevel == 1, "nested ordered: " + labels[x].Text);
            AssertEqual("2.", labels[p.Single(q => q.GetText() == "y")].Text, "second nested");
            AssertTrue(p.Any(q => q.GetText() == "☒ done") && p.Any(q => q.GetText() == "☐ todo"), "task markers");
            AssertEqual("3.", labels[p.Single(q => q.GetText() == "three")].Text, "start number");
            AssertEqual("4.", labels[p.Single(q => q.GetText() == "four")].Text, "continues");
            var quote = p.Single(q => q.GetText() == "quote");
            var deeper = p.Single(q => q.GetText() == "deeper");
            AssertTrue(quote.StyleId == "Quote" && deeper.StyleId == "Quote" && (deeper.Format.IndentLeftPt ?? 0) == 36, "nested quote indent");
            var code = p.Single(q => q.StyleId == "Code");
            AssertEqual("var x = 1;\n  indented", code.GetText(), "code block keeps spaces and lines");
            AssertTrue(doc.Styles.Contains("Code"), "code style created");
            AssertTrue(p.Any(q => q.Format.Borders?.Bottom is not null), "thematic break border");
            doc.SaveTo(Path.Combine(outDir, "md-import-blocks.docx"));
        });

        Check("markdown import: GFM table with header row and column alignment", () =>
        {
            var doc = MarkdownConversion.Import("| Left | Mid | Right |\n|:-----|:---:|------:|\n| a | **b** | c |\n| d |");
            var t = doc.Blocks.OfType<DocTable>().Single();
            AssertEqual(3, t.Columns, "columns");
            AssertTrue(t.Rows[0].IsHeader && t.Rows[0].Cells[0].Paragraphs[0].Runs[0].Format.Bold, "header row bold");
            AssertEqual(ParagraphAlignment.Center, t.Rows[1].Cells[1].Paragraphs[0].Alignment, "center column");
            AssertEqual(ParagraphAlignment.Right, t.Rows[1].Cells[2].Paragraphs[0].Alignment, "right column");
            AssertTrue(t.Rows[1].Cells[1].Paragraphs[0].Runs[0].Format.Bold, "inline formatting in cell");
            AssertEqual("", t.Rows[2].Cells[2].Paragraphs[0].GetText(), "short row filled");
            doc.SaveTo(Path.Combine(outDir, "md-import-table.docx"));
        });

        Check("markdown import: pictures from data URIs and the resolver; remote pictures and unsafe links are not followed", () =>
        {
            var png = Convert.ToBase64String(Png());
            int asked = 0;
            var o = new MarkdownImportOptions
            {
                ImageResolver = r => { asked++; return r.Url == "local.png" ? Png() : null; },
            };
            var result = MarkdownConversion.ImportDetailed($"![red](data:image/png;base64,{png}) ![file](local.png) ![remote](https://tracker.example/p.png) [bad](javascript:alert(1)) [ok](#section)", null, o);
            var p = result.Document.Blocks.OfType<Paragraph>().Single();
            AssertEqual(2, p.Runs.Count(r => r.Image is not null), "two pictures");
            AssertEqual("red", p.Runs.First(r => r.Image is not null).Image!.AltText, "alt text");
            AssertEqual(2, asked, "resolver asked for the two non-data URLs");
            AssertTrue(p.GetText().Contains("remote", StringComparison.Ordinal), "unresolved picture becomes its alt text");
            AssertTrue(p.Runs.Single(r => r.TextContent == "bad").HyperlinkUrl is null, "javascript: link dropped");
            AssertEqual("section", p.Runs.Single(r => r.TextContent == "ok").HyperlinkAnchor, "fragment becomes an internal link");
            AssertTrue(result.Warnings.Count >= 1 && result.BlockedResources.Contains("javascript:alert(1)"), "reported");
            result.Document.SaveTo(Path.Combine(outDir, "md-import-images.docx"));
        });

        Check("markdown import: raw HTML is sanitized and converted, kept as text, or dropped", () =>
        {
            var md = "Text with <b>bold</b>, H<sub>2</sub>O and <span onclick=\"x()\">span</span>.\n\n<div><script>alert(1)</script><p>Block <i>html</i></p><img src=x onerror=alert(1)></div>\n\nafter";
            var doc = MarkdownConversion.Import(md);
            var p = doc.Blocks.OfType<Paragraph>().ToList();
            AssertTrue(p[0].Runs.Single(r => r.TextContent == "bold").Format.Bold, "inline b");
            AssertEqual(VerticalTextPosition.Subscript, p[0].Runs.Single(r => r.TextContent == "2").Format.VerticalPosition, "sub");
            var all = string.Join("|", p.Select(q => q.GetText()));
            AssertNotContains(all, "alert", "script removed");
            AssertTrue(p.Any(q => q.GetText() == "Block html" && q.Runs.Single(r => r.TextContent == "html").Format.Italic), "block html converted: " + all);
            var asText = MarkdownConversion.Import("<b>x</b>", new MarkdownImportOptions { RawHtml = RawHtmlImport.Text });
            AssertEqual("<b>x</b>", asText.Blocks.OfType<Paragraph>().Single().GetText(), "as text");
            var dropped = MarkdownConversion.Import("a <b>x</b>\n\n<div>gone</div>", new MarkdownImportOptions { RawHtml = RawHtmlImport.Drop });
            AssertEqual("a x", dropped.Blocks.OfType<Paragraph>().Single().GetText(), "dropped");
            var path = Path.Combine(outDir, "md-import-html.docx");
            doc.SaveTo(path);
            var xml = ReadPart(path, "word/document.xml");
            AssertNotContains(xml, "alert", "nothing executable in the package");
            AssertNotContains(xml, "onclick", "no handler text");
        });

        Check("markdown import: appends to an existing document; hostile input stays bounded", () =>
        {
            var d = new Document();
            d.AddParagraph("existing");
            MarkdownConversion.Import("# added", d);
            AssertEqual(2, d.Blocks.Count, "appended");
            var deep = new string('>', 5000) + " deep\n\n" + string.Concat(Enumerable.Repeat("- ", 3000)) + "x\n\n" + new string('*', 20000);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var doc = MarkdownConversion.Import(deep);
            AssertTrue(sw.ElapsedMilliseconds < 10000 && doc.Blocks.Count > 0, sw.ElapsedMilliseconds + " ms");
            doc.SaveTo(Path.Combine(outDir, "md-import-hostile.docx"));
        });
    }
}
