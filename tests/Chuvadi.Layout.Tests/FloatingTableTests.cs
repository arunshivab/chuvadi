using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Model;
using Xunit;

namespace Chuvadi.Layout.Tests;

/// <summary>Floating tables (FlowTable.Floating): position, wrapping, splitting and overlap.</summary>
public sealed class FloatingTableTests
{
    private const double PageWidth = 595.3;
    private const double PageHeight = 841.9;

    private static string Words(int seed, int n)
    {
        string[] w = "admission approval assurance beam calibration clinical consultant daily department discharge dose imaging insurance invoice laboratory machine nursing patient pharmacy planning quality radiation report results review schedule summary theatre treatment ward".Split(' ');
        return string.Join(' ', Enumerable.Range(0, n).Select(i => w[((seed * 7) + (i * 5) + (i / 3)) % w.Length]));
    }

    private static FlowTable Table(int rows, double width, FloatingTablePosition fp)
    {
        FlowTable t = new FlowTable { Floating = fp };
        t.ColumnWidths.Add(width);
        BorderLine thin = new BorderLine(0.75, LayoutColor.Black);
        for (int r = 0; r < rows; r++)
        {
            FlowTableRow row = new FlowTableRow();
            FlowTableCell c = new FlowTableCell { BorderTop = thin, BorderBottom = thin, BorderLeft = thin, BorderRight = thin };
            c.Blocks.Add(Flow.P("r" + (r + 1)));
            row.Cells.Add(c);
            t.Rows.Add(row);
        }
        return t;
    }

    private static LaidOutDocument Open(params FlowBlock[] blocks) => DocumentLayout.Open(Flow.Doc(blocks), Flow.Options());

    private static FlowBlock[] Text(string tag, int count) => Enumerable.Range(0, count).Select(i => (FlowBlock)Flow.P(tag + i + " " + Words(i, 30))).ToArray();

    private static List<LayoutBox> Tables(LaidOutPage p) => p.Boxes.Where(b => b.Kind == LayoutBoxKind.Table).OrderBy(b => b.Rect.Y).ToList();

    private static List<LayoutBox> BodyLines(LaidOutPage p) => Flow.Lines(p).Where(b => b.Path.Count(c => c == '/') == 1 && b.Rect.Width > 0).ToList();

    private static void NoOverlap(LaidOutPage p)
    {
        foreach (LayoutBox t in Tables(p))
        {
            foreach (LayoutBox l in BodyLines(p))
            {
                bool v = l.Rect.Y < t.Rect.Bottom - 0.5 && l.Rect.Bottom > t.Rect.Y + 0.5;
                bool h = l.Rect.X < t.Rect.Right - 0.5 && l.Rect.Right > t.Rect.X + 0.5;
                Assert.False(v && h, $"{l.Path} {l.Rect} overlaps {t.Rect}");
            }
        }
    }

    [Theory]
    [InlineData(TableHorizontalAlignment.Left, 72)]
    [InlineData(TableHorizontalAlignment.Center, 72 + ((451.3 - 100) / 2))]
    [InlineData(TableHorizontalAlignment.Right, 523.3 - 100)]
    [InlineData(TableHorizontalAlignment.Inside, 72)]
    [InlineData(TableHorizontalAlignment.Outside, 523.3 - 100)]
    public void Horizontal_alignment_is_relative_to_the_anchor(TableHorizontalAlignment align, double x)
    {
        FloatingTablePosition fp = new FloatingTablePosition { HorizontalAnchor = TableHorizontalAnchor.Margin, HorizontalAlignment = align, VerticalAnchor = TableVerticalAnchor.Paragraph };
        using LaidOutDocument doc = Open(Flow.P("before"), Table(2, 100, fp), Flow.P("after"));
        Assert.Equal(x, Assert.Single(Tables(doc.GetPage(0))).Rect.X, 2);
    }

    [Fact]
    public void Offsets_are_measured_from_the_page_by_default()
    {
        using LaidOutDocument doc = Open(Table(1, 100, new FloatingTablePosition { OffsetX = 50, OffsetY = 30 }), Flow.P("text"));
        LayoutBox t = Assert.Single(Tables(doc.GetPage(0)));
        Assert.Equal(50, t.Rect.X, 2);
        Assert.Equal(30, t.Rect.Y, 2);
    }

