using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.Tests;

internal static class Tbl
{
    public static readonly BorderLine Thin = new BorderLine(0.5, LayoutColor.Black);

    public static FlowTableCell Cell(string text, int span = 1, VerticalMerge merge = VerticalMerge.None)
    {
        FlowTableCell c = new FlowTableCell { ColumnSpan = span, VerticalMerge = merge };
        if (text.Length > 0 || merge != VerticalMerge.Continue)
        {
            c.Blocks.Add(Flow.P(text));
        }
        return c;
    }

    public static FlowTableRow Row(params string[] cells)
    {
        FlowTableRow r = new FlowTableRow();
        foreach (string c in cells)
        {
            r.Cells.Add(Cell(c));
        }
        return r;
    }

    public static FlowTable Table(double[] widths, params FlowTableRow[] rows)
    {
        FlowTable t = new FlowTable
        {
            Format = new TableFormat
            {
                BorderTop = Thin, BorderBottom = Thin, BorderLeft = Thin, BorderRight = Thin,
                BorderInsideHorizontal = Thin, BorderInsideVertical = Thin,
            },
        };
        foreach (double w in widths)
        {
            t.ColumnWidths.Add(w);
        }
        foreach (FlowTableRow r in rows)
        {
            t.Rows.Add(r);
        }
        return t;
    }

    public static List<LayoutBox> Cells(LaidOutPage p) => p.Boxes.Where(b => b.Kind == LayoutBoxKind.TableCell).ToList();
}

public class FlowTableTests
{
    private static readonly double Lh = Flow.LineHeight("Carlito-Regular.ttf", 11);

    [Fact]
    public void Simple_table_lays_out_cells_on_the_grid()
    {
        FlowTable t = Tbl.Table(new[] { 100.0, 150, 200 }, Tbl.Row("a", "b", "c"), Tbl.Row("d", "e", "f"));
        using LaidOutDocument doc = DocumentLayout.Open(Flow.Doc(t, Flow.P("after")), Flow.Options());
        LaidOutPage p = doc.GetPage(0);
        List<LayoutBox> cells = Tbl.Cells(p);
        Assert.Equal(6, cells.Count);
        // Each row makes room for the border above it (0.5 pt); the last row also for the bottom border.
        double b = Tbl.Thin.Width;
        Assert.Equal(new LayoutRect(72, 72, 100, Lh + b), cells[0].Rect);
        Assert.Equal(new LayoutRect(172, 72, 150, Lh + b), cells[1].Rect);
        Assert.Equal(72 + Lh + b, cells[3].Rect.Y, 6);
        Assert.Equal(Lh + b + b, cells[3].Rect.Height, 6);
        Assert.Equal("a\tb\tc\nd\te\tf\nafter", p.GetText());
        // Text sits inside the default left cell margin (5.4 pt).
        Assert.Equal(72 + 5.4, Flow.Runs(p).Single(r => r.Text == "a").X, 6);
        // The paragraph after the table starts below it.
        Assert.Equal(72 + (2 * Lh) + (3 * b), Flow.Lines(p).Single(l => l.Path.StartsWith("1/2", StringComparison.Ordinal)).Rect.Y, 6);
        // The first line of a cell starts below the border above its row.
        Assert.Equal(72 + b, Flow.Lines(p).First().Rect.Y, 6);
        Assert.True(p.DisplayList.OfType<StrokeLineOp>().Count() >= 10);
    }

    [Fact]
    public void Missing_grid_widths_share_the_available_width_and_spans_add_up()
    {
        FlowTableRow top = new FlowTableRow();
        top.Cells.Add(Tbl.Cell("wide", span: 2));
        top.Cells.Add(Tbl.Cell("x"));
        FlowTable t = Tbl.Table(Array.Empty<double>(), top, Tbl.Row("1", "2", "3"));
        using LaidOutDocument doc = DocumentLayout.Open(Flow.Doc(t), Flow.Options());
        List<LayoutBox> cells = Tbl.Cells(doc.GetPage(0));
        double share = 451.3 / 3;
        Assert.Equal(2 * share, cells[0].Rect.Width, 4);
        Assert.Equal(share, cells[1].Rect.Width, 4);
    }

