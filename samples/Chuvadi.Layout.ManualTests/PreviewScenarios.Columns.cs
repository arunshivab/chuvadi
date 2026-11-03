using System;
using System.Collections.Generic;
using System.Globalization;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.ManualTests
{
    internal static partial class PreviewScenarios
    {
        private static readonly string[] ColumnWords =
            "admission approval assurance beam calibration clinical consultant daily department discharge dose imaging insurance invoice laboratory machine nursing patient pharmacy planning quality radiation report results review schedule summary theatre treatment ward".Split(' ');

        private static FlowParagraph ColumnParagraph(string tag, int seed, int words)
        {
            List<string> w = new List<string> { tag };
            for (int i = 0; i < words; i++)
            {
                w.Add(ColumnWords[((seed * 7) + (i * 5) + (i / 3)) % ColumnWords.Length]);
            }
            return FlowParagraph.Of(string.Join(" ", w) + ".", Body, new ParagraphFormat { SpaceAfter = 4 });
        }

        /// <summary>
        /// Columns showcase: one column; two balanced columns with a separator between continuous breaks; three unequal
        /// columns with a column break; one column again.
        /// </summary>
        internal static FlowDocument ColumnsShowcase()
        {
            FlowDocument d = new FlowDocument();
            FlowSection intro = new FlowSection();
            intro.Blocks.Add(FlowParagraph.Of("Columns showcase: one column, two balanced columns with a separator, three unequal columns with a column break.",
                new CharacterFormat { FontFamily = "Calibri", FontSize = 14, Bold = true }, new ParagraphFormat { SpaceAfter = 8 }));
            intro.Blocks.Add(ColumnParagraph("C0", 1, 50));
            d.Sections.Add(intro);

            FlowSection two = new FlowSection { Start = SectionStart.Continuous, ColumnCount = 2, ColumnSeparator = true };
            for (int i = 0; i < 6; i++)
            {
                two.Blocks.Add(ColumnParagraph("C2-" + (i + 1).ToString(CultureInfo.InvariantCulture), 10 + i, 26));
            }
            d.Sections.Add(two);

            FlowSection three = new FlowSection { Start = SectionStart.Continuous, ColumnSeparator = true };
            three.ColumnWidths.Add(new FlowColumn(100, 20));
            three.ColumnWidths.Add(new FlowColumn(125, 30));
            three.ColumnWidths.Add(new FlowColumn(176.3, 0));
            for (int i = 0; i < 7; i++)
            {
                FlowParagraph p = ColumnParagraph("C3-" + (i + 1).ToString(CultureInfo.InvariantCulture), 20 + i, 16);
                if (i == 1)
                {
                    p.Inlines.Add(new BreakInline(BreakKind.Column, Body));
                }
                three.Blocks.Add(p);
            }
            d.Sections.Add(three);

            FlowSection end = new FlowSection { Start = SectionStart.Continuous };
            end.Blocks.Add(ColumnParagraph("C9", 40, 40));
            d.Sections.Add(end);
            return d;
        }
    }
}