    [Theory]
    [InlineData(TableVerticalAlignment.Top)]
    [InlineData(TableVerticalAlignment.Center)]
    [InlineData(TableVerticalAlignment.Bottom)]
    public void Vertical_alignment_is_relative_to_the_margins(TableVerticalAlignment align)
    {
        FloatingTablePosition fp = new FloatingTablePosition { HorizontalAnchor = TableHorizontalAnchor.Margin, VerticalAnchor = TableVerticalAnchor.Margin, VerticalAlignment = align };
        using LaidOutDocument doc = Open(Table(3, 100, fp), Flow.P("text"));
        LayoutBox t = Assert.Single(Tables(doc.GetPage(0)));
        double top = 72, bottom = PageHeight - 72;
        double expected = align switch
        {
            TableVerticalAlignment.Top => top,
            TableVerticalAlignment.Center => top + ((bottom - top - t.Rect.Height) / 2),
            _ => bottom - t.Rect.Height,
        };
        Assert.Equal(expected, t.Rect.Y, 1);
    }

    [Fact]
    public void Text_flows_on_both_sides_at_least_the_distances_away()
    {
        FloatingTablePosition fp = new FloatingTablePosition
        {
            HorizontalAnchor = TableHorizontalAnchor.Margin, OffsetX = 180, VerticalAnchor = TableVerticalAnchor.Paragraph,
            DistanceLeft = 9, DistanceRight = 12,
        };
        using LaidOutDocument doc = Open(new FlowBlock[] { Table(4, 100, fp) }.Concat(Text("W", 4)).Concat(new FlowBlock[] { Flow.P("end") }).ToArray());
        LaidOutPage p = doc.GetPage(0);
        LayoutBox t = Assert.Single(Tables(p));
        Assert.Equal(252, t.Rect.X, 2);
        List<LayoutBox> beside = BodyLines(p).Where(l => l.Rect.Y < t.Rect.Bottom).ToList();
        Assert.Contains(beside, l => l.Rect.X >= t.Rect.Right + 12 - 0.01);
        Assert.All(beside.Where(l => l.Rect.X < t.Rect.X), l => Assert.True(l.Rect.Right <= t.Rect.X - 9 + 0.01));
        NoOverlap(p);
    }

    [Fact]
    public void A_paragraph_anchored_table_taller_than_the_space_left_continues_at_the_top_of_the_next_page()
    {
        FloatingTablePosition fp = new FloatingTablePosition { HorizontalAnchor = TableHorizontalAnchor.Margin, VerticalAnchor = TableVerticalAnchor.Paragraph };
        List<FlowBlock> blocks = Text("A", 12).ToList();
        blocks.Add(Table(60, 120, fp));
        blocks.AddRange(Text("B", 3));
        using LaidOutDocument doc = Open(blocks.ToArray());
        Assert.True(doc.ResolvePageCount() >= 2);
        LayoutBox first = Assert.Single(Tables(doc.GetPage(0)));
        LayoutBox rest = Assert.Single(Tables(doc.GetPage(1)));
        Assert.Equal(first.Rect.X, rest.Rect.X, 2);
        Assert.Equal(72, rest.Rect.Y, 1);
        int rows = Enumerable.Range(0, doc.ResolvePageCount()).Sum(i => doc.GetPage(i).Boxes.Count(b => b.Kind == LayoutBoxKind.Line && b.Path.Count(c => c == '/') > 1));
        Assert.Equal(60, rows); // every row once
        Assert.Contains("B0", doc.GetPage(1).GetText());
        NoOverlap(doc.GetPage(1));
    }

    [Fact]
    public void A_page_anchored_table_is_placed_exactly_and_never_split()
    {
        FloatingTablePosition fp = new FloatingTablePosition { VerticalAnchor = TableVerticalAnchor.Page, OffsetY = 800, HorizontalAnchor = TableHorizontalAnchor.Margin };
        using LaidOutDocument doc = Open(Flow.P("one"), Table(5, 100, fp), Flow.P("two"));
        Assert.Equal(1, doc.ResolvePageCount());
        LayoutBox t = Assert.Single(Tables(doc.GetPage(0)));
        Assert.Equal(800, t.Rect.Y, 1);
        Assert.True(t.Rect.Bottom > PageHeight);
    }

    [Fact]
    public void A_margin_anchored_table_above_its_paragraph_makes_the_text_above_flow_around_it()
    {
        FloatingTablePosition fp = new FloatingTablePosition { HorizontalAnchor = TableHorizontalAnchor.Margin, VerticalAnchor = TableVerticalAnchor.Margin, OffsetY = 0, DistanceRight = 9 };
        List<FlowBlock> blocks = Text("U", 6).ToList();
        blocks.Add(Table(3, 150, fp));
        blocks.Add(Flow.P("after"));
        using LaidOutDocument doc = Open(blocks.ToArray());
        LaidOutPage p = doc.GetPage(0);
        LayoutBox t = Assert.Single(Tables(p));
        Assert.Equal(72, t.Rect.Y, 1);
        Assert.Contains(BodyLines(p), l => l.Rect.Y < t.Rect.Bottom && Math.Abs(l.Rect.X - (72 + 150 + 0.75 + 9)) < 0.1);
        NoOverlap(p);
    }

