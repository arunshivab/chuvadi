using System;
using System.Collections.Generic;
using System.Linq;
using Chuvadi.Layout.Drawing;

namespace Chuvadi.Layout.ManualTests
{
    internal static partial class VerificationGroups
    {
        private static IEnumerable<Action> FloatingGroups()
        {
            yield return F1_FloatingTablesSitWhereTheFileSaysAndTextNeverOverlaps;
        }

        private static void F1_FloatingTablesSitWhereTheFileSaysAndTextNeverOverlaps()
        {
            using LaidOutDocument doc = DocumentLayout.Open(PreviewScenarios.FloatingShowcase(), PreviewScenarios.Options());
            int n = doc.ResolvePageCount();
            Check(n == 1, "floating showcase pages: " + n);
            LaidOutPage p = doc.GetPage(0);
            List<LayoutBox> tables = p.Boxes.Where(b => b.Kind == LayoutBoxKind.Table).OrderBy(b => b.Rect.Y).ToList();
            Check(tables.Count == 2, "two floating tables, got " + tables.Count);
            Check(Math.Abs(tables[0].Rect.Y - 72) < 0.1 && Math.Abs(tables[0].Rect.Right - 523.3) < 0.1, "table B at the top right of the margins");
            Check(Math.Abs(tables[1].Rect.X - 272) < 0.1, "table A 200 pt from the margin");
            List<LayoutBox> lines = p.Boxes.Where(b => b.Kind == LayoutBoxKind.Line && b.Path.Count(c => c == '/') == 1 && b.Rect.Width > 0).ToList();
            foreach (LayoutBox t in tables)
            {
                foreach (LayoutBox l in lines)
                {
                    bool v = l.Rect.Y < t.Rect.Bottom - 0.5 && l.Rect.Bottom > t.Rect.Y + 0.5;
                    bool h = l.Rect.X < t.Rect.Right - 0.5 && l.Rect.Right > t.Rect.X + 0.5;
                    Check(!(v && h), $"line {l.Path} overlaps table {t.Path}");
                }
            }
            Check(lines.Any(l => l.Rect.X > tables[1].Rect.Right && l.Rect.Y < tables[1].Rect.Bottom), "text on the right of table A");
            Check(lines.Any(l => l.Rect.Right < tables[1].Rect.X && l.Rect.Y >= tables[1].Rect.Y && l.Rect.Y < tables[1].Rect.Bottom), "text on the left of table A");
        }
    }
}
