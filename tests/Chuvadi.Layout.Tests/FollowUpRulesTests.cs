using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Model;
using Xunit;

namespace Chuvadi.Layout.Tests;

/// <summary>
/// The follow-up rules of D-181: the right margin is a hard limit (F5); the paragraph mark sizes only an empty line (F6),
/// so a line of pictures is the pictures' height (F4); any repeat-fill character (X2); text overflowing past the last used
/// column prints in full and continues on the next page (F11); real digit widths for substituted workbook fonts (F7c).
/// </summary>
public class FollowUpRulesTests
{
    private static LaidOutDocument Lay(FlowDocument d) => DocumentLayout.Open(d, Flow.Options());

    private static double TextWidth(string text)
    {
        // Measured on a page wide enough for one line: the line box's width is the text's width.
        FlowDocument d = Flow.Doc(Flow.P(text));
        d.Sections[0].Page = PageGeometry.A4 with { Width = 2000 };
        using LaidOutDocument doc = Lay(d);
        return Assert.Single(Flow.Lines(doc.GetPage(0))).Rect.Width;
    }

    [Fact]
    public void A_line_a_hair_wider_than_the_text_area_wraps()
    {
        const string text = "admission approval assurance beam calibration";
        double w = TextWidth(text);
        foreach ((double area, int lines) in new[] { (w + 0.001, 1), (w, 1), (w - 0.0008, 2) })
        {
            FlowDocument d = Flow.Doc(Flow.P(text));
            PageGeometry a4 = PageGeometry.A4;
            d.Sections[0].Page = a4 with { Width = a4.MarginLeft + a4.MarginRight + area };
            using LaidOutDocument doc = Lay(d);
            Assert.Equal(lines, Flow.Lines(doc.GetPage(0)).Count);
        }
    }

    [Fact]
    public void The_paragraph_mark_does_not_raise_a_line_with_text()
    {
        // 8 pt text, 11 pt mark (A7 B1): the line is the text's height. An empty paragraph takes the mark's (B3, B4).
        CharacterFormat small = Flow.Calibri11 with { FontSize = 8 };
        FlowParagraph text = FlowParagraph.Of("Eight point text", small, new ParagraphFormat());
        text.MarkFormat = Flow.Calibri11;
        FlowParagraph empty = new FlowParagraph { MarkFormat = Flow.Calibri11 };
        using LaidOutDocument doc = Lay(Flow.Doc(text, empty));
        List<LayoutBox> lines = Flow.Lines(doc.GetPage(0));
        Assert.Equal(Flow.LineHeight("Carlito-Regular.ttf", 8), lines[0].Rect.Height, 3);
        Assert.Equal(Flow.LineHeight("Carlito-Regular.ttf", 11), lines[1].Rect.Height, 3);
    }

    [Fact]
    public void A_line_of_pictures_only_is_the_pictures_height()
    {
        FlowParagraph pic = new FlowParagraph { MarkFormat = Flow.Calibri11 };
        pic.Inlines.Add(new ImageInline(new LayoutImage(new byte[] { 1 }, LayoutImageFormat.Png, 10, 10), 50, 40, Flow.Calibri11));
        FlowParagraph mixed = FlowParagraph.Of("Text ", Flow.Calibri11, new ParagraphFormat());
        mixed.MarkFormat = Flow.Calibri11;
        mixed.Inlines.Add(new ImageInline(new LayoutImage(new byte[] { 1 }, LayoutImageFormat.Png, 10, 10), 50, 40, Flow.Calibri11));
        using LaidOutDocument doc = Lay(Flow.Doc(pic, mixed));
        List<LayoutBox> lines = Flow.Lines(doc.GetPage(0));
        Assert.Equal(40, lines[0].Rect.Height, 6);                 // A7 C1: exactly the picture
        Assert.True(lines[1].Rect.Height > 40 + 2, "a picture in a text line keeps the text's descent (A7 C4)");
    }

