using System;
using System.Collections.Generic;
using Chuvadi.Sheets.Model;
using SheetsBorder = Chuvadi.Sheets.Model.BorderStyle;

namespace Chuvadi.Layout.ReferenceCorpus
{
    /// <summary>The xlsx cases: one feature family each, built with Chuvadi.Sheets. Paper is stated (A4) unless the case is about paper.</summary>
    internal static class XlsxCases
    {
        private static Sheet NewSheet(Workbook wb, string name, int paper = 9)
        {
            Sheet s = wb.AddSheet(name);
            s.PageSetup.PaperSizeCode = paper;
            return s;
        }

        /// <summary>A header row and <paramref name="rows"/> rows of a small register.</summary>
        private static void Register(Sheet s, int rows, int firstRow = 1)
        {
            CellStyle head = new CellStyleBuilder().Bold().Background("DDE7F5").BorderAll(SheetsBorder.Thin, "808080").Build();
            CellStyle money = new CellStyleBuilder().Format("#,##0.00").BorderAll(SheetsBorder.Thin, "BFBFBF").Build();
            CellStyle text = new CellStyleBuilder().BorderAll(SheetsBorder.Thin, "BFBFBF").Build();
            string[] heads = { "Code", "Service", "Department", "Qty", "Rate", "Amount" };
            for (int c = 0; c < heads.Length; c++)
            {
                s.Cell(firstRow, c + 1).Value = heads[c];
                s.Cell(firstRow, c + 1).Style = head;
            }
            for (int r = 0; r < rows; r++)
            {
                int row = firstRow + 1 + r;
                s.Cell(row, 1).Value = "SV-" + (1001 + r);
                s.Cell(row, 1).Style = text;
                s.Cell(row, 2).Value = Sample.Sentence(r, 3 + (r % 3));
                s.Cell(row, 2).Style = text;
                s.Cell(row, 3).Value = new[] { "Radiology", "Oncology", "Cardiology", "Pharmacy" }[r % 4];
                s.Cell(row, 3).Style = text;
                s.Cell(row, 4).Value = (r % 7) + 1;
                s.Cell(row, 4).Style = text;
                s.Cell(row, 5).Value = 150.5 * ((r % 11) + 1);
                s.Cell(row, 5).Style = money;
                s.Cell(row, 6).Value = 150.5 * ((r % 11) + 1) * ((r % 7) + 1);
                s.Cell(row, 6).Style = money;
            }
            s.Columns[1].Width = 10;
            s.Columns[2].Width = 30;
            s.Columns[3].Width = 14;
            s.Columns[4].Width = 6;
            s.Columns[5].Width = 12;
            s.Columns[6].Width = 14;
        }

