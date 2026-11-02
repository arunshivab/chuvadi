using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.Tests;

/// <summary>Text flowing beside square, tight and through floating objects (L11).</summary>
public class FloatWrapTests
{
    private static readonly LayoutImage Png = new LayoutImage(new byte[] { 0x89, 0x50 }, LayoutImageFormat.Png, 10, 10);

    // A4 with 72 pt margins: text area x in [72, 523.3].
    private const double AreaLeft = 72;
    private const double AreaRight = 595.3 - 72;

    private static readonly string Long = string.Join(" ", Enumerable.Range(1, 220).Select(i => "word" + i));

    private static FlowParagraph Anchored(AnchoredImageInline a, string text)
    {
        FlowParagraph p = Flow.P(text);
        p.Inlines.Insert(0, a);
        return p;
    }

    private static (LaidOutPage Page, ImageOp Image, List<LayoutBox> Lines, LaidOutDocument Doc) Lay(AnchoredImageInline a, string? text = null)
    {
        LaidOutDocument doc = DocumentLayout.Open(Flow.Doc(Anchored(a, text ?? Long)), Flow.Options());
        LaidOutPage p = doc.GetPage(0);
        return (p, Assert.Single(p.DisplayList.OfType<ImageOp>()), Flow.Lines(p), doc);
    }

