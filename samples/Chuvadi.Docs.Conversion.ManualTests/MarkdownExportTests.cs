using System;
using System.IO;
using System.Linq;
using Chuvadi.Docs.Conversion;
using Chuvadi.Docs.Word;
using Chuvadi.Markup.Markdown;
using Paragraph = Chuvadi.Docs.Word.Paragraph;
using static Chuvadi.Docs.Conversion.ManualTests.Program;

namespace Chuvadi.Docs.Conversion.ManualTests;

public static class MarkdownExportTests
{
    internal static Document Sample()
    {
        var d = new Document();
        d.AddHeading("Report *2026*", 1);
        d.AddHeading("Section", 2);
        d.AddParagraph().Text("Plain ").Text("bold", TextFormat.BoldText).Text(" and ").Text("italic", TextFormat.ItalicText)
            .Text(" and ").Text("code", new TextFormat { Font = "Consolas" }).Text(" ").Hyperlink("site", "https://example.com").Text(" 1 * 2 = 2 [x]");
        d.AddBullet("first");
        d.AddBullet("nested", 1);
        d.AddBullet("second");
        d.AddNumbered("one");
        d.AddNumbered("two");
        d.AddParagraph("A quote", ParagraphStyle.Quote);
        var t = d.AddTable(2);
        t.HeaderRow("Name", "Value");
        t.AddRow("a|b", "1");
        var c = d.AddParagraph("x");
        c.StyleId = "Normal";
        c.Text(" with ").Text("H", TextFormat.None).Text("2", TextFormat.SubscriptText).Text("O");
        d.AddParagraph("Note").Footnote(d, "Footnote text");
        d.AddParagraph().Image(ImageSpec.FromBytes(Png(), "image/png")).Text(" picture");
        return d;
    }

    public static void Run(string outDir)
    {
        Check("markdown export: structure, emphasis, links, escaping, lists, quote, table, notes, pictures", () =>
        {
            var md = MarkdownConversion.Export(Sample());
            File.WriteAllText(Path.Combine(outDir, "md-export.md"), md);
            AssertContains(md, "# Report \\*2026\\*", "heading with escaped asterisks");
            AssertContains(md, "## Section", "h2");
            AssertContains(md, "Plain **bold** and *italic* and `code` [site](<https://example.com>) 1 \\* 2 = 2 \\[x\\]", "inline");
            AssertContains(md, "- first\n  - nested\n- second", "nested bullets");
            AssertContains(md, "1. one\n2. two", "ordered");
            AssertContains(md, "> A quote", "quote");
            AssertContains(md, "| Name | Value |", "table header");
            AssertContains(md, "a\\|b", "pipe escaped in a cell");
            AssertContains(md, "<sub>2</sub>", "subscript as html");
            AssertContains(md, "Note\\[1\\]", "note reference (literal text, escaped)");
            AssertContains(md, "\\[1\\] Footnote text", "note at the end");
            AssertContains(md, "](<data:image/png;base64,", "picture as data URI");
            // What we write parses back to the same structure.
            var ast = MarkdownParser.Parse(md);
            AssertEqual(2, ast.Descendants().OfType<Heading>().Count(), "headings parse back");
            AssertEqual(1, ast.Descendants().OfType<Table>().Count(), "table parses back");
            AssertEqual(1, ast.Descendants().OfType<BlockQuote>().Count(), "quote parses back");
        });

        Check("markdown export: options (labels as text, underline html, image resolver/omit, writer chars, tracked view)", () =>
        {
            var d = Sample();
            d.AddParagraph().Text("under", new TextFormat { Underline = true });
            d.AddParagraph("Keep ").TrackedDelete("old", "R").TrackedInsert("new", "R");
            var o = new MarkdownExportOptions { ListLabelsAsText = true, UnderlineAsHtml = true, ImageUrlResolver = img => "img" + img.Index + ".png" };
            o.Writer.BulletChar = '*';
            o.Writer.EmphasisChar = '_';
            var md = MarkdownConversion.Export(d, o);
            AssertContains(md, "1\\. one", "label written as text (escaped so it stays text)");
            AssertContains(md, "<u>under</u>", "underline as html");
            AssertContains(md, "](<img1.png>)", "resolver url");
            AssertContains(md, "_italic_", "emphasis char");
            AssertContains(md, "Keep new", "final view");
            var markup = MarkdownConversion.Export(d, new MarkdownExportOptions { TrackedChanges = TrackedChangesView.Markup, Images = ImageExportMode.Omit });
            AssertContains(markup, "~~old~~", "deletion as strikethrough");
            AssertNotContains(markup, "data:image", "pictures omitted");
            var ast = MarkdownConversion.ToAst(d);
            AssertTrue(ast.Children.Count > 5, "AST for callers");
        });

        Check("markdown export: loaded Word-style document (fields, bookmarks, headers) writes clean text", () =>
        {
            var d = new Document();
            d.Header.Add(new Paragraph("Header text"));
            d.AddParagraph().Bookmarked("top", "Anchor").Text(" ").Field("DATE", "1 Jan 2026");
            d.AddParagraph().InternalLink("back", "top");
            var path = Path.Combine(outDir, "md-export-source.docx");
            d.SaveTo(path);
            var loaded = Document.Load(path);
            var md = MarkdownConversion.Export(loaded, new MarkdownExportOptions { IncludeHeadersFooters = true });
            AssertContains(md, "Header text", "header included");
            AssertContains(md, "Anchor 1 Jan 2026", "field result kept, markers not written");
            AssertContains(md, "[back](<#top>)", "internal link");
        });
    }
}
