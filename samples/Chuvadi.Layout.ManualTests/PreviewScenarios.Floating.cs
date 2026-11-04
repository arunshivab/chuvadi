using System.Collections.Generic;
using System.Globalization;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.ManualTests
{
    internal static partial class PreviewScenarios
    {
        private static FlowTable FloatingTable(string tag, int rows, double width, FloatingTablePosition fp)
        {
            FlowTable t = new FlowTable { Floating = fp };
            t.ColumnWidths.Add(width / 2);
            t.ColumnWidths.Add(width / 2);
            BorderLine thin = new BorderLine(0.75, LayoutColor.Black);
            for (int r = 0; r < rows; r++)
            {
                FlowTableRow row = new FlowTableRow();
                for (int c = 0; c < 2; c++)
                {
                    FlowTableCell cell = new FlowTableCell { BorderTop = thin, BorderBottom = thin, BorderLeft = thin, BorderRight = thin };
                    cell.Blocks.Add(FlowParagraph.Of(tag + " r" + (r + 1).ToString(CultureInfo.InvariantCulture) + " c" + (c + 1).ToString(CultureInfo.InvariantCulture), Body, new ParagraphFormat()));
                    row.Cells.Add(cell);
                }
                t.Rows.Add(row);
            }
            return t;
        }

        /// <summary>
        /// Floating tables showcase: a table anchored to its paragraph 200 pt from the margin with text on both sides; a
        /// table at the top margin, above its paragraph, with the text above flowing beside it.
        /// </summary>
        internal static FlowDocument FloatingShowcase()
        {
            FlowDocument d = new FlowDocument();
            FlowSection s = new FlowSection();
            s.Blocks.Add(FlowParagraph.Of("Floating tables showcase: text flows on both sides of a table anchored to its paragraph, and beside a table placed at the top margin.",
                new CharacterFormat { FontFamily = "Calibri", FontSize = 14, Bold = true }, new ParagraphFormat { SpaceAfter = 8 }));
            for (int i = 0; i < 3; i++)
            {
                s.Blocks.Add(ColumnParagraph("F" + (i + 1).ToString(CultureInfo.InvariantCulture), 50 + i, 40));
            }
            s.Blocks.Add(FloatingTable("A", 4, 150, new FloatingTablePosition
            {
                HorizontalAnchor = TableHorizontalAnchor.Margin, OffsetX = 200, VerticalAnchor = TableVerticalAnchor.Paragraph,
                DistanceLeft = 9, DistanceRight = 9,
            }));
            for (int i = 3; i < 9; i++)
            {
                s.Blocks.Add(ColumnParagraph("F" + (i + 1).ToString(CultureInfo.InvariantCulture), 50 + i, 40));
            }
            s.Blocks.Add(FloatingTable("B", 3, 160, new FloatingTablePosition
            {
                HorizontalAnchor = TableHorizontalAnchor.Margin, HorizontalAlignment = TableHorizontalAlignment.Right,
                VerticalAnchor = TableVerticalAnchor.Margin, OffsetY = 0, DistanceLeft = 9, DistanceBottom = 9,
            }));
            s.Blocks.Add(ColumnParagraph("F10", 60, 40));
            d.Sections.Add(s);
            return d;
        }
    }
}