    private static List<string> Words(string text)
        => text.Split(new[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries).Where(w => w.StartsWith("word", StringComparison.Ordinal)).ToList();

    private static bool Beside(LayoutBox line, LayoutRect img) => line.Rect.Y < img.Bottom && line.Rect.Bottom > img.Y;

    [Fact]
    public void Square_left_image_text_starts_after_the_image_and_returns_below_it()
    {
        AnchoredImageInline a = new AnchoredImageInline(Png, 100, 60, Flow.Calibri11) { Wrap = FloatWrap.Square, HorizontalAlignment = FloatAlignment.Start };
        (LaidOutPage _, ImageOp img, List<LayoutBox> lines, LaidOutDocument doc) = Lay(a);
        using (doc)
        {
            List<LayoutBox> beside = lines.Where(l => Beside(l, img.Destination)).ToList();
            List<LayoutBox> below = lines.Where(l => l.Rect.Y >= img.Destination.Bottom).ToList();
            Assert.True(beside.Count >= 3, $"lines beside the image: {beside.Count}");
            Assert.All(beside, l => Assert.Equal(AreaLeft + 100 + 9, l.Rect.X, 3));
            Assert.NotEmpty(below);
            Assert.All(below, l => Assert.Equal(AreaLeft, l.Rect.X, 3));
            Assert.DoesNotContain(doc.Warnings, w => w.Code == "TextWrapApproximated");
            Assert.Equal(72, lines[0].Rect.Y, 3);
        }
    }

    [Fact]
    public void Square_right_image_text_stops_before_the_image()
    {
        AnchoredImageInline a = new AnchoredImageInline(Png, 120, 60, Flow.Calibri11) { Wrap = FloatWrap.Square, HorizontalAlignment = FloatAlignment.End, DistanceLeft = 12 };
        (LaidOutPage _, ImageOp img, List<LayoutBox> lines, LaidOutDocument doc) = Lay(a);
        using (doc)
        {
            Assert.Equal(AreaRight - 120, img.Destination.X, 3);
            List<LayoutBox> beside = lines.Where(l => Beside(l, img.Destination)).ToList();
            Assert.NotEmpty(beside);
            Assert.All(beside, l => Assert.True(l.Rect.Right <= img.Destination.X - 12 + 0.01, $"line right {l.Rect.Right}"));
            Assert.Contains(lines, l => l.Rect.Y >= img.Destination.Bottom && l.Rect.Right > img.Destination.X);
        }
    }

    [Fact]
    public void Square_both_sides_fills_left_then_right_on_the_same_row()
    {
        AnchoredImageInline a = new AnchoredImageInline(Png, 80, 50, Flow.Calibri11) { Wrap = FloatWrap.Square, HorizontalAlignment = FloatAlignment.Center };
        (LaidOutPage page, ImageOp img, List<LayoutBox> lines, LaidOutDocument doc) = Lay(a);
        using (doc)
        {
            LayoutRect r = img.Destination;
            List<IGrouping<double, LayoutBox>> rows = lines.Where(l => Beside(l, r)).GroupBy(l => Math.Round(l.Rect.Y, 3)).ToList();
            Assert.NotEmpty(rows);
            foreach (IGrouping<double, LayoutBox> row in rows)
            {
                List<LayoutBox> pieces = row.OrderBy(l => l.Rect.X).ToList();
                Assert.Equal(2, pieces.Count);
                Assert.True(pieces[0].Rect.Right <= r.X - 9 + 0.01);
                Assert.Equal(r.Right + 9, pieces[1].Rect.X, 3);
            }
            // Reading order: the left piece comes before the right piece in the page text.
            List<string> words = Words(page.GetText());
            Assert.Equal(Enumerable.Range(1, words.Count).Select(i => "word" + i), words);
        }
    }

    [Theory]
    [InlineData(FloatWrapSide.Left)]
    [InlineData(FloatWrapSide.Right)]
    [InlineData(FloatWrapSide.Largest)]
    public void Wrap_side_limits_text_to_one_side(FloatWrapSide side)
    {
        // Image left of centre: the right side is the larger.
        AnchoredImageInline a = new AnchoredImageInline(Png, 80, 50, Flow.Calibri11) { Wrap = FloatWrap.Square, WrapSide = side, OffsetX = 120 };
        (LaidOutPage _, ImageOp img, List<LayoutBox> lines, LaidOutDocument doc) = Lay(a);
        using (doc)
        {
            LayoutRect r = img.Destination;
            List<LayoutBox> beside = lines.Where(l => Beside(l, r)).ToList();
            Assert.NotEmpty(beside);
            if (side == FloatWrapSide.Left)
            {
                Assert.All(beside, l => Assert.True(l.Rect.Right <= r.X - 9 + 0.01));
            }
            else
            {
                Assert.All(beside, l => Assert.True(l.Rect.X >= r.Right + 9 - 0.01));
            }
            Assert.Equal(beside.Count, beside.Select(l => Math.Round(l.Rect.Y, 3)).Distinct().Count());
        }
    }

    [Fact]
    public void Tight_polygon_lets_lines_move_left_as_the_contour_narrows()
    {
        // A triangle: full width at the top, a point at the bottom-left. Text beside it starts further left on lower rows.
        AnchoredImageInline a = new AnchoredImageInline(Png, 150, 90, Flow.Calibri11)
        {
            Wrap = FloatWrap.Tight,
            HorizontalAlignment = FloatAlignment.Start,
            DistanceRight = 6,
            WrapPolygon = new[] { new WrapPoint(0, 0), new WrapPoint(150, 0), new WrapPoint(0, 90) },
        };
        (LaidOutPage _, ImageOp img, List<LayoutBox> lines, LaidOutDocument doc) = Lay(a);
        using (doc)
        {
            List<LayoutBox> beside = lines.Where(l => Beside(l, img.Destination)).OrderBy(l => l.Rect.Y).ToList();
            Assert.True(beside.Count >= 4);
            for (int i = 1; i < beside.Count; i++)
            {
                Assert.True(beside[i].Rect.X < beside[i - 1].Rect.X, $"row {i}: {beside[i].Rect.X} vs {beside[i - 1].Rect.X}");
            }
            // The first row's top edge is where the triangle is widest: x(y) = 150 * (1 - y/90) at the row's top.
            Assert.Equal(AreaLeft + 150 + 6, beside[0].Rect.X, 3);
        }
    }

    [Fact]
    public void Through_is_laid_out_as_tight_with_a_warning()
    {
        AnchoredImageInline a = new AnchoredImageInline(Png, 100, 60, Flow.Calibri11) { Wrap = FloatWrap.Through, HorizontalAlignment = FloatAlignment.Start };
        (LaidOutPage _, ImageOp img, List<LayoutBox> lines, LaidOutDocument doc) = Lay(a);
        using (doc)
        {
            Assert.Contains(lines, l => Beside(l, img.Destination) && Math.Abs(l.Rect.X - (AreaLeft + 109)) < 0.01);
            Assert.Contains(doc.Warnings, w => w.Code == "TextWrapApproximated");
        }
    }

    [Fact]
    public void Too_narrow_a_gap_moves_text_below_the_image()
    {
        AnchoredImageInline a = new AnchoredImageInline(Png, AreaRight - AreaLeft - 20, 40, Flow.Calibri11) { Wrap = FloatWrap.Square, HorizontalAlignment = FloatAlignment.Start, DistanceBottom = 3 };
        (LaidOutPage _, ImageOp img, List<LayoutBox> lines, LaidOutDocument doc) = Lay(a, "short text");
        using (doc)
        {
            Assert.Equal(img.Destination.Bottom + 3, lines[0].Rect.Y, 3);
        }
    }

    [Fact]
    public void Page_relative_image_anchored_later_reflows_the_text_above_its_anchor()
    {
        AnchoredImageInline a = new AnchoredImageInline(Png, 120, 100, Flow.Calibri11)
        {
            Wrap = FloatWrap.Square,
            HorizontalRelativeTo = HorizontalAnchor.Margin,
            HorizontalAlignment = FloatAlignment.Start,
            VerticalRelativeTo = VerticalAnchor.Page,
            OffsetY = 72,
        };
        FlowDocument d = Flow.Doc(Flow.P("first paragraph " + Long), Flow.P("second"), Anchored(a, "anchor paragraph"));
        using LaidOutDocument doc = DocumentLayout.Open(d, Flow.Options());
        LaidOutPage p = doc.GetPage(0);
        ImageOp img = Assert.Single(p.DisplayList.OfType<ImageOp>());
        Assert.Equal(new LayoutRect(72, 72, 120, 100), img.Destination);
        List<LayoutBox> lines = Flow.Lines(p);
        Assert.Equal(72 + 120 + 9, lines[0].Rect.X, 3);
        Assert.All(lines.Where(l => Beside(l, img.Destination)), l => Assert.True(l.Rect.X >= 72 + 120 + 9 - 0.01));
    }

    [Fact]
    public void Paragraph_beside_an_image_continues_on_the_next_page_without_losing_text()
    {
        List<FlowBlock> blocks = Enumerable.Range(1, 38).Select(i => (FlowBlock)Flow.P("filler " + i)).ToList();
        AnchoredImageInline a = new AnchoredImageInline(Png, 150, 200, Flow.Calibri11) { Wrap = FloatWrap.Square, HorizontalAlignment = FloatAlignment.Start };
        blocks.Add(Anchored(a, Long));
        blocks.Add(Flow.P("after"));
        using LaidOutDocument doc = DocumentLayout.Open(Flow.Doc(blocks.ToArray()), Flow.Options());
        int pages = doc.ResolvePageCount();
        Assert.True(pages >= 2);
        string all = string.Concat(Enumerable.Range(0, pages).Select(i => doc.GetPage(i).GetText() + "\n"));
        Assert.Equal(Enumerable.Range(1, 220).Select(i => "word" + i), Words(all));
        Assert.Contains("after", all, StringComparison.Ordinal);
        // Rebuilding a page gives the same result (pagination is re-run from the checkpoint).
        string again = doc.GetPage(1).GetText();
        Assert.Equal(doc.GetPage(1).GetText(), again);
    }
}