    [Fact]
    public void Vertical_merge_spans_rows_and_grows_the_last_one()
    {
        FlowTableRow r1 = new FlowTableRow();
        FlowTableCell merged = new FlowTableCell { VerticalMerge = VerticalMerge.Restart };
        merged.Blocks.Add(Flow.P("one\ntwo\nthree\nfour"));
        r1.Cells.Add(merged);
        r1.Cells.Add(Tbl.Cell("r1"));
        FlowTableRow r2 = new FlowTableRow();
        r2.Cells.Add(Tbl.Cell(string.Empty, merge: VerticalMerge.Continue));
        r2.Cells.Add(Tbl.Cell("r2"));
        FlowTable t = Tbl.Table(new[] { 100.0, 100 }, r1, r2);
        using LaidOutDocument doc = DocumentLayout.Open(Flow.Doc(t), Flow.Options());
        LaidOutPage p = doc.GetPage(0);
        List<LayoutBox> cells = Tbl.Cells(p);
        Assert.Equal(3, cells.Count);
        // Four lines plus the top and bottom borders (0.5 pt each); the second row grows to fit them.
        double b = Tbl.Thin.Width;
        Assert.Equal((4 * Lh) + (2 * b), cells[0].Rect.Height, 4);
        Assert.Equal(Lh + b, cells[1].Rect.Height, 4);
        Assert.Equal((3 * Lh) + b, cells[2].Rect.Height, 4);
    }

    [Fact]
    public void Row_height_rules()
    {
        FlowTableRow exact = Tbl.Row("clipped text\nsecond line");
        exact.Height = 10;
        exact.HeightRule = RowHeightRule.Exact;
        FlowTableRow atLeast = Tbl.Row("x");
        atLeast.Height = 40;
        atLeast.HeightRule = RowHeightRule.AtLeast;
        FlowTable t = Tbl.Table(new[] { 200.0 }, exact, atLeast);
        using LaidOutDocument doc = DocumentLayout.Open(Flow.Doc(t), Flow.Options());
        LaidOutPage p = doc.GetPage(0);
        List<LayoutBox> cells = Tbl.Cells(p);
        Assert.Equal(10, cells[0].Rect.Height, 6);
        Assert.Equal(40, cells[1].Rect.Height, 6);
        Assert.Contains(p.DisplayList.OfType<PushClipOp>(), c => Math.Abs(c.Rect.Height - 10) < 0.001);
    }

    [Fact]
    public void Long_table_repeats_header_rows_on_each_page()
    {
        FlowTableRow head = Tbl.Row("Code", "Name");
        head.IsHeader = true;
        List<FlowTableRow> rows = new List<FlowTableRow> { head };
        for (int i = 1; i <= 120; i++)
        {
            rows.Add(Tbl.Row("C" + i, "Item " + i));
        }
        FlowTable t = Tbl.Table(new[] { 100.0, 200 }, rows.ToArray());
        using LaidOutDocument doc = DocumentLayout.Open(Flow.Doc(t), Flow.Options());
        int n = doc.ResolvePageCount();
        Assert.True(n >= 3);
        for (int i = 1; i < n; i++)
        {
            Assert.StartsWith("Code\tName\n", doc.GetPage(i).GetText(), StringComparison.Ordinal);
        }
        string all = string.Concat(Enumerable.Range(0, n).Select(i => doc.GetPage(i).GetText() + "\n"));
        for (int i = 1; i <= 120; i++)
        {
            Assert.Contains("C" + i + "\tItem " + i + "\n", all, StringComparison.Ordinal);
        }
        Assert.Equal(1, all.Split('\n').Count(l => l == "C60\tItem 60"));
    }

