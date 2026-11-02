using System;
using System.Collections.Generic;
using System.Globalization;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.ManualTests
{
    internal static partial class PreviewScenarios
    {
        internal static GridWorkbook GridShowcase()
        {
            GridWorkbook book = new GridWorkbook { Title = "Grid showcase" };
            GridSheet s = new GridSheet("Charges");
            book.Sheets.Add(s);
            BorderLine thin = new BorderLine(0.5, LayoutColor.Black);
            GridCellStyle title = new GridCellStyle
            {
                Font = new CharacterFormat { FontFamily = "Calibri", FontSize = 16, Bold = true, Color = new LayoutColor(0x1F, 0x3A, 0x60) },
                HorizontalAlignment = CellHorizontalAlignment.Center,
                VerticalAlignment = CellVerticalAlignmentKind.Center,
            };
            GridCellStyle head = new GridCellStyle
            {
                Font = new CharacterFormat { FontFamily = "Calibri", FontSize = 11, Bold = true, Color = LayoutColor.White },
                Fill = new LayoutColor(0x1F, 0x3A, 0x60),
                BorderBottom = thin, BorderTop = thin, BorderLeft = thin, BorderRight = thin,
                HorizontalAlignment = CellHorizontalAlignment.Center,
                VerticalAlignment = CellVerticalAlignmentKind.Center,
                WrapText = true,
            };
            GridCellStyle cell = new GridCellStyle { BorderBottom = thin, BorderTop = thin, BorderLeft = thin, BorderRight = thin };
            GridCellStyle wrap = cell with { WrapText = true, VerticalAlignment = CellVerticalAlignmentKind.Top };
            GridCellStyle money = cell with { };
            GridCellStyle zebra = cell with { Fill = new LayoutColor(0xF2, 0xF6, 0xFC) };

            s.Columns[0] = new GridColumn(8.7109375);
            s.Columns[1] = new GridColumn(34.7109375);
            s.Columns[2] = new GridColumn(10.7109375);
            s.Columns[3] = new GridColumn(12.7109375);
            s.Columns[4] = new GridColumn(14.7109375);
            s.Rows[0] = new GridRow(30);
            s.Rows[2] = new GridRow(30);
            s.Set(0, 0, "Schedule of Charges \u2014 Radiation Oncology", style: title);
            s.MergedRanges.Add(CellRange.Parse("A1:E1"));
            s.Set(1, 0, "Prices in INR, inclusive of consumables. Overflowing note text spills into empty cells to its right.");
            string[] heads = { "Code", "Service", "Unit", "Rate", "Package rate" };
            for (int c = 0; c < heads.Length; c++)
            {
                s.Set(2, c, heads[c], style: head);
            }
            string[] services =
            {
                "Consultation", "CT simulation", "IMRT planning with dosimetric verification and patient-specific QA",
                "IMRT fraction", "VMAT fraction", "SRS single fraction", "Brachytherapy HDR session",
                "Weekly port film", "Mould room immobilisation (thermoplastic mask)", "Follow-up visit",
            };
            for (int i = 0; i < 64; i++)
            {
                int r = 3 + i;
                GridCellStyle st = i % 2 == 1 ? zebra : cell;
                s.Set(r, 0, "RT-" + (101 + i).ToString(CultureInfo.InvariantCulture), style: st);
                s.Set(r, 1, services[i % services.Length], style: st with { WrapText = true, VerticalAlignment = CellVerticalAlignmentKind.Top });
                s.Set(r, 2, i % 3 == 0 ? "per visit" : "each", style: st with { HorizontalAlignment = CellHorizontalAlignment.Center });
                double rate = 1500 + (i * 875.5);
                s.Set(r, 3, rate.ToString("#,##0.00", CultureInfo.InvariantCulture), CellValueKind.Number, st);
                s.Set(r, 4, i == 5 ? "12345678901.00" : (rate * 9.5).ToString("#,##0.00", CultureInfo.InvariantCulture), CellValueKind.Number, st);
            }
            s.PrintTitleRows = (2, 2);
            s.PageSetup = new GridPageSetup
            {
                PrintGridlines = false,
                CenterHorizontally = true,
                OddHeader = "&L&\"Calibri,Bold\"Example Medical Center&R&A",
                OddFooter = "&LPrinted &D&CPage &P of &N&R&F",
            };

            GridSheet g = new GridSheet("Gridlines");
            book.Sheets.Add(g);
            for (int r = 0; r < 12; r++)
            {
                for (int c = 0; c < 6; c++)
                {
                    g.Set(r, c, (r * 6) + c < 3 ? "label" : ((r + 1) * (c + 1)).ToString(CultureInfo.InvariantCulture), (r * 6) + c < 3 ? CellValueKind.Text : CellValueKind.Number);
                }
            }
            g.PageSetup = new GridPageSetup { PrintGridlines = true, PrintHeadings = true, Landscape = true, OddFooter = "&C&P / &N" };
            return book;
        }

        internal static LayoutOptions XlsxOptions()
        {
            LayoutOptions o = Options();
            o.DocumentName = "charges.xlsx";
            return o;
        }

        private static partial IEnumerable<(string, Func<LaidOutDocument>)> LaterScenarios()
        {
            yield return ("grid-showcase", () => DocumentLayout.Open(GridShowcase(), Options()));
            yield return ("table-showcase", () => DocumentLayout.Open(TableShowcase(), Options()));
            yield return ("wrap-showcase", () => DocumentLayout.Open(WrapShowcase(), Options()));
            yield return ("nested-split", () => DocumentLayout.Open(NestedSplitShowcase(), Options()));
            yield return ("xlsx-import", () => Chuvadi.Layout.Sheets.SheetsLayout.Open(new System.IO.MemoryStream(XlsxShowcase()), null, XlsxOptions()));
            yield return ("docx-import", () => Chuvadi.Layout.Docs.DocsLayout.Open(new System.IO.MemoryStream(DocxShowcase()), null, DocxOptions()));
        }
    }
}
