using System.IO.Compression;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Engine;
using Chuvadi.Layout.Fonts;
using Chuvadi.Layout.Model;
using Chuvadi.Markup.Html;
using Chuvadi.Markup.Sanitization;
using LParagraph = Chuvadi.Layout.Model.FlowParagraph;

namespace Chuvadi.Layout.Markup.Tests;

public class HtmlImportTests
{
    private static LayoutOptions Options(string culture = "en-IN")
    {
        LayoutOptions o = new LayoutOptions { Culture = new System.Globalization.CultureInfo(culture), Now = new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.FromHours(5.5)) };
        o.FontSources.Add(BundledFontSource.Instance);
        return o;
    }

    private static FlowDocument Import(string html, HtmlImportOptions? o = null, List<LayoutWarning>? warnings = null)
        => HtmlImporter.Import(html, o, Options(), warnings is null ? null : warnings.Add);

    private static List<LParagraph> Paragraphs(FlowDocument d) => d.Sections[0].Blocks.OfType<LParagraph>().ToList();

    private static string Text(LParagraph p) => string.Concat(p.Inlines.OfType<TextRun>().Select(r => r.Text));

    private static List<LParagraph> AllParagraphs(IEnumerable<FlowBlock> blocks)
    {
        List<LParagraph> list = new List<LParagraph>();
        foreach (FlowBlock b in blocks)
        {
            if (b is LParagraph p)
            {
                list.Add(p);
            }
            else if (b is FlowTable t)
            {
                foreach (FlowTableRow r in t.Rows)
                {
                    foreach (FlowTableCell c in r.Cells)
                    {
                        list.AddRange(AllParagraphs(c.Blocks));
                    }
                }
            }
        }
        return list;
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }
        return ~crc;
    }

    /// <summary>A small solid-colour PNG, as a data: URI.</summary>
    private static string PngDataUri(int w, int h)
    {
        static byte[] Chunk(string type, byte[] data)
        {
            byte[] t = System.Text.Encoding.ASCII.GetBytes(type);
            byte[] all = t.Concat(data).ToArray();
            byte[] len = BitConverter.GetBytes(data.Length).Reverse().ToArray();
            byte[] crc = BitConverter.GetBytes(Crc32(all)).Reverse().ToArray();
            return len.Concat(all).Concat(crc).ToArray();
        }
        byte[] header = BitConverter.GetBytes(w).Reverse().Concat(BitConverter.GetBytes(h).Reverse()).Concat(new byte[] { 8, 2, 0, 0, 0 }).ToArray();
        using MemoryStream raw = new MemoryStream();
        for (int y = 0; y < h; y++)
        {
            raw.WriteByte(0);
            for (int x = 0; x < w; x++)
            {
                raw.Write(new byte[] { 200, 30, 30 });
            }
        }
        using MemoryStream z = new MemoryStream();
        using (ZLibStream zs = new ZLibStream(z, CompressionLevel.Optimal, true))
        {
            raw.Position = 0;
            raw.CopyTo(zs);
        }
        byte[] png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }
            .Concat(Chunk("IHDR", header)).Concat(Chunk("IDAT", z.ToArray())).Concat(Chunk("IEND", Array.Empty<byte>())).ToArray();
        return "data:image/png;base64," + Convert.ToBase64String(png);
    }

    [Fact]
    public void Headings_paragraphs_and_inline_formatting()
    {
        FlowDocument d = Import("<h1>Title</h1><p>Some <b>bold</b>, <i>italic</i>, <u>under</u>, <s>gone</s>, H<sub>2</sub>O, x<sup>2</sup>, <code>code</code> and <mark>marked</mark> text.</p>");
        Assert.Equal("Title", d.Title);
        List<LParagraph> ps = Paragraphs(d);
        Assert.Equal(2, ps.Count);
        TextRun h = ps[0].Inlines.OfType<TextRun>().Single();
        Assert.True(h.Format.Bold);
        Assert.Equal(24, h.Format.FontSize, 3); // 2em of 12 pt
        Assert.True(ps[0].Format.KeepWithNext);
        List<TextRun> runs = ps[1].Inlines.OfType<TextRun>().ToList();
        Assert.True(runs.Single(r => r.Text == "bold").Format.Bold);
        Assert.True(runs.Single(r => r.Text == "italic").Format.Italic);
        Assert.Equal(UnderlineStyle.Single, runs.Single(r => r.Text == "under").Format.Underline);
        Assert.True(runs.Single(r => r.Text == "gone").Format.Strikethrough);
        Assert.Equal(VerticalTextAlignment.Subscript, runs.Single(r => r.Text == "2" && r.Format.VerticalAlignment != VerticalTextAlignment.Superscript).Format.VerticalAlignment);
        Assert.Contains(runs, r => r.Text == "2" && r.Format.VerticalAlignment == VerticalTextAlignment.Superscript);
        TextRun code = runs.Single(r => r.Text == "code");
        Assert.Equal("Courier New", code.Format.FontFamily);
        Assert.Equal(GenericFontFamily.Monospace, code.Format.GenericFamily);
        Assert.Equal(new LayoutColor(255, 255, 0), runs.Single(r => r.Text == "marked").Format.Background);
        Assert.Equal("Some bold, italic, under, gone, H2O, x2, code and marked text.", Text(ps[1]));
    }

    [Fact]
    public void Whitespace_collapses_like_a_browser_but_pre_and_nbsp_are_kept()
    {
        FlowDocument d = Import("<p>\n   a   b\n\t c  <b> d </b> e   </p><pre>  x\n    y</pre><p>1&nbsp;&nbsp;2</p>");
        List<LParagraph> ps = Paragraphs(d);
        Assert.Equal("a b c d e", Text(ps[0]));
        Assert.Equal("  x\n    y", Text(ps[1]));
        Assert.Equal("1\u00A0\u00A02", Text(ps[2]));
        Assert.Equal(3, ps.Count);
    }

    [Fact]
    public void Text_is_never_altered()
    {
        string text = "Rs 1,23,456.00 \u2014 'quotes' \"double\" & <tags> \u0BA4\u0BAE\u0BBF\u0BB4\u0BCD \U0001F600";
        FlowDocument d = Import("<p>" + System.Net.WebUtility.HtmlEncode(text) + "</p>");
        Assert.Equal(text, Text(Paragraphs(d).Single()));
    }

    [Fact]
    public void Links_keep_their_targets_including_inert_links_from_the_mail_policy()
    {
        // The default policy makes links inert; the target is kept in data-chuvadi-href and laid out as a link.
        FlowDocument d = Import("<p><a href=\"https://example.org/a\">site</a> and <a href=\"javascript:alert(1)\">bad</a> and <a href=\"mailto:a@b.org\">mail</a></p>");
        List<TextRun> runs = Paragraphs(d)[0].Inlines.OfType<TextRun>().ToList();
        Assert.Equal("https://example.org/a", runs.Single(r => r.Text == "site").HyperlinkUri);
        Assert.Null(runs.Single(r => r.Text == "bad").HyperlinkUri);
        Assert.Equal("mailto:a@b.org", runs.Single(r => r.Text == "mail").HyperlinkUri);
        Assert.Equal(new LayoutColor(0, 0, 0xEE), runs.Single(r => r.Text == "site").Format.Color);

        FlowDocument reader = Import("<p><a href=\"notes.html\">local</a></p>", new HtmlImportOptions { Policy = new SanitizerPolicy { AllowRelativeLinks = true, Links = LinkHandling.Active }, LinkResolver = u => "https://docs.example/" + u });
        Assert.Equal("https://docs.example/notes.html", Paragraphs(reader)[0].Inlines.OfType<TextRun>().Single().HyperlinkUri);
    }

    [Fact]
    public void Inline_css_and_presentational_attributes()
    {
        FlowDocument d = Import(
            "<p style=\"color:#336699;font-family:'Segoe UI',Arial,sans-serif;font-size:18px;text-align:center;line-height:1.5;letter-spacing:1px\">styled</p>" +
            "<p align=\"right\"><font color=\"ff0000\" face=\"Georgia\" size=\"5\">legacy</font></p>" +
            "<p style=\"font:italic bold 10pt/14pt Verdana, sans-serif;text-transform:uppercase;text-decoration:underline\">short</p>" +
            "<p style=\"color:rgb(10, 20, 30);background-color:hsl(120,100%,50%)\">rgb</p>");
        List<LParagraph> ps = Paragraphs(d);
        TextRun styled = ps[0].Inlines.OfType<TextRun>().Single();
        Assert.Equal(new LayoutColor(0x33, 0x66, 0x99), styled.Format.Color);
        Assert.Equal("Segoe UI", styled.Format.FontFamily);
        Assert.Equal(GenericFontFamily.SansSerif, styled.Format.GenericFamily);
        Assert.Equal(13.5, styled.Format.FontSize, 3);
        Assert.Equal(0.75, styled.Format.CharacterSpacing, 3);
        Assert.Equal(ParagraphAlignment.Center, ps[0].Format.Alignment);
        Assert.Equal(LineSpacingRule.AtLeast, ps[0].Format.LineSpacingRule);
        Assert.Equal(20.25, ps[0].Format.LineSpacing, 3);

        TextRun legacy = ps[1].Inlines.OfType<TextRun>().Single();
        Assert.Equal(ParagraphAlignment.Right, ps[1].Format.Alignment);
        Assert.Equal(new LayoutColor(255, 0, 0), legacy.Format.Color);
        Assert.Equal("Georgia", legacy.Format.FontFamily);
        Assert.Equal(18, legacy.Format.FontSize, 3); // size 5 = 24 px

        TextRun shorthand = ps[2].Inlines.OfType<TextRun>().Single();
        Assert.True(shorthand.Format.Bold);
        Assert.True(shorthand.Format.Italic);
        Assert.Equal(10, shorthand.Format.FontSize, 3);
        Assert.Equal("Verdana", shorthand.Format.FontFamily);
        Assert.Equal(TextCapitals.AllCaps, shorthand.Format.Capitals);
        Assert.Equal(UnderlineStyle.Single, shorthand.Format.Underline);
        Assert.Equal("short", shorthand.Text);
        Assert.Equal(14, ps[2].Format.LineSpacing, 3);

        Assert.Equal(new LayoutColor(10, 20, 30), ps[3].Inlines.OfType<TextRun>().Single().Format.Color);
        Assert.Equal(new LayoutColor(0, 255, 0), ps[3].Format.Background);
    }

    [Fact]
    public void Margins_collapse_and_blocks_get_borders_padding_and_backgrounds()
    {
        FlowDocument d = Import(
            "<p>one</p><p>two</p>" +
            "<div style=\"margin:30px 0;border-left:3px solid #ccc;padding-left:10px;background:#f0f0f0\"><p>quoted</p></div>" +
            "<div style=\"border:1px solid red;padding:5px\">boxed</div>");
        List<LParagraph> ps = Paragraphs(d);
        Assert.Equal(0, ps[0].Format.SpaceBefore); // top of the page
        Assert.Equal(12, ps[0].Format.SpaceAfter, 3); // 1em, collapsed with the next 1em
        Assert.Equal(22.5, ps[1].Format.SpaceAfter, 3); // 30 px wins over 1em
        LParagraph quoted = ps[2];
        Assert.Equal(3 * 0.75 + 7.5, quoted.Format.LeftIndent, 3);
        Assert.Equal(2.25, quoted.Format.BorderLeft.Width, 3);
        Assert.Equal(7.5, quoted.Format.BorderLeft.Space, 3);
        Assert.Equal(new LayoutColor(0xF0, 0xF0, 0xF0), quoted.Format.Background);
        LParagraph boxed = ps[3];
        Assert.True(boxed.Format.BorderTop.IsVisible);
        Assert.True(boxed.Format.BorderBottom.IsVisible);
        Assert.Equal(new LayoutColor(255, 0, 0), boxed.Format.BorderTop.Color);
        Assert.Equal(3.75, boxed.Format.BorderTop.Space, 3);

        FlowDocument noBg = Import("<p style=\"background:#eee\">x</p>", new HtmlImportOptions { PrintBackgrounds = false });
        Assert.Null(Paragraphs(noBg)[0].Format.Background);
    }

    [Fact]
    public void Lists_number_like_a_browser()
    {
        FlowDocument d = Import(
            "<ul><li>one</li><li>two<ul><li>nested</li></ul></li></ul>" +
            "<ol start=\"3\" type=\"i\"><li>three</li><li value=\"10\">ten</li><li>eleven</li></ol>" +
            "<ol reversed><li>c</li><li>b</li><li>a</li></ol>" +
            "<ul style=\"list-style:none;padding:0\"><li>bare</li></ul>" +
            "<ol style=\"list-style-type:upper-alpha\"><li>A</li><li>B</li></ol>");
        List<LParagraph> ps = Paragraphs(d);
        LParagraph P(string t) => ps.Single(p => Text(p) == t);
        Assert.Equal("\u2022", P("one").ListLabel!.Text);
        Assert.Equal("\u25E6", P("nested").ListLabel!.Text);
        Assert.True(P("nested").Format.LeftIndent > P("one").Format.LeftIndent);
        Assert.True(P("one").Format.FirstLineIndent < 0, "the marker hangs");
        Assert.Equal("iii.", P("three").ListLabel!.Text);
        Assert.Equal("x.", P("ten").ListLabel!.Text);
        Assert.Equal("xi.", P("eleven").ListLabel!.Text);
        Assert.Equal("3.", P("c").ListLabel!.Text);
        Assert.Equal("1.", P("a").ListLabel!.Text);
        Assert.Null(P("bare").ListLabel);
        Assert.Equal("B.", P("B").ListLabel!.Text);
    }

    [Fact]
    public void Tables_place_spans_widths_borders_and_backgrounds()
    {
        FlowDocument d = Import(
            "<table border=\"1\" cellpadding=\"4\" width=\"100%\" bgcolor=\"#eeeeee\">" +
            "<thead><tr><th>A</th><th>B</th><th>C</th></tr></thead>" +
            "<tr><td rowspan=\"2\">tall</td><td colspan=\"2\" style=\"background-color:#ffeecc\">wide</td></tr>" +
            "<tr><td>b2</td><td align=\"right\" valign=\"top\">c2</td></tr>" +
            "<tfoot><tr><td colspan=\"3\">foot</td></tr></tfoot>" +
            "<tr><td>last</td></tr></table>");
        FlowTable t = d.Sections[0].Blocks.OfType<FlowTable>().Single();
        Assert.Equal(3, t.ColumnWidths.Count);
        double contentWidth = PaperDefaults.PageForCulture(new System.Globalization.CultureInfo("en-IN")).ContentWidth;
        Assert.Equal(contentWidth, t.ColumnWidths.Sum() + (t.Format.CellSpacing * 4), 1);
        Assert.Equal(5, t.Rows.Count);
        Assert.True(t.Rows[0].IsHeader);
        Assert.Equal("A", Text((LParagraph)t.Rows[0].Cells[0].Blocks[0]));
        Assert.True(((LParagraph)t.Rows[0].Cells[0].Blocks[0]).Inlines.OfType<TextRun>().Single().Format.Bold);
        Assert.Equal(ParagraphAlignment.Center, ((LParagraph)t.Rows[0].Cells[0].Blocks[0]).Format.Alignment);
        Assert.Equal(VerticalMerge.Restart, t.Rows[1].Cells[0].VerticalMerge);
        Assert.Equal(2, t.Rows[1].Cells[1].ColumnSpan);
        Assert.Equal(new LayoutColor(0xFF, 0xEE, 0xCC), t.Rows[1].Cells[1].Background);
        Assert.Equal(new LayoutColor(0xEE, 0xEE, 0xEE), t.Rows[2].Cells[1].Background);
        Assert.Equal(VerticalMerge.Continue, t.Rows[2].Cells[0].VerticalMerge);
        Assert.Equal(CellVerticalAlignment.Center, t.Rows[2].Cells[1].VerticalAlignment);
        Assert.Equal(CellVerticalAlignment.Top, t.Rows[2].Cells[2].VerticalAlignment);
        Assert.Equal(ParagraphAlignment.Right, ((LParagraph)t.Rows[2].Cells[2].Blocks[0]).Format.Alignment);
        Assert.Equal("last", Text((LParagraph)t.Rows[3].Cells[0].Blocks[0])); // tbody rows before tfoot
        Assert.Equal("foot", Text((LParagraph)t.Rows[4].Cells[0].Blocks[0]));
        Assert.Equal(3, t.Rows[3].Cells.Count); // missing cells are filled
        Assert.True(t.Format.BorderTop.IsVisible);
        Assert.True(t.Format.BorderInsideVertical.IsVisible);
        Assert.Equal(3, t.Format.CellMarginLeft, 3);
    }

    [Fact]
    public void Table_widths_follow_content_and_stated_widths()
    {
        FlowDocument d = Import(
            "<table><tr><td>a</td><td>bb</td></tr></table>" +
            "<table width=\"600\" align=\"center\"><tr><td width=\"150\">fixed</td><td>rest of the row text</td></tr></table>");
        List<FlowTable> ts = d.Sections[0].Blocks.OfType<FlowTable>().ToList();
        Assert.True(ts[0].ColumnWidths.Sum() < 60, "an auto table shrinks to its content");
        Assert.Equal(450, ts[1].ColumnWidths.Sum(), 1);
        Assert.Equal(ParagraphAlignment.Center, ts[1].Format.Alignment);
        Assert.InRange(ts[1].ColumnWidths[0], 112.5, 115); // 150 px plus padding
    }

    [Fact]
    public void Mail_layout_tables_become_blocks_so_long_mail_splits_across_pages()
    {
        // The usual mail skeleton: a full-width wrapper table, a 600 px container, a two-column header and a body row.
        string body = string.Concat(Enumerable.Repeat("<p>Paragraph of the mail body that goes on and on.</p>", 120));
        string html = "<center><table width=\"100%\" bgcolor=\"#eeeeee\"><tr><td align=\"center\">" +
            "<table width=\"600\" bgcolor=\"#ffffff\"><tr><td>logo</td><td>date</td></tr>" +
            "<tr><td colspan=\"2\" style=\"padding:20px\"><h2>Inner</h2>" + body + "</td></tr></table>" +
            "</td></tr></table></center>";
        FlowDocument d = Import(html);
        // The one-column wrapper is page layout: it became blocks, and the container is a top-level table.
        FlowTable container = d.Sections[0].Blocks.OfType<FlowTable>().Single();
        Assert.Equal(ParagraphAlignment.Center, container.Format.Alignment);
        Assert.Equal(15, container.Rows[1].Cells[0].MarginLeft);
        Assert.Contains(AllParagraphs(d.Sections[0].Blocks), p => Text(p) == "Inner");
        using LaidOutDocument laid = HtmlLayout.Open(html, null, Options());
        Assert.True(laid.ResolvePageCount() >= 3);
        Assert.DoesNotContain(laid.Warnings, w => w.Code == LayoutWarningCodes.ContentTallerThanPage);

        // A one-column table nested in a cell is layout too: its cell content flows in the outer cell.
        FlowDocument nested = Import("<table><tr><td>left</td><td><table style=\"background:#fafafa\"><tr><td style=\"padding:10px\">inner text</td></tr></table></td></tr></table>");
        FlowTable outer = nested.Sections[0].Blocks.OfType<FlowTable>().Single();
        LParagraph inner = outer.Rows[0].Cells[1].Blocks.OfType<LParagraph>().Single();
        Assert.Equal("inner text", Text(inner));
        Assert.Equal(new LayoutColor(0xFA, 0xFA, 0xFA), inner.Format.Background);
        Assert.Equal(7.5, inner.Format.LeftIndent, 3);
    }

    [Fact]
    public void Tables_nested_beyond_the_limit_are_flattened_with_a_warning()
    {
        string html = "x";
        for (int i = 0; i < 12; i++)
        {
            html = "<table><tr><td>" + html + "</td><td>y</td></tr></table>";
        }
        LayoutOptions o = Options();
        o.Limits = LayoutLimits.ForUntrustedInput(); // MaxNestingDepth 8
        List<LayoutWarning> warnings = new List<LayoutWarning>();
        FlowDocument d = HtmlImporter.Import(html, null, o, warnings.Add);
        Assert.Contains(warnings, w => w.Code == LayoutWarningCodes.UnsupportedFeature && w.Message.Contains("nested"));
        Assert.Contains(AllParagraphs(d.Sections[0].Blocks), p => Text(p) == "x");
        using LaidOutDocument laid = DocumentLayout.Open(d, LayoutSourceFormat.Html, warnings, o);
        Assert.Equal(1, laid.ResolvePageCount());
    }

    [Fact]
    public void Data_images_are_drawn_and_blocked_images_print_their_alt_text()
    {
        string png = PngDataUri(40, 20);
        List<LayoutWarning> warnings = new List<LayoutWarning>();
        FlowDocument d = Import(
            "<p><img src=\"" + png + "\" alt=\"logo\"> <img src=\"" + png + "\" width=\"80\"> <img src=\"" + png + "\" style=\"width:100%;max-width:300px\"></p>" +
            "<p><img src=\"https://tracker.example/p.gif\" alt=\"Remote banner\"><img src=\"https://tracker.example/1x1.gif\" alt=\"\"></p>" +
            "<img src=\"" + png + "\" style=\"display:block;margin:0 auto\">", null, warnings);
        List<LParagraph> ps = Paragraphs(d);
        List<ImageInline> images = ps[0].Inlines.OfType<ImageInline>().ToList();
        Assert.Equal(3, images.Count);
        Assert.Equal(30, images[0].Width, 3); // 40 px at 96 dpi
        Assert.Equal(15, images[0].Height, 3);
        Assert.Equal("logo", images[0].Description);
        Assert.Equal(60, images[1].Width, 3);
        Assert.Equal(30, images[1].Height, 3); // aspect kept
        Assert.Equal(225, images[2].Width, 3); // max-width 300 px
        Assert.Equal("Remote banner", Text(ps[1]));
        Assert.True(ps[1].Inlines.OfType<TextRun>().Single().Format.Italic);
        Assert.Contains(warnings, w => w.Code == LayoutWarningCodes.ImagePlaceholder);
        Assert.Equal(ParagraphAlignment.Center, ps[2].Format.Alignment);
        Assert.Single(ps[2].Inlines.OfType<ImageInline>());

        FlowDocument omit = Import("<p>a<img src=\"https://x.example/i.png\" alt=\"gone\">b</p>", new HtmlImportOptions { MissingImages = MissingImageHandling.Omit });
        Assert.Equal("ab", Text(Paragraphs(omit)[0]));
    }

    [Fact]
    public void Hidden_content_is_not_printed()
    {
        FlowDocument d = Import(
            "<div style=\"display:none\">preheader</div>" +
            "<div style=\"max-height:0;overflow:hidden\">also hidden</div>" +
            "<span style=\"visibility:hidden\">invisible</span>" +
            "<p hidden>attr hidden</p>" +
            "<p style=\"mso-hide:all\">outlook only</p>" +
            "<p>shown<span style=\"font-size:0\">zero</span></p>");
        List<LParagraph> ps = Paragraphs(d);
        Assert.Equal("shown", Text(ps.Single()));
    }

    [Fact]
    public void Line_breaks_rules_quotes_and_page_breaks()
    {
        FlowDocument d = Import("<p>a<br>b <br> c</p><hr><p><q>outer <q>inner</q></q></p><p style=\"page-break-before:always\">new page</p><h2>Head</h2><p>after</p>");
        List<LParagraph> ps = Paragraphs(d);
        Assert.Equal(2, ps[0].Inlines.OfType<BreakInline>().Count());
        Assert.Equal("abc", Text(ps[0]).Replace(" ", string.Empty));
        Assert.Equal("b", ps[0].Inlines.OfType<TextRun>().ElementAt(1).Text); // the space before <br> is dropped
        Assert.True(ps[1].Format.BorderBottom.IsVisible);
        Assert.Equal("\u201Couter \u2018inner\u2019\u201D", Text(ps[2]));
        Assert.True(ps[3].Format.PageBreakBefore);
        Assert.True(ps[4].Format.KeepWithNext);
    }

    [Fact]
    public void Style_sheets_svg_and_forms_warn_and_the_rest_lays_out()
    {
        List<LayoutWarning> warnings = new List<LayoutWarning>();
        FlowDocument d = Import(
            "<style>p{color:red}</style><p>text</p><svg><circle r=\"5\"/></svg><form><input value=\"x\"><button>Send</button></form><script>alert(1)</script>",
            new HtmlImportOptions { Policy = new SanitizerPolicy { AllowSvg = true, AllowFormControls = true } }, warnings);
        List<LParagraph> ps = AllParagraphs(d.Sections[0].Blocks);
        Assert.Equal("text", Text(ps[0]));
        Assert.DoesNotContain(ps, p => Text(p).Contains("alert"));
        Assert.Contains(warnings, w => w.Code == LayoutWarningCodes.UnsupportedFeature && w.Message.Contains("Style sheets"));
        Assert.Contains(warnings, w => w.Code == LayoutWarningCodes.UnsupportedFeature && w.Message.Contains("SVG"));
    }

    [Fact]
    public void Bytes_honour_the_meta_charset_and_the_title_comes_from_the_document()
    {
        byte[] latin1 = System.Text.Encoding.Latin1.GetBytes("<html><head><meta charset=\"iso-8859-1\"><title>Caf\u00E9 menu</title></head><body><p>Caf\u00E9 cr\u00E8me</p></body></html>");
        FlowDocument d = HtmlImporter.Import(latin1, null, Options());
        Assert.Equal("Caf\u00E9 menu", d.Title);
        Assert.Equal("Caf\u00E9 cr\u00E8me", Text(Paragraphs(d).Single()));
        FlowDocument titled = HtmlImporter.Import(latin1, new HtmlImportOptions { Title = "Given" }, Options());
        Assert.Equal("Given", titled.Title);
    }

    [Fact]
    public void Sanitized_results_and_dom_nodes_can_be_laid_out_directly()
    {
        SanitizeResult r = HtmlSanitizer.Sanitize("<p onclick=\"x()\">safe <img src=\"https://x/y.png\"></p>", new SanitizerPolicy());
        List<LayoutWarning> warnings = new List<LayoutWarning>();
        FlowDocument d = HtmlImporter.Import(r, null, Options(), warnings.Add);
        Assert.Equal("safe", Text(Paragraphs(d).Single()));
        Assert.Contains(warnings, w => w.Code == LayoutWarningCodes.ImagePlaceholder);

        HtmlDocument dom = HtmlParser.Parse("<title>T</title><h1>Dom</h1><p>trusted</p>");
        using LaidOutDocument laid = HtmlLayout.Open(dom, null, Options());
        Assert.Equal(LayoutSourceFormat.Html, laid.Source.Format);
        Assert.Equal("T", laid.Source.Title);
        Assert.Contains("trusted", laid.GetPage(0).GetText());
    }

    [Fact]
    public void A_mail_lays_out_end_to_end_to_svg()
    {
        string png = PngDataUri(120, 40);
        string html = "<!doctype html><html><head><title>Newsletter</title></head><body style=\"font-family:Arial,sans-serif\">" +
            "<table width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" bgcolor=\"#eeeeee\"><tr><td align=\"center\">" +
            "<table width=\"600\" cellpadding=\"20\" bgcolor=\"#ffffff\" style=\"border:1px solid #cccccc\">" +
            "<tr><td><img src=\"" + png + "\" alt=\"Logo\" style=\"display:block\"></td><td align=\"right\">28 Oct 2026</td></tr>" +
            "<tr><td colspan=\"2\" align=\"left\"><h1>Hello</h1><p>" + string.Concat(Enumerable.Repeat("Lorem ipsum dolor sit amet. ", 200)) + "</p></td></tr>" +
            "</table></td></tr></table></body></html>";
        using LaidOutDocument laid = HtmlLayout.Open(System.Text.Encoding.UTF8.GetBytes(html), null, Options());
        int pages = laid.ResolvePageCount();
        Assert.True(pages >= 2, "the long cell splits across pages");
        using MemoryStream svg = new MemoryStream();
        LayoutSvg.Write(laid.GetPage(0), svg);
        string s = System.Text.Encoding.UTF8.GetString(svg.ToArray());
        Assert.Contains("<image", s);
        Assert.Contains("Hello", laid.GetPage(0).GetText());
        Assert.Equal("Newsletter", laid.Source.Title);
    }

    [Fact]
    public void Limits_and_cancellation_are_mapped()
    {
        LayoutOptions o = Options();
        o.Limits = new LayoutLimits { MaxSourceBytes = 100 };
        Assert.Throws<LayoutLimitExceededException>(() => HtmlImporter.Import(new string('x', 200), null, o));
        LayoutLimitExceededException ex = Assert.Throws<LayoutLimitExceededException>(() =>
            HtmlImporter.Import("<p>" + new string('x', 9 * 1024 * 1024) + "</p>", null, Options()));
        Assert.StartsWith("Markup", ex.LimitName);
        using CancellationTokenSource cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => HtmlImporter.Import("<p>x</p>", null, Options(), null, cts.Token));
    }

    [Fact]
    public void Core_open_points_to_the_add_on()
    {
        using MemoryStream ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<p>x</p>"));
        LayoutFormatException ex = Assert.Throws<LayoutFormatException>(() => DocumentLayout.Open(ms, LayoutSourceFormat.Html, Options()));
        Assert.Contains("HtmlLayout.Open", ex.Message);
    }

    [Fact]
    public void Legacy_colour_parsing_follows_the_html_rules()
    {
        Assert.True(HtmlCss.TryLegacyColor("ff0000", out LayoutColor a));
        Assert.Equal(new LayoutColor(255, 0, 0), a);
        Assert.True(HtmlCss.TryLegacyColor("#abc", out LayoutColor b));
        Assert.Equal(new LayoutColor(0xAA, 0xBB, 0xCC), b);
        Assert.True(HtmlCss.TryLegacyColor("chucknorris", out LayoutColor c));
        Assert.Equal(new LayoutColor(0xC0, 0x00, 0x00), c);
        Assert.True(HtmlCss.TryLegacyColor("Navy", out LayoutColor d));
        Assert.Equal(new LayoutColor(0, 0, 0x80), d);
        Assert.False(HtmlCss.TryLegacyColor("transparent", out _));
        Assert.True(HtmlCss.TryColor("#11223344", LayoutColor.Black, out LayoutColor e));
        Assert.Equal(new LayoutColor(0x11, 0x22, 0x33, 0x44), e);
        Assert.True(HtmlCss.TryColor("rgb(255 0 0 / 50%)", LayoutColor.Black, out LayoutColor f));
        Assert.Equal(new LayoutColor(255, 0, 0, 128), f);
    }
}
