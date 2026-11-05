using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Engine.Grid;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.Tests;

internal static class Grid
{
    public static GridWorkbook Book(params GridSheet[] sheets)
    {
        GridWorkbook b = new GridWorkbook();
        foreach (GridSheet s in sheets)
        {
            b.Sheets.Add(s);
        }
        return b;
    }

    public static GridSheet Filled(int rows, int cols, string name = "Sheet1")
    {
        GridSheet s = new GridSheet(name);
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                s.Set(r, c, CellAddress.Format(r, c));
            }
        }
        return s;
    }

    public static LaidOutDocument Open(GridWorkbook b) => DocumentLayout.Open(b, Flow.Options());

    public static IEnumerable<LayoutBox> Cells(LaidOutPage p) => p.Boxes.Where(x => x.Kind == LayoutBoxKind.SheetCell);
}

public class GridGeometryTests
{
    [Theory]
    [InlineData(9.140625, 7, 64)]   // 8.43 displayed characters, Calibri 11
    [InlineData(8.7109375, 7, 61)]
    [InlineData(20.7109375, 7, 145)]
    [InlineData(0, 7, 0)]
    public void Width_to_pixels_follows_ecma_376(double width, int mdw, int px)
    {
        Assert.Equal(px, GridGeometry.WidthToPixels(width, mdw));
    }

    [Fact]
    public void Default_column_is_61_px_for_Calibri_11_and_rows_15_pt()
    {
        GridSheet s = new GridSheet("S");
        s.Set(0, 0, "x");
        s.Set(1, 1, "y");
        using LaidOutDocument doc = Grid.Open(Grid.Book(s));
        LaidOutPage p = doc.GetPage(0);
        List<LayoutBox> cells = Grid.Cells(p).ToList();
        Assert.Equal(45.75, cells[0].Rect.Width, 6); // 8 x 7 px + 5 px = 61 px (ECMA-376 18.3.1.81; W1)
        Assert.Equal(15, cells[0].Rect.Height, 6);
        Assert.Equal(50.4, cells[0].Rect.X, 6); // Excel default left margin 0.7 in
        Assert.Equal(54, cells[0].Rect.Y, 6);   // top margin 0.75 in
    }

    [Fact]
    public void Explicit_widths_heights_and_hidden_tracks()
    {
        GridSheet s = Grid.Filled(3, 3);
        s.Columns[1] = new GridColumn(20.7109375);
        s.Columns[2] = new GridColumn(10, Hidden: true);
        s.Rows[1] = new GridRow(30);
        s.Rows[2] = new GridRow(null, Hidden: true);
        using LaidOutDocument doc = Grid.Open(Grid.Book(s));
        LaidOutPage p = doc.GetPage(0);
        LayoutBox b1 = Grid.Cells(p).Single(c => c.Path == "Sheet1!B1");
        Assert.Equal(145 * 0.75, b1.Rect.Width, 6);
        Assert.DoesNotContain(Grid.Cells(p), c => c.Path == "Sheet1!C1" || c.Path == "Sheet1!A3");
        Assert.Equal(30, Grid.Cells(p).Single(c => c.Path == "Sheet1!A2").Rect.Height, 6);
        Assert.Equal("A1\tB1\nA2\tB2", p.GetText());
    }
}

public class GridPaginationTests
{
    /// <summary>The default column width for Calibri 11: 61 px = 45.75 pt (ECMA-376 18.3.1.81; W1).</summary>
    private const double Col = 45.75;

    [Fact]
    public void Rows_paginate_by_height_and_the_count_is_final_at_open()
    {
        using LaidOutDocument doc = Grid.Open(Grid.Book(Grid.Filled(100, 5)));
        Assert.True(doc.IsPageCountFinal);
        int perPage = (int)Math.Floor((841.9 - 108 + 0.01) / 15);
        Assert.Equal((int)Math.Ceiling(100.0 / perPage), doc.PageCount);
        Assert.StartsWith("A" + (perPage + 1), doc.GetPage(1).GetText(), StringComparison.Ordinal);
        Assert.Equal("Sheet1", doc.GetPage(0).SourceName);
    }

