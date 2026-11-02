using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Fonts;
using Chuvadi.Layout.Model;
using Chuvadi.Markup.Markdown;
using LParagraph = Chuvadi.Layout.Model.FlowParagraph;

namespace Chuvadi.Layout.Markup.Tests;

public class MarkdownImportTests
{
    private static LayoutOptions Options(string culture = "en-IN")
    {
        LayoutOptions o = new LayoutOptions { Culture = new System.Globalization.CultureInfo(culture), Now = new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.FromHours(5.5)) };
        o.FontSources.Add(BundledFontSource.Instance);
        return o;
    }

    private static FlowDocument Import(string md, MarkdownImportOptions? o = null, List<LayoutWarning>? warnings = null)
        => MarkdownImporter.Import(md, o, Options(), warnings is null ? null : warnings.Add);

    private static List<LParagraph> Paragraphs(FlowDocument d) => d.Sections[0].Blocks.OfType<LParagraph>().ToList();

    private static string Text(LParagraph p) => string.Concat(p.Inlines.OfType<TextRun>().Select(r => r.Text));

    [Fact]
    public void Headings_and_inline_formatting()
    {
        FlowDocument d = Import("# Title\n\nSome **bold**, *italic*, ~~gone~~ and `code`, a [link](https://example.org) and [local](notes.md).");
        Assert.Equal("Title", d.Title);
        List<LParagraph> ps = Paragraphs(d);
        Assert.True(ps[0].Inlines.OfType<TextRun>().Single().Format.Bold);
        Assert.Equal(20, ps[0].Inlines.OfType<TextRun>().Single().Format.FontSize);
        Assert.True(ps[0].Format.KeepWithNext);
        List<TextRun> runs = ps[1].Inlines.OfType<TextRun>().ToList();
        Assert.True(runs.Single(r => r.Text == "bold").Format.Bold);
        Assert.True(runs.Single(r => r.Text == "italic").Format.Italic);
        Assert.True(runs.Single(r => r.Text == "gone").Format.Strikethrough);
        TextRun code = runs.Single(r => r.Text == "code");
        Assert.Equal("Consolas", code.Format.FontFamily);
        Assert.Equal(GenericFontFamily.Monospace, code.Format.GenericFamily);
        Assert.NotNull(code.Format.Background);
        Assert.Equal("https://example.org/", runs.Single(r => r.Text == "link").HyperlinkUri);
        Assert.Null(runs.Single(r => r.Text == "local").HyperlinkUri); // relative: no resolver, no link

        FlowDocument resolved = Import("[local](notes.md)", new MarkdownImportOptions { LinkResolver = u => "https://docs.example/" + u });
        Assert.Equal("https://docs.example/notes.md", Paragraphs(resolved)[0].Inlines.OfType<TextRun>().Single().HyperlinkUri);
    }

    [Fact]
    public void Lists_get_labels_hanging_indents_and_nesting()
    {
        FlowDocument d = Import("- one\n- two\n  - nested\n\n3. three\n4. four\n\n- [ ] open\n- [x] done");
        List<LParagraph> ps = Paragraphs(d);
        Assert.Equal("\u2022", ps[0].ListLabel!.Text);
        Assert.Equal("\u25E6", ps[2].ListLabel!.Text);
        Assert.True(ps[2].Format.LeftIndent > ps[0].Format.LeftIndent);
        Assert.True(ps[0].Format.FirstLineIndent < 0, "the label hangs");
        Assert.Equal("3.", ps.Single(p => Text(p) == "three").ListLabel!.Text);
        Assert.Equal("4.", ps.Single(p => Text(p) == "four").ListLabel!.Text);
        Assert.Equal("\u25A1", ps.Single(p => Text(p) == "open").ListLabel!.Text);
        Assert.Equal("\u2713", ps.Single(p => Text(p) == "done").ListLabel!.Text);
    }

    [Fact]
    public void Code_blocks_quotes_rules_and_breaks()
    {
        FlowDocument d = Import("> quoted\n\n```\nline 1\n    indented\n```\n\n---\n\nhard  \nbreak");
        List<LParagraph> ps = Paragraphs(d);
        LParagraph quote = ps[0];
        Assert.True(quote.Format.BorderLeft.IsVisible);
        Assert.True(quote.Format.LeftIndent > 0);
        LParagraph code = ps[1];
        Assert.Equal("line 1\n    indented", Text(code));
        Assert.NotNull(code.Format.Background);
        Assert.Equal(GenericFontFamily.Monospace, code.MarkFormat.GenericFamily);
        Assert.True(ps[2].Format.BorderBottom.IsVisible); // thematic break
        Assert.Contains(ps[3].Inlines, i => i is BreakInline { Kind: BreakKind.Line });
    }

    [Fact]
    public void Tables_keep_alignment_header_and_proportional_columns()
    {
        FlowDocument d = Import("| Item | Qty | Price |\n|:--|:-:|--:|\n| A long item name here | 4 | 120.00 |\n| B | 2 | 5.00 |");
        FlowTable t = d.Sections[0].Blocks.OfType<FlowTable>().Single();
        Assert.Equal(3, t.ColumnWidths.Count);
        Assert.True(t.ColumnWidths[0] > t.ColumnWidths[1]);
        Assert.True(t.Rows[0].IsHeader);
        Assert.True(t.Rows[0].Cells[0].Blocks.OfType<LParagraph>().Single().Inlines.OfType<TextRun>().Single().Format.Bold);
        Assert.Equal(ParagraphAlignment.Center, t.Rows[1].Cells[1].Blocks.OfType<LParagraph>().Single().Format.Alignment);
        Assert.Equal(ParagraphAlignment.Right, t.Rows[1].Cells[2].Blocks.OfType<LParagraph>().Single().Format.Alignment);
        Assert.Equal(Math.Round(d.Sections[0].Page.ContentWidth, 3), Math.Round(t.ColumnWidths.Sum(), 3));
    }

    private const string Png1x1 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    [Fact]
    public void Images_come_from_data_uris_or_the_resolver_never_the_network()
    {
        List<LayoutWarning> warnings = new List<LayoutWarning>();
        FlowDocument d = Import($"![dot](data:image/png;base64,{Png1x1}) ![remote](https://example.org/x.png)", null, warnings);
        LParagraph p = Paragraphs(d)[0];
        ImageInline img = Assert.Single(p.Inlines.OfType<ImageInline>());
        Assert.Equal(0.75, img.Width, 3); // 1 px at 96 dpi
        Assert.Equal("[remote]", p.Inlines.OfType<TextRun>().Last().Text);
        Assert.Contains(warnings, w => w.Code == "ImagePlaceholder");

        byte[] png = Convert.FromBase64String(Png1x1);
        FlowDocument resolved = Import("![x](pics/x.png)", new MarkdownImportOptions { ImageResolver = u => u == "pics/x.png" ? png : null });
        Assert.Single(Paragraphs(resolved)[0].Inlines.OfType<ImageInline>());
    }

    [Fact]
    public void Raw_html_is_printed_literally_or_stripped()
    {
        Assert.Equal("<b>x</b>", Text(Paragraphs(Import("<b>x</b>"))[0]));
        Assert.Contains(Paragraphs(Import("<div>\nblock\n</div>")), p => Text(p).Contains("<div>", StringComparison.Ordinal));
        Assert.DoesNotContain(Paragraphs(Import("<div>\nblock\n</div>", new MarkdownImportOptions { RawHtml = RawHtmlMode.Strip })), p => Text(p).Contains("<div>", StringComparison.Ordinal));
        List<LayoutWarning> warnings = new List<LayoutWarning>();
        Import("<div>x</div>", new MarkdownImportOptions { RawHtml = RawHtmlMode.Passthrough }, warnings);
        Assert.Contains(warnings, w => w.Code == "UnsupportedFeature");
    }

    [Fact]
    public void Lays_out_with_culture_paper_footer_and_page_breaks()
    {
        string md = "# One\n\n" + string.Join("\n\n", Enumerable.Range(1, 80).Select(i => "Paragraph " + i + " of the long document.")) + "\n\n# Two\n\nEnd.";
        using LaidOutDocument us = MarkdownLayout.Open(md, new MarkdownImportOptions { PageBreakBeforeHeading1 = true }, Options("en-US"));
        Assert.Equal(LayoutSourceFormat.Markdown, us.Source.Format);
        Assert.Equal(612, us.GetPage(0).WidthPt, 1);
        int n = us.ResolvePageCount();
        Assert.True(n >= 3);
        LaidOutPage last = us.GetPage(n - 1);
        Assert.StartsWith("Two", last.GetText(), StringComparison.Ordinal);
        string footer = string.Concat(last.DisplayList.OfType<GlyphRunOp>().Where(r => r.Y > last.HeightPt - 60).Select(r => r.Text));
        Assert.Equal($"Page {n} of {n}", footer);

        using LaidOutDocument india = MarkdownLayout.Open(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Hello")), new MarkdownImportOptions { PageNumbers = false }, Options("en-IN"));
        Assert.Equal(595.3, india.GetPage(0).WidthPt, 1);
        Assert.DoesNotContain(india.GetPage(0).DisplayList.OfType<GlyphRunOp>(), r => r.Text.Contains("Page", StringComparison.Ordinal));
    }

    [Fact]
    public void Limits_and_cancellation()
    {
        LayoutOptions tight = Options();
        tight.Limits = new LayoutLimits { MaxSourceBytes = 10 };
        Assert.Throws<LayoutLimitExceededException>(() => MarkdownLayout.Open("this is longer than ten characters", null, tight));
        using CancellationTokenSource cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => MarkdownLayout.Open("# x", null, Options(), cts.Token));
        // Hostile nesting is flattened by the parser and indents stop at half the text width: the text still lays out.
        string deep = string.Concat(Enumerable.Repeat("> ", 600)) + "deep";
        using LaidOutDocument laid = MarkdownLayout.Open(deep, null, Options());
        Assert.Contains("deep", laid.GetPage(0).GetText(), StringComparison.Ordinal);
        FlowDocument flow = MarkdownImporter.Import(deep, null, Options());
        Assert.All(flow.Sections[0].Blocks.OfType<LParagraph>(), p => Assert.True(p.Format.LeftIndent <= flow.Sections[0].Page.ContentWidth / 2));
    }

    [Fact]
    public void Markup_limits_become_layout_limits()
    {
        // Any MarkupLimitExceededException (input size, nodes, the 1.0.0 inline work budget) surfaces as Layout's limit error.
        MarkdownOptions tight = MarkdownOptions.Gfm();
        tight.Limits = new Chuvadi.Markup.MarkupLimits { MaxInputChars = 16 };
        LayoutLimitExceededException ex = Assert.Throws<LayoutLimitExceededException>(
            () => MarkdownLayout.Open("# A heading longer than sixteen characters", new MarkdownImportOptions { Parser = tight }, Options()));
        Assert.Equal("MarkupMaxInputChars", ex.LimitName);

        // A long run of emphasis delimiters parses in linear time under the default budget.
        string hostile = new string('*', 200_000) + "a" + new string('*', 200_000);
        using LaidOutDocument ok = MarkdownLayout.Open(hostile, null, Options());
        Assert.True(ok.PageCount >= 1);
    }

    [Fact]
    public void An_ast_built_in_code_lays_out()
    {
        MarkdownDocument doc = new MarkdownDocument();
        Heading h = new Heading { Level = 2 };
        h.AppendChild(new TextInline("Built"));
        doc.AppendChild(h);
        using LaidOutDocument laid = MarkdownLayout.Open(doc, null, Options());
        Assert.Contains("Built", laid.GetPage(0).GetText(), StringComparison.Ordinal);
    }
}
