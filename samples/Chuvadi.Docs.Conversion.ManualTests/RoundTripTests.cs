using System;
using System.IO;
using System.Linq;
using Chuvadi.Docs.Conversion;
using Chuvadi.Docs.Word;
using Chuvadi.Markup.Markdown;
using Paragraph = Chuvadi.Docs.Word.Paragraph;
using static Chuvadi.Docs.Conversion.ManualTests.Program;

namespace Chuvadi.Docs.Conversion.ManualTests;

public static class RoundTripTests
{
    private static readonly string[] Corpus =
    {
        "# Heading\n\nParagraph with **bold**, *italic*, ~~strike~~ and `code`.",
        "## Second\n\n- one\n- two\n  - nested\n- three",
        "1. first\n2. second\n\nbetween\n\n7. seven\n8. eight",
        "> quoted text\n> continues",
        "```\ncode block\n  indented\n```",
        "| a | b |\n|---|---|\n| 1 | 2 |",
        "A [link](https://example.com) and an <https://auto.example> autolink.",
        "Special characters: 1 * 2, [brackets], _underscores_, #hash, back\\slash, <angle>.",
        "Line one  \nline two",
        "Mixed ***bold italic*** text",
    };

    public static void Run(string outDir)
    {
        Check("markdown → docx → markdown renders the same HTML (corpus)", () =>
        {
            int i = 0;
            foreach (var md in Corpus)
            {
                i++;
                var doc = MarkdownConversion.Import(md);
                var path = Path.Combine(outDir, $"rt-md-{i}.docx");
                doc.SaveTo(path);
                var back = MarkdownConversion.Export(Document.Load(path));
                var expected = MarkdownConverter.ToHtml(md);
                var actual = MarkdownConverter.ToHtml(back);
                AssertTrue(Normalize(expected) == Normalize(actual), $"case {i}:\n--- md ---\n{md}\n--- back ---\n{back}\n--- expected ---\n{expected}\n--- actual ---\n{actual}");
            }
        });

        Check("docx → html → docx keeps the structure (headings, lists, tables, emphasis, links)", () =>
        {
            var d = MarkdownExportTests.Sample();
            var html = HtmlConversion.Export(d);
            var back = HtmlConversion.Import(html);
            back.SaveTo(Path.Combine(outDir, "rt-html.docx"));
            var before = d.Blocks.OfType<Paragraph>().Where(p => p.StyleId.StartsWith("Heading", StringComparison.Ordinal)).Select(p => p.GetText()).ToList();
            var after = back.Blocks.OfType<Paragraph>().Where(p => p.StyleId.StartsWith("Heading", StringComparison.Ordinal)).Select(p => p.GetText()).ToList();
            AssertTrue(before.SequenceEqual(after), string.Join("|", after));
            AssertEqual(d.GetListLabels().Count, back.GetListLabels().Count, "list items");
            var t = back.Blocks.OfType<DocTable>().Single();
            AssertTrue(t.Rows[0].IsHeader && t.Rows[1].Cells[0].Paragraphs[0].GetText() == "a|b", "table");
            var p = back.Blocks.OfType<Paragraph>().First(q => q.GetText().StartsWith("Plain", StringComparison.Ordinal));
            AssertTrue(p.Runs.Single(r => r.TextContent == "bold").Format.Bold && p.Runs.Single(r => r.TextContent == "site").HyperlinkUrl == "https://example.com", "inline");
            AssertTrue(back.Blocks.OfType<Paragraph>().Any(q => q.Runs.Any(r => r.Image is not null)), "picture comes back");
        });

        Check("text → docx → text is the identity for plain lines", () =>
        {
            var text = "alpha\n\tbeta\ngamma δ\n";
            var doc = TextConversion.Import(text);
            AssertEqual(text, TextConversion.Export(doc), "identity");
        });
    }

    // A soft line break renders as a newline, an imported one as a space (SoftBreakImport.Space): the
    // same in a browser, so white space is compared collapsed.
    private static string Normalize(string html) => System.Text.RegularExpressions.Regex.Replace(html, @"\s+", " ").Replace("> <", "><", StringComparison.Ordinal)
        // Bold italic nests either way round: the same rendering.
        .Replace("<em><strong>", "<strong><em>", StringComparison.Ordinal).Replace("</strong></em>", "</em></strong>", StringComparison.Ordinal).Trim();
}
