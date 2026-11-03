using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Engine;
using Chuvadi.Layout.Model;
using Xunit;

namespace Chuvadi.Layout.Tests;

/// <summary>
/// Newspaper columns (w:cols). Positions come from the file (ECMA-376 17.6.4); where the standard is silent the rules
/// follow Word as measured with calib-columns.docx (balancing, separator extent, column-break mark, footnotes).
/// </summary>
public sealed class ColumnTests
{
    private const double Left = 72;
    private const double Content = 595.3 - 144; // A4 text width with 1 in margins
    private static readonly CharacterFormat F = Flow.Calibri11;

    private static string Words(int seed, int n)
    {
        string[] w = "admission approval assurance beam calibration clinical consultant daily department discharge dose imaging insurance invoice laboratory machine nursing patient pharmacy planning quality radiation report results review schedule summary theatre treatment ward".Split(' ');
        return string.Join(' ', Enumerable.Range(0, n).Select(i => w[((seed * 7) + (i * 5) + (i / 3)) % w.Length]));
    }

    private static FlowSection Section(int columns, SectionStart start = SectionStart.NextPage, params FlowBlock[] blocks)
    {
        FlowSection s = new FlowSection { ColumnCount = columns, Start = start };
        foreach (FlowBlock b in blocks)
        {
            s.Blocks.Add(b);
        }
        return s;
    }

    private static FlowBlock[] Paras(string tag, int count, int words)
        => Enumerable.Range(0, count).Select(i => (FlowBlock)Flow.P(tag + i.ToString("00") + " " + Words(i, words))).ToArray();

    private static LaidOutDocument Open(params FlowSection[] sections)
    {
        FlowDocument d = new FlowDocument();
        foreach (FlowSection s in sections)
        {
            d.Sections.Add(s);
        }
        return DocumentLayout.Open(d, Flow.Options());
    }

    private static List<LayoutBox> InColumn(LaidOutPage p, double colLeft, double colWidth)
        => Flow.Lines(p).Where(b => b.Rect.X >= colLeft - 0.5 && b.Rect.X < colLeft + colWidth).OrderBy(b => b.Rect.Y).ToList();

    private static List<StrokeLineOp> Separators(LaidOutPage p)
        => p.DisplayList.OfType<StrokeLineOp>().Where(o => Math.Abs(o.X1 - o.X2) < 1e-9 && Math.Abs(o.Width - 0.75) < 1e-9).ToList();

    [Fact]
    public void Two_equal_columns_fill_left_then_right_with_the_default_gap()
    {
        using LaidOutDocument doc = Open(Section(2, SectionStart.NextPage, Paras("K", 40, 28)));
        Assert.True(doc.ResolvePageCount() >= 2);
        LaidOutPage p = doc.GetPage(0);
        double w = (Content - 36) / 2;
        List<LayoutBox> c1 = InColumn(p, Left, w);
        List<LayoutBox> c2 = InColumn(p, Left + w + 36, w);
        Assert.NotEmpty(c1);
        Assert.NotEmpty(c2);
        Assert.Equal(c1.Count + c2.Count, Flow.Lines(p).Count);
        Assert.All(c1, b => Assert.True(b.Rect.Right <= Left + w + 0.01));
        Assert.Equal(Left + w + 36, c2.Min(b => b.Rect.X), 2);
        // Column 1 is full (its last line reaches the foot of the body) before column 2 starts at the top.
        Assert.True(c1[^1].Rect.Bottom > 769.9 - (3 * 13.43));
        Assert.Equal(c1[0].Rect.Y, c2[0].Rect.Y, 2);
        Assert.DoesNotContain(doc.Warnings, x => x.Code == LayoutWarningCodes.MultiColumnNotSupported);
    }

    [Fact]
    public void Unequal_columns_take_the_widths_and_gaps_of_the_file()
    {
        FlowSection s = Section(1, SectionStart.NextPage, Paras("U", 40, 22));
        s.ColumnWidths.Add(new FlowColumn(100, 20));
        s.ColumnWidths.Add(new FlowColumn(125, 30));
        s.ColumnWidths.Add(new FlowColumn(176.3, 0));
        using LaidOutDocument doc = Open(s);
        LaidOutPage p = doc.GetPage(0);
        Assert.NotEmpty(InColumn(p, 72, 100));
        Assert.Equal(192, InColumn(p, 192, 125).Min(b => b.Rect.X), 2);
        Assert.Equal(347, InColumn(p, 347, 176.3).Min(b => b.Rect.X), 2);
        Assert.All(InColumn(p, 192, 125), b => Assert.True(b.Rect.Right <= 317 + 0.01));
    }

