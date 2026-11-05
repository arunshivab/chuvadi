using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Tests;
using Xunit;

namespace Chuvadi.Layout.Docs.Tests;

/// <summary>
/// Floating tables read from a .docx (Fixtures/floating/calib-floating.docx): positions as the file states them
/// (ECMA-376 17.4.57), and where the standard is silent as Word 2021 drew them in its PDF of the same file.
/// </summary>
public sealed class FloatingTableLayoutTests
{
    private static LaidOutDocument Open(string folder, string file)
    {
        using FileStream fs = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", folder, file));
        return DocsLayout.Open(fs, null, DocsImportTests.Options());
    }

    private static List<LayoutBox> Tables(LaidOutPage p) => p.Boxes.Where(b => b.Kind == LayoutBoxKind.Table).OrderBy(b => b.Rect.Y).ToList();

    /// <summary>Body lines (not the lines inside table cells).</summary>
    private static List<LayoutBox> Body(LaidOutPage p, string block)
        => Flow.Lines(p).Where(b => b.Path.StartsWith(block + ":", StringComparison.Ordinal)).OrderBy(b => b.Rect.Y).ToList();

    private static void NoTextOverlapsATable(LaidOutPage p)
    {
        foreach (LayoutBox t in Tables(p))
        {
            foreach (LayoutBox l in Flow.Lines(p).Where(b => b.Path.Count(c => c == '/') == 1 && b.Rect.Width > 0))
            {
                bool vertical = l.Rect.Y < t.Rect.Bottom - 0.5 && l.Rect.Bottom > t.Rect.Y + 0.5;
                bool horizontal = l.Rect.X < t.Rect.Right - 0.5 && l.Rect.Right > t.Rect.X + 0.5;
                Assert.False(vertical && horizontal, $"line {l.Path} at {l.Rect} overlaps the table {t.Path} at {t.Rect}");
            }
        }
    }

    [Fact]
    public void The_floating_tables_sit_where_the_file_and_Word_put_them()
    {
        using LaidOutDocument doc = Open("floating", "calib-floating.docx");
        Assert.Equal(11, doc.ResolvePageCount()); // Word: 13, with the two pages decision A avoids (T2)
        Assert.DoesNotContain(doc.Warnings, w => w.Message.Contains("Floating tables", StringComparison.Ordinal));
        for (int i = 0; i < 11; i++)
        {
            NoTextOverlapsATable(doc.GetPage(i));
        }

        // T1: 200 pt from the margin; top at the top of the following paragraph; text on both sides, 9 pt away
        // (Word: right text at 431.8 = 272 + 150 + 0.75 border + 9).
        LaidOutPage p1 = doc.GetPage(0);
        LayoutBox t1 = Assert.Single(Tables(p1));
        Assert.Equal(272, t1.Rect.X, 1);
        List<LayoutBox> beside = Body(p1, "1/6");
        Assert.InRange(beside[0].Rect.Y - t1.Rect.Y, -0.06, 0.06); // tblpY is 1 twip (0.05 pt), used as written
        Assert.Contains(beside, b => Math.Abs(b.Rect.X - 431.75) < 0.1);
        Assert.All(beside.Where(b => b.Rect.X < 100), b => Assert.True(b.Rect.Right <= 272 - 9 + 0.01));

        // T2: right and bottom aligned to the page: exactly at the foot of the page, whole (decision A).
        LayoutBox t2 = Assert.Single(Tables(doc.GetPage(1)));
        Assert.Equal(595.3 - t2.Rect.Width, t2.Rect.X, 1);
        Assert.Equal(841.9, t2.Rect.Bottom, 1);

        // T3: 72 pt below the top margin, above its paragraph: the text above flows beside it (Word: 261.8).
        LaidOutPage p5 = doc.GetPage(4);
        LayoutBox t3 = Assert.Single(Tables(p5));
        Assert.Equal(144, t3.Rect.Y, 1);
        Assert.Contains(Flow.Lines(p5), b => Math.Abs(b.Rect.X - 261.8) < 0.1 && b.Rect.Y < t3.Rect.Bottom);

        // T4: the "never overlap" table at the top margin, the other below it; no overlap.
        List<LayoutBox> t4 = Tables(doc.GetPage(6));
        Assert.Equal(2, t4.Count);
        Assert.Equal(72, t4[0].Rect.Y, 0);
        Assert.True(t4[1].Rect.Y >= t4[0].Rect.Bottom - 0.01);

        // T5: anchored to its paragraph and taller than the space left: continues at the top of the next page at the
        // same x, and the paragraph after it starts there, beside it (Word: 231.8).
        LayoutBox a = Assert.Single(Tables(doc.GetPage(7)));
        LayoutBox b5 = Assert.Single(Tables(doc.GetPage(8)));
        Assert.Equal(a.Rect.X, b5.Rect.X, 2);
        Assert.Equal(72, b5.Rect.Y, 0);
        Assert.Equal(231.8, Body(doc.GetPage(8), "1/108")[0].Rect.X, 1);

        // T6: anchors not stated: the page and the page (the standard; Word would use the margins).
        LayoutBox t6 = Assert.Single(Tables(doc.GetPage(9)));
        Assert.Equal(100, t6.Rect.X, 1);
        Assert.Equal(25, t6.Rect.Y, 1);

        // T7: relative to column 2 (Word: 330.65).
        LayoutBox t7 = Assert.Single(Tables(doc.GetPage(10)));
        Assert.Equal(72 + ((451.3 - 36) / 2) + 36 + 15, t7.Rect.X, 1);
    }

    [Fact]
    public void The_last_section_of_a_document_is_not_balanced()
    {
        // Word 2021 (4 Nov 2026): the last section keeps all its text in column 1, after a continuous or a next-page break.
        foreach (string file in new[] { "calib-lastsection-continuous.docx", "calib-lastsection-nextpage.docx" })
        {
            using LaidOutDocument doc = Open("columns", file);
            LaidOutPage last = doc.GetPage(doc.ResolvePageCount() - 1);
            List<LayoutBox> lines = Flow.Lines(last).Where(b => b.Path.StartsWith("2/", StringComparison.Ordinal)).ToList();
            Assert.NotEmpty(lines);
            Assert.All(lines, l => Assert.Equal(72, l.Rect.X, 1));
        }
    }
}
