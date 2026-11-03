using System;
using System.Collections.Generic;
using System.Linq;
using Chuvadi.Layout.Drawing;

namespace Chuvadi.Layout.ManualTests
{
    internal static partial class VerificationGroups
    {
        private static IEnumerable<Action> ColumnGroups()
        {
            yield return C1_ColumnsShowcaseFillsBalancesAndSeparates;
        }

        /// <summary>The columns showcase: every paragraph once, balanced two-column region, separators, the column break.</summary>
        private static void C1_ColumnsShowcaseFillsBalancesAndSeparates()
        {
            using LaidOutDocument doc = DocumentLayout.Open(PreviewScenarios.ColumnsShowcase(), PreviewScenarios.Options());
            int n = doc.ResolvePageCount();
            Check(n == 1, "columns showcase pages: " + n);
            LaidOutPage p = doc.GetPage(0);
            string text = p.GetText();
            foreach (string tag in new[] { "C0", "C2-1", "C2-6", "C3-1", "C3-7", "C9" })
            {
                Check(text.Split(tag + " ").Length == 2, tag + " appears exactly once");
            }
            List<LayoutBox> lines = p.Boxes.Where(b => b.Kind == LayoutBoxKind.Line).ToList();
            double w = (451.3 - 36) / 2;
            int left = lines.Count(b => b.Path.StartsWith("2/", StringComparison.Ordinal) && b.Rect.X < 72 + w);
            int right = lines.Count(b => b.Path.StartsWith("2/", StringComparison.Ordinal) && b.Rect.X >= 72 + w + 36 - 0.5);
            Check(left > 0 && Math.Abs(left - right) <= 1, $"two-column region balanced: {left} and {right} lines");
            List<StrokeLineOp> rules = p.DisplayList.OfType<StrokeLineOp>().Where(o => Math.Abs(o.X1 - o.X2) < 1e-9 && Math.Abs(o.Width - 0.75) < 1e-9).ToList();
            Check(rules.Count == 3, "separators: one in the two-column region, two in the three-column region, got " + rules.Count);
            Check(lines.Any(b => b.Path.StartsWith("3/", StringComparison.Ordinal) && Math.Abs(b.Rect.X - 192) < 0.01), "the column break moves text to column 2 at 192 pt");
            double lastColumns = lines.Where(b => !b.Path.StartsWith("4/", StringComparison.Ordinal)).Max(b => b.Rect.Bottom);
            Check(lines.Where(b => b.Path.StartsWith("4/", StringComparison.Ordinal)).Min(b => b.Rect.Y) >= lastColumns - 0.01, "the last one-column section starts below the columns");
        }
    }
}