    [Fact]
    public void A_paragraph_split_between_columns_of_different_widths_keeps_every_word_once()
    {
        FlowSection s = Section(1, SectionStart.NextPage, Paras("W", 60, 40));
        s.ColumnWidths.Add(new FlowColumn(100, 20));
        s.ColumnWidths.Add(new FlowColumn(331.3, 0));
        using LaidOutDocument doc = Open(s);
        string all = string.Concat(Enumerable.Range(0, doc.ResolvePageCount()).Select(i => doc.GetPage(i).GetText()));
        string expected = string.Concat(Enumerable.Range(0, 60).Select(i => "W" + i.ToString("00") + " " + Words(i, 40)));
        Assert.Equal(expected.Replace(" ", ""), all.Replace("\n", "").Replace(" ", ""));
    }

    [Fact]
    public void The_separator_is_centred_in_the_gap_and_ends_at_the_longest_column()
    {
        FlowSection s = Section(2, SectionStart.NextPage, Paras("S", 34, 28));
        s.ColumnSeparator = true;
        using LaidOutDocument doc = Open(s);
        double w = (Content - 36) / 2;
        for (int i = 0; i < doc.ResolvePageCount(); i++)
        {
            LaidOutPage p = doc.GetPage(i);
            if (InColumn(p, Left + w + 36, w).Count == 0)
            {
                Assert.Empty(Separators(p)); // nothing in column 2: no line before it
                continue;
            }
            StrokeLineOp rule = Assert.Single(Separators(p));
            Assert.Equal(Left + w + 18, rule.X1, 2);
            Assert.Equal(72, rule.Y1, 2);
            double longest = Flow.Lines(p).Max(b => b.Rect.Bottom);
            Assert.Equal(longest, rule.Y2, 2);
        }
    }

    [Fact]
    public void No_separator_before_an_empty_column()
    {
        FlowSection s = Section(3, SectionStart.NextPage, Paras("E", 2, 10));
        s.ColumnSeparator = true;
        using LaidOutDocument doc = Open(s);
        Assert.Empty(Separators(doc.GetPage(0)));
    }

    [Fact]
    public void A_column_break_moves_to_the_next_column_and_carries_the_paragraph_mark()
    {
        FlowParagraph brk = Flow.P("before the break");
        brk.Inlines.Add(new BreakInline(BreakKind.Column, F));
        using LaidOutDocument doc = Open(Section(2, SectionStart.NextPage, Flow.P("first"), brk, Flow.P("after the break")));
        LaidOutPage p = doc.GetPage(0);
        double w = (Content - 36) / 2;
        List<LayoutBox> c1 = InColumn(p, Left, w);
        Assert.Equal(2, c1.Count);
        // Column 2 starts with the empty line of the break paragraph's mark (Word, calibration K2 and K6), then the text.
        Assert.Contains("after the break", p.GetText());
        List<LayoutBox> c2 = InColumn(p, Left + w + 36, w);
        Assert.Equal(2, c2.Count);
        Assert.True(c2[1].Rect.Y > c2[0].Rect.Y);
        Assert.Equal(1, doc.ResolvePageCount());
    }

    [Fact]
    public void A_column_break_in_the_last_column_starts_a_new_page()
    {
        FlowParagraph b1 = Flow.P("one");
        b1.Inlines.Add(new BreakInline(BreakKind.Column, F));
        FlowParagraph b2 = Flow.P("two");
        b2.Inlines.Add(new BreakInline(BreakKind.Column, F));
        using LaidOutDocument doc = Open(Section(2, SectionStart.NextPage, b1, b2, Flow.P("three")));
        Assert.Equal(2, doc.ResolvePageCount());
        Assert.Contains("three", doc.GetPage(1).GetText());
    }

    [Fact]
    public void A_column_break_in_one_column_is_a_page_break_as_before()
    {
        FlowParagraph b = Flow.P("one");
        b.Inlines.Add(new BreakInline(BreakKind.Column, F));
        using LaidOutDocument doc = Open(Section(1, SectionStart.NextPage, b, Flow.P("two")));
        Assert.Equal(2, doc.ResolvePageCount());
        Assert.StartsWith("two", doc.GetPage(1).GetText().TrimStart('\n'));
    }

