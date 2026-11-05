using System;
using System.Collections.Generic;
using System.Linq;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.ManualTests
{
    internal static partial class VerificationGroups
    {
        private static IEnumerable<Action> L4Groups()
        {
            yield return L4_ShowcasePrintsWithTitlesHeadersAndFooters;
            yield return L4_DefaultColumnIs61PixelsForCalibri11;
            yield return L4_FitToOnePageWide;
            yield return L4_PrintAreaAndManualBreaks;
        }

        private static void L4_ShowcasePrintsWithTitlesHeadersAndFooters()
        {
            using LaidOutDocument doc = DocumentLayout.Open(PreviewScenarios.GridShowcase(), PreviewScenarios.Options());
            Check(doc.IsPageCountFinal && doc.PageCount == 3, "expected 3 final pages, got " + doc.PageCount);
            LaidOutPage p2 = doc.GetPage(1);
            Check(p2.GetText().StartsWith("Code\tService\tUnit\tRate\tPackage rate", StringComparison.Ordinal), "title row must repeat on page 2");
            Check(p2.DisplayList.OfType<GlyphRunOp>().Any(r => r.Text == "Page 2 of 3"), "footer &P of &N");
            Check(doc.GetPage(0).DisplayList.OfType<GlyphRunOp>().Any(r => r.Text.All(c => c == '#')), "#### for a number that does not fit");
            Check(doc.GetPage(2).WidthPt > doc.GetPage(2).HeightPt, "Gridlines sheet is landscape");
        }

        private static void L4_DefaultColumnIs61PixelsForCalibri11()
        {
            GridWorkbook b = new GridWorkbook();
            GridSheet s = new GridSheet("S");
            s.Set(0, 0, "x");
            b.Sheets.Add(s);
            using LaidOutDocument doc = DocumentLayout.Open(b, PreviewScenarios.Options());
            LayoutBox a1 = doc.GetPage(0).Boxes.Single(x => x.Kind == LayoutBoxKind.SheetCell);
            Check(Math.Abs(a1.Rect.Width - 45.75) < 0.001, "default column must be 61 px = 45.75 pt (ECMA-376 18.3.1.81), got " + a1.Rect.Width);
        }

        private static void L4_FitToOnePageWide()
        {
            GridWorkbook b = new GridWorkbook();
            GridSheet s = new GridSheet("Wide");
            for (int c = 0; c < 30; c++)
            {
                s.Set(0, c, "c" + c);
            }
            s.PageSetup = new GridPageSetup { FitToPage = true, FitToWidth = 1, FitToHeight = 0 };
            b.Sheets.Add(s);
            using LaidOutDocument doc = DocumentLayout.Open(b, PreviewScenarios.Options());
            Check(doc.PageCount == 1, "fit to one page wide");
        }

        private static void L4_PrintAreaAndManualBreaks()
        {
            GridWorkbook b = new GridWorkbook();
            GridSheet s = new GridSheet("S");
            for (int r = 0; r < 20; r++)
            {
                s.Set(r, 0, "r" + r);
            }
            s.PrintAreas.Add(CellRange.Parse("A3:A12"));
            s.RowBreaks.Add(7);
            b.Sheets.Add(s);
            using LaidOutDocument doc = DocumentLayout.Open(b, PreviewScenarios.Options());
            Check(doc.PageCount == 2, "manual break inside the print area");
            Check(doc.GetPage(0).GetText().StartsWith("r2", StringComparison.Ordinal) && doc.GetPage(1).GetText().StartsWith("r7", StringComparison.Ordinal), "break before row 8");
        }
    }
}
