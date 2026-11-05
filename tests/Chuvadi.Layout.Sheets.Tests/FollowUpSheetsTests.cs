using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Sheets;
using Chuvadi.Layout.Tests;
using Chuvadi.Sheets.Model;
using Xunit;

namespace Chuvadi.Layout.Sheets.Tests;

/// <summary>The follow-up rules of D-181 on workbooks written with Chuvadi.Sheets: X2 (any fill character) and W1 (61 px).</summary>
public class FollowUpSheetsTests
{
    [Fact]
    public void A_star_fill_repeats_the_star_across_the_cell()
    {
        Workbook wb = new Workbook();
        Sheet s = wb.AddSheet("F");
        s.Cell("A1").Value = 1234.5;
        s.Cell("A1").Style = new CellStyleBuilder().Format("**0.00").Build();
        s.Columns[1].Width = 20;
        using LaidOutDocument doc = SheetsLayout.Open(new MemoryStream(SheetsImportTests.Save(wb)), null, SheetsImportTests.Options());
        LaidOutPage p = doc.GetPage(0);
        GlyphRunOp value = Assert.Single(Flow.Runs(p), r => r.Text == "1234.50");
        GlyphRunOp stars = Assert.Single(Flow.Runs(p), r => r.Text.Length > 3 && r.Text.Trim('*').Length == 0);
        Assert.True(stars.X + stars.Width <= value.X + 0.01, "the stars fill the space before the value");
        Assert.DoesNotContain(doc.Warnings, w => w.Code == "FillApproximated");
    }

    [Fact]
    public void A_sheet_without_defaultColWidth_has_61_px_columns()
    {
        Workbook wb = new Workbook();
        wb.AddSheet("D").Cell("A1").Value = "x";
        using LaidOutDocument doc = SheetsLayout.Open(new MemoryStream(SheetsImportTests.Save(wb)), null, SheetsImportTests.Options());
        LayoutBox a1 = doc.GetPage(0).Boxes.Single(b => b.Kind == LayoutBoxKind.SheetCell);
        Assert.Equal(45.75, a1.Rect.Width, 6); // 8 x 7 px + 5 px (ECMA-376 18.3.1.81)
    }
}
