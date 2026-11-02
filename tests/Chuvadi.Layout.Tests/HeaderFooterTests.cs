using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.Tests;

public class HeaderFooterTests
{
    private static FlowParagraph Fields(params FlowInline[] inlines)
    {
        FlowParagraph p = new FlowParagraph { MarkFormat = Flow.Calibri11 };
        foreach (FlowInline i in inlines)
        {
            p.Inlines.Add(i);
        }
        return p;
    }

    private static TextRun T(string s) => new TextRun(s, Flow.Calibri11);

    private static FieldInline F(FieldKind k) => new FieldInline(k, Flow.Calibri11, "1");

    private static FlowDocument ManyPages(int lines, HeaderFooterSet hf)
    {
        FlowDocument d = Flow.Doc(Enumerable.Range(1, lines).Select(i => (FlowBlock)Flow.P("body " + i)).ToArray());
        d.Sections[0].HeadersFooters = hf;
        return d;
    }

    private static string RunText(LaidOutPage p, double minY, double maxY)
        => string.Join(" ", Flow.Runs(p).Where(r => r.Y >= minY && r.Y <= maxY).OrderBy(r => r.X).Select(r => r.Text));

    [Fact]
    public void Page_fields_in_footers_are_per_page_and_exact_after_the_count_is_final()
    {
        HeaderFooterSet hf = new HeaderFooterSet { DefaultFooter = new List<FlowBlock> { Fields(T("Page "), F(FieldKind.Page), T(" of "), F(FieldKind.NumPages)) } };
        using LaidOutDocument doc = DocumentLayout.Open(ManyPages(130, hf), Flow.Options());
        int n = doc.ResolvePageCount();
        Assert.Equal(3, n);
        for (int i = 0; i < n; i++)
        {
            LaidOutPage p = doc.GetPage(i);
            Assert.False(p.IsProvisional);
            Assert.Equal($"Page{i + 1}of3", RunText(p, 790, 842).Replace(" ", string.Empty, StringComparison.Ordinal));
            Assert.Contains(p.Boxes, b => b.Kind == LayoutBoxKind.Footer);
        }
    }