    [Fact]
    public void Columns_split_down_then_over_by_default_and_over_then_down_on_request()
    {
        GridSheet s = Grid.Filled(60, 15);
        using LaidOutDocument doc = Grid.Open(Grid.Book(s));
        Assert.Equal(4, doc.PageCount);
        Assert.StartsWith("A49", doc.GetPage(1).GetText(), StringComparison.Ordinal);
        Assert.StartsWith("K1", doc.GetPage(2).GetText(), StringComparison.Ordinal);

        GridSheet o = Grid.Filled(60, 15);
        o.PageSetup = new GridPageSetup { PageOrder = GridPageOrder.OverThenDown };
        using LaidOutDocument doc2 = Grid.Open(Grid.Book(o));
        Assert.StartsWith("K1", doc2.GetPage(1).GetText(), StringComparison.Ordinal);
        Assert.StartsWith("A49", doc2.GetPage(2).GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Fit_to_one_page_wide_uses_the_largest_whole_percent()
    {
        GridSheet s = Grid.Filled(30, 20);
        s.PageSetup = new GridPageSetup { FitToPage = true, FitToWidth = 1, FitToHeight = 0 };
        using LaidOutDocument doc = Grid.Open(Grid.Book(s));
        Assert.Equal(1, doc.PageCount);
        LayoutBox t1 = Grid.Cells(doc.GetPage(0)).Single(c => c.Path == "Sheet1!T1");
        double scale = Math.Floor((595.3 - 100.8) / (20 * Col) * 100) / 100;
        Assert.Equal(50.4 + (19 * Col * scale), t1.Rect.X, 4);
        Assert.Equal(Col * scale, t1.Rect.Width, 4);
    }

    [Fact]
    public void Explicit_scale_shrinks_and_fit_to_ignores_manual_breaks()
    {
        GridSheet s = Grid.Filled(100, 3);
        s.PageSetup = new GridPageSetup { Scale = 50 };
        using LaidOutDocument doc = Grid.Open(Grid.Book(s));
        Assert.Equal(2, doc.PageCount);
        Assert.Equal(7.5, Grid.Cells(doc.GetPage(0)).First().Rect.Height, 6);

        GridSheet f = Grid.Filled(10, 3);
        f.RowBreaks.Add(5);
        using LaidOutDocument withBreak = Grid.Open(Grid.Book(f));
        Assert.Equal(2, withBreak.PageCount);
        Assert.StartsWith("A6", withBreak.GetPage(1).GetText(), StringComparison.Ordinal);
        f.PageSetup = new GridPageSetup { FitToPage = true };
        using LaidOutDocument fit = Grid.Open(Grid.Book(f));
        Assert.Equal(1, fit.PageCount);
    }

    [Fact]
    public void Print_titles_repeat_on_later_pages()
    {
        GridSheet s = Grid.Filled(120, 3);
        s.PrintTitleRows = (0, 0);
        using LaidOutDocument doc = Grid.Open(Grid.Book(s));
        Assert.StartsWith("A1\tB1\tC1\nA49", doc.GetPage(1).GetText(), StringComparison.Ordinal);
        // With the title row, page 2 holds one row fewer of body rows than page 1.
        Assert.StartsWith("A1\tB1\tC1\nA96", doc.GetPage(2).GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Print_area_limits_output_and_multiple_areas_print_separately()
    {
        GridSheet s = Grid.Filled(20, 10);
        s.PrintAreas.Add(CellRange.Parse("$B$2:$C$3"));
        s.PrintAreas.Add(CellRange.Parse("E5"));
        using LaidOutDocument doc = Grid.Open(Grid.Book(s));
        Assert.Equal(2, doc.PageCount);
        Assert.Equal("B2\tC2\nB3\tC3", doc.GetPage(0).GetText());
        Assert.Equal("E5", doc.GetPage(1).GetText());
    }

    [Fact]
    public void Hidden_and_empty_sheets_produce_no_pages_and_numbering_continues()
    {
        GridSheet a = Grid.Filled(2, 2, "A");
        GridSheet h = Grid.Filled(2, 2, "Hidden");
        h.Hidden = true;
        GridSheet empty = new GridSheet("Empty");
        GridSheet b = Grid.Filled(2, 2, "B");
        using LaidOutDocument doc = Grid.Open(Grid.Book(a, h, empty, b));
        Assert.Equal(2, doc.PageCount);
        Assert.Equal("B", doc.GetPage(1).SourceName);
        Assert.Equal("2", doc.GetPage(1).PageLabel);
    }

    [Fact]
    public void Limits_apply()
    {
        LayoutOptions o = Flow.Options();
        o.Limits.MaxCellsPerSheet = 10;
        Assert.Throws<LayoutLimitExceededException>(() => DocumentLayout.Open(Grid.Book(Grid.Filled(5, 5)), o));
        LayoutOptions p = Flow.Options();
        p.Limits.MaxPages = 1;
        Assert.Throws<LayoutLimitExceededException>(() => DocumentLayout.Open(Grid.Book(Grid.Filled(200, 2)), p));
    }
}

public class GridCellRenderingTests
{
    /// <summary>The default column width for Calibri 11: 61 px = 45.75 pt (ECMA-376 18.3.1.81; W1).</summary>
    private const double Col = 45.75;

    private static LaidOutPage One(GridSheet s) => Grid.Open(Grid.Book(s)).GetPage(0);

    [Fact]
    public void General_alignment_puts_numbers_right_and_text_left()
    {
        GridSheet s = new GridSheet("S");
        s.Set(0, 0, "text");
        s.Set(1, 0, "42", CellValueKind.Number);
        LaidOutPage p = One(s);
        GlyphRunOp text = Flow.Runs(p).Single(r => r.Text == "text");
        GlyphRunOp num = Flow.Runs(p).Single(r => r.Text == "42");
        Assert.Equal(50.4 + 1.5, text.X, 3);
        Assert.Equal(50.4 + Col - 1.5, num.X + num.Width, 3);
    }

    [Fact]
    public void Numbers_that_do_not_fit_show_hashes()
    {
        GridSheet s = new GridSheet("S");
        s.Columns[0] = new GridColumn(3);
        s.Set(0, 0, "123456789", CellValueKind.Number);
        LaidOutPage p = One(s);
        GlyphRunOp run = Assert.Single(Flow.Runs(p));
        Assert.Matches("^#+$", run.Text);
        Assert.True(run.Width <= GridGeometry.PixelsToPoints(GridGeometry.WidthToPixels(3, 7)));
    }

    [Fact]
    public void Text_overflows_into_empty_neighbours_but_not_into_filled_ones()
    {
        GridSheet s = new GridSheet("S");
        s.Set(0, 0, "A long label that spills over");
        s.Set(1, 0, "Blocked long label here");
        s.Set(1, 1, "x");
        s.Set(2, 5, "wide"); // makes the used range span A:F, so A1 has empty neighbours on the page
        LaidOutPage p = One(s);
        List<PushClipOp> clips = p.DisplayList.OfType<PushClipOp>().ToList();
        Assert.True(clips[0].Rect.Width > Col * 2, "row 1 clip should extend over empty cells");
        Assert.Equal(Col, clips[1].Rect.Width, 6);
    }

    [Fact]
    public void Wrapped_text_grows_a_row_without_stored_height()
    {
        GridSheet s = new GridSheet("S");
        s.Set(0, 0, "This text is long enough to wrap onto several lines in a narrow column", style: new GridCellStyle { WrapText = true });
        s.Set(1, 0, "below");
        LaidOutPage p = One(s);
        LayoutBox a1 = Grid.Cells(p).Single(c => c.Path == "S!A1");
        Assert.True(a1.Rect.Height > 45, "auto-fitted height " + a1.Rect.Height);
        Assert.True(Flow.Runs(p).Count(r => r.Font.FamilyName == "Carlito") >= 4);
    }

    [Fact]
    public void Merged_cell_centres_across_its_range_and_suppresses_inner_gridlines()
    {
        GridSheet s = new GridSheet("S");
        s.Set(0, 0, "Title", style: new GridCellStyle { HorizontalAlignment = CellHorizontalAlignment.Center });
        s.MergedRanges.Add(CellRange.Parse("A1:C1"));
        s.Set(1, 0, "a");
        s.PageSetup = new GridPageSetup { PrintGridlines = true };
        LaidOutPage p = One(s);
        LayoutBox a1 = Grid.Cells(p).Single(c => c.Path == "S!A1");
        Assert.Equal(3 * Col, a1.Rect.Width, 6);
        GlyphRunOp title = Flow.Runs(p).Single(r => r.Text == "Title");
        Assert.Equal(50.4 + (1.5 * Col), title.X + (title.Width / 2), 2);
        // No vertical gridline at x = A|B or B|C inside row 1.
        double xAB = 50.4 + Col;
        Assert.DoesNotContain(p.DisplayList.OfType<StrokeLineOp>(), l => Math.Abs(l.X1 - xAB) < 0.01 && Math.Abs(l.X2 - xAB) < 0.01 && l.Y1 < 54 + 1 && l.Y2 > 54 + 14);
    }

    [Fact]
    public void Fills_borders_and_fonts()
    {
        GridSheet s = new GridSheet("S");
        BorderLine thin = new BorderLine(0.75, LayoutColor.Black);
        s.Set(0, 0, "Head", style: new GridCellStyle
        {
            Fill = new LayoutColor(0xDD, 0xEB, 0xF7),
            BorderBottom = thin,
            Font = new CharacterFormat { FontFamily = "Arial", FontSize = 12, Bold = true },
        });
        LaidOutPage p = One(s);
        FillRectOp fill = Assert.Single(p.DisplayList.OfType<FillRectOp>());
        Assert.Equal(new LayoutRect(50.4, 54, Col, 15), fill.Rect);
        Assert.Contains(p.DisplayList.OfType<StrokeLineOp>(), l => l.Width == 0.75 && Math.Abs(l.Y1 - 69) < 0.01);
        GlyphRunOp run = Assert.Single(Flow.Runs(p));
        Assert.Equal("Liberation Sans", run.Font.FamilyName);
        Assert.True(run.Font.IsBold);
        // Paint order: fill, text, border.
        List<LayoutOp> ops = p.DisplayList.ToList();
        Assert.True(ops.IndexOf(fill) < ops.IndexOf(run));
        Assert.True(ops.IndexOf(run) < ops.FindLastIndex(o => o is StrokeLineOp));
    }

    [Fact]
    public void Headings_print_row_numbers_and_column_letters()
    {
        GridSheet s = Grid.Filled(2, 2);
        s.PageSetup = new GridPageSetup { PrintHeadings = true };
        LaidOutPage p = One(s);
        string[] texts = Flow.Runs(p).Select(r => r.Text).ToArray();
        Assert.Contains("A", texts);
        Assert.Contains("B", texts);
        Assert.Contains("1", texts);
        Assert.Contains("2", texts);
    }

    [Fact]
    public void Header_and_footer_codes_are_substituted()
    {
        GridSheet s = Grid.Filled(120, 2, "Data");
        s.PageSetup = new GridPageSetup { OddHeader = "&L&\"Arial,Bold\"&14Report&RSheet &A", OddFooter = "&CPage &P of &N&R&D" };
        LayoutOptions o = Flow.Options();
        using LaidOutDocument doc = DocumentLayout.Open(Grid.Book(s), o);
        LaidOutPage p = doc.GetPage(1);
        string[] texts = Flow.Runs(p).Select(r => r.Text).ToArray();
        Assert.Contains("Report", texts);
        Assert.Contains("Sheet Data", texts);
        Assert.Contains("Page 2 of 3", texts);
        GlyphRunOp report = Flow.Runs(p).Single(r => r.Text == "Report");
        Assert.Equal(14, report.FontSize, 6);
        Assert.True(report.Font.IsBold);
        // Win ascent plus the external leading Windows reports for Arial (hhea line gap 67), as Word and Excel lay out.
        Assert.Equal(21.6 + ((1854.0 + 67) / 2048 * 14), report.Y, 3);
        Assert.Contains(p.Boxes, b => b.Kind == LayoutBoxKind.Footer);
    }

    [Fact]
    public void Header_parser_handles_page_offsets_ampersands_and_colours()
    {
        HeaderFooterValues v = new HeaderFooterValues(5, 9, "S", "f.xlsx", "C:/f.xlsx", new DateTimeOffset(2026, 1, 2, 3, 4, 0, TimeSpan.Zero), System.Globalization.CultureInfo.InvariantCulture);
        var (l, c, r, pic) = ExcelHeaderFooter.Parse("&P+1 &P-2 A&&B &KFF0000red&G", CharacterFormat.Default, v);
        string text = string.Concat(c.Inlines.OfType<TextRun>().Select(t => t.Text));
        Assert.Equal("6 3 A&B red", text);
        Assert.Equal(new LayoutColor(255, 0, 0), c.Inlines.OfType<TextRun>().Last().Format.Color);
        Assert.True(pic);
        Assert.Empty(l.Inlines);
        Assert.Empty(r.Inlines);
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(701, "ZZ")]
    [InlineData(702, "AAA")]
    [InlineData(16383, "XFD")]
    public void Column_names(int index, string name)
    {
        Assert.Equal(name, CellAddress.ColumnName(index));
        Assert.Equal((0, index), CellAddress.Parse(name + "1"));
    }
}