    [Fact]
    public void Columns_before_a_continuous_break_are_balanced_and_the_next_section_starts_below()
    {
        FlowSection cols = Section(2, SectionStart.NextPage, Paras("B", 5, 24));
        FlowSection after = Section(1, SectionStart.Continuous, Flow.P("after the columns"));
        using LaidOutDocument doc = Open(cols, after);
        LaidOutPage p = doc.GetPage(0);
        double w = (Content - 36) / 2;
        List<LayoutBox> c1 = InColumn(p, Left, w).Where(b => b.Path.StartsWith("1/", StringComparison.Ordinal)).ToList();
        List<LayoutBox> c2 = InColumn(p, Left + w + 36, w);
        Assert.True(Math.Abs(c1.Count - c2.Count) <= 1, $"{c1.Count} and {c2.Count} lines");
        LayoutBox next = Flow.Lines(p).Single(b => b.Path.StartsWith("2/", StringComparison.Ordinal));
        Assert.True(next.Rect.Y >= Math.Max(c1[^1].Rect.Bottom, c2[^1].Rect.Bottom) - 0.01);
        Assert.Equal(1, doc.ResolvePageCount());
    }

    [Fact]
    public void Balancing_can_be_switched_off()
    {
        FlowSection cols = Section(2, SectionStart.NextPage, Paras("B", 5, 24));
        cols.BalanceColumns = false;
        using LaidOutDocument doc = Open(cols, Section(1, SectionStart.Continuous, Flow.P("after")));
        LaidOutPage p = doc.GetPage(0);
        double w = (Content - 36) / 2;
        Assert.DoesNotContain(InColumn(p, Left + w + 36, w), b => b.Path.StartsWith("1/", StringComparison.Ordinal));
    }

    [Fact]
    public void Columns_before_a_next_page_break_are_not_balanced()
    {
        using LaidOutDocument doc = Open(Section(2, SectionStart.NextPage, Paras("N", 5, 24)), Section(1, SectionStart.NextPage, Flow.P("next")));
        LaidOutPage p = doc.GetPage(0);
        double w = (Content - 36) / 2;
        Assert.Empty(InColumn(p, Left + w + 36, w));
    }

    [Fact]
    public void A_next_column_section_starts_at_the_top_of_the_next_column()
    {
        using LaidOutDocument doc = Open(Section(2, SectionStart.NextPage, Flow.P("first section")), Section(2, SectionStart.NextColumn, Flow.P("second section")));
        LaidOutPage p = doc.GetPage(0);
        double w = (Content - 36) / 2;
        LayoutBox second = Assert.Single(InColumn(p, Left + w + 36, w));
        Assert.Equal(InColumn(p, Left, w)[0].Rect.Y, second.Rect.Y, 2);
        Assert.Equal(1, doc.ResolvePageCount());
    }

    [Fact]
    public void Columns_narrower_than_a_point_warn_and_still_lay_out()
    {
        FlowSection s = Section(40, SectionStart.NextPage, Flow.P("x"));
        s.ColumnSpacing = 36;
        using LaidOutDocument doc = Open(s);
        Assert.True(doc.ResolvePageCount() >= 1);
        Assert.Contains(doc.Warnings, x => x.Code == LayoutWarningCodes.ColumnTooNarrow);
    }

    [Fact]
    public void Pages_with_columns_rebuild_identically_from_their_checkpoints()
    {
        FlowSection s = Section(3, SectionStart.NextPage, Paras("R", 80, 30));
        s.ColumnSeparator = true;
        using LaidOutDocument doc = Open(s, Section(2, SectionStart.Continuous, Paras("Q", 6, 20)));
        int n = doc.ResolvePageCount();
        Assert.True(n >= 2);
        List<string> first = Enumerable.Range(0, n).Select(i => doc.GetPage(i).GetText()).ToList();
        for (int i = n - 1; i >= 0; i--)
        {
            Assert.Equal(first[i], doc.GetPage(i).GetText());
        }
    }

    [Fact]
    public void A_table_without_column_widths_takes_the_width_of_its_column()
    {
        FlowTable t = new FlowTable { Format = new TableFormat { GridFromLayoutWidth = true, PreferredWidthPercent = 100 } };
        t.ColumnWidths.Add(225.65);
        t.ColumnWidths.Add(225.65);
        FlowTableRow r = new FlowTableRow();
        foreach (string text in new[] { "a", "b" })
        {
            FlowTableCell c = new FlowTableCell();
            c.Blocks.Add(Flow.P(text));
            r.Cells.Add(c);
        }
        t.Rows.Add(r);
        using LaidOutDocument doc = Open(Section(2, SectionStart.NextPage, t));
        LaidOutPage p = doc.GetPage(0);
        LayoutBox table = p.Boxes.First(b => b.Kind == LayoutBoxKind.Table);
        Assert.Equal((Content - 36) / 2, table.Rect.Width, 1);
    }
}