    [Fact]
    public void Numpages_before_the_count_is_final_is_provisional_then_rebuilt()
    {
        HeaderFooterSet hf = new HeaderFooterSet { DefaultHeader = new List<FlowBlock> { Fields(F(FieldKind.NumPages)) } };
        using LaidOutDocument doc = DocumentLayout.Open(ManyPages(400, hf), Flow.Options());
        LaidOutPage early = doc.GetPage(0);
        Assert.True(early.IsProvisional);
        Assert.Contains(doc.Warnings, w => w.Code == "ProvisionalPageCount");
        int n = doc.ResolvePageCount();
        LaidOutPage exact = doc.GetPage(0);
        Assert.False(exact.IsProvisional);
        Assert.Contains(Flow.Runs(exact), r => r.Text == n.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void First_and_even_page_headers()
    {
        HeaderFooterSet hf = new HeaderFooterSet
        {
            DifferentFirstPage = true,
            DifferentOddEven = true,
            FirstPageHeader = new List<FlowBlock> { Flow.P("FIRST") },
            EvenPageHeader = new List<FlowBlock> { Flow.P("EVEN") },
            DefaultHeader = new List<FlowBlock> { Flow.P("ODD") },
        };
        using LaidOutDocument doc = DocumentLayout.Open(ManyPages(200, hf), Flow.Options());
        Assert.True(doc.ResolvePageCount() >= 4);
        Assert.Contains(Flow.Runs(doc.GetPage(0)), r => r.Text == "FIRST");
        Assert.Contains(Flow.Runs(doc.GetPage(1)), r => r.Text == "EVEN");
        Assert.Contains(Flow.Runs(doc.GetPage(2)), r => r.Text == "ODD");
        Assert.Contains(Flow.Runs(doc.GetPage(3)), r => r.Text == "EVEN");
        // Header text is not part of the body text.
        Assert.DoesNotContain("FIRST", doc.GetPage(0).GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Later_sections_inherit_headers_and_restart_numbering()
    {
        FlowDocument d = Flow.Doc(Flow.P("one"));
        d.Sections[0].HeadersFooters = new HeaderFooterSet { DefaultFooter = new List<FlowBlock> { Fields(F(FieldKind.Page)) } };
        FlowSection s2 = new FlowSection { PageNumberStart = 1, PageNumberFormat = PageNumberFormat.UpperRoman };
        s2.Blocks.Add(Flow.P("two"));
        d.Sections.Add(s2);
        using LaidOutDocument doc = DocumentLayout.Open(d, Flow.Options());
        Assert.Equal(2, doc.ResolvePageCount());
        Assert.Contains(Flow.Runs(doc.GetPage(1)), r => r.Text == "I");
        Assert.Equal("I", doc.GetPage(1).PageLabel);
    }

    [Fact]
    public void Tall_header_pushes_the_body_down_and_tall_footer_pushes_it_up()
    {
        string tall = string.Join("\n", Enumerable.Range(1, 6).Select(i => "header line " + i));
        HeaderFooterSet hf = new HeaderFooterSet { DefaultHeader = new List<FlowBlock> { Flow.P(tall) } };
        FlowDocument d = Flow.Doc(Flow.P("body"));
        d.Sections[0].HeadersFooters = hf;
        using LaidOutDocument doc = DocumentLayout.Open(d, Flow.Options());
        double lh = Flow.LineHeight("Carlito-Regular.ttf", 11);
        LayoutBox body = Flow.Lines(doc.GetPage(0)).Single();
        Assert.Equal(35.4 + (6 * lh), body.Rect.Y, 4);

        HeaderFooterSet small = new HeaderFooterSet { DefaultHeader = new List<FlowBlock> { Flow.P("h") } };
        FlowDocument d2 = Flow.Doc(Flow.P("body"));
        d2.Sections[0].HeadersFooters = small;
        using LaidOutDocument doc2 = DocumentLayout.Open(d2, Flow.Options());
        Assert.Equal(72, Flow.Lines(doc2.GetPage(0)).Single().Rect.Y, 6);
    }

    [Fact]
    public void Date_and_file_name_fields_use_the_options()
    {
        FlowDocument d = Flow.Doc(Flow.P("x"));
        d.Sections[0].HeadersFooters = new HeaderFooterSet
        {
            DefaultHeader = new List<FlowBlock> { Fields(new FieldInline(FieldKind.Date, Flow.Calibri11) { FormatString = "yyyy-MM-dd" }, T(" "), F(FieldKind.FileName)) },
        };
        LayoutOptions o = Flow.Options();
        o.DocumentName = "report.docx";
        using LaidOutDocument doc = DocumentLayout.Open(d, o);
        string[] texts = Flow.Runs(doc.GetPage(0)).Select(r => r.Text).ToArray();
        Assert.Contains("2026-10-28", texts);
        Assert.Contains("report.docx", texts);
    }

    /// <summary>A shaper that records the scripts it is asked to shape (Tamil only), delegating to the simple shaper.</summary>
    private sealed class RecordingShaper : Chuvadi.Layout.Text.IShaper
    {
        public List<(Chuvadi.Layout.Text.UnicodeScript Script, string Text)> Calls { get; } = new();

        public string Name => "recording";

        public bool Supports(Chuvadi.Layout.Fonts.FontFace face, Chuvadi.Layout.Text.UnicodeScript script) => script == Chuvadi.Layout.Text.UnicodeScript.Tamil;

        public IReadOnlyList<Chuvadi.Layout.Text.ShapedGlyph> Shape(Chuvadi.Layout.Fonts.FontFace face, string text, int start, int length, Chuvadi.Layout.Text.ShapingOptions options)
        {
            Calls.Add((options.Script, text.Substring(start, length)));
            return Chuvadi.Layout.Text.SimpleShaper.Instance.Shape(face, text, start, length, options);
        }
    }

    [Fact]
    public void Page_dependent_field_values_are_shaped_as_text_runs()
    {
        // D-90: a field value in a header is laid out like any text: complex scripts go through the shaper, a character
        // outside the BMP is one glyph for its surrogate pair, and a character the face lacks takes the script fallback.
        FlowDocument d = Flow.Doc(Flow.P("x"));
        d.Sections[0].HeadersFooters = new HeaderFooterSet
        {
            DefaultHeader = new List<FlowBlock>
            {
                Fields(new FieldInline(FieldKind.Date, Flow.Calibri11) { FormatString = "d MMMM yyyy" }, T(" "), F(FieldKind.FileName), T(" "), F(FieldKind.Page)),
            },
        };
        LayoutOptions o = Flow.Options();
        o.Culture = new System.Globalization.CultureInfo("ta-IN");
        o.DocumentName = "\u0B85\u0BB1\u0BBF\u0B95\u0BCD\u0B95\u0BC8 \U0001F600.docx";
        RecordingShaper shaper = new RecordingShaper();
        o.Shaper = shaper;
        using LaidOutDocument doc = DocumentLayout.Open(d, o);
        LaidOutPage page = doc.GetPage(0);
        string month = new DateTime(2026, 10, 28).ToString("MMMM", o.Culture);

        // The Tamil month name and the Tamil file name were shaped, in the Tamil fallback face.
        Assert.Contains(shaper.Calls, c => c.Script == Chuvadi.Layout.Text.UnicodeScript.Tamil && c.Text.Contains(month, StringComparison.Ordinal));
        Assert.Contains(shaper.Calls, c => c.Script == Chuvadi.Layout.Text.UnicodeScript.Tamil && c.Text.Contains("\u0B85\u0BB1\u0BBF\u0B95\u0BCD\u0B95\u0BC8", StringComparison.Ordinal));
        GlyphRunOp tamil = Assert.Single(Flow.Runs(page), r => r.Text.Contains("\u0B85\u0BB1", StringComparison.Ordinal));
        Assert.Equal("LiPi Sans Tamil", tamil.Font.FamilyName);

        // The emoji is one glyph whose cluster is the UTF-16 index of its high surrogate in the run's text.
        GlyphRunOp emoji = Assert.Single(Flow.Runs(page), r => r.Text.Contains("\U0001F600", StringComparison.Ordinal));
        int at = emoji.Text.IndexOf("\U0001F600", StringComparison.Ordinal);
        Assert.Single(emoji.Glyphs, g => g.Cluster == at);
        Assert.DoesNotContain(emoji.Glyphs, g => g.Cluster == at + 1);

        // The header's runs read as written, including the page number (page text holds the body only).
        string header = string.Concat(Flow.Runs(page).Where(r => r.Y < 72).OrderBy(r => r.X).Select(r => r.Text));
        Assert.Contains(month, header, StringComparison.Ordinal);
        Assert.Contains("\U0001F600.docx 1", header, StringComparison.Ordinal);
    }
}

public class FloatingImageTests
{
    private static readonly LayoutImage Png = new LayoutImage(new byte[] { 0x89, 0x50 }, LayoutImageFormat.Png, 10, 10);

    private static FlowParagraph Anchored(AnchoredImageInline a, string text)
    {
        FlowParagraph p = Flow.P(text);
        p.Inlines.Insert(0, a);
        return p;
    }

    [Fact]
    public void Top_and_bottom_image_pushes_its_paragraph_text_below_it()
    {
        AnchoredImageInline a = new AnchoredImageInline(Png, 100, 80, Flow.Calibri11) { Wrap = FloatWrap.TopAndBottom, DistanceBottom = 4 };
        using LaidOutDocument doc = DocumentLayout.Open(Flow.Doc(Anchored(a, "text after image"), Flow.P("next")), Flow.Options());
        LaidOutPage p = doc.GetPage(0);
        ImageOp img = Assert.Single(p.DisplayList.OfType<ImageOp>());
        Assert.Equal(new LayoutRect(72, 72, 100, 80), img.Destination);
        Assert.Equal(72 + 80 + 4, Flow.Lines(p)[0].Rect.Y, 6);
    }

    [Fact]
    public void Behind_text_image_is_painted_first_and_does_not_move_text()
    {
        AnchoredImageInline behind = new AnchoredImageInline(Png, 200, 30, Flow.Calibri11)
        {
            Wrap = FloatWrap.BehindText,
            HorizontalRelativeTo = HorizontalAnchor.Page,
            VerticalRelativeTo = VerticalAnchor.Page,
            HorizontalAlignment = FloatAlignment.Center,
            OffsetY = 10,
        };
        using LaidOutDocument doc2 = DocumentLayout.Open(Flow.Doc(Anchored(behind, "over the image")), Flow.Options());
        LaidOutPage p = doc2.GetPage(0);
        List<LayoutOp> ops = p.DisplayList.ToList();
        ImageOp img = ops.OfType<ImageOp>().Single();
        Assert.Equal((595.3 - 200) / 2, img.Destination.X, 4);
        Assert.Equal(10, img.Destination.Y, 6);
        Assert.True(ops.IndexOf(img) < ops.FindIndex(o => o is GlyphRunOp));
        // Behind-text images do not move text.
        Assert.Equal(72, Flow.Lines(p)[0].Rect.Y, 6);
    }

    [Fact]
    public void Anchors_follow_their_paragraph_to_the_next_page()
    {
        List<FlowBlock> blocks = Enumerable.Range(1, 49).Select(i => (FlowBlock)Flow.P("filler " + i)).ToList();
        AnchoredImageInline a = new AnchoredImageInline(Png, 60, 40, Flow.Calibri11) { Wrap = FloatWrap.TopAndBottom };
        blocks.Add(Anchored(a, "anchor paragraph\nsecond line"));
        using LaidOutDocument doc = DocumentLayout.Open(Flow.Doc(blocks.ToArray()), Flow.Options());
        Assert.Equal(2, doc.ResolvePageCount());
        Assert.Empty(doc.GetPage(0).DisplayList.OfType<ImageOp>());
        Assert.Single(doc.GetPage(1).DisplayList.OfType<ImageOp>());
    }
}
