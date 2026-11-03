using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Model;
using Chuvadi.Layout.Tests;
using Xunit;

namespace Chuvadi.Layout.Docs.Tests;

/// <summary>
/// Newspaper columns read from a .docx (Fixtures/columns/calib-columns.docx), checked against the file and against the
/// positions measured in Word 2021's PDF of the same file where ECMA-376 is silent.
/// </summary>
public sealed class ColumnLayoutTests
{
    private const double ColumnWidth = (451.3 - 36) / 2; // two equal columns, gap not stated: 720 twips
    private const double Column2 = 72 + ColumnWidth + 36;

    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "columns", "calib-columns.docx");

    private static LaidOutDocument Open()
    {
        using FileStream fs = File.OpenRead(Fixture);
        return DocsLayout.Open(fs, null, DocsImportTests.Options());
    }

    private static List<LayoutBox> Lines(LaidOutPage p, string section, double left, double width)
        => Flow.Lines(p).Where(b => b.Path.StartsWith(section + "/", StringComparison.Ordinal) && b.Rect.X >= left - 0.5 && b.Rect.X < left + width)
            .OrderBy(b => b.Rect.Y).ToList();

    private static List<StrokeLineOp> Separators(LaidOutPage p)
        => p.DisplayList.OfType<StrokeLineOp>().Where(o => Math.Abs(o.X1 - o.X2) < 1e-9 && Math.Abs(o.Width - 0.75) < 1e-9).OrderBy(o => o.X1).ToList();

    [Fact]
    public void The_column_settings_are_read_as_the_file_states_them()
    {
        using FileStream fs = File.OpenRead(Fixture);
        FlowDocument d = DocsImporter.Import(fs, null, DocsImportTests.Options());
        Assert.Equal(7, d.Sections.Count);
        FlowSection k1 = d.Sections[0];
        Assert.Equal(2, k1.ColumnCount);
        Assert.Equal(36, k1.ColumnSpacing, 6);
        Assert.True(k1.ColumnSeparator);
        Assert.Empty(k1.ColumnWidths);
        FlowSection k2 = d.Sections[1];
        Assert.Equal(new[] { new FlowColumn(100, 20), new FlowColumn(125, 30), new FlowColumn(176.3, 0) }, k2.ColumnWidths);
        Assert.True(k2.ColumnSeparator);
        Assert.Equal(SectionStart.Continuous, d.Sections[3].Start);
        Assert.False(d.Sections[3].ColumnSeparator);
        Assert.Equal(SectionStart.NextColumn, d.Sections[6].Start);
    }

    [Fact]
    public void The_pages_match_the_columns_Word_draws()
    {
        using LaidOutDocument doc = Open();
        Assert.Equal(6, doc.ResolvePageCount());

        // Page 1: column 2 at 315.65; one separator centred in the gap (Word 297.7), from the top of the body to the
        // bottom of the longest column.
        LaidOutPage p1 = doc.GetPage(0);
        Assert.Equal(Column2, Lines(p1, "1", Column2, ColumnWidth).Min(b => b.Rect.X), 2);
        StrokeLineOp r1 = Assert.Single(Separators(p1));
        Assert.Equal(297.65, r1.X1, 1);
        Assert.Equal(72, r1.Y1, 2);
        Assert.Equal(Flow.Lines(p1).Max(b => b.Rect.Bottom), r1.Y2, 2);

        // Page 2: K1 ends at a next-page break and is not balanced: column 1 full (50 lines), column 2 15 lines (Word).
        LaidOutPage p2 = doc.GetPage(1);
        Assert.Equal(50, Lines(p2, "1", 72, ColumnWidth).Count);
        Assert.Equal(15, Lines(p2, "1", Column2, ColumnWidth).Count);

        // Page 3: three unequal columns at 72, 192 and 347 (2000/400, 2500/600, 3526 twips), two separators (Word 182.1, 332.1).
        LaidOutPage p3 = doc.GetPage(2);
        List<StrokeLineOp> r3 = Separators(p3);
        Assert.Equal(2, r3.Count);
        Assert.Equal(182, r3[0].X1, 1);
        Assert.Equal(332, r3[1].X1, 1);
        // After the column break, column 2 starts with the break paragraph's mark as an empty line (Word: 95.9 against 82.4).
        List<LayoutBox> c2 = Lines(p3, "2", 192, 125);
        Assert.Equal(72, c2[0].Rect.Y, 2);
        Assert.Equal(51, c2.Count);

        // Page 4: column 3 is empty, so only the separator before column 2 is drawn.
        Assert.Single(Separators(doc.GetPage(3)));
        Assert.Equal(6, Lines(doc.GetPage(3), "2", 192, 125).Count);

        // Page 5: K4 sits between continuous breaks and is balanced: 15 lines in each column (Word).
        LaidOutPage p5 = doc.GetPage(4);
        Assert.Equal(15, Lines(p5, "4", 72, ColumnWidth).Count);
        Assert.Equal(15, Lines(p5, "4", Column2, ColumnWidth).Count);
        // K5 (one column) starts below the balanced columns.
        Assert.True(Lines(p5, "5", 72, 451.3)[0].Rect.Y >= Lines(p5, "4", Column2, ColumnWidth)[^1].Rect.Bottom - 0.01);
        // The footnote of K6-02 (column 1) is drawn in column 1, at its width.
        LayoutBox notes = Assert.Single(p5.Boxes, b => b.Kind == LayoutBoxKind.Footnotes);
        Assert.Equal(72, notes.Rect.X, 2);
        Assert.Equal(ColumnWidth, notes.Rect.Width, 2);
        // The picture positioned relative to the column sits at the left edge of column 2.
        LayoutBox picture = Assert.Single(p5.Boxes, b => b.Kind == LayoutBoxKind.Image);
        Assert.Equal(Column2, picture.Rect.X, 2);

        // Page 6: K7 starts at the top of the next column (column 2).
        LaidOutPage p6 = doc.GetPage(5);
        List<LayoutBox> k7 = Flow.Lines(p6).Where(b => b.Path.StartsWith("7/", StringComparison.Ordinal)).ToList();
        Assert.All(k7, b => Assert.Equal(Column2, b.Rect.X, 2));
        Assert.Equal(72, k7.Min(b => b.Rect.Y), 2);
    }
}
