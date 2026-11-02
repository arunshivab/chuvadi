using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Chuvadi.Layout.Drawing;

namespace Chuvadi.Layout.Acceptance
{
    /// <summary>One glyph with its text, in top-down page points (baseline y).</summary>
    internal readonly record struct TextGlyph(double X, double Baseline, double Advance, string Text, double Size);

    /// <summary>Glyphs sharing a baseline, left to right.</summary>
    internal sealed record TextLine(double Baseline, double Left, double Right, double Size, string Text)
    {
        public string Normalized { get; } = TextNormalizer.Normalize(Text);
    }

    /// <summary>The text of one page as lines in reading order (top to bottom, then left to right).</summary>
    internal sealed record PageText(int Index, double Width, double Height, IReadOnlyList<TextLine> Lines)
    {
        public string Normalized
        {
            get
            {
                StringBuilder sb = new StringBuilder();
                foreach (TextLine l in Lines)
                {
                    sb.Append(l.Normalized);
                }
                return sb.ToString();
            }
        }
    }

    /// <summary>The text of a whole document, page by page.</summary>
    internal sealed record DocumentText(IReadOnlyList<PageText> Pages);

    /// <summary>
    /// Text comparison is about which characters land on which page and line, not about how a PDF writer spells
    /// them: whitespace is dropped (Word positions words instead of drawing spaces), compatibility forms are folded
    /// (NFKC: ligatures such as "fi" become two letters), soft hyphens and zero-width characters are dropped, and
    /// hyphen variants become '-'. A run of four or more leader characters ('.', '-', '_', '·') becomes three: tab
    /// leaders are filled to a grid of the leader's advance, so writers differ by a character or two.
    /// </summary>
    internal static class TextNormalizer
    {
        public static string Normalize(string s)
        {
            string n = s.Normalize(NormalizationForm.FormKC);
            StringBuilder sb = new StringBuilder(n.Length);
            foreach (char c in n)
            {
                if (char.IsWhiteSpace(c) || c is '\u00AD' or '\u200B' or '\u200C' or '\u200D' or '\u2060' or '\uFEFF')
                {
                    continue;
                }
                char ch = c is '\u2010' or '\u2011' ? '-' : c;
                if (ch is '.' or '-' or '_' or '\u00B7' && sb.Length >= 3 && sb[^1] == ch && sb[^2] == ch && sb[^3] == ch)
                {
                    continue;
                }
                sb.Append(ch);
            }
            return sb.ToString();
        }
    }

    /// <summary>Groups glyphs into lines by baseline.</summary>
    internal static class LineGrouper
    {
        public static List<TextLine> Group(List<TextGlyph> glyphs)
        {
            List<TextLine> lines = new List<TextLine>();
            if (glyphs.Count == 0)
            {
                return lines;
            }
            glyphs.Sort((a, b) => a.Baseline != b.Baseline ? a.Baseline.CompareTo(b.Baseline) : a.X.CompareTo(b.X));
            List<TextGlyph> current = new List<TextGlyph>();
            double baseline = glyphs[0].Baseline;
            foreach (TextGlyph g in glyphs)
            {
                double tolerance = 0.3 * Math.Max(1, Math.Min(g.Size, current.Count > 0 ? current[0].Size : g.Size));
                if (current.Count > 0 && g.Baseline - baseline > tolerance)
                {
                    lines.Add(Line(current));
                    current.Clear();
                }
                if (current.Count == 0)
                {
                    baseline = g.Baseline;
                }
                current.Add(g);
            }
            lines.Add(Line(current));
            return lines;
        }

        private static TextLine Line(List<TextGlyph> glyphs)
        {
            glyphs.Sort((a, b) => a.X.CompareTo(b.X));
            StringBuilder sb = new StringBuilder();
            double left = double.PositiveInfinity, right = double.NegativeInfinity, size = 0, baseline = 0;
            double penEnd = double.NaN;
            foreach (TextGlyph g in glyphs)
            {
                if (!double.IsNaN(penEnd) && g.X - penEnd > 0.2 * Math.Max(1, g.Size))
                {
                    sb.Append(' ');
                }
                sb.Append(g.Text);
                if (!string.IsNullOrWhiteSpace(g.Text))
                {
                    left = Math.Min(left, g.X);
                    right = Math.Max(right, g.X + g.Advance);
                }
                size = Math.Max(size, g.Size);
                baseline = g.Baseline;
                penEnd = g.X + g.Advance;
            }
            if (double.IsInfinity(left))
            {
                left = glyphs[0].X;
                right = left;
            }
            return new TextLine(Math.Round(baseline, 3), left, right, size, sb.ToString());
        }
    }

    /// <summary>Reads the text of laid-out pages from their display lists (the same way a PDF's text is read).</summary>
    internal static class LayoutText
    {
        public static DocumentText Read(LaidOutDocument doc)
        {
            List<PageText> pages = new List<PageText>();
            int n = doc.ResolvePageCount();
            for (int i = 0; i < n; i++)
            {
                pages.Add(Read(doc.GetPage(i)));
            }
            return new DocumentText(pages);
        }

        public static PageText Read(LaidOutPage page)
        {
            List<TextGlyph> glyphs = new List<TextGlyph>();
            foreach (LayoutOp op in page.DisplayList)
            {
                if (op is GlyphRunOp run)
                {
                    AddRun(run, glyphs);
                }
            }
            return new PageText(page.Index, page.WidthPt, page.HeightPt, LineGrouper.Group(glyphs));
        }

        private static void AddRun(GlyphRunOp run, List<TextGlyph> glyphs)
        {
            // Each cluster's text goes with its first glyph: [cluster, next larger cluster).
            SortedSet<int> clusters = new SortedSet<int>();
            foreach (PositionedGlyph g in run.Glyphs)
            {
                clusters.Add(g.Cluster);
            }
            HashSet<int> given = new HashSet<int>();
            foreach (PositionedGlyph g in run.Glyphs)
            {
                string text = string.Empty;
                if (given.Add(g.Cluster) && g.Cluster >= 0 && g.Cluster < run.Text.Length)
                {
                    int end = run.Text.Length;
                    foreach (int c in clusters.GetViewBetween(g.Cluster + 1, int.MaxValue))
                    {
                        end = Math.Min(end, c);
                        break;
                    }
                    text = run.Text.Substring(g.Cluster, Math.Max(0, end - g.Cluster));
                }
                glyphs.Add(new TextGlyph(run.X + g.X, run.Y + g.Y, g.Advance, text, run.FontSize));
            }
        }

        public static string Describe(TextLine l) => string.Create(CultureInfo.InvariantCulture, $"y {l.Baseline:0.##} x {l.Left:0.##} '{l.Text}'");
    }
}