    [Fact]
    public void Tall_row_splits_across_pages_and_cant_split_rows_move()
    {
        List<FlowBlock> blocks = Enumerable.Range(1, 40).Select(i => (FlowBlock)Flow.P("filler " + i)).ToList();
        string tall = string.Join("\n", Enumerable.Range(1, 20).Select(i => "cell line " + i));
        FlowTable t = Tbl.Table(new[] { 300.0 }, Tbl.Row(tall));
        blocks.Add(t);
        using LaidOutDocument doc = DocumentLayout.Open(Flow.Doc(blocks.ToArray()), Flow.Options());
        Assert.Equal(2, doc.ResolvePageCount());
        Assert.Contains("cell line 1", doc.GetPage(0).GetText(), StringComparison.Ordinal);
        Assert.Contains("cell line 20", doc.GetPage(1).GetText(), StringComparison.Ordinal);

        FlowTableRow keep = Tbl.Row(tall);
        keep.CantSplit = true;
        List<FlowBlock> blocks2 = Enumerable.Range(1, 40).Select(i => (FlowBlock)Flow.P("filler " + i)).ToList();
        blocks2.Add(Tbl.Table(new[] { 300.0 }, keep));
        using LaidOutDocument doc2 = DocumentLayout.Open(Flow.Doc(blocks2.ToArray()), Flow.Options());
        Assert.DoesNotContain("cell line", doc2.GetPage(0).GetText(), StringComparison.Ordinal);
        Assert.StartsWith("cell line 1", doc2.GetPage(1).GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Nested_tables_and_cell_alignment_and_centred_tables()
    {
        FlowTable inner = Tbl.Table(new[] { 60.0, 60 }, Tbl.Row("i1", "i2"));
        FlowTableCell outerCell = new FlowTableCell { VerticalAlignment = CellVerticalAlignment.Center };
        outerCell.Blocks.Add(inner);
        FlowTableRow row = new FlowTableRow();
        row.Cells.Add(outerCell);
        FlowTableCell side = new FlowTableCell { VerticalAlignment = CellVerticalAlignment.Bottom };
        side.Blocks.Add(Flow.P("bottom"));
        row.Cells.Add(side);
        row.Height = 60;
        row.HeightRule = RowHeightRule.AtLeast;
        FlowTable outer = Tbl.Table(new[] { 150.0, 100 }, row);
        outer.Format = outer.Format with { Alignment = ParagraphAlignment.Center };
        using LaidOutDocument doc = DocumentLayout.Open(Flow.Doc(outer), Flow.Options());
        LaidOutPage p = doc.GetPage(0);
        LayoutBox outerBox = p.Boxes.First(b => b.Kind == LayoutBoxKind.Table);
        Assert.Equal(72 + ((451.3 - 250) / 2), outerBox.Rect.X, 4);
        Assert.Equal(2, p.Boxes.Count(b => b.Kind == LayoutBoxKind.Table));
        GlyphRunOp bottom = Flow.Runs(p).Single(r => r.Text == "bottom");
        Assert.True(bottom.Y > 72 + 40, "bottom-aligned text sits low in the 60 pt row");
        Assert.Contains("i1", p.GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Nested_table_splits_between_its_rows_across_pages()
    {
        FlowTable inner = Tbl.Table(new[] { 100.0, 100 }, Enumerable.Range(1, 90).Select(i => Tbl.Row("n" + i, "v" + i)).ToArray());
        FlowTableCell outerCell = new FlowTableCell();
        outerCell.Blocks.Add(Flow.P("before nested"));
        outerCell.Blocks.Add(inner);
        outerCell.Blocks.Add(Flow.P("after nested"));
        FlowTableRow row = new FlowTableRow();
        row.Cells.Add(outerCell);
        FlowTable outer = Tbl.Table(new[] { 300.0 }, row);
        using LaidOutDocument doc = DocumentLayout.Open(Flow.Doc(Flow.P("intro"), outer, Flow.P("end")), Flow.Options());
        int pages = doc.ResolvePageCount();
        Assert.True(pages >= 2, $"pages: {pages}");
        Assert.DoesNotContain(doc.Warnings, w => w.Code == "ContentTallerThanPage");
        List<string> seen = new List<string>();
        for (int i = 0; i < pages; i++)
        {
            LaidOutPage p = doc.GetPage(i);
            foreach (LayoutBox cell in p.Boxes.Where(b => b.Kind == LayoutBoxKind.TableCell))
            {
                Assert.True(cell.Rect.Bottom <= p.HeightPt - 72 + 0.01, $"cell below the body on page {i + 1}");
            }
            seen.AddRange(p.GetText().Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries).Where(w => w.StartsWith('n') && w.Length > 1 && char.IsDigit(w[1])));
            if (i < pages - 1)
            {
                Assert.Equal(2, p.Boxes.Count(b => b.Kind == LayoutBoxKind.Table));
            }
        }
        Assert.Equal(Enumerable.Range(1, 90).Select(i => "n" + i), seen);
        // The first page is filled: the nested table did not move whole to page 2.
        Assert.Contains("n1", doc.GetPage(0).GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Cell_shading_is_drawn_behind_text()
    {
        FlowTableRow r = Tbl.Row("shaded");
        r.Cells[0].Background = new LayoutColor(0xEE, 0xEE, 0xEE);
        using LaidOutDocument doc = DocumentLayout.Open(Flow.Doc(Tbl.Table(new[] { 120.0 }, r)), Flow.Options());
        LaidOutPage p = doc.GetPage(0);
        List<LayoutOp> ops = p.DisplayList.ToList();
        int fill = ops.FindIndex(o => o is FillRectOp);
        int text = ops.FindIndex(o => o is GlyphRunOp);
        Assert.True(fill >= 0 && fill < text);
    }

    [Fact]
    public void Nesting_depth_is_limited()
    {
        FlowTable t = Tbl.Table(new[] { 100.0 }, Tbl.Row("leaf"));
        for (int i = 0; i < 5; i++)
        {
            FlowTableCell c = new FlowTableCell();
            c.Blocks.Add(t);
            FlowTableRow r = new FlowTableRow();
            r.Cells.Add(c);
            t = Tbl.Table(new[] { 400.0 }, r);
        }
        LayoutOptions o = Flow.Options();
        o.Limits.MaxNestingDepth = 3;
        using LaidOutDocument doc = DocumentLayout.Open(Flow.Doc(t), o);
        Assert.Throws<LayoutLimitExceededException>(() => doc.GetPage(0));
    }
}
