using System;
using System.Collections.Generic;
using System.Linq;
using Chuvadi.Layout.Drawing;

namespace Chuvadi.Layout.ManualTests
{
    internal static partial class VerificationGroups
    {
        private static IEnumerable<Action> L6Groups()
        {
            yield return L6_ShowcaseFootersCountPagesExactly;
            yield return L6_FloatingImageSitsAboveItsParagraphText;
        }

        private static void L6_ShowcaseFootersCountPagesExactly()
        {
            using LaidOutDocument doc = DocumentLayout.Open(PreviewScenarios.FlowShowcase(), PreviewScenarios.Options());
            int n = doc.ResolvePageCount();
            for (int i = 0; i < n; i++)
            {
                LaidOutPage p = doc.GetPage(i);
                string footer = string.Concat(p.DisplayList.OfType<GlyphRunOp>().Where(r => r.Y > p.HeightPt - 72).OrderBy(r => r.X).Select(r => r.Text));
                Check(footer == $"Page {i + 1} of {n}", $"footer on page {i + 1}: '{footer}'");
                Check(!p.IsProvisional, "page " + (i + 1) + " is provisional after ResolvePageCount");
                Check(p.Boxes.Any(b => b.Kind == LayoutBoxKind.Header) && p.Boxes.Any(b => b.Kind == LayoutBoxKind.Footer), "header and footer boxes");
            }
        }

        private static void L6_FloatingImageSitsAboveItsParagraphText()
        {
            using LaidOutDocument doc = DocumentLayout.Open(PreviewScenarios.FlowShowcase(), PreviewScenarios.Options());
            doc.ResolvePageCount();
            foreach (LaidOutPage p in doc.GetPages())
            {
                ImageOp? img = p.DisplayList.OfType<ImageOp>().FirstOrDefault();
                if (img is null)
                {
                    continue;
                }
                LayoutBox line = p.Boxes.Where(b => b.Kind == LayoutBoxKind.Line && b.Rect.Y >= img.Destination.Y).OrderBy(b => b.Rect.Y).First();
                Check(line.Rect.Y >= img.Destination.Bottom, "text must continue below the top-and-bottom image");
                return;
            }
            throw new InvalidOperationException("floating image not found");
        }
    }
}
