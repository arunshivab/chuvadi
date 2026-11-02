using System.Text;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Fonts;
using Chuvadi.Layout.Import;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.Tests;

public class FlowBasicsTests
{
    [Fact]
    public void Short_paragraph_is_one_line_on_one_page_with_extractable_text()
    {
        using LaidOutDocument doc = DocumentLayout.Open(Flow.Doc(Flow.P("Hello, world.")), Flow.Options());
        LaidOutPage page = doc.GetPage(0);
        Assert.Equal(1, doc.ResolvePageCount());
        Assert.Equal(595.3, page.WidthPt, 3);
        Assert.Equal("Hello, world.", page.GetText());
        GlyphRunOp run = Assert.Single(Flow.Runs(page));
        Assert.Equal("Carlito", run.Font.FamilyName);
        Assert.Equal(13, run.Glyphs.Count);
        Assert.Equal(72, run.X, 6);
        // Baseline = top margin + Carlito ascent at 11 pt (1950/2048*11).
        Assert.Equal(72 + (1950.0 / 2048 * 11), run.Y, 6);
        Assert.Equal("1", page.PageLabel);
        Assert.Contains(doc.FontSubstitutions, s => s.RequestedFamily == "Calibri" && s.UsedFamily == "Carlito" && s.MetricCompatible);
    }

    [Fact]
    public void Long_paragraph_wraps_at_spaces_within_the_text_area()
    {
        string text = string.Join(" ", Enumerable.Repeat("lorem ipsum dolor sit amet", 30));
        using LaidOutDocument doc = DocumentLayout.Open(Flow.Doc(Flow.P(text)), Flow.Options());
        LaidOutPage page = doc.GetPage(0);
        List<LayoutBox> lines = Flow.Lines(page);
        Assert.True(lines.Count > 5);
        foreach (LayoutBox l in lines)
        {
            Assert.True(l.Rect.Right <= 595.3 - 72 + 0.02, $"line exceeds margin: {l.Rect}");
        }
        string extracted = page.GetText().Replace("\n", string.Empty, StringComparison.Ordinal);
        Assert.Equal(text.Replace(" ", string.Empty, StringComparison.Ordinal), extracted.Replace(" ", string.Empty, StringComparison.Ordinal));
        // Every line but the last ends at a word boundary.
        foreach (string line in page.GetText().Split('\n'))
        {
            Assert.False(line.StartsWith(' '));
        }
    }

