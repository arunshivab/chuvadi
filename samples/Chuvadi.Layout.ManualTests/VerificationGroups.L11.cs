using System;
using System.Collections.Generic;
using System.Linq;
using Chuvadi.Layout.Drawing;

namespace Chuvadi.Layout.ManualTests
{
    internal static partial class VerificationGroups
    {
        private static IEnumerable<Action> L11Groups()
        {
            yield return L11_WrapShowcaseTextNeverOverlapsSquareImages;
            yield return L11_NestedTableSplitsAndKeepsEveryRow;
        }

        private static void L11_WrapShowcaseTextNeverOverlapsSquareImages()
        {
            using LaidOutDocument doc = DocumentLayout.Open(PreviewScenarios.WrapShowcase(), PreviewScenarios.Options());
            doc.ResolvePageCount();
            int images = 0;
            int beside = 0;
            foreach (LaidOutPage p in doc.GetPages())
            {
                List<LayoutBox> lines = p.Boxes.Where(b => b.Kind == LayoutBoxKind.Line).ToList();
                foreach (ImageOp img in p.DisplayList.OfType<ImageOp>())
                {
                    images++;
                    LayoutRect r = img.Destination;
                    bool triangle = img.Image.PixelWidth == 68;
                    foreach (LayoutBox l in lines)
                    {
                        bool vertical = l.Rect.Y < r.Bottom && l.Rect.Bottom > r.Y;
                        if (!vertical)
                        {
                            continue;
                        }
                        beside++;
                        if (!triangle)
                        {
                            Check(l.Rect.Right <= r.X + 0.01 || l.Rect.X >= r.Right - 0.01, $"line at y {l.Rect.Y:0.##} overlaps the image at x {r.X:0.##}-{r.Right:0.##}");
                        }
                    }
                }
            }
            Check(images == 6, "wrap showcase images: " + images);
            Check(beside > 20, "lines beside images: " + beside);
            Check(!doc.Warnings.Any(w => w.Code == "TextWrapApproximated"), "no wrap approximation in the showcase");
        }

        private static void L11_NestedTableSplitsAndKeepsEveryRow()
        {
            using LaidOutDocument doc = DocumentLayout.Open(PreviewScenarios.NestedSplitShowcase(), PreviewScenarios.Options());
            int n = doc.ResolvePageCount();
            Check(n == 2, "nested split pages: " + n);
            string all = string.Concat(doc.GetPages().Select(p => p.GetText()));
            for (int i = 1; i <= 60; i++)
            {
                string code = "P-" + i.ToString("000", System.Globalization.CultureInfo.InvariantCulture);
                Check(all.Split(code).Length == 2, code + " appears exactly once");
            }
            Check(doc.GetPage(0).GetText().Contains("P-001", StringComparison.Ordinal), "the nested table starts on page 1");
            Check(!doc.Warnings.Any(w => w.Code == "ContentTallerThanPage"), "no clipped content");
        }
    }
}
