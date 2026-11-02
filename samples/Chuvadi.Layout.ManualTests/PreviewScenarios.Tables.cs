using System.Globalization;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.ManualTests
{
    internal static partial class PreviewScenarios
    {
        internal static FlowDocument TableShowcase()
        {
            BorderLine thin = new BorderLine(0.5, new LayoutColor(0x40, 0x40, 0x40));
            CharacterFormat head = Body with { Bold = true, Color = LayoutColor.White };
            FlowDocument d = new FlowDocument();
            FlowSection s = new FlowSection();
            d.Sections.Add(s);
            s.Blocks.Add(FlowParagraph.Of("Tables", Body with { FontFamily = "Cambria", FontSize = 18, Bold = true }, new ParagraphFormat { SpaceAfter = 6 }));
            s.Blocks.Add(FlowParagraph.Of("A long table repeats its header row on every page, rows split at the page edge, cells span columns and rows, and tables nest.", Body, new ParagraphFormat { SpaceAfter = 8 }));

            FlowTable t = new FlowTable
            {
                Format = new TableFormat { BorderTop = thin, BorderBottom = thin, BorderLeft = thin, BorderRight = thin, BorderInsideHorizontal = thin, BorderInsideVertical = thin, CellMarginTop = 2, CellMarginBottom = 2 },
            };
            foreach (double w in new[] { 60.0, 190, 90, 110 })
            {
                t.ColumnWidths.Add(w);
            }
            FlowTableRow hr = new FlowTableRow { IsHeader = true };
            foreach (string h in new[] { "Code", "Description", "Unit", "Amount (INR)" })
            {
                FlowTableCell c = new FlowTableCell { Background = new LayoutColor(0x1F, 0x3A, 0x60), VerticalAlignment = CellVerticalAlignment.Center };
                c.Blocks.Add(FlowParagraph.Of(h, head, new ParagraphFormat { Alignment = h.StartsWith("Amount", System.StringComparison.Ordinal) ? ParagraphAlignment.Right : ParagraphAlignment.Left }));
                hr.Cells.Add(c);
            }
            t.Rows.Add(hr);

            FlowTableRow group = new FlowTableRow();
            FlowTableCell g = new FlowTableCell { ColumnSpan = 4, Background = new LayoutColor(0xDD, 0xEB, 0xF7) };
            g.Blocks.Add(FlowParagraph.Of("Group A \u2014 spans all four columns", Body with { Bold = true }));
            group.Cells.Add(g);
            t.Rows.Add(group);

            FlowTableRow m1 = new FlowTableRow();
            FlowTableCell merged = new FlowTableCell { VerticalMerge = VerticalMerge.Restart, VerticalAlignment = CellVerticalAlignment.Center };
            merged.Blocks.Add(FlowParagraph.Of("A-1", Body with { Bold = true }));
            m1.Cells.Add(merged);
            m1.Cells.Add(Cell("Vertically merged code cell (rows 1-3)"));
            m1.Cells.Add(Cell("each"));
            m1.Cells.Add(Cell("1,200.00", ParagraphAlignment.Right));
            t.Rows.Add(m1);
            for (int k = 0; k < 2; k++)
            {
                FlowTableRow mr = new FlowTableRow();
                mr.Cells.Add(new FlowTableCell { VerticalMerge = VerticalMerge.Continue });
                mr.Cells.Add(Cell("Continuation row " + (k + 2).ToString(CultureInfo.InvariantCulture)));
                mr.Cells.Add(Cell("each"));
                mr.Cells.Add(Cell("850.00", ParagraphAlignment.Right));
                t.Rows.Add(mr);
            }

            FlowTableRow nestRow = new FlowTableRow();
            nestRow.Cells.Add(Cell("A-4"));
            FlowTableCell nestCell = new FlowTableCell();
            nestCell.Blocks.Add(FlowParagraph.Of("Nested breakdown:", Body with { Italic = true }));
            FlowTable inner = new FlowTable { Format = new TableFormat { BorderTop = thin, BorderBottom = thin, BorderLeft = thin, BorderRight = thin, BorderInsideHorizontal = thin, BorderInsideVertical = thin } };
            inner.ColumnWidths.Add(100);
            inner.ColumnWidths.Add(70);
            foreach ((string a, string b) in new[] { ("Planning", "2,000"), ("Imaging", "1,500") })
            {
                FlowTableRow ir = new FlowTableRow();
                ir.Cells.Add(Cell(a));
                ir.Cells.Add(Cell(b, ParagraphAlignment.Right));
                inner.Rows.Add(ir);
            }
            nestCell.Blocks.Add(inner);
            nestRow.Cells.Add(nestCell);
            nestRow.Cells.Add(Cell("package"));
            nestRow.Cells.Add(Cell("3,500.00", ParagraphAlignment.Right));
            t.Rows.Add(nestRow);

            for (int i = 5; i <= 70; i++)
            {
                FlowTableRow r = new FlowTableRow();
                r.Cells.Add(Cell("A-" + i.ToString(CultureInfo.InvariantCulture)));
                string desc = i % 9 == 0
                    ? "A long description that wraps over several lines inside its cell, so that the row is tall and may need to split where the page ends. " + string.Concat(System.Linq.Enumerable.Repeat("More text. ", 25))
                    : "Line item " + i.ToString(CultureInfo.InvariantCulture);
                r.Cells.Add(Cell(desc));
                r.Cells.Add(Cell(i % 2 == 0 ? "each" : "per visit"));
                r.Cells.Add(Cell((i * 137.25).ToString("#,##0.00", CultureInfo.InvariantCulture), ParagraphAlignment.Right));
                if (i % 2 == 1)
                {
                    foreach (FlowTableCell c in r.Cells)
                    {
                        c.Background = new LayoutColor(0xF5, 0xF5, 0xF5);
                    }
                }
                t.Rows.Add(r);
            }
            s.Blocks.Add(t);
            s.Blocks.Add(FlowParagraph.Of("End of table.", Body, new ParagraphFormat { SpaceBefore = 8 }));
            return d;
        }

        private static FlowTableCell Cell(string text, ParagraphAlignment align = ParagraphAlignment.Left)
        {
            FlowTableCell c = new FlowTableCell();
            c.Blocks.Add(FlowParagraph.Of(text, Body, new ParagraphFormat { Alignment = align }));
            return c;
        }
    }
}
