using Chuvadi.Docs.Model;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Fonts;
using Chuvadi.Layout.Docs;
using Chuvadi.Layout.Tests;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.Docs.Tests;

/// <summary>Chuvadi.Docs 1.6.0 content through the docx importer: notes, revisions, comments, symbols, text boxes, floating tables, embedded fonts.</summary>
public class DocsImport160Tests
{
    private static LaidOutDocument Lay(Document doc, LayoutOptions? options = null)
        => DocsLayout.Open(new MemoryStream(DocsImportTests.Save(doc)), null, options ?? DocsImportTests.Options());

    private static string AllText(LaidOutDocument doc)
        => string.Join("\n", Enumerable.Range(0, doc.ResolvePageCount()).Select(i => doc.GetPage(i).GetText()));

    private static Document A4()
    {
        Document d = new Document();
        d.Page.Size = PageSize.A4;
        return d;
    }

    [Fact]
    public void Footnote_is_laid_out_at_the_foot_of_the_page_under_a_separator()
    {
        Document d = A4();
        d.AddParagraph().Text("Body text").Footnote(d, "The note text.").Text(" continues.");
        using LaidOutDocument doc = Lay(d);
        Assert.Equal(1, doc.ResolvePageCount());
        LaidOutPage page = doc.GetPage(0);
        LayoutBox notes = Assert.Single(page.Boxes, b => b.Kind == LayoutBoxKind.Footnotes);
        // At the foot of the body (A4, 72 pt margins): the notes end at the bottom margin.
        Assert.Equal(841.9 - 72, notes.Rect.Bottom, 0);
        Assert.Contains("The note text.", page.GetText(), StringComparison.Ordinal);
        // The reference draws its number; the separator is a two-inch rule above the notes.
        Assert.Contains(Flow.Runs(page), r => r.Text == "1");
        Assert.Contains(page.DisplayList.OfType<StrokeLineOp>(), l => Math.Abs(l.X2 - l.X1 - 144) < 0.01 && l.Y1 >= notes.Rect.Y && l.Y1 <= notes.Rect.Y + 20);
    }

    [Fact]
    public void Footnotes_stay_on_the_page_of_their_reference_and_take_room_from_the_body()
    {
        Document d = A4();
        for (int i = 0; i < 40; i++)
        {
            Paragraph p = d.AddParagraph().Text("Paragraph " + i + " " + string.Join(" ", Enumerable.Repeat("text", 20)));
            if (i % 5 == 0)
            {
                p.Footnote(d, "Note for paragraph " + i + ". " + string.Join(" ", Enumerable.Repeat("note", 30)));
            }
        }
        using LaidOutDocument doc = Lay(d);
        int pages = doc.ResolvePageCount();
        Assert.True(pages >= 2);
        for (int i = 0; i < 40; i += 5)
        {
            int refPage = Enumerable.Range(0, pages).First(k => doc.GetPage(k).GetText().Contains("Paragraph " + i + " ", StringComparison.Ordinal));
            Assert.Contains("Note for paragraph " + i + ".", doc.GetPage(refPage).GetText(), StringComparison.Ordinal);
        }
        for (int k = 0; k < pages; k++)
        {
            LaidOutPage page = doc.GetPage(k);
            if (page.Boxes.FirstOrDefault(b => b.Kind == LayoutBoxKind.Footnotes) is LayoutBox notes)
            {
                // No body line runs into the footnote area.
                Assert.All(page.Boxes.Where(b => b.Kind == LayoutBoxKind.Line && !b.Path.StartsWith("footnote", StringComparison.Ordinal) && b.Rect.Y < notes.Rect.Y),
                    l => Assert.True(l.Rect.Bottom <= notes.Rect.Y + 0.01));
            }
        }
    }

    [Fact]
    public void Endnotes_follow_the_text_at_the_end_of_the_document()
    {
        Document d = A4();
        d.AddParagraph().Text("Text with an endnote").Endnote(d, "The endnote.");
        d.AddParagraph().Text("Last body paragraph.");
        using LaidOutDocument doc = Lay(d);
        string text = AllText(doc);
        Assert.True(text.IndexOf("The endnote.", StringComparison.Ordinal) > text.IndexOf("Last body paragraph.", StringComparison.Ordinal));
        Assert.Contains(Flow.Runs(doc.GetPage(0)), r => r.Text == "i"); // endnotes number in lower roman by default
    }