    [Fact]
    public void Any_repeat_fill_character_fills_the_cell()
    {
        GridSheet s = new GridSheet("S");
        s.Columns[0] = new GridColumn(20);
        s.SetCell(0, 0, new GridCell("1234.50") { FillCharacter = '*', FillIndex = 0 });
        s.SetCell(1, 0, new GridCell("Total") { FillCharacter = '=', FillIndex = 5 });
        using LaidOutDocument doc = Grid.Open(Grid.Book(s));
        LaidOutPage p = doc.GetPage(0);
        GlyphRunOp stars = Assert.Single(Flow.Runs(p), r => r.Text.Length > 3 && r.Text.Trim('*').Length == 0);
        GlyphRunOp equals = Assert.Single(Flow.Runs(p), r => r.Text.Length > 3 && r.Text.Trim('=').Length == 0);
        Assert.True(stars.X + stars.Width < Flow.Runs(p).Single(r => r.Text == "1234.50").X, "the fill comes before the value");
        Assert.True(equals.X > Flow.Runs(p).Single(r => r.Text == "Total").X, "the fill comes after the text");
        Assert.DoesNotContain(doc.Warnings, w => w.Code == "FillApproximated");
    }

    [Fact]
    public void Text_past_the_last_used_column_prints_in_full()
    {
        // A1 holds the only value; its text is about 8 default columns long.
        GridSheet s = new GridSheet("S");
        s.Set(0, 0, "This label runs well past the last used column of the sheet");
        using LaidOutDocument doc = Grid.Open(Grid.Book(s));
        Assert.Equal(1, doc.PageCount);
        PushClipOp clip = Assert.Single(doc.GetPage(0).DisplayList.OfType<PushClipOp>());
        Assert.True(clip.Rect.Width > 4 * 45.75, "the text is not cut at column A, got " + clip.Rect.Width);
    }

    [Fact]
    public void Overflowing_text_continues_on_the_page_it_runs_onto()
    {
        // Columns of width 8 (56 px = 42 pt): 11 fit across the A4 printable width (A to K), so text from I1 runs onto page 2.
        GridSheet s = new GridSheet("S");
        for (int c = 0; c < 40; c++)
        {
            s.Columns[c] = new GridColumn(8);
        }
        s.Set(1, 0, "x");
        s.Set(0, 8, "Text that starts in the last column of page one and goes on across the page break");
        using LaidOutDocument doc = Grid.Open(Grid.Book(s));
        Assert.True(doc.PageCount >= 2);
        GlyphRunOp first = Assert.Single(Flow.Runs(doc.GetPage(0)), r => r.Text.StartsWith("Text that", StringComparison.Ordinal));
        GlyphRunOp second = Assert.Single(Flow.Runs(doc.GetPage(1)), r => r.Text.StartsWith("Text that", StringComparison.Ordinal));
        Assert.True(second.X < 50.4, "on page 2 the run starts left of the grid, where the cell would be, got " + second.X);
        PushClipOp clip = doc.GetPage(1).DisplayList.OfType<PushClipOp>().First();
        Assert.True(clip.Rect.X >= 50.4 - 0.01, "only the part on page 2 is drawn");
        Assert.Equal(first.Y, second.Y, 3);
    }

    [Fact]
    public void A_missing_Cambria_still_sizes_columns_with_Cambria_digits()
    {
        // Real Cambria's digits are 554/1000 em: MDW 8 at 11 pt (Caladea's give 7).
        GridSheet s = new GridSheet("S") { NormalFont = new CharacterFormat { FontFamily = "Cambria", FontSize = 11 } };
        s.Columns[0] = new GridColumn(10);
        s.Set(0, 0, "x");
        using LaidOutDocument doc = Grid.Open(Grid.Book(s));
        LayoutBox a1 = Grid.Cells(doc.GetPage(0)).Single(c => c.Path == "S!A1");
        Assert.Equal(80 * 0.75, a1.Rect.Width, 6); // Truncate(((256 x 10 + 16) / 256) x 8) = 80 px
    }
}
