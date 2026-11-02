using System;
using System.Collections.Generic;
using System.Globalization;

namespace Chuvadi.Layout.Acceptance
{
    /// <summary>How one page compares.</summary>
    internal sealed record PageComparison(
        int Page,
        bool SizeEqual,
        bool TextEqual,
        string? TextDifference,
        int ReferenceLines,
        int LayoutLines,
        int MatchedLines,
        double MaxBaselineDelta,
        double MaxLeftDelta,
        int LinesOverTolerance,
        string? WorstLine);

    /// <summary>
    /// How one document compares with its reference. Tier A (DESIGN.md section 9): page count and every page's text
    /// identical (so page breaks are identical). Geometry: every reference line found, with its baseline and left edge
    /// within the tolerance.
    /// </summary>
    internal sealed record FileComparison(int ReferencePages, int LayoutPages, IReadOnlyList<PageComparison> Pages, double Tolerance)
    {
        public bool PageCountEqual => ReferencePages == LayoutPages;

        public bool TextEqual
        {
            get
            {
                if (!PageCountEqual)
                {
                    return false;
                }
                foreach (PageComparison p in Pages)
                {
                    if (!p.TextEqual)
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        public bool GeometryPass
        {
            get
            {
                if (!TextEqual)
                {
                    return false;
                }
                foreach (PageComparison p in Pages)
                {
                    if (!p.SizeEqual || p.LinesOverTolerance > 0 || p.MatchedLines < p.ReferenceLines)
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        public double MaxBaselineDelta
        {
            get
            {
                double m = 0;
                foreach (PageComparison p in Pages)
                {
                    m = Math.Max(m, p.MaxBaselineDelta);
                }
                return m;
            }
        }

        public double MaxLeftDelta
        {
            get
            {
                double m = 0;
                foreach (PageComparison p in Pages)
                {
                    m = Math.Max(m, p.MaxLeftDelta);
                }
                return m;
            }
        }

        /// <summary>
        /// Share of reference lines found in the layout with the same text on the same page: a measure of line-break
        /// positions (a line broken elsewhere has other text).
        /// </summary>
        public double LineMatchRatio
        {
            get
            {
                int refLines = 0, matched = 0;
                foreach (PageComparison p in Pages)
                {
                    refLines += p.ReferenceLines;
                    matched += p.MatchedLines;
                }
                return refLines == 0 ? 1 : (double)matched / refLines;
            }
        }

        /// <summary>First page whose text differs (1-based), or 0.</summary>
        public int FirstTextDifferencePage
        {
            get
            {
                foreach (PageComparison p in Pages)
                {
                    if (!p.TextEqual)
                    {
                        return p.Page + 1;
                    }
                }
                return 0;
            }
        }
    }

    internal static class Comparer
    {
        /// <summary>Page sizes may differ by this much (PDF writers round the media box).</summary>
        private const double SizeTolerance = 0.5;

        public static FileComparison Compare(DocumentText reference, DocumentText layout, double tolerance)
        {
            List<PageComparison> pages = new List<PageComparison>();
            int n = Math.Max(reference.Pages.Count, layout.Pages.Count);
            for (int i = 0; i < n; i++)
            {
                PageText? r = i < reference.Pages.Count ? reference.Pages[i] : null;
                PageText? l = i < layout.Pages.Count ? layout.Pages[i] : null;
                pages.Add(ComparePage(i, r, l, tolerance));
            }
            return new FileComparison(reference.Pages.Count, layout.Pages.Count, pages, tolerance);
        }

        private static PageComparison ComparePage(int index, PageText? r, PageText? l, double tolerance)
        {
            if (r is null || l is null)
            {
                return new PageComparison(index, false, false, r is null ? "page missing in the reference" : "page missing in the layout",
                    r?.Lines.Count ?? 0, l?.Lines.Count ?? 0, 0, 0, 0, 0, null);
            }
            bool size = Math.Abs(r.Width - l.Width) <= SizeTolerance && Math.Abs(r.Height - l.Height) <= SizeTolerance;
            string rt = r.Normalized;
            string lt = l.Normalized;
            bool textEqual = string.Equals(rt, lt, StringComparison.Ordinal);
            string? diff = textEqual ? null : Difference(rt, lt);

            // Lines: longest common subsequence on normalised line text.
            int[] match = Match(r.Lines, l.Lines);
            int matched = 0, over = 0;
            double maxDy = 0, maxDx = 0;
            string? worst = null;
            double worstScore = -1;
            for (int a = 0; a < r.Lines.Count; a++)
            {
                if (match[a] < 0)
                {
                    continue;
                }
                matched++;
                TextLine rl = r.Lines[a];
                TextLine ll = l.Lines[match[a]];
                double dy = Math.Abs(rl.Baseline - ll.Baseline);
                double dx = Math.Abs(rl.Left - ll.Left);
                maxDy = Math.Max(maxDy, dy);
                maxDx = Math.Max(maxDx, dx);
                if (dy > tolerance || dx > tolerance)
                {
                    over++;
                }
                if (Math.Max(dy, dx) > worstScore)
                {
                    worstScore = Math.Max(dy, dx);
                    worst = string.Create(CultureInfo.InvariantCulture,
                        $"'{Clip(rl.Text, 40)}': reference y {rl.Baseline:0.##} x {rl.Left:0.##}, layout y {ll.Baseline:0.##} x {ll.Left:0.##}");
                }
            }
            return new PageComparison(index, size, textEqual, diff, r.Lines.Count, l.Lines.Count, matched, maxDy, maxDx, over, worst);
        }

        /// <summary>Maps each reference line to a layout line (or -1) by the longest common subsequence of line texts.</summary>
        internal static int[] Match(IReadOnlyList<TextLine> r, IReadOnlyList<TextLine> l)
        {
            int n = r.Count, m = l.Count;
            int[,] dp = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
            {
                for (int j = m - 1; j >= 0; j--)
                {
                    dp[i, j] = string.Equals(r[i].Normalized, l[j].Normalized, StringComparison.Ordinal)
                        ? dp[i + 1, j + 1] + 1
                        : Math.Max(dp[i + 1, j], dp[i, j + 1]);
                }
            }
            int[] match = new int[n];
            Array.Fill(match, -1);
            int x = 0, y = 0;
            while (x < n && y < m)
            {
                if (string.Equals(r[x].Normalized, l[y].Normalized, StringComparison.Ordinal))
                {
                    match[x] = y;
                    x++;
                    y++;
                }
                else if (dp[x + 1, y] >= dp[x, y + 1])
                {
                    x++;
                }
                else
                {
                    y++;
                }
            }
            return match;
        }

        private static string Difference(string r, string l)
        {
            int i = 0;
            while (i < r.Length && i < l.Length && r[i] == l[i])
            {
                i++;
            }
            int from = Math.Max(0, i - 20);
            return string.Create(CultureInfo.InvariantCulture,
                $"at character {i}: reference '\u2026{Clip(r.Substring(from), 50)}' vs layout '\u2026{Clip(l.Substring(Math.Min(from, l.Length)), 50)}'");
        }

        private static string Clip(string s, int n) => s.Length <= n ? s : s.Substring(0, n) + "\u2026";
    }
}
