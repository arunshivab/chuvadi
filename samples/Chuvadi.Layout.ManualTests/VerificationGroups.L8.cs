using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chuvadi.Layout.Drawing;

namespace Chuvadi.Layout.ManualTests
{
    internal static partial class VerificationGroups
    {
        private static IEnumerable<Action> L8Groups()
        {
            yield return L8_DocxShowcaseLaysOutLikeWord;
        }

        private static void L8_DocxShowcaseLaysOutLikeWord()
        {
            using LaidOutDocument doc = Chuvadi.Layout.Documents.AutoDetectLayout.Open(new MemoryStream(PreviewScenarios.DocxShowcase()), LayoutSourceFormat.Auto, PreviewScenarios.DocxOptions());
            Check(doc.Source.Format == LayoutSourceFormat.Docx, "sniffed as docx");
            int n = doc.ResolvePageCount();
            Check(n >= 2, "docx showcase pages: " + n);
            LaidOutPage first = doc.GetPage(0);
            Check(Math.Abs(first.WidthPt - 595.3) < 0.5, "A4 portrait first section");
            string text = first.GetText();
            Check(text.Contains("Treatment Summary", StringComparison.Ordinal), "heading");
            Check(text.Contains("\u2022", StringComparison.Ordinal) && text.Contains("1.", StringComparison.Ordinal), "bullet and number labels");
            Check(first.DisplayList.OfType<FillRectOp>().Any(f => f.Color.ToHex() == "1F3A60"), "header row shading");
            Check(first.DisplayList.OfType<PathOp>().Count(p => p.Fill is LayoutColor c && c.A < 255) >= 10, "watermark letters");
            string footer = string.Concat(first.DisplayList.OfType<GlyphRunOp>().Where(r => r.Y > first.HeightPt - 60).OrderBy(r => r.X).Select(r => r.Text));
            Check(footer.Contains($"Page 1 of {n}", StringComparison.Ordinal), "footer: " + footer);
            LaidOutPage annex = Enumerable.Range(0, n).Select(i => doc.GetPage(i)).First(p => p.WidthPt > p.HeightPt);
            Check(annex.PageLabel == "i", "annex numbered in lower roman: " + annex.PageLabel);
            Check(annex.GetText().Contains("Fraction\tDate", StringComparison.Ordinal), "annex table header");
        }
    }
}