        public static IEnumerable<Case> All()
        {
            yield return new Case("x01-number-formats", "number formats", "general, fixed, thousands, currency, percent, scientific, fractions, dates, times, text, custom with colours", () =>
            {
                Workbook wb = new Workbook();
                Sheet s = NewSheet(wb, "Formats");
                string[] formats = { "General", "0", "0.00", "#,##0", "#,##0.00", "\u20B9 #,##0.00", "0%", "0.00%", "0.00E+00", "# ?/?", "dd/mm/yyyy", "d-mmm-yy", "mmmm d, yyyy", "hh:mm:ss", "h:mm AM/PM", "@", "0.00;[Red]-0.00", "[Blue]#,##0;[Red]-#,##0;\"zero\"", "000000", "#,##0,\"K\"" };
                double[] values = { 1234.5678, -42.25, 0, 0.375, 45567.75, 1234567.891 };
                s.Cell(1, 1).Value = "Format";
                for (int v = 0; v < values.Length; v++)
                {
                    s.Cell(1, v + 2).Value = values[v];
                }
                for (int f = 0; f < formats.Length; f++)
                {
                    s.Cell(f + 2, 1).Value = formats[f];
                    for (int v = 0; v < values.Length; v++)
                    {
                        s.Cell(f + 2, v + 2).Value = values[v];
                        s.Cell(f + 2, v + 2).Style = new CellStyleBuilder().Format(formats[f]).Build();
                    }
                }
                s.Columns[1].Width = 28;
                for (int c = 2; c <= 7; c++)
                {
                    s.Columns[c].Width = 16;
                }
                return wb;
            });
            yield return new Case("x02-accounting", "number formats", "accounting formats with fill (repeat) characters and alignment", () =>
            {
                Workbook wb = new Workbook();
                Sheet s = NewSheet(wb, "Accounting");
                string[] formats = { "_(* #,##0.00_);_(* (#,##0.00);_(* \"-\"??_);_(@_)", "_(\"$\"* #,##0.00_);_(\"$\"* (#,##0.00);_(\"$\"* \"-\"??_);_(@_)", "_-\u20B9* #,##0.00_-;-\u20B9* #,##0.00_-;_-\u20B9* \"-\"??_-;_-@_-", "**0.00", "@*." };
                double[] values = { 1234.5, -987.25, 0, 12 };
                for (int f = 0; f < formats.Length; f++)
                {
                    for (int v = 0; v < values.Length; v++)
                    {
                        Cell c = s.Cell(f + 1, v + 1);
                        if (formats[f] == "@*.")
                        {
                            c.Value = "Label " + v;
                        }
                        else
                        {
                            c.Value = values[v];
                        }
                        c.Style = new CellStyleBuilder().Format(formats[f]).Build();
                    }
                }
                for (int c = 1; c <= 4; c++)
                {
                    s.Columns[c].Width = 18;
                }
                return wb;
            });
            yield return new Case("x03-fonts-alignment", "fonts and alignment", "fonts, sizes, bold/italic/underline, horizontal and vertical alignment, wrap, Tamil", () =>
            {
                Workbook wb = new Workbook();
                Sheet s = NewSheet(wb, "Fonts");
                string[] fonts = { "Calibri", "Cambria", "Arial", "Times New Roman", "Courier New" };
                for (int i = 0; i < fonts.Length; i++)
                {
                    s.Cell(i + 1, 1).Value = fonts[i] + " " + (9 + (i * 2));
                    s.Cell(i + 1, 1).Style = new CellStyleBuilder().Font(fonts[i], 9 + (i * 2)).Bold(i % 2 == 0).Italic(i % 3 == 0).Underline(i == 4).Build();
                }
                HorizontalAlign[] h = { HorizontalAlign.Left, HorizontalAlign.Center, HorizontalAlign.Right, HorizontalAlign.Justify };
                VerticalAlign[] v = { VerticalAlign.Top, VerticalAlign.Center, VerticalAlign.Bottom, VerticalAlign.Top };
                for (int i = 0; i < 4; i++)
                {
                    Cell c = s.Cell(7, i + 1);
                    c.Value = Sample.Sentence(i, 6);
                    c.Style = new CellStyleBuilder().HAlign(h[i]).VAlign(v[i]).WrapText().BorderAll(SheetsBorder.Thin, "808080").Build();
                }
                s.Rows[7].Height = 60;
                s.Cell(9, 1).Value = Sample.Tamil1;
                s.Cell(10, 1).Value = Sample.Tamil2;
                s.Columns[1].Width = 30;
                s.Columns[2].Width = 20;
                s.Columns[3].Width = 20;
                s.Columns[4].Width = 20;
                return wb;
            });
            yield return new Case("x04-merges", "merges", "merged title, merged blocks with centred and wrapped text", () =>
            {
                Workbook wb = new Workbook();
                Sheet s = NewSheet(wb, "Merges");
                s.Cell("A1").Value = "Schedule of Charges 2026";
                s.Cell("A1").Style = new CellStyleBuilder().Font("Cambria", 16).Bold().HAlign(HorizontalAlign.Center).Build();
                s.MergeCells("A1:F1");
                Register(s, 12, 3);
                s.Cell("A17").Value = Sample.Paragraph(3, 2);
                s.Cell("A17").Style = new CellStyleBuilder().WrapText().VAlign(VerticalAlign.Top).BorderAll(SheetsBorder.Medium, "000000").Build();
                s.MergeCells("A17:F20");
                s.Cell("H3").Value = "Vertical";
                s.Cell("H3").Style = new CellStyleBuilder().VAlign(VerticalAlign.Center).HAlign(HorizontalAlign.Center).Build();
                s.MergeCells("H3:H10");
                return wb;
            });
            yield return new Case("x05-borders", "borders", "every border style on boxes and single edges", () =>
            {
                Workbook wb = new Workbook();
                Sheet s = NewSheet(wb, "Borders");
                SheetsBorder[] styles = (SheetsBorder[])Enum.GetValues(typeof(SheetsBorder));
                for (int i = 1; i < styles.Length; i++)
                {
                    s.Cell((i * 2) - 1, 2).Value = styles[i].ToString();
                    s.Cell((i * 2) - 1, 2).Style = new CellStyleBuilder().BorderAll(styles[i], "000000").Build();
                    s.Cell((i * 2) - 1, 4).Value = "bottom";
                    s.Cell((i * 2) - 1, 4).Style = new CellStyleBuilder().BorderBottom(styles[i], "C00000").Build();
                }
                s.Columns[2].Width = 22;
                s.Columns[4].Width = 14;
                return wb;
            });
            yield return new Case("x06-fills", "fills", "solid fills in theme-like colours and font colours on fills", () =>
            {
                Workbook wb = new Workbook();
                Sheet s = NewSheet(wb, "Fills");
                string[] colours = { "FFC7CE", "C6EFCE", "FFEB9C", "DDEBF7", "1F3864", "000000", "FFFFFF", "F4B084", "A9D08E", "BDD7EE" };
                for (int i = 0; i < colours.Length; i++)
                {
                    for (int c = 0; c < 4; c++)
                    {
                        Cell cell = s.Cell(i + 1, c + 1);
                        cell.Value = "#" + colours[i];
                        cell.Style = new CellStyleBuilder().Background(colours[i]).Foreground(i is 4 or 5 ? "FFFFFF" : "000000").Bold(c == 0).Build();
                    }
                }
                return wb;
            });
            yield return new Case("x07-conditional", "conditional formats", "cell-is rules, a three-colour scale and data bars", () =>
            {
                Workbook wb = new Workbook();
                Sheet s = NewSheet(wb, "Conditional");
                for (int r = 1; r <= 20; r++)
                {
                    s.Cell(r, 1).Value = (r * 37) % 100;
                    s.Cell(r, 2).Value = (r * 53) % 100;
                    s.Cell(r, 3).Value = (r * 29) % 100;
                }
                s.AddConditionalFormat("A1:A20", ConditionalFormat.CellIs(ComparisonOp.GreaterThan, 60, new CellStyleBuilder().Background("FFC7CE").Foreground("9C0006").Build()));
                s.AddConditionalFormat("A1:A20", ConditionalFormat.CellIs(ComparisonOp.LessThan, 20, new CellStyleBuilder().Background("C6EFCE").Foreground("006100").Build()));
                s.AddConditionalFormat("B1:B20", ConditionalFormat.ColorScale.RedWhiteGreen);
                s.AddConditionalFormat("C1:C20", ConditionalFormat.DataBar.Blue);
                s.Columns[3].Width = 20;
                return wb;
            });
            yield return new Case("x08-print-area", "print areas and titles", "a print area that leaves out rows and columns", () =>
            {
                Workbook wb = new Workbook();
                Sheet s = NewSheet(wb, "Area");
                Register(s, 40);
                s.Cell("H1").Value = "Outside the print area";
                s.PageSetup.SetPrintArea("A1:D20");
                return wb;
            });
            yield return new Case("x09-print-titles", "print areas and titles", "a long register with the header row repeated on every page", () =>
            {
                Workbook wb = new Workbook();
                Sheet s = NewSheet(wb, "Titles");
                Register(s, 160);
                s.PageSetup.PrintTitleRows = (1, 1);
                return wb;
            });
            yield return new Case("x10-page-breaks", "page breaks", "manual row and column breaks", () =>
            {
                Workbook wb = new Workbook();
                Sheet s = NewSheet(wb, "Breaks");
                Register(s, 60);
                s.PageSetup.AddRowBreak(15);
                s.PageSetup.AddRowBreak(40);
                s.PageSetup.AddColumnBreak(3);
                return wb;
            });
            yield return new Case("x11-fit-width", "fit to page", "fit to one page wide, as many tall as needed", () =>
            {
                Workbook wb = new Workbook();
                Sheet s = NewSheet(wb, "FitWide");
                Register(s, 120);
                for (int c = 7; c <= 16; c++)
                {
                    s.Cell(1, c).Value = "Extra " + c;
                    for (int r = 2; r <= 121; r++)
                    {
                        s.Cell(r, c).Value = r * c;
                    }
                }
                s.PageSetup.FitTo(1, 0);
                return wb;
            });
            yield return new Case("x12-fit-one-page", "fit to page", "a wide, long block fitted to one page", () =>
            {
                Workbook wb = new Workbook();
                Sheet s = NewSheet(wb, "FitOne");
                Register(s, 90);
                s.PageSetup.FitTo(1, 1);
                return wb;
            });
            yield return new Case("x13-wide-sheet", "wide sheets", "30 columns across several pages, down then over", () =>
            {
                Workbook wb = new Workbook();
                Sheet s = NewSheet(wb, "Wide");
                for (int c = 1; c <= 30; c++)
                {
                    s.Cell(1, c).Value = "Column " + c;
                    s.Cell(1, c).Style = new CellStyleBuilder().Bold().Build();
                    s.Columns[c].Width = 12;
                    for (int r = 2; r <= 70; r++)
                    {
                        s.Cell(r, c).Value = (r * 100) + c;
                    }
                }
                return wb;
            });
            yield return new Case("x14-wide-over-then-down", "wide sheets", "the same wide sheet printed over, then down", () =>
            {
                Workbook wb = new Workbook();
                Sheet s = NewSheet(wb, "WideOver");
                for (int c = 1; c <= 30; c++)
                {
                    s.Cell(1, c).Value = "Column " + c;
                    s.Columns[c].Width = 12;
                    for (int r = 2; r <= 70; r++)
                    {
                        s.Cell(r, c).Value = (r * 100) + c;
                    }
                }
                s.PageSetup.PageOrder = PageOrder.OverThenDown;
                return wb;
            });
            yield return new Case("x15-long-sheet", "page breaks", "300 rows with automatic page breaks", () =>
            {
                Workbook wb = new Workbook();
                Sheet s = NewSheet(wb, "Long");
                Register(s, 300);
                return wb;
            });
            yield return new Case("x16-landscape", "page setup", "A4 landscape", () =>
            {
                Workbook wb = new Workbook();
                Sheet s = NewSheet(wb, "Landscape");
                Register(s, 60);
                s.PageSetup.Orientation = PageOrientation.Landscape;
                return wb;
            });
            yield return new Case("x17-letter", "page setup", "US Letter paper with wide margins", () =>
            {
                Workbook wb = new Workbook();
                Sheet s = NewSheet(wb, "Letter", 1);
                Register(s, 60);
                s.PageSetup.Margins.Set(1.0, 1.0, 1.0, 1.0);
                return wb;
            });
            yield return new Case("x18-header-footer", "page setup", "header and footer with sheet name, page X of Y and date-free text", () =>
            {
                Workbook wb = new Workbook();
                Sheet s = NewSheet(wb, "Report");
                Register(s, 100);
                s.PageSetup.HeaderFooter.OddHeader = "&L&\"Arial,Bold\"Example Medical Center&C&A&RConfidential";
                s.PageSetup.HeaderFooter.OddFooter = "&LPrinted copy&CPage &P of &N";
                return wb;
            });
            yield return new Case("x19-gridlines-headings", "page setup", "printed gridlines and row/column headings", () =>
            {
                Workbook wb = new Workbook();
                Sheet s = NewSheet(wb, "Grid");
                for (int r = 1; r <= 40; r++)
                {
                    for (int c = 1; c <= 6; c++)
                    {
                        s.Cell(r, c).Value = (r * c).ToString();
                    }
                }
                s.PageSetup.PrintGridlines = true;
                s.PageSetup.PrintHeadings = true;
                return wb;
            });
            yield return new Case("x20-widths-heights", "fonts and alignment", "column widths from 2 to 60 and row heights from 8 to 60", () =>
            {
                Workbook wb = new Workbook();
                Sheet s = NewSheet(wb, "Sizes");
                double[] widths = { 2, 5, 8.43, 12, 20, 35, 60 };
                for (int c = 0; c < widths.Length; c++)
                {
                    s.Columns[c + 1].Width = widths[c];
                    s.Cell(1, c + 1).Value = "w" + widths[c];
                }
                double[] heights = { 8, 12, 15, 20, 30, 45, 60 };
                for (int r = 0; r < heights.Length; r++)
                {
                    s.Rows[r + 2].Height = heights[r];
                    s.Cell(r + 2, 1).Value = "h" + heights[r];
                    s.Cell(r + 2, 3).Value = Sample.Sentence(r, 4);
                }
                return wb;
            });
            yield return new Case("x21-hidden", "fonts and alignment", "hidden rows and columns are not printed", () =>
            {
                Workbook wb = new Workbook();
                Sheet s = NewSheet(wb, "Hidden");
                Register(s, 40);
                s.Columns[3].Hidden = true;
                for (int r = 10; r <= 20; r++)
                {
                    s.Rows[r].Hidden = true;
                }
                return wb;
            });
            yield return new Case("x22-overflow", "fonts and alignment", "long text overflowing into empty neighbours, stopped by filled cells; right-aligned overflow", () =>
            {
                Workbook wb = new Workbook();
                Sheet s = NewSheet(wb, "Overflow");
                for (int r = 1; r <= 12; r++)
                {
                    s.Cell(r, 1).Value = Sample.Sentence(r, 6 + r);
                    if (r % 3 == 0)
                    {
                        s.Cell(r, 3).Value = "stop";
                    }
                }
                s.Cell(14, 4).Value = "Right aligned text that overflows to the left";
                s.Cell(14, 4).Style = new CellStyleBuilder().HAlign(HorizontalAlign.Right).Build();
                s.Cell(15, 3).Value = "Centred text spilling both ways";
                s.Cell(15, 3).Style = new CellStyleBuilder().HAlign(HorizontalAlign.Center).Build();
                return wb;
            });
            yield return new Case("x23-wrap-tall", "fonts and alignment", "wrapped text in tall rows with explicit heights", () =>
            {
                Workbook wb = new Workbook();
                Sheet s = NewSheet(wb, "Wrap");
                s.Columns[1].Width = 25;
                s.Columns[2].Width = 40;
                for (int r = 1; r <= 20; r++)
                {
                    s.Cell(r, 1).Value = "Row " + r;
                    s.Cell(r, 2).Value = Sample.Paragraph(r, 1 + (r % 3));
                    s.Cell(r, 2).Style = new CellStyleBuilder().WrapText().VAlign(VerticalAlign.Top).BorderAll(SheetsBorder.Thin, "A6A6A6").Build();
                    s.Rows[r].Height = 15 * (1 + (r % 4));
                }
                return wb;
            });
            yield return new Case("x24-multi-sheet", "page setup", "three sheets with different setups printed in order", () =>
            {
                Workbook wb = new Workbook();
                Sheet a = NewSheet(wb, "Summary");
                Register(a, 20);
                Sheet b = NewSheet(wb, "Details");
                Register(b, 80);
                b.PageSetup.Orientation = PageOrientation.Landscape;
                Sheet c = NewSheet(wb, "Notes");
                for (int r = 1; r <= 10; r++)
                {
                    c.Cell(r, 1).Value = Sample.Sentence(r + 50, 10);
                }
                return wb;
            });
            yield return new Case("x25-scale-center", "fit to page", "75% scaling, centred horizontally and vertically", () =>
            {
                Workbook wb = new Workbook();
                Sheet s = NewSheet(wb, "Scaled");
                Register(s, 30);
                s.PageSetup.Scale = 75;
                s.PageSetup.CenterHorizontally = true;
                s.PageSetup.CenterVertically = true;
                return wb;
            });
        }
    }
}
