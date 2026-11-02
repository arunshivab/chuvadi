using System.Globalization;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.ManualTests
{
    internal static partial class PreviewScenarios
    {
        /// <summary>A layout table whose cell holds a long nested table: the outer row splits between the nested rows.</summary>
        internal static FlowDocument NestedSplitShowcase()
        {
            BorderLine thin = new BorderLine(0.5, new LayoutColor(0x40, 0x40, 0x40));
            BorderLine outerLine = new BorderLine(1.5, new LayoutColor(0x1F, 0x3A, 0x60));
            FlowDocument d = new FlowDocument();
            FlowSection s = new FlowSection();
            d.Sections.Add(s);
            s.Blocks.Add(FlowParagraph.Of("Nested table across pages", Body with { FontFamily = "Cambria", FontSize = 18, Bold = true }, new ParagraphFormat { SpaceAfter = 6 }));
            for (int i = 0; i < 3; i++)
            {
                s.Blocks.Add(FlowParagraph.Of("The outer table is a two-column layout table. Its right cell holds a nested price list that is longer than the rest of the page, so the outer row splits between nested rows.", Body, new ParagraphFormat { SpaceAfter = 8 }));
            }

            FlowTable inner = new FlowTable
            {
                Format = new TableFormat { BorderTop = thin, BorderBottom = thin, BorderLeft = thin, BorderRight = thin, BorderInsideHorizontal = thin, BorderInsideVertical = thin, CellMarginTop = 1, CellMarginBottom = 1 },
            };
            inner.ColumnWidths.Add(60);
            inner.ColumnWidths.Add(170);
            inner.ColumnWidths.Add(80);
            for (int i = 1; i <= 60; i++)
            {
                FlowTableRow r = new FlowTableRow();
                foreach (string text in new[] { "P-" + i.ToString("000", CultureInfo.InvariantCulture), "Item " + i.ToString(CultureInfo.InvariantCulture), (i * 125.5).ToString("N2", CultureInfo.InvariantCulture) })
                {
                    FlowTableCell c = new FlowTableCell();
                    if (i % 2 == 0)
                    {
                        c.Background = new LayoutColor(0xF2, 0xF2, 0xF2);
                    }
                    c.Blocks.Add(FlowParagraph.Of(text, Body with { FontSize = 10 }));
                    r.Cells.Add(c);
                }
                inner.Rows.Add(r);
            }

            FlowTable outer = new FlowTable
            {
                Format = new TableFormat { BorderTop = outerLine, BorderBottom = outerLine, BorderLeft = outerLine, BorderRight = outerLine, BorderInsideVertical = outerLine },
            };
            outer.ColumnWidths.Add(110);
            outer.ColumnWidths.Add(340);
            FlowTableRow row = new FlowTableRow();
            FlowTableCell left = new FlowTableCell { Background = new LayoutColor(0xDD, 0xEB, 0xF7) };
            left.Blocks.Add(FlowParagraph.Of("Price list", Body with { Bold = true }));
            left.Blocks.Add(FlowParagraph.Of("Left column of the layout table.", Body with { FontSize = 9 }));
            FlowTableCell right = new FlowTableCell();
            right.Blocks.Add(FlowParagraph.Of("Nested table:", Body with { Italic = true }, new ParagraphFormat { SpaceAfter = 4 }));
            right.Blocks.Add(inner);
            right.Blocks.Add(FlowParagraph.Of("End of the nested table.", Body with { Italic = true }, new ParagraphFormat { SpaceBefore = 4 }));
            row.Cells.Add(left);
            row.Cells.Add(right);
            outer.Rows.Add(row);
            s.Blocks.Add(outer);
            s.Blocks.Add(FlowParagraph.Of("Text after the outer table.", Body, new ParagraphFormat { SpaceBefore = 8 }));
            return d;
        }
    }
}