    [Fact]
    public void Tables_may_overlap_unless_one_of_them_forbids_it()
    {
        FloatingTablePosition at = new FloatingTablePosition { HorizontalAnchor = TableHorizontalAnchor.Margin, VerticalAnchor = TableVerticalAnchor.Margin };
        using (LaidOutDocument doc = Open(Table(2, 100, at), Table(2, 100, at), Flow.P("text")))
        {
            List<LayoutBox> t = Tables(doc.GetPage(0));
            Assert.Equal(t[0].Rect.Y, t[1].Rect.Y, 2); // overlap allowed (not stated: allowed, 17.4.56)
        }
        using (LaidOutDocument doc = Open(Table(2, 100, at), Table(2, 100, at with { AllowOverlap = false }), Flow.P("text")))
        {
            List<LayoutBox> t = Tables(doc.GetPage(0));
            Assert.Equal(2, t.Count);
            Assert.True(t[1].Rect.Y >= t[0].Rect.Bottom - 0.01); // moved down until clear (decision B)
        }
    }

    [Fact]
    public void A_table_that_may_not_overlap_moves_down_to_the_stated_distance_below()
    {
        // N5: Word, calib-overlap O2: the later table moves down and keeps the upper table's bottomFromText (9 pt).
        FloatingTablePosition at = new FloatingTablePosition { HorizontalAnchor = TableHorizontalAnchor.Margin, VerticalAnchor = TableVerticalAnchor.Margin, DistanceBottom = 9 };
        using LaidOutDocument doc = Open(Table(2, 100, at), Table(2, 100, at with { AllowOverlap = false }), Flow.P("text"));
        List<LayoutBox> t = Tables(doc.GetPage(0));
        Assert.Equal(2, t.Count);
        Assert.Equal(t[0].Rect.Bottom + 9, t[1].Rect.Y, 2);
    }

    [Fact]
    public void A_table_anchored_to_the_column_follows_its_column()
    {
        FlowDocument d = new FlowDocument();
        FlowSection s = new FlowSection { ColumnCount = 2 };
        FlowParagraph brk = Flow.P("column 1");
        brk.Inlines.Add(new BreakInline(BreakKind.Column, Flow.Calibri11));
        s.Blocks.Add(brk);
        s.Blocks.Add(Table(2, 80, new FloatingTablePosition { HorizontalAnchor = TableHorizontalAnchor.Column, OffsetX = 15, VerticalAnchor = TableVerticalAnchor.Paragraph }));
        s.Blocks.Add(Flow.P("column 2 " + Words(3, 40)));
        d.Sections.Add(s);
        using LaidOutDocument doc = DocumentLayout.Open(d, Flow.Options());
        LayoutBox t = Assert.Single(Tables(doc.GetPage(0)));
        Assert.Equal(72 + ((451.3 - 36) / 2) + 36 + 15, t.Rect.X, 2);
    }

    [Fact]
    public void Pages_with_floating_tables_rebuild_identically_from_their_checkpoints()
    {
        FloatingTablePosition para = new FloatingTablePosition { HorizontalAnchor = TableHorizontalAnchor.Margin, OffsetX = 200, VerticalAnchor = TableVerticalAnchor.Paragraph, DistanceLeft = 9 };
        FloatingTablePosition margin = new FloatingTablePosition { HorizontalAnchor = TableHorizontalAnchor.Margin, VerticalAnchor = TableVerticalAnchor.Margin, OffsetY = 100 };
        List<FlowBlock> blocks = new List<FlowBlock>();
        for (int i = 0; i < 6; i++)
        {
            blocks.AddRange(Text("P" + i + "-", 5));
            blocks.Add(Table(4 + (i * 6), 120, i % 2 == 0 ? para : margin));
        }
        using LaidOutDocument doc = Open(blocks.ToArray());
        int n = doc.ResolvePageCount();
        List<string> first = Enumerable.Range(0, n).Select(i => doc.GetPage(i).GetText()).ToList();
        for (int i = n - 1; i >= 0; i--)
        {
            Assert.Equal(first[i], doc.GetPage(i).GetText());
            NoOverlap(doc.GetPage(i));
        }
    }
}
