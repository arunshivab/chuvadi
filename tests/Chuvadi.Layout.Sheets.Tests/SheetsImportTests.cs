using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Fonts;
using Chuvadi.Layout.Sheets;
using Chuvadi.Layout.Tests;
using Chuvadi.Layout.Model;
using Chuvadi.Sheets.Excel;
using CellAddress = Chuvadi.Layout.Model.CellAddress;
using SheetsBorderStyle = Chuvadi.Sheets.Excel.BorderStyle;

namespace Chuvadi.Layout.Sheets.Tests;

/// <summary>Workbooks written with Chuvadi.Sheets, read back through the xlsx importer (L7).</summary>
public class SheetsImportTests
{
    internal static LayoutOptions Options()
    {
        LayoutOptions o = Flow.Options();
        o.Now = new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.FromHours(5.5));
        return o;
    }

    internal static byte[] Save(Workbook wb)
    {
        using MemoryStream ms = new MemoryStream();
        wb.SaveTo(ms);
        return ms.ToArray();
    }

    internal static GridWorkbook Import(Workbook wb, SheetsImportOptions? o = null, List<LayoutWarning>? warnings = null)
        => SheetsImporter.Import(new MemoryStream(Save(wb)), o, Options(), warnings is null ? null : warnings.Add);

    internal static Workbook Sample()
    {
        Workbook wb = new Workbook();
        Sheet s = wb.AddSheet("Charges");
        s.Cell("A1").Value = "Schedule of Charges";
        s.Cell("A1").Style = new CellStyleBuilder().Font("Cambria", 14).Bold().Foreground("1F3A60").Build();
        s.MergeCells("A1:C1");
        s.Cell("A2").Value = "Code";
        s.Cell("B2").Value = "Service";
        s.Cell("C2").Value = "Rate";
        for (int i = 0; i < 3; i++)
        {
            s.Cell(3 + i, 1).Value = "RT-" + (101 + i);
            s.Cell(3 + i, 2).Value = "Service " + i;
            s.Cell(3 + i, 3).Value = 1500.5 + i;
            s.Cell(3 + i, 3).Style = new CellStyleBuilder().Format("#,##0.00").BorderAll(SheetsBorderStyle.Thin, "FF0000").Build();
        }
        s.Cell("C6").Value = -2.5;
        s.Cell("C6").Style = new CellStyleBuilder().Format("0.00;[Red]-0.00").Build();
        s.Cell("A7").Value = true;
        s.Columns[2].Width = 30;
        s.Rows[2].Height = 24;
        return wb;
    }

    [Fact]
    public void Cells_take_the_displayed_text_kind_and_style()
    {
        GridSheet g = Assert.Single(Import(Sample()).Sheets);
        Assert.Equal("Charges", g.Name);
        GridCell title = g.GetCell(0, 0)!;
        Assert.Equal("Schedule of Charges", title.DisplayText);
        Assert.Equal("Cambria", title.Style.Font.FontFamily);
        Assert.Equal(14, title.Style.Font.FontSize);
        Assert.True(title.Style.Font.Bold);
        Assert.Equal(LayoutColor.FromHex("1F3A60"), title.Style.Font.Color);
        Assert.Contains(new CellRange(0, 0, 0, 2), g.MergedRanges);

        GridCell rate = g.GetCell(2, 2)!;
        Assert.Equal("1,500.50", rate.DisplayText);
        Assert.Equal(CellValueKind.Number, rate.Kind);
        Assert.Equal(LayoutColor.FromHex("FF0000"), rate.Style.BorderTop.Color);
        Assert.Equal(0.5, rate.Style.BorderTop.Width);

        GridCell negative = g.GetCell(5, 2)!;
        Assert.Equal("-2.50", negative.DisplayText);
        Assert.Equal(new LayoutColor(0xFF, 0, 0), negative.Style.Font.Color); // [Red] in the number format
        Assert.Equal(CellValueKind.Boolean, g.GetCell(6, 0)!.Kind);
        Assert.Equal("TRUE", g.GetCell(6, 0)!.DisplayText);

        Assert.Equal(30, g.Columns[1].Width);
        Assert.Equal(24, g.Rows[1].Height);
        Assert.Equal("Calibri", g.NormalFont.FontFamily);
    }

    [Fact]
    public void Page_setup_print_area_titles_and_breaks_are_carried_over()
    {
        Workbook wb = Sample();
        Sheet s = wb.Sheets[0];
        s.PageSetup.PaperSizeCode = 9;
        s.PageSetup.Orientation = PageOrientation.Landscape;
        s.PageSetup.Margins.Set(0.5, 0.5, 1, 1);
        s.PageSetup.FitTo(1, 0);
        s.PageSetup.SetPrintArea("A1:C6");
        s.PageSetup.PrintTitleRows = (2, 2);
        s.PageSetup.AddRowBreak(4);
        s.PageSetup.PrintGridlines = true;
        s.PageSetup.HeaderFooter.OddFooter = "&CPage &P of &N";
        GridSheet g = Import(wb).Sheets[0];
        GridPageSetup ps = g.PageSetup;
        Assert.Equal(595.3, ps.PaperWidth, 1);
        Assert.Equal(841.9, ps.PaperHeight, 1);
        Assert.True(ps.Landscape);
        Assert.Equal(36, ps.MarginLeft, 6);
        Assert.Equal(72, ps.MarginTop, 6);
        Assert.True(ps.FitToPage);
        Assert.Equal(1, ps.FitToWidth);
        Assert.Equal(0, ps.FitToHeight);
        Assert.True(ps.PrintGridlines);
        Assert.Equal("&CPage &P of &N", ps.OddFooter);
        Assert.Equal(new CellRange(0, 0, 5, 2), Assert.Single(g.PrintAreas));
        Assert.Equal((1, 1), g.PrintTitleRows);
        Assert.Contains(4, g.RowBreaks); // a new page starts at row 5 (index 4)
    }

    [Fact]
    public void Missing_paper_size_follows_the_culture_region_unless_overridden()
    {
        // D-30: like a Windows printer default. Invariant culture (no region) gives A4.
        Assert.Equal(595.3, Import(Sample()).Sheets[0].PageSetup.PaperWidth, 1);
        Assert.Equal(612, ImportWithCulture(Sample(), "en-US").Sheets[0].PageSetup.PaperWidth, 1);
        Assert.Equal(612, ImportWithCulture(Sample(), "es-MX").Sheets[0].PageSetup.PaperWidth, 1);
        Assert.Equal(595.3, ImportWithCulture(Sample(), "en-IN").Sheets[0].PageSetup.PaperWidth, 1);
        Assert.Equal(595.3, ImportWithCulture(Sample(), "en-GB").Sheets[0].PageSetup.PaperWidth, 1);
        Assert.Equal(612, Import(Sample(), new SheetsImportOptions { DefaultPaperSizeCode = 1 }).Sheets[0].PageSetup.PaperWidth, 1);
        Assert.Equal(PaperDefaults.Letter, PaperDefaults.ForCulture(new System.Globalization.CultureInfo("en")));
    }

    private static GridWorkbook ImportWithCulture(Workbook wb, string culture)
    {
        LayoutOptions o = Options();
        o.Culture = new System.Globalization.CultureInfo(culture);
        return SheetsImporter.Import(new MemoryStream(Save(wb)), null, o);
    }

    [Fact]
    public void Whole_column_print_areas_stop_at_the_last_used_row()
    {
        Workbook wb = Sample();
        wb.Sheets[0].PageSetup.SetPrintArea("B:C", "2:3");
        GridSheet g = Import(wb).Sheets[0];
        Assert.Equal(new[] { new CellRange(0, 1, 6, 2), new CellRange(1, 0, 2, 2) }, g.PrintAreas);
    }

    [Fact]
    public void Hidden_sheets_rows_and_columns()
    {
        Workbook wb = Sample();
        Sheet hidden = wb.AddSheet("Secret");
        hidden.Cell("A1").Value = "x";
        hidden.Visibility = SheetVisibility.Hidden;
        wb.Sheets[0].Rows[4].Hidden = true;
        wb.Sheets[0].Columns[2].Hidden = true;
        GridWorkbook book = Import(wb);
        GridSheet g = Assert.Single(book.Sheets);
        Assert.True(g.Rows[3].Hidden);
        Assert.True(g.Columns[1].Hidden);
        Assert.Equal(2, Import(wb, new SheetsImportOptions { IncludeHiddenSheets = true }).Sheets.Count);
        Assert.Equal("Secret", Import(wb, new SheetsImportOptions { SheetNames = new[] { "secret" }, IncludeHiddenSheets = true }).Sheets[0].Name);
    }

    [Fact]
    public void Zero_values_and_errors_print_as_the_sheet_says()
    {
        Workbook wb = new Workbook();
        Sheet s = wb.AddSheet("S");
        s.Cell("A1").Value = 0.0;
        s.Cell("A2").Value = 5.0;
        s.View.ShowZeros = false;
        GridSheet g = Import(wb).Sheets[0];
        Assert.Equal(string.Empty, g.GetCell(0, 0)!.DisplayText);
        Assert.Equal("5", g.GetCell(1, 0)!.DisplayText);
    }

    [Fact]
    public void Conditional_formats_are_applied()
    {
        Workbook wb = new Workbook();
        Sheet s = wb.AddSheet("S");
        for (int i = 1; i <= 5; i++)
        {
            s.Cell(i, 1).Value = (double)(i * 10);
            s.Cell(i, 2).Value = (double)(i * 10);
        }
        s.AddConditionalFormat("A1:A5", ConditionalFormat.CellIs(ComparisonOp.GreaterThan, 30, new CellStyleBuilder().Background("FFC7CE").Foreground("9C0006").Build()));
        s.AddConditionalFormat("B1:B5", ConditionalFormat.DataBar.Blue);
        GridSheet g = Import(wb).Sheets[0];
        Assert.Null(g.GetCell(0, 0)!.Style.Fill);
        Assert.Equal(LayoutColor.FromHex("FFC7CE"), g.GetCell(3, 0)!.Style.Fill);
        Assert.Equal(LayoutColor.FromHex("9C0006"), g.GetCell(4, 0)!.Style.Font.Color);
        GridDataBar small = g.GetCell(0, 1)!.DataBar!;
        GridDataBar large = g.GetCell(4, 1)!.DataBar!;
        Assert.True(large.Length > small.Length);
        Assert.Equal(0.9, large.Length, 3); // Excel's default maximum length is 90%
        Assert.Equal(LayoutColor.FromHex("638EC6"), large.Color);

        GridSheet plain = Import(wb, new SheetsImportOptions { ApplyConditionalFormats = false }).Sheets[0];
        Assert.Null(plain.GetCell(3, 0)!.Style.Fill);
        Assert.Null(plain.GetCell(4, 1)!.DataBar);
    }

    [Fact]
    public void Black_and_white_printing_drops_fills_and_colours()
    {
        Workbook wb = Sample();
        wb.Sheets[0].Cell("B3").Style = new CellStyleBuilder().Background("FFFF00").Foreground("00FF00").Build();
        wb.Sheets[0].PageSetup.BlackAndWhite = true;
        GridSheet g = Import(wb).Sheets[0];
        Assert.Null(g.GetCell(2, 1)!.Style.Fill);
        Assert.Equal(LayoutColor.Black, g.GetCell(2, 1)!.Style.Font.Color);
        Assert.Equal(LayoutColor.Black, g.GetCell(2, 2)!.Style.BorderTop.Color);
        Assert.Equal(LayoutColor.FromHex("FFFF00"), Import(wb, new SheetsImportOptions { HonourBlackAndWhite = false }).Sheets[0].GetCell(2, 1)!.Style.Fill);
    }

    [Fact]
    public void Open_sniffs_xlsx_and_lays_it_out()
    {
        using LaidOutDocument doc = SheetsLayout.Open(new MemoryStream(Save(Sample())), null, Options());
        Assert.Equal(LayoutSourceFormat.Xlsx, doc.Source.Format);
        Assert.Equal(1, doc.ResolvePageCount());
        string text = doc.GetPage(0).GetText();
        Assert.Contains("Schedule of Charges", text, StringComparison.Ordinal);
        Assert.Contains("1,502.50", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Encrypted_workbooks_need_the_password()
    {
        // Sample(), encrypted with password "secret" (Fixtures/encrypted/sample-secret.xlsx), read from memory only.
        byte[] enc = EncryptedFixtures.Open("sample-secret.xlsx").ToArray();
        LayoutFormatException ex = Assert.Throws<LayoutFormatException>(() => SheetsLayout.Open(new MemoryStream(enc), null, Options()));
        Assert.Contains("Password", ex.Message, StringComparison.Ordinal);
        LayoutOptions o = Options();
        o.Password = "secret";
        using LaidOutDocument doc = SheetsLayout.Open(new MemoryStream(enc), null, o);
        Assert.Contains("Schedule of Charges", doc.GetPage(0).GetText(), StringComparison.Ordinal);
        o.Password = "wrong";
        Assert.Throws<LayoutFormatException>(() => SheetsLayout.Open(new MemoryStream(enc), null, o));
    }

    [Fact]
    public void Corrupt_input_is_a_format_error()
    {
        byte[] bytes = Save(Sample());
        Array.Resize(ref bytes, bytes.Length / 2);
        Assert.Throws<LayoutFormatException>(() => SheetsLayout.Open(new MemoryStream(bytes), null, Options()));
    }

    [Fact]
    public void Image_headers_are_probed()
    {
        LayoutImage png = LayoutImage.FromEncoded(BigPng(40, 25));
        Assert.Equal((LayoutImageFormat.Png, 40, 25), (png.Format, png.PixelWidth, png.PixelHeight));
        byte[] gif = { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a', 7, 0, 3, 0, 0, 0 };
        LayoutImage g = LayoutImage.FromEncoded(gif);
        Assert.Equal((LayoutImageFormat.Gif, 7, 3), (g.Format, g.PixelWidth, g.PixelHeight));
        Assert.Equal(LayoutImageFormat.Unknown, LayoutImage.FromEncoded(new byte[] { 1, 2, 3 }).Format);
    }

    private static byte[] BigPng(int w, int h)
    {
        byte[] b = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(b, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(16), w);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(20), h);
        return b;
    }
}

/// <summary>Excel-produced workbooks from the Apache POI corpus (Fixtures/xlsx, Apache-2.0).</summary>
public class SheetsExcelFixtureTests
{
    private static LaidOutDocument Open(string file, List<LayoutWarning>? _ = null)
    {
        LayoutOptions o = Flow.Options();
        o.Now = new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.FromHours(5.5));
        using FileStream fs = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "xlsx", file));
        return SheetsLayout.Open(fs, null, o);
    }

    [Fact]
    public void SampleSS_text_and_red_font()
    {
        using LaidOutDocument doc = Open("SampleSS.xlsx");
        Assert.Equal(2, doc.ResolvePageCount());
        LaidOutPage p = doc.GetPage(0);
        Assert.Contains("Test spreadsheet", p.GetText(), StringComparison.Ordinal);
        GlyphRunOp red = p.DisplayList.OfType<GlyphRunOp>().First(r => r.Text.Contains("red", StringComparison.Ordinal));
        Assert.True(red.Color.R > 200 && red.Color.G < 80, $"red text colour {red.Color.ToHex()}");
    }

    [Fact]
    public void Border_colours_from_theme_and_index()
    {
        using LaidOutDocument doc = Open("50846-border_colours.xlsx");
        List<string> colours = doc.GetPage(0).DisplayList.OfType<StrokeLineOp>().Select(l => l.Color.ToHex()).ToList();
        // Theme accent 2 (C0504D in the Office 2007 theme) darkened by tint -0.25, and indexed/RGB red.
        Assert.Equal(new[] { "953735", "FF0000" }, colours);
    }

    [Fact]
    public void Conditional_formats_from_Excel_2010()
    {
        using LaidOutDocument doc = Open("NewStyleConditionalFormattings.xlsx");
        doc.ResolvePageCount();
        LaidOutPage p = doc.GetPage(0);
        List<FillRectOp> fills = p.DisplayList.OfType<FillRectOp>().ToList();
        Assert.True(fills.Count > 40, $"fills {fills.Count}");
        Assert.True(fills.Select(f => f.Color).Distinct().Count() > 10, "colour scales give many distinct fills");
        Assert.Contains(doc.Warnings, w => w.Code == Chuvadi.Layout.Engine.LayoutWarningCodes.IconSetApproximated);
    }

    [Fact]
    public void Icon_sets_are_carried_and_drawn()
    {
        GridWorkbook book = SheetsImporter.Import(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "xlsx", "ConditionalFormattingSamples.xlsx")), null, SheetsImportTests.Options());
        List<GridIcon> icons = book.Sheets.SelectMany(sh => sh.Cells().Select(c => c.Cell.Icon)).OfType<GridIcon>().ToList();
        Assert.NotEmpty(icons);
        Assert.Contains(icons, i => i.IconSet == "3Arrows");
        Assert.All(icons, i => Assert.InRange(i.Index, 0, i.Count - 1));
        Assert.True(icons.Select(i => i.Index).Distinct().Count() > 1, "several icons of a set are used");

        // Each icon becomes vector paths at the cell's left edge.
        GridSheet sheet = new GridSheet("Icons");
        string[] sets = { "3Arrows", "3TrafficLights1", "4Rating", "5Quarters", "3Stars", "3Triangles", "4RedToBlack" };
        for (int r = 0; r < sets.Length; r++)
        {
            for (int i = 0; i < 3; i++)
            {
                sheet.SetCell(r, i, new GridCell(string.Empty, CellValueKind.Number) { Icon = new GridIcon(sets[r], i, sets[r].StartsWith('3') ? 3 : sets[r][0] - '0') });
            }
        }
        GridWorkbook wb = new GridWorkbook();
        wb.Sheets.Add(sheet);
        using LaidOutDocument doc = DocumentLayout.Open(wb, SheetsImportTests.Options());
        LaidOutPage page = doc.GetPage(0);
        Assert.True(page.DisplayList.OfType<PathOp>().Count() >= 15);
        Assert.Contains(doc.Warnings, w => w.Code == Chuvadi.Layout.Engine.LayoutWarningCodes.IconSetApproximated);
    }

    [Fact]
    public void Right_to_left_sheets_put_column_A_on_the_right()
    {
        Workbook wb = new Workbook();
        Sheet s = wb.AddSheet("RTL");
        s.Cell("A1").Value = "First";
        s.Cell("C1").Value = "Third";
        GridSheet ltrSheet = SheetsImportTests.Import(wb).Sheets[0];
        Assert.False(ltrSheet.RightToLeft);
        s.RightToLeft = true;
        GridSheet rtlSheet = SheetsImportTests.Import(wb).Sheets[0];
        Assert.True(rtlSheet.RightToLeft);

        static (double A, double C) Positions(Workbook book)
        {
            using LaidOutDocument doc = SheetsLayout.Open(new MemoryStream(SheetsImportTests.Save(book)), null, SheetsImportTests.Options());
            List<LayoutBox> cells = doc.GetPage(0).Boxes.Where(b => b.Kind == LayoutBoxKind.SheetCell).ToList();
            return (cells.Single(b => b.Path.EndsWith("!A1", StringComparison.Ordinal)).Rect.X, cells.Single(b => b.Path.EndsWith("!C1", StringComparison.Ordinal)).Rect.X);
        }
        (double rtlA, double rtlC) = Positions(wb);
        Assert.True(rtlA > rtlC, $"A1 at {rtlA}, C1 at {rtlC}");
        s.RightToLeft = false;
        (double ltrA, double ltrC) = Positions(wb);
        Assert.True(ltrA < ltrC);
    }

    [Fact]
    public void Zero_height_sheets_show_only_rows_that_say_so()
    {
        Workbook wb = new Workbook();
        Sheet s = wb.AddSheet("Z");
        s.Cell("A1").Value = "shown";
        s.Cell("A3").Value = "also shown";
        s.Format.ZeroHeight = true;
        GridSheet g = SheetsImportTests.Import(wb).Sheets[0];
        Assert.True(g.RowsHiddenByDefault);
        Assert.False(g.Rows[0].Hidden);
        Assert.False(g.Rows[2].Hidden);
        Assert.False(g.Rows.ContainsKey(1)); // row 2 is not in the file, so it is hidden

        GridWorkbook book = new GridWorkbook();
        book.Sheets.Add(g);
        using LaidOutDocument doc = DocumentLayout.Open(book, SheetsImportTests.Options());
        List<LayoutBox> cells = doc.GetPage(0).Boxes.Where(b => b.Kind == LayoutBoxKind.SheetCell).ToList();
        double y1 = cells.Single(b => b.Path.EndsWith("!A1", StringComparison.Ordinal)).Rect.Bottom;
        double y3 = cells.Single(b => b.Path.EndsWith("!A3", StringComparison.Ordinal)).Rect.Y;
        Assert.Equal(y1, y3, 3);
    }

    [Fact]
    public void Show_formulas_view_prints_the_formulas()
    {
        Workbook wb = new Workbook();
        Sheet s = wb.AddSheet("F");
        s.Cell("A1").Value = 2.0;
        s.Cell("A2").Formula = "A1*3";
        GridSheet values = SheetsImportTests.Import(wb).Sheets[0];
        Assert.NotEqual("=A1*3", values.GetCell(1, 0)?.DisplayText);
        s.View.ShowFormulas = true;
        GridSheet formulas = SheetsImportTests.Import(wb).Sheets[0];
        GridCell f = formulas.GetCell(1, 0)!;
        Assert.Equal("=A1*3", f.DisplayText);
        Assert.Equal(CellHorizontalAlignment.Left, f.Style.HorizontalAlignment);
        Assert.Equal("2", formulas.GetCell(0, 0)!.DisplayText); // constants are unchanged
    }

    [Fact]
    public void Rich_text_hyperlinks_and_repeat_fill_from_Sheets_1_7()
    {
        Workbook wb = new Workbook();
        Sheet s = wb.AddSheet("R");
        s.Cell("A1").RichText = new RichText(new RichTextRun("plain "), new RichTextRun("bold red", new RichTextFont { Bold = true, Color = ColorRef.FromRgb("FF0000") }));
        s.Cell("A2").Value = "site";
        s.Hyperlinks.Add(new Hyperlink("A2", target: "https://example.org/"));
        s.Cell("A3").Value = "jump";
        s.Hyperlinks.Add(new Hyperlink("A3", location: "'R'!A1"));
        s.Cell("A4").Value = 1234.5;
        s.Cell("A4").Style = new CellStyleBuilder().Format("_($* #,##0.00_);_($* (#,##0.00);_($* \"-\"??_);_(@_)").Build();
        s.Columns[1].Width = 20;
        GridSheet g = SheetsImportTests.Import(wb).Sheets[0]; // through a saved file: what Excel would hand over

        GridCell rich = g.GetCell(0, 0)!;
        Assert.Equal(2, rich.Runs!.Count);
        Assert.False(rich.Runs[0].Format.Bold);
        Assert.True(rich.Runs[1].Format.Bold);
        Assert.Equal(LayoutColor.FromHex("FF0000"), rich.Runs[1].Format.Color);
        Assert.Equal("Calibri", rich.Runs[1].Format.FontFamily); // unstated: the cell font

        Assert.Equal("https://example.org/", g.GetCell(1, 0)!.HyperlinkUri);
        Assert.Equal("'R'!A1", g.GetCell(2, 0)!.HyperlinkLocation);

        GridCell money = g.GetCell(3, 0)!;
        Assert.Equal(' ', money.FillCharacter);
        Assert.Equal("$", money.DisplayText[..money.FillIndex!.Value].Trim());

        GridWorkbook book = new GridWorkbook();
        book.Sheets.Add(g);
        using LaidOutDocument doc = DocumentLayout.Open(book, SheetsImportTests.Options());
        LaidOutPage page = doc.GetPage(0);
        Assert.Contains(page.DisplayList.OfType<LinkOp>(), l => l.Uri == "https://example.org/");
        Assert.Contains(page.DisplayList.OfType<LinkOp>(), l => l.Anchor == "'R'!A1");
        // The currency sign sits at the cell's left edge and the number at its right edge.
        LayoutBox cellBox = page.Boxes.Single(b => b.Path.EndsWith("!A4", StringComparison.Ordinal));
        GlyphRunOp dollar = page.DisplayList.OfType<GlyphRunOp>().Single(r => r.Text.Contains('$'));
        GlyphRunOp number = page.DisplayList.OfType<GlyphRunOp>().Single(r => r.Text.Contains("1,234.50", StringComparison.Ordinal));
        Assert.True(dollar.X < cellBox.Rect.X + 12, "dollar at the left edge");
        Assert.True(number.X + number.Width > cellBox.Rect.Right - 12, "number at the right edge");
    }

    [Fact]
    public void Comments_print_at_end_or_as_displayed()
    {
        Workbook wb = new Workbook();
        Sheet s = wb.AddSheet("C");
        s.Cell("A1").Value = "noted";
        s.Comments.Add(new CellComment("A1", "Reviewer", "Reviewer:\nCheck this value") { Visible = true, Anchor = new CommentAnchor(2, 15, 1, 10, 4, 15, 5, 4) });
        s.PageSetup.CellComments = PrintCellComments.AtEnd;
        GridWorkbook atEnd = SheetsImporter.Import(wb, null, SheetsImportTests.Options());
        Assert.Equal(2, atEnd.Sheets.Count);
        Assert.StartsWith("Cell: A1\nComment: Reviewer:", atEnd.Sheets[1].GetCell(0, 0)!.DisplayText, StringComparison.Ordinal);

        s.PageSetup.CellComments = PrintCellComments.AsDisplayed;
        GridWorkbook shown = SheetsImporter.Import(wb, null, SheetsImportTests.Options());
        GridDrawing box = Assert.Single(shown.Sheets[0].Drawings);
        Assert.Equal(GridDrawingKind.Shape, box.Kind);
        Assert.Contains("Check this value", box.Text, StringComparison.Ordinal);
        Assert.Equal(1, box.From!.Value.Column);
    }

    [Fact]
    public void Printer_specific_paper_codes_fall_back_to_the_culture_paper()
    {
        Workbook wb = SheetsImportTests.Sample();
        wb.Sheets[0].PageSetup.PaperSizeCode = 256; // a driver-defined code
        List<LayoutWarning> warnings = new List<LayoutWarning>();
        LayoutOptions india = SheetsImportTests.Options();
        india.Culture = new System.Globalization.CultureInfo("en-IN");
        GridWorkbook a4 = SheetsImporter.Import(new MemoryStream(SheetsImportTests.Save(wb)), null, india, warnings.Add);
        Assert.Equal(595.3, a4.Sheets[0].PageSetup.PaperWidth, 1);
        Assert.Contains(warnings, w => w.Message.Contains("256", StringComparison.Ordinal));
        LayoutOptions us = SheetsImportTests.Options();
        us.Culture = new System.Globalization.CultureInfo("en-US");
        Assert.Equal(612, SheetsImporter.Import(new MemoryStream(SheetsImportTests.Save(wb)), null, us).Sheets[0].PageSetup.PaperWidth, 1);
    }

    [Fact]
    public void Flipped_lines_run_between_the_flipped_corners()
    {
        GridSheet sheet = new GridSheet("L");
        sheet.SetCell(0, 0, new GridCell("x"));
        sheet.Drawings.Add(new GridDrawing(GridDrawingKind.Shape) { Geometry = "line", X = 10, Y = 10, Width = 100, Height = 50, Line = LayoutColor.Black, FlipVertical = true });
        GridWorkbook wb = new GridWorkbook();
        wb.Sheets.Add(sheet);
        using LaidOutDocument doc = DocumentLayout.Open(wb, SheetsImportTests.Options());
        StrokeLineOp line = doc.GetPage(0).DisplayList.OfType<StrokeLineOp>().Single(l => l.X2 - l.X1 > 50);
        Assert.True(line.Y1 > line.Y2, "a vertically flipped line rises from left to right");
    }

    [Fact]
    public void Pictures_are_drawn()
    {
        using LaidOutDocument doc = Open("WithDrawing.xlsx");
        int images = doc.GetPages().Sum(p => p.DisplayList.OfType<ImageOp>().Count());
        Assert.True(images >= 2, $"images {images}");
    }

    [Fact]
    public void Charts_become_placeholders_with_a_warning()
    {
        using LaidOutDocument doc = Open("WithChart.xlsx");
        doc.ResolvePageCount();
        foreach (LaidOutPage _ in doc.GetPages())
        {
        }
        Assert.Contains(doc.Warnings, w => w.Code == "ChartPlaceholder");
    }

    [Fact]
    public void Many_sheets_with_shapes_lay_out()
    {
        using LaidOutDocument doc = Open("ConditionalFormattingSamples.xlsx");
        Assert.True(doc.ResolvePageCount() >= 20);
        Assert.Contains(doc.GetPage(0).DisplayList, op => op is PathOp);
    }

    [Fact]
    public void Backslash_part_names_are_sniffed_as_xlsx()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "xlsx", "49609.xlsx"));
        Assert.Equal(LayoutSourceFormat.Xlsx, DocumentLayout.Sniff(bytes));
        // Since Chuvadi.Sheets 1.7.0 part names match case-insensitively ("[content_types].xml") and '\\' separates.
        using LaidOutDocument doc = Open("49609.xlsx");
        Assert.True(doc.ResolvePageCount() >= 1);
    }
}
