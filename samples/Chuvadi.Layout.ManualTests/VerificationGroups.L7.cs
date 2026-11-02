using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chuvadi.Layout.Drawing;

namespace Chuvadi.Layout.ManualTests
{
    internal static partial class VerificationGroups
    {
        private static IEnumerable<Action> L7Groups()
        {
            yield return L7_XlsxShowcasePrintsLikeExcel;
        }

        private static void L7_XlsxShowcasePrintsLikeExcel()
        {
            using LaidOutDocument doc = Chuvadi.Layout.Sheets.SheetsLayout.Open(new MemoryStream(PreviewScenarios.XlsxShowcase()), null, PreviewScenarios.XlsxOptions());
            int n = doc.ResolvePageCount();
            Check(n == 2, "xlsx showcase pages: " + n);
            for (int i = 0; i < n; i++)
            {
                LaidOutPage p = doc.GetPage(i);
                string text = p.GetText();
                Check(text.Contains("Code\tService", StringComparison.Ordinal), $"print title row on page {i + 1}");
                Check(Math.Abs(p.WidthPt - 595.3) < 0.5, "A4 page");
                string footer = string.Concat(p.DisplayList.OfType<GlyphRunOp>().Where(r => r.Y > p.HeightPt - 40).OrderBy(r => r.X).Select(r => r.Text));
                Check(footer.Contains($"Page {i + 1} of {n}", StringComparison.Ordinal) && footer.Contains("charges.xlsx", StringComparison.Ordinal), "footer: " + footer);
            }
            LaidOutPage first = doc.GetPage(0);
            Check(first.GetText().Contains("RT-101", StringComparison.Ordinal) && first.GetText().Contains("1,500.00", StringComparison.Ordinal), "first data row");
            Check(first.GetText().Contains("(10.00)", StringComparison.Ordinal), "negative in parentheses");
            Check(first.DisplayList.OfType<FillRectOp>().Any(f => f.Color.ToHex() == "FFC7CE"), "conditional fill");
            Check(first.DisplayList.OfType<FillRectOp>().Count(f => f.Color.ToHex() == "63BE7B") > 20, "data bars");
            Check(first.DisplayList.OfType<GlyphRunOp>().Any(r => r.Color.ToHex() == "FF0000" || r.Color.ToHex() == "9C0006"), "red negatives");
        }
    }
}