    [Fact]
    public void Lines_per_page_follow_the_font_line_height()
    {
        string text = string.Join("\n", Enumerable.Range(1, 200).Select(i => "Line " + i));
        using LaidOutDocument doc = DocumentLayout.OpenText(text, null, Flow.Options());
        double lh = Flow.LineHeight("LiberationMono-Regular.ttf", 10);
        int perPage = (int)Math.Floor((841.9 - 144 + 0.01) / lh);
        Assert.Equal((int)Math.Ceiling(200.0 / perPage), doc.ResolvePageCount());
        Assert.Equal(perPage, Flow.Lines(doc.GetPage(0)).Count);
        Assert.StartsWith("Line " + (perPage + 1), doc.GetPage(1).GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Layout_is_lazy_and_the_page_count_becomes_final()
    {
        string text = string.Join("\n", Enumerable.Range(1, 5000).Select(i => "Row " + i));
        using LaidOutDocument doc = DocumentLayout.OpenText(text, null, Flow.Options());
        Assert.False(doc.IsPageCountFinal);
        doc.GetPage(0);
        Assert.False(doc.IsPageCountFinal);
        int estimate = doc.PageCount;
        Assert.True(estimate > 50, "estimate after one page should extrapolate, got " + estimate);
        int final = doc.ResolvePageCount();
        Assert.True(doc.IsPageCountFinal);
        Assert.Equal(final, doc.PageCount);
        Assert.Contains("Row 5000", doc.GetPage(final - 1).GetText(), StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.GetPage(final));
    }

    [Fact]
    public void Pages_are_deterministic_when_rebuilt_from_checkpoints()
    {
        string text = string.Join("\n\n", Enumerable.Range(1, 60).Select(i => string.Join(" ", Enumerable.Repeat("word" + i, 40))));
        LayoutOptions o = Flow.Options();
        o.PageCacheSize = 1;
        using LaidOutDocument doc = DocumentLayout.OpenText(text, null, o);
        int n = doc.ResolvePageCount();
        string[] first = Enumerable.Range(0, n).Select(i => doc.GetPage(i).GetText()).ToArray();
        string[] again = Enumerable.Range(0, n).Reverse().Select(i => doc.GetPage(i).GetText()).Reverse().ToArray();
        Assert.Equal(first, again);
        Assert.Equal(text.Replace("\n", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal),
            string.Concat(first).Replace("\n", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public void Cancellation_and_limits_are_honoured()
    {
        string text = string.Join("\n", Enumerable.Range(1, 3000).Select(i => "x" + i));
        using CancellationTokenSource cts = new CancellationTokenSource();
        cts.Cancel();
        using LaidOutDocument doc = DocumentLayout.OpenText(text, null, Flow.Options());
        Assert.ThrowsAny<OperationCanceledException>(() => doc.GetPage(0, cts.Token));

        LayoutOptions limited = Flow.Options();
        limited.Limits.MaxPages = 3;
        using LaidOutDocument doc2 = DocumentLayout.OpenText(text, null, limited);
        LayoutLimitExceededException ex = Assert.Throws<LayoutLimitExceededException>(() => doc2.ResolvePageCount());
        Assert.Equal("MaxPages", ex.LimitName);

        LayoutOptions tiny = Flow.Options();
        tiny.Limits.MaxSourceBytes = 10;
        Assert.Throws<LayoutLimitExceededException>(() => DocumentLayout.OpenText(new MemoryStream(new byte[100]), null, tiny));
    }
}

public class FlowPaginationRulesTests
{
    private static ParagraphFormat Spaced(double after = 0) => new ParagraphFormat { SpaceAfter = after };

    /// <summary>Fills the page with single-line paragraphs, leaving <paramref name="linesLeft"/> lines of room.</summary>
    private static List<FlowBlock> Filler(int linesLeft, out double lh)
    {
        lh = Flow.LineHeight("Carlito-Regular.ttf", 11);
        int perPage = (int)Math.Floor((841.9 - 144 + 0.01) / lh);
        return Enumerable.Range(1, perPage - linesLeft).Select(i => (FlowBlock)Flow.P("filler " + i)).ToList();
    }

    private static string Multi(int lines) => string.Join("\n", Enumerable.Range(1, lines).Select(i => "para line " + i));

    [Fact]
    public void Orphan_control_moves_a_paragraph_that_would_leave_one_line()
    {
        List<FlowBlock> blocks = Filler(1, out _);
        blocks.Add(Flow.P(Multi(4)));
        using LaidOutDocument doc = DocumentLayout.Open(Flow.Doc(blocks.ToArray()), Flow.Options());
        Assert.DoesNotContain("para line 1", doc.GetPage(0).GetText(), StringComparison.Ordinal);
        Assert.StartsWith("para line 1", doc.GetPage(1).GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Widow_control_carries_two_lines()
    {
        List<FlowBlock> blocks = Filler(3, out _);
        blocks.Add(Flow.P(Multi(4)));
        using LaidOutDocument doc = DocumentLayout.Open(Flow.Doc(blocks.ToArray()), Flow.Options());
        Assert.EndsWith("para line 2", doc.GetPage(0).GetText(), StringComparison.Ordinal);
        Assert.StartsWith("para line 3", doc.GetPage(1).GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Without_widow_control_lines_fill_the_page()
    {
        List<FlowBlock> blocks = Filler(3, out _);
        blocks.Add(Flow.P(Multi(4), new ParagraphFormat { WidowControl = false }));
        using LaidOutDocument doc = DocumentLayout.Open(Flow.Doc(blocks.ToArray()), Flow.Options());
        Assert.EndsWith("para line 3", doc.GetPage(0).GetText(), StringComparison.Ordinal);
        Assert.Equal("para line 4", doc.GetPage(1).GetText());
    }

    [Fact]
    public void Keep_with_next_moves_a_heading_to_the_next_page()
    {
        List<FlowBlock> blocks = Filler(1, out _);
        blocks.Add(Flow.P("Heading", new ParagraphFormat { KeepWithNext = true }));
        blocks.Add(Flow.P("Body after heading"));
        using LaidOutDocument doc = DocumentLayout.Open(Flow.Doc(blocks.ToArray()), Flow.Options());
        Assert.DoesNotContain("Heading", doc.GetPage(0).GetText(), StringComparison.Ordinal);
        Assert.StartsWith("Heading\nBody after heading", doc.GetPage(1).GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Keep_lines_together_moves_the_whole_paragraph()
    {
        List<FlowBlock> blocks = Filler(5, out _);
        blocks.Add(Flow.P(Multi(8), new ParagraphFormat { KeepLinesTogether = true }));
        using LaidOutDocument doc = DocumentLayout.Open(Flow.Doc(blocks.ToArray()), Flow.Options());
        Assert.StartsWith("para line 1", doc.GetPage(1).GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Page_break_inline_and_page_break_before()
    {
        FlowParagraph p = new FlowParagraph { MarkFormat = Flow.Calibri11 };
        p.Inlines.Add(new TextRun("before", Flow.Calibri11));
        p.Inlines.Add(new BreakInline(BreakKind.Page, Flow.Calibri11));
        p.Inlines.Add(new TextRun("after", Flow.Calibri11));
        FlowParagraph q = Flow.P("own page", new ParagraphFormat { PageBreakBefore = true, SpaceBefore = 24 });
        using LaidOutDocument doc = DocumentLayout.Open(Flow.Doc(p, q), Flow.Options());
        Assert.Equal(3, doc.ResolvePageCount());
        Assert.Equal("before", doc.GetPage(0).GetText());
        Assert.Equal("after", doc.GetPage(1).GetText());
        Assert.Equal("own page", doc.GetPage(2).GetText());
        // Space before is kept after a hard break.
        Assert.Equal(72 + 24, Flow.Lines(doc.GetPage(2))[0].Rect.Y, 6);
    }

    [Fact]
    public void Space_before_is_suppressed_at_the_top_of_a_page_reached_by_flow()
    {
        List<FlowBlock> blocks = Filler(0, out _);
        blocks.Add(Flow.P("next", new ParagraphFormat { SpaceBefore = 30 }));
        using LaidOutDocument doc = DocumentLayout.Open(Flow.Doc(blocks.ToArray()), Flow.Options());
        Assert.Equal(72, Flow.Lines(doc.GetPage(1))[0].Rect.Y, 6);
        using LaidOutDocument first = DocumentLayout.Open(Flow.Doc(Flow.P("first", new ParagraphFormat { SpaceBefore = 30 })), Flow.Options());
        Assert.Equal(102, Flow.Lines(first.GetPage(0))[0].Rect.Y, 6);
    }

    [Fact]
    public void Spacing_between_paragraphs_is_the_larger_of_after_and_before_and_contextual_spacing_removes_it()
    {
        ParagraphFormat list = new ParagraphFormat { SpaceBefore = 6, SpaceAfter = 10, ContextualSpacing = true, StyleId = "List" };
        double lh = Flow.LineHeight("Carlito-Regular.ttf", 11);
        using LaidOutDocument doc = DocumentLayout.Open(Flow.Doc(Flow.P("a", Spaced(10)), Flow.P("b", new ParagraphFormat { SpaceBefore = 6 }), Flow.P("c", list), Flow.P("d", list)), Flow.Options());
        List<LayoutBox> lines = Flow.Lines(doc.GetPage(0));
        // 10 after and 6 before give 10, not 16 (Word and LibreOffice; reference corpus d05).
        Assert.Equal(72 + lh + 10, lines[1].Rect.Y, 6);
        Assert.Equal(lines[2].Rect.Bottom, lines[3].Rect.Y, 6);
    }
}

public class FlowLineTests
{
    private static LaidOutPage One(FlowParagraph p)
    {
        LaidOutDocument doc = DocumentLayout.Open(Flow.Doc(p), Flow.Options());
        return doc.GetPage(0);
    }

    [Fact]
    public void Center_and_right_alignment()
    {
        LaidOutPage c = One(Flow.P("mid", new ParagraphFormat { Alignment = ParagraphAlignment.Center }));
        LayoutBox line = Flow.Lines(c)[0];
        Assert.Equal(72 + ((451.3 - line.Rect.Width) / 2), line.Rect.X, 4);
        LaidOutPage r = One(Flow.P("end", new ParagraphFormat { Alignment = ParagraphAlignment.Right }));
        Assert.Equal(523.3, Flow.Lines(r)[0].Rect.Right, 4);
    }

    [Fact]
    public void Justified_lines_fill_the_width_except_the_last()
    {
        string text = string.Join(" ", Enumerable.Repeat("justify me please", 20));
        LaidOutPage page = One(Flow.P(text, new ParagraphFormat { Alignment = ParagraphAlignment.Justify }));
        List<LayoutBox> lines = Flow.Lines(page);
        for (int i = 0; i < lines.Count - 1; i++)
        {
            Assert.Equal(523.3, lines[i].Rect.Right, 1);
        }
        Assert.True(lines[^1].Rect.Right < 523.3 - 1);
    }

    [Fact]
    public void Indents_apply_to_first_and_following_lines()
    {
        string text = string.Join(" ", Enumerable.Repeat("indent", 60));
        LaidOutPage page = One(Flow.P(text, new ParagraphFormat { LeftIndent = 36, RightIndent = 18, FirstLineIndent = -18 }));
        List<LayoutBox> lines = Flow.Lines(page);
        Assert.Equal(72 + 18, lines[0].Rect.X, 6);
        Assert.Equal(72 + 36, lines[1].Rect.X, 6);
        Assert.All(lines, l => Assert.True(l.Rect.Right <= 523.3 - 18 + 0.02));
    }

    [Fact]
    public void Right_tab_aligns_text_end_at_the_stop_and_draws_leaders()
    {
        FlowParagraph p = new FlowParagraph
        {
            Format = new ParagraphFormat { TabStops = new[] { new TabStop(400, TabAlignment.Right, TabLeader.Dot) } },
            MarkFormat = Flow.Calibri11,
        };
        p.Inlines.Add(new TextRun("Chapter 1\t12", Flow.Calibri11));
        LaidOutPage page = One(p);
        GlyphRunOp last = Flow.Runs(page).Last(r => r.Text == "12");
        Assert.Equal(72 + 400, last.X + last.Width, 3);
        Assert.Contains(Flow.Runs(page), r => r.Text.Length > 5 && r.Text.All(ch => ch == '.'));
        Assert.Equal("Chapter 1\t12", page.GetText());
    }

    [Fact]
    public void Each_tab_draws_the_leader_of_its_own_stop()
    {
        // A centre stop without a leader, then a dotted decimal stop (reference corpus d35): only the second tab has dots.
        FlowParagraph p = new FlowParagraph
        {
            Format = new ParagraphFormat { TabStops = new[] { new TabStop(150, TabAlignment.Center), new TabStop(300, TabAlignment.Decimal, TabLeader.Dot) } },
            MarkFormat = Flow.Calibri11,
        };
        p.Inlines.Add(new TextRun("Item\tcentre\t411.50", Flow.Calibri11));
        LaidOutPage page = One(p);
        GlyphRunOp centre = Flow.Runs(page).Single(r => r.Text == "centre");
        List<GlyphRunOp> dots = Flow.Runs(page).Where(r => r.Text.Length > 0 && r.Text.All(ch => ch == '.')).ToList();
        GlyphRunOp leader = Assert.Single(dots);
        Assert.True(leader.X >= centre.X + centre.Width - 0.01);
    }

    [Fact]
    public void Default_tab_stops_every_half_inch()
    {
        LaidOutPage page = One(Flow.P("a\tb"));
        GlyphRunOp b = Flow.Runs(page).Single(r => r.Text == "b");
        Assert.Equal(72 + 36, b.X, 3);
    }

    [Fact]
    public void Soft_hyphen_shows_only_at_a_break()
    {
        string word = "extra\u00ADordinarily";
        string text = string.Join(" ", Enumerable.Repeat(word, 40));
        LaidOutPage page = One(Flow.P(text));
        string extracted = page.GetText();
        Assert.DoesNotContain("\u00AD", extracted, StringComparison.Ordinal);
        Assert.Contains("extraordinarily", extracted, StringComparison.Ordinal);
        foreach (string line in extracted.Split('\n'))
        {
            Assert.True(line.EndsWith("extraordinarily", StringComparison.Ordinal) || line.EndsWith("extra-", StringComparison.Ordinal) || line.Length == 0, line);
        }
    }

    [Fact]
    public void Overlong_word_is_broken_at_a_cluster_boundary()
    {
        string word = new string('W', 200);
        LaidOutPage page = One(Flow.P(word));
        List<LayoutBox> lines = Flow.Lines(page);
        Assert.True(lines.Count >= 3);
        Assert.All(lines, l => Assert.True(l.Rect.Right <= 523.3 + 0.02));
        Assert.Equal(word, page.GetText().Replace("\n", string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public void Line_break_inline_starts_a_new_line_and_justify_stretches_it()
    {
        FlowParagraph p = new FlowParagraph { Format = new ParagraphFormat { Alignment = ParagraphAlignment.Justify }, MarkFormat = Flow.Calibri11 };
        p.Inlines.Add(new TextRun("one two", Flow.Calibri11));
        p.Inlines.Add(new BreakInline(BreakKind.Line, Flow.Calibri11));
        p.Inlines.Add(new TextRun("three", Flow.Calibri11));
        LaidOutPage page = One(p);
        Assert.Equal("one two\nthree", page.GetText());
        Assert.Equal(523.3, Flow.Lines(page)[0].Rect.Right, 1);
    }

    [Fact]
    public void Exact_and_at_least_line_spacing()
    {
        string text = string.Join("\n", Enumerable.Range(1, 3).Select(i => "l" + i));
        LaidOutPage exact = One(Flow.P(text, new ParagraphFormat { LineSpacingRule = LineSpacingRule.Exact, LineSpacing = 20 }));
        List<LayoutBox> e = Flow.Lines(exact);
        Assert.Equal(20, e[1].Rect.Y - e[0].Rect.Y, 6);
        LaidOutPage atLeast = One(Flow.P(text, new ParagraphFormat { LineSpacingRule = LineSpacingRule.AtLeast, LineSpacing = 5 }));
        List<LayoutBox> a = Flow.Lines(atLeast);
        Assert.Equal(Flow.LineHeight("Carlito-Regular.ttf", 11), a[1].Rect.Y - a[0].Rect.Y, 6);
        LaidOutPage dbl = One(Flow.P(text, new ParagraphFormat { LineSpacing = 2 }));
        List<LayoutBox> d = Flow.Lines(dbl);
        Assert.Equal(2 * Flow.LineHeight("Carlito-Regular.ttf", 11), d[1].Rect.Y - d[0].Rect.Y, 6);
    }

    [Fact]
    public void Decorations_and_highlight_are_drawn()
    {
        CharacterFormat f = Flow.Calibri11 with { Underline = UnderlineStyle.Single, Strikethrough = true, Background = new LayoutColor(255, 255, 0) };
        LaidOutPage page = One(Flow.P("marked", null, f));
        Assert.Equal(2, page.DisplayList.OfType<StrokeLineOp>().Count());
        FillRectOp hl = Assert.Single(page.DisplayList.OfType<FillRectOp>());
        Assert.Equal(new LayoutColor(255, 255, 0), hl.Color);
        // Highlight is painted before the text.
        Assert.True(page.DisplayList.ToList().IndexOf(hl) < page.DisplayList.ToList().FindIndex(o => o is GlyphRunOp));
    }

    [Fact]
    public void Kerning_follows_the_run_threshold()
    {
        LaidOutPage plain = One(Flow.P("AV", null, Flow.Calibri11 with { FontFamily = "Arial" }));
        LaidOutPage kerned = One(Flow.P("AV", null, Flow.Calibri11 with { FontFamily = "Arial", KerningMinimumSize = 8 }));
        double w0 = Flow.Runs(plain).Single().Width;
        double w1 = Flow.Runs(kerned).Single().Width;
        Assert.Equal(152.0 / 2048 * 11, w0 - w1, 6);
    }

    [Fact]
    public void Superscript_is_smaller_and_raised()
    {
        FlowParagraph p = new FlowParagraph { MarkFormat = Flow.Calibri11 };
        p.Inlines.Add(new TextRun("x", Flow.Calibri11));
        p.Inlines.Add(new TextRun("2", Flow.Calibri11 with { VerticalAlignment = VerticalTextAlignment.Superscript }));
        LaidOutPage page = One(p);
        GlyphRunOp x = Flow.Runs(page).Single(r => r.Text == "x");
        GlyphRunOp sup = Flow.Runs(page).Single(r => r.Text == "2");
        Assert.True(sup.FontSize < x.FontSize);
        Assert.True(sup.Y < x.Y);
    }

    [Fact]
    public void List_label_hangs_and_text_aligns_at_the_indent()
    {
        FlowParagraph p = Flow.P("Item text", new ParagraphFormat { LeftIndent = 36, FirstLineIndent = -18 });
        p.ListLabel = new ListLabel("\u2022", Flow.Calibri11 with { FontFamily = "Arial" });
        LaidOutPage page = One(p);
        GlyphRunOp bullet = Flow.Runs(page).First();
        GlyphRunOp text = Flow.Runs(page).Single(r => r.Text == "Item text");
        Assert.Equal(72 + 18, bullet.X, 3);
        Assert.Equal(72 + 36, text.X, 3);
    }

    [Fact]
    public void Indic_text_falls_back_and_warns_without_a_shaper()
    {
        using LaidOutDocument doc = DocumentLayout.Open(Flow.Doc(Flow.P("Tamil: \u0BA4\u0BAE\u0BBF\u0BB4\u0BCD")), Flow.Options());
        LaidOutPage page = doc.GetPage(0);
        Assert.Contains(Flow.Runs(page), r => r.Font.FamilyName == "LiPi Sans Tamil");
        Assert.Contains(doc.Warnings, w => w.Code == "ComplexScriptNotShaped");
        Assert.Contains(doc.FontSubstitutions, s => s.Kind == FontMatchKind.ScriptFallback);
    }

    [Fact]
    public void Space_between_complex_script_words_takes_the_complex_script_font_and_size()
    {
        // Calibri 10 pt for Latin, LiPi Sans Tamil 12 pt for Tamil (reference corpus d04): the space between two Tamil
        // words is drawn in the Tamil run at 12 pt; the space after a Latin word stays Calibri 10 pt.
        CharacterFormat cf = new CharacterFormat { FontFamily = "Calibri", FontSize = 10, ComplexScriptFontFamily = "LiPi Sans Tamil", ComplexScriptFontSize = 12 };
        using LaidOutDocument doc = DocumentLayout.Open(Flow.Doc(Flow.P("ab \u0B95 \u0B95", cf: cf)), Flow.Options());
        List<GlyphRunOp> runs = Flow.Runs(doc.GetPage(0)).ToList();
        GlyphRunOp tamil = Assert.Single(runs, r => r.Font.FamilyName == "LiPi Sans Tamil");
        Assert.Equal("\u0B95 \u0B95", tamil.Text);
        Assert.Equal(12, tamil.FontSize);
        Assert.Equal("ab ", Assert.Single(runs, r => r.Font.FamilyName != "LiPi Sans Tamil").Text);
    }

    [Fact]
    public void Hyperlinks_produce_link_ops()
    {
        FlowParagraph p = new FlowParagraph { MarkFormat = Flow.Calibri11 };
        p.Inlines.Add(new TextRun("see ", Flow.Calibri11));
        p.Inlines.Add(new TextRun("site", Flow.Calibri11) { HyperlinkUri = "https://example.org" });
        LinkOp link = Assert.Single(One(p).DisplayList.OfType<LinkOp>());
        Assert.Equal("https://example.org", link.Uri);
    }

    [Fact]
    public void Inline_images_and_placeholders()
    {
        FlowParagraph p = new FlowParagraph { MarkFormat = Flow.Calibri11 };
        p.Inlines.Add(new ImageInline(new LayoutImage(new byte[] { 1 }, LayoutImageFormat.Png, 10, 10), 50, 40, Flow.Calibri11));
        p.Inlines.Add(new ImageInline(new LayoutImage(new byte[] { 1 }, LayoutImageFormat.Emf, 10, 10), 30, 20, Flow.Calibri11));
        using LaidOutDocument doc = DocumentLayout.Open(Flow.Doc(p), Flow.Options());
        LaidOutPage page = doc.GetPage(0);
        ImageOp img = Assert.Single(page.DisplayList.OfType<ImageOp>());
        Assert.Equal(new LayoutRect(72, img.Destination.Y, 50, 40), img.Destination);
        Assert.Contains(doc.Warnings, w => w.Code == "ImagePlaceholder");
        Assert.Equal(2, page.Boxes.Count(b => b.Kind == LayoutBoxKind.Image));
        // The line is tall enough for the image.
        Assert.True(Flow.Lines(page)[0].Rect.Height >= 40);
    }
}

public class FlowSectionTests
{
    [Fact]
    public void New_section_with_landscape_page_starts_a_new_page()
    {
        FlowDocument d = Flow.Doc(Flow.P("portrait"));
        FlowSection land = new FlowSection { Page = PageGeometry.A4 with { Width = 841.9, Height = 595.3 } };
        land.Blocks.Add(Flow.P("landscape"));
        d.Sections.Add(land);
        using LaidOutDocument doc = DocumentLayout.Open(d, Flow.Options());
        Assert.Equal(2, doc.ResolvePageCount());
        Assert.Equal(841.9, doc.GetPage(1).WidthPt, 3);
        Assert.Equal("landscape", doc.GetPage(1).GetText());
    }

    [Fact]
    public void Continuous_section_of_same_size_stays_on_the_page()
    {
        FlowDocument d = Flow.Doc(Flow.P("one"));
        FlowSection s2 = new FlowSection { Start = SectionStart.Continuous, Page = PageGeometry.A4 with { MarginLeft = 144 } };
        s2.Blocks.Add(Flow.P("two"));
        d.Sections.Add(s2);
        using LaidOutDocument doc = DocumentLayout.Open(d, Flow.Options());
        Assert.Equal(1, doc.ResolvePageCount());
        Assert.Equal(144, Flow.Lines(doc.GetPage(0))[1].Rect.X, 6);
    }

    [Fact]
    public void Odd_page_section_inserts_a_blank_page_and_numbering_restarts()
    {
        FlowDocument d = Flow.Doc(Flow.P("page one"));
        FlowSection s2 = new FlowSection { Start = SectionStart.OddPage };
        s2.Blocks.Add(Flow.P("chapter"));
        d.Sections.Add(s2);
        FlowSection s3 = new FlowSection { PageNumberStart = 1, PageNumberFormat = PageNumberFormat.LowerRoman };
        s3.Blocks.Add(Flow.P("appendix"));
        d.Sections.Add(s3);
        using LaidOutDocument doc = DocumentLayout.Open(d, Flow.Options());
        Assert.Equal(4, doc.ResolvePageCount());
        Assert.Equal(string.Empty, doc.GetPage(1).GetText());
        Assert.Equal("chapter", doc.GetPage(2).GetText());
        Assert.Equal("3", doc.GetPage(2).PageLabel);
        Assert.Equal("i", doc.GetPage(3).PageLabel);
    }

    [Fact]
    public void Multi_column_sections_warn()
    {
        FlowDocument d = Flow.Doc(Flow.P("cols"));
        d.Sections[0].ColumnCount = 2;
        using LaidOutDocument doc = DocumentLayout.Open(d, Flow.Options());
        Assert.Contains(doc.Warnings, w => w.Code == "MultiColumnNotSupported");
    }

    [Fact]
    public void Empty_document_has_no_pages()
    {
        using LaidOutDocument doc = DocumentLayout.Open(new FlowDocument(), Flow.Options());
        Assert.Equal(0, doc.ResolvePageCount());
        Assert.True(doc.IsPageCountFinal);
    }
}

public class TextImportTests
{
    [Theory]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, 0x41 }, "A")]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x41, 0x00 }, "A")]
    [InlineData(new byte[] { 0xFE, 0xFF, 0x00, 0x41 }, "A")]
    [InlineData(new byte[] { 0x41, 0xE9 }, "A\u00E9")] // invalid UTF-8 \u2192 Latin-1
    [InlineData(new byte[] { 0x41, 0xC3, 0xA9 }, "A\u00E9")]
    public void Decoding(byte[] bytes, string expected)
    {
        Assert.Equal(expected, TextImporter.Decode(bytes));
    }

    [Fact]
    public void Lines_tabs_and_form_feeds()
    {
        using LaidOutDocument doc = DocumentLayout.OpenText(new MemoryStream(Encoding.UTF8.GetBytes("a\r\nb\tc\r\n\fnext page")), null, Flow.Options());
        Assert.Equal(2, doc.ResolvePageCount());
        Assert.Equal("a\nb\tc", doc.GetPage(0).GetText());
        Assert.Equal("next page", doc.GetPage(1).GetText());
        GlyphRunOp c = Flow.Runs(doc.GetPage(0)).Single(r => r.Text == "c");
        FontFace mono = TestFonts.Face("LiberationMono-Regular.ttf");
        double digit = mono.GetAdvance(mono.GetGlyphId('0')) * 10.0 / mono.UnitsPerEm;
        Assert.Equal(72 + (8 * digit), c.X, 3);
    }

    [Fact]
    public void Sniffing()
    {
        Assert.Equal(LayoutSourceFormat.Text, DocumentLayout.Sniff(Encoding.ASCII.GetBytes("hello")));
        byte[] docxLike = new byte[] { 0x50, 0x4B, 0x03, 0x04 }.Concat(Encoding.ASCII.GetBytes("....word/document.xml")).ToArray();
        Assert.Equal(LayoutSourceFormat.Docx, DocumentLayout.Sniff(docxLike));
        // Sniffed as a document; the engine names the package that lays it out (D-115).
        LayoutFormatException ex = Assert.Throws<LayoutFormatException>(() => DocumentLayout.Open(new MemoryStream(docxLike), LayoutSourceFormat.Auto, Flow.Options()));
        Assert.Contains("Chuvadi.Layout.Docs", ex.Message, StringComparison.Ordinal);
        Assert.Throws<LayoutFormatException>(() => DocumentLayout.Sniff(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0, 0, 0, 0 }));
    }

    [Fact]
    public void Input_stream_is_left_open_and_unchanged()
    {
        byte[] data = Encoding.UTF8.GetBytes("unchanged");
        byte[] copy = (byte[])data.Clone();
        MemoryStream ms = new MemoryStream(data, writable: false);
        using LaidOutDocument doc = DocumentLayout.Open(ms, LayoutSourceFormat.Auto, Flow.Options());
        Assert.True(ms.CanRead);
        Assert.Equal(copy, data);
        Assert.Equal("unchanged", doc.GetPage(0).GetText());
    }
}

public class FlowBorderAndPerformanceTests
{
    [Fact]
    public void Top_and_bottom_borders_take_their_width_and_spacing()
    {
        BorderLine b = new BorderLine(1, LayoutColor.Black, Space: 4);
        double lh = Flow.LineHeight("Carlito-Regular.ttf", 11);
        using LaidOutDocument doc = DocumentLayout.Open(Flow.Doc(Flow.P("boxed", new ParagraphFormat { BorderTop = b, BorderBottom = b }), Flow.P("after")), Flow.Options());
        List<LayoutBox> lines = Flow.Lines(doc.GetPage(0));
        Assert.Equal(72 + 5, lines[0].Rect.Y, 6);
        Assert.Equal(72 + 5 + lh + 5, lines[1].Rect.Y, 6);
    }

    [Fact]
    public void First_page_of_a_large_document_is_fast_and_lazy()
    {
        // ~300 pages of justified Calibri text.
        string para = string.Join(" ", Enumerable.Repeat("The quick brown fox jumps over the lazy dog.", 12));
        FlowDocument d = Flow.Doc(Enumerable.Range(0, 1500).Select(i => (FlowBlock)Flow.P(para, new ParagraphFormat { Alignment = ParagraphAlignment.Justify, SpaceAfter = 8 })).ToArray());
        using LaidOutDocument doc = DocumentLayout.Open(d, Flow.Options());
        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        doc.GetPage(0);
        double first = sw.Elapsed.TotalSeconds;
        sw.Restart();
        doc.GetPage(1);
        double second = sw.Elapsed.TotalSeconds;
        Assert.False(doc.IsPageCountFinal);
        Assert.True(first < 1.0, $"first page took {first:0.000} s");
        Assert.True(second < 0.3, $"second page took {second:0.000} s");
        sw.Restart();
        int pages = doc.ResolvePageCount();
        double all = sw.Elapsed.TotalSeconds;
        Assert.True(pages > 150, "pages: " + pages);
        Assert.True(all < 20, $"full pagination took {all:0.000} s");
    }
}

public class PaperAndConcurrencyTests
{
    [Fact]
    public void Plain_text_uses_the_culture_paper()
    {
        LayoutOptions us = Flow.Options();
        us.Culture = new System.Globalization.CultureInfo("en-US");
        using LaidOutDocument a = DocumentLayout.OpenText("x", null, us);
        Assert.Equal(612, a.GetPage(0).WidthPt, 1);
        LayoutOptions india = Flow.Options();
        india.Culture = new System.Globalization.CultureInfo("en-IN");
        using LaidOutDocument b = DocumentLayout.OpenText("x", null, india);
        Assert.Equal(595.3, b.GetPage(0).WidthPt, 1);
        using LaidOutDocument c = DocumentLayout.OpenText("x", new TextImportOptions { Page = PageGeometry.Letter }, india);
        Assert.Equal(612, c.GetPage(0).WidthPt, 1);
    }

    [Fact]
    public async Task Resolving_the_page_count_in_the_background_while_pages_are_read_gives_the_same_pages()
    {
        string text = string.Join("\n", Enumerable.Range(1, 6000).Select(i => "Line " + i + " of the background resolve test."));
        using LaidOutDocument reference = DocumentLayout.OpenText(text, null, Flow.Options());
        int n = reference.ResolvePageCount();
        string[] expected = Enumerable.Range(0, n).Select(i => reference.GetPage(i).GetText()).ToArray();

        using LaidOutDocument doc = DocumentLayout.OpenText(text, null, Flow.Options());
        Task<int> resolve = Task.Run(() => doc.ResolvePageCount());
        List<string> seen = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            seen.Add(doc.GetPage(i).GetText());
        }
        Assert.Equal(n, await resolve);
        Assert.True(doc.IsPageCountFinal);
        Assert.Equal(expected.Take(5), seen);
        Assert.Equal(expected[n - 1], doc.GetPage(n - 1).GetText());
    }

    [Fact]
    public void Cancellation_stops_resolving()
    {
        string text = string.Join("\n", Enumerable.Range(1, 20000).Select(i => "Line " + i));
        using LaidOutDocument doc = DocumentLayout.OpenText(text, null, Flow.Options());
        using CancellationTokenSource cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => doc.ResolvePageCount(cts.Token));
        Assert.False(doc.IsPageCountFinal);
        Assert.True(doc.ResolvePageCount() > 100);
    }

    [Fact]
    public void Untrusted_limits_are_tighter_than_the_defaults()
    {
        LayoutLimits d = new LayoutLimits();
        LayoutLimits u = LayoutLimits.ForUntrustedInput();
        Assert.True(u.MaxPages < d.MaxPages && u.MaxSourceBytes < d.MaxSourceBytes && u.MaxLayoutBytes < d.MaxLayoutBytes);
        LayoutOptions o = Flow.Options();
        o.Limits = u;
        o.Limits.MaxPages = 3;
        string text = string.Join("\n", Enumerable.Range(1, 1000).Select(i => "Line " + i));
        using LaidOutDocument doc = DocumentLayout.OpenText(text, null, o);
        LayoutLimitExceededException ex = Assert.Throws<LayoutLimitExceededException>(() => doc.ResolvePageCount());
        Assert.Equal("MaxPages", ex.LimitName);
    }
}