    [Fact]
    public void Tracked_changes_are_laid_out_accepted_and_comments_take_no_room()
    {
        Document d = A4();
        Comment c = d.Comments.Add("Reviewer", "A comment.");
        d.AddParagraph().Text("Dose ").TrackedDelete("60 Gy", "Reviewer").TrackedInsert("50 Gy", "Reviewer").Text(" daily.");
        d.AddParagraph().Commented("Commented text.", c);
        using LaidOutDocument fromFile = Lay(d);
        string text = fromFile.GetPage(0).GetText();
        Assert.Contains("Dose 50 Gy daily.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("60 Gy", text, StringComparison.Ordinal);
        Assert.Contains("Commented text.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("A comment.", text, StringComparison.Ordinal);

        // A Document passed in directly is not changed: deleted runs are skipped instead.
        using LaidOutDocument direct = DocsLayout.Open(d, null, DocsImportTests.Options());
        Assert.Contains("Dose 50 Gy daily.", direct.GetPage(0).GetText(), StringComparison.Ordinal);
        Assert.Contains(d.Blocks.OfType<Paragraph>().First().Runs, r => r.IsDeleted);
    }

    [Fact]
    public void Symbol_characters_use_their_font()
    {
        Document d = A4();
        d.AddParagraph().Text("Copyright ").Symbol("Liberation Serif", '\u00A9');
        using LaidOutDocument doc = Lay(d);
        GlyphRunOp sym = Assert.Single(Flow.Runs(doc.GetPage(0)), r => r.Text == "\u00A9");
        Assert.Equal("Liberation Serif", sym.Font.FamilyName);
    }

    [Fact]
    public void Floating_text_box_draws_its_frame_and_text_and_text_wraps_beside_it()
    {
        Document d = A4();
        TextBox box = new TextBox
        {
            Placement = ImagePlacement.Floating,
            Position = new FloatingPosition { HorizontalAnchor = Chuvadi.Docs.Model.HorizontalAnchor.Margin, HAlign = HorizontalAlignment.Right, VerticalAnchor = Chuvadi.Docs.Model.VerticalAnchor.Paragraph, Wrap = TextWrap.Square },
            WidthPt = 150,
            HeightPt = 80,
            FillHex = "EAF1FB",
            LineHex = "2F5496",
        };
        box.Paragraphs.Add(new Paragraph("Inside the box"));
        d.AddParagraph().TextBox(box).Text(string.Join(" ", Enumerable.Repeat("wrapping text", 40)));
        using LaidOutDocument doc = Lay(d);
        LaidOutPage page = doc.GetPage(0);
        LayoutBox tb = Assert.Single(page.Boxes, b => b.Kind == LayoutBoxKind.TextBox);
        Assert.Equal(523.3, tb.Rect.Right, 1);
        Assert.Equal(150, tb.Rect.Width, 3);
        Assert.Contains(page.DisplayList.OfType<FillRectOp>(), f => f.Rect == tb.Rect);
        GlyphRunOp inside = Assert.Single(Flow.Runs(page), r => r.Text == "Inside the box");
        Assert.Equal(tb.Rect.X + 7.2, inside.X, 3);
        // Body lines beside the box end before it.
        Assert.All(page.Boxes.Where(b => b.Kind == LayoutBoxKind.Line && b.Rect.Y < tb.Rect.Bottom && b.Rect.Bottom > tb.Rect.Y && b.Rect.X < tb.Rect.X),
            l => Assert.True(l.Rect.Right <= tb.Rect.X + 0.01));
        Assert.Contains("Inside the box", page.GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Inline_text_box_takes_its_size_in_the_line()
    {
        Document d = A4();
        TextBox box = new TextBox { Placement = ImagePlacement.Inline, WidthPt = 100, HeightPt = 30 };
        box.Paragraphs.Add(new Paragraph("Boxed"));
        d.AddParagraph().Text("Before ").TextBox(box).Text(" after");
        using LaidOutDocument doc = Lay(d);
        LaidOutPage page = doc.GetPage(0);
        LayoutBox tb = Assert.Single(page.Boxes, b => b.Kind == LayoutBoxKind.TextBox);
        Assert.Equal(100, tb.Rect.Width, 3);
        Assert.Equal(30, tb.Rect.Height, 3);
        GlyphRunOp after = Assert.Single(Flow.Runs(page), r => r.Text.StartsWith(" after", StringComparison.Ordinal) || r.Text == "after");
        Assert.True(after.X >= tb.Rect.Right - 0.01);
        Assert.Contains(Flow.Runs(page), r => r.Text == "Boxed");
    }

    [Fact]
    public void Floating_table_takes_its_horizontal_position()
    {
        Document d = A4();
        DocTable t = d.AddTable(1);
        t.ColumnWidthsPt = new double[] { 100 };
        t.Format.FloatingPosition = new TableFloatingPosition { HorizontalAnchor = TableFloatAnchor.Margin, XPt = 200 };
        t.AddRow("cell");
        List<LayoutWarning> warnings = new List<LayoutWarning>();
        FlowDocument flow = DocsImportTests.Import(d, null, warnings);
        FlowTable ft = flow.Sections[0].Blocks.OfType<FlowTable>().Single();
        Assert.Equal(200, ft.Format.Indent, 3);
        Assert.Contains(warnings, w => w.Message.Contains("Floating tables", StringComparison.Ordinal));
    }

    [Fact]
    public void Embedded_fonts_are_used_before_any_other_source()
    {
        // The only font available is the one the document embeds.
        Document d = A4();
        d.EmbedFont("Carlito", TestFonts.Bytes("Carlito-Regular.ttf"));
        d.AddParagraph().Text("Embedded", new TextFormat { Font = "Carlito" });
        LayoutOptions o = new LayoutOptions { AllowSystemFonts = false };
        using LaidOutDocument doc = Lay(d, o);
        GlyphRunOp run = Assert.Single(Flow.Runs(doc.GetPage(0)), r => r.Text == "Embedded");
        Assert.Equal(FontOrigin.Embedded, run.Font.Origin);

        LayoutOptions off = new LayoutOptions { AllowSystemFonts = false, UseEmbeddedFonts = false };
        off.FontSources.Add(BundledFontSource.Instance);
        using LaidOutDocument doc2 = Lay(d, off);
        Assert.Equal(FontOrigin.Bundled, Assert.Single(Flow.Runs(doc2.GetPage(0)), r => r.Text == "Embedded").Font.Origin);
    }
}
