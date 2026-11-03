using System;
using System.Collections.Generic;
using Chuvadi.Layout.Model;
using Chuvadi.Layout.Text;

namespace Chuvadi.Layout.Engine.Flow
{
    /// <summary>A fitted line.</summary>
    internal sealed record FittedLine
    {
        public required int Start { get; init; }

        /// <summary>End (exclusive), including trailing spaces and the break character.</summary>
        public required int End { get; init; }

        /// <summary>X of the line start relative to the text area's left edge (indents applied).</summary>
        public required double StartX { get; init; }

        /// <summary>Available right edge relative to the text area's left edge.</summary>
        public required double MaxX { get; init; }

        /// <summary>X where the visible content ends (trailing spaces and break excluded, tabs included).</summary>
        public required double ContentEndX { get; init; }

        /// <summary>Resolved tab widths by character index.</summary>
        public required Dictionary<int, double> TabWidths { get; init; }

        public required bool EndsWithLineBreak { get; init; }
        public required bool EndsWithPageBreak { get; init; }

        /// <summary>The break that ends the line is a column break (<see cref="EndsWithPageBreak"/> is also true).</summary>
        public bool EndsWithColumnBreak { get; init; }

        public required bool IsLastLine { get; init; }

        /// <summary>The line ends at a soft hyphen that must be drawn as a hyphen.</summary>
        public required bool Hyphenated { get; init; }

        /// <summary>Index after the last visible character (trailing spaces, breaks and hidden soft hyphens excluded).</summary>
        public required int VisibleEnd { get; init; }

        public required double Ascent { get; init; }
        public required double Descent { get; init; }

        /// <summary>Line box height after the line-spacing rule.</summary>
        public required double Height { get; init; }

        /// <summary>Baseline offset from the line box top.</summary>
        public required double Baseline { get; init; }

        /// <summary>
        /// This line is a further piece of the previous line's row: text flowing on the other side of a floating
        /// object (square or tight wrapping on both sides) shares the row's top.
        /// </summary>
        public bool SameRow { get; init; }
    }

    /// <summary>Breaks a measured paragraph into lines for a given text-area width.</summary>
    internal static class LineFitter
    {
        private const double Epsilon = 0.01;

        public static List<FittedLine> Fit(MeasuredParagraph m, double areaWidth, double defaultTabStop)
        {
            ParagraphFormat pf = m.Source.Format;
            int n = m.Text.Length;
            List<FittedLine> lines = new List<FittedLine>();
            double maxX = areaWidth - pf.RightIndent;
            int s = 0;
            bool first = true;
            while (true)
            {
                FittedLine line = FitLine(m, s, first, pf.LeftIndent + (first ? pf.FirstLineIndent : 0), maxX, defaultTabStop, true)!;
                lines.Add(line);
                if (line.IsLastLine)
                {
                    break;
                }
                if (line.End >= n)
                {
                    // Text ends with a break character: the paragraph mark sits on a final empty line.
                    lines.Add(MarkLine(m, pf.LeftIndent, maxX, false));
                    break;
                }
                s = line.End;
                first = false;
            }
            return lines;
        }

        /// <summary>The final empty line that carries only the paragraph mark (after a trailing break character).</summary>
        internal static FittedLine MarkLine(MeasuredParagraph m, double startX, double maxX, bool sameRow)
        {
            ParagraphFormat pf = m.Source.Format;
            int n = m.Text.Length;
            (double h2, double b2) = ApplySpacing(pf, m.MarkAscent, m.MarkDescent);
            return new FittedLine
            {
                Start = n,
                End = n,
                StartX = startX,
                MaxX = maxX,
                ContentEndX = startX,
                TabWidths = new Dictionary<int, double>(),
                EndsWithLineBreak = false,
                EndsWithPageBreak = false,
                IsLastLine = true,
                Hyphenated = false,
                VisibleEnd = n,
                Ascent = m.MarkAscent,
                Descent = m.MarkDescent,
                Height = h2,
                Baseline = b2,
                SameRow = sameRow,
            };
        }

        /// <summary>
        /// Fits one line starting at character <paramref name="s"/> between <paramref name="x0"/> and
        /// <paramref name="maxX"/> (both relative to the text area's left edge). With <paramref name="allowEmergency"/>
        /// false, returns null when not even the first break opportunity fits instead of breaking inside a word
        /// (used for the narrow spaces beside floating objects).
        /// </summary>
        internal static FittedLine? FitLine(MeasuredParagraph m, int s, bool first, double x0, double maxX, double defaultTabStop, bool allowEmergency)
        {
            string t = m.Text;
            int n = t.Length;
            ParagraphFormat pf = m.Source.Format;
            double x = x0;
            double contentEnd = x0;
            Dictionary<int, double> tabs = new Dictionary<int, double>();
            int i = s;
            int end = s;
            bool lineBreak = false, pageBreak = false, columnBreak = false, hyphenated = false;
            int visibleEnd = s;
            while (i < n)
            {
                int j = NextOpportunity(m, i);
                // Measure atom [i, j): width up to each tab, trailing-space width.
                Dictionary<int, double> atomTabs = new Dictionary<int, double>();
                double ax = x;
                double visibleX = x;
                int atomVisibleEnd = i;
                for (int k = i; k < j; k++)
                {
                    char c = t[k];
                    if (c == '\t')
                    {
                        double w = TabWidth(m, k, ax, maxX, defaultTabStop, first && k < FirstTextIndex(m));
                        atomTabs[k] = w;
                        ax += w;
                        visibleX = ax;
                        atomVisibleEnd = k + 1;
                    }
                    else if (c == ParagraphMeasurer.LineSeparator || ParagraphMeasurer.IsPageOrColumnBreak(c))
                    {
                        // zero width
                    }
                    else
                    {
                        ax += m.Advance[k];
                        if (!IsTrailingSpace(c) && c != '\u00AD')
                        {
                            visibleX = ax;
                            atomVisibleEnd = k + 1;
                        }
                    }
                }
                bool softHyphenEnd = j > i && t[j - 1] == '\u00AD' && j < n;
                double hyphenW = softHyphenEnd && m.SoftHyphens.TryGetValue(j - 1, out var sh) ? sh.Width : 0;
                bool nonEmpty = i > s;
                if (nonEmpty && visibleX > maxX + Epsilon)
                {
                    break; // the atom starts the next line
                }
                if (!nonEmpty && visibleX > maxX + Epsilon)
                {
                    if (!allowEmergency)
                    {
                        return null; // nothing fits: the caller tries a wider space
                    }
                    // Emergency break inside the atom at a cluster boundary.
                    int k2 = EmergencyBreak(m, i, j, x, maxX);
                    if (k2 < j)
                    {
                        double ex = x + m.Width(i, k2);
                        end = k2;
                        contentEnd = ex;
                        visibleEnd = k2;
                        foreach (KeyValuePair<int, double> kv in atomTabs)
                        {
                            if (kv.Key < k2)
                            {
                                tabs[kv.Key] = kv.Value;
                            }
                        }
                        i = k2;
                        goto emit;
                    }
                }
                foreach (KeyValuePair<int, double> kv in atomTabs)
                {
                    tabs[kv.Key] = kv.Value;
                }
                if (atomVisibleEnd > i || atomTabs.Count > 0)
                {
                    contentEnd = visibleX;
                    visibleEnd = atomVisibleEnd;
                }
                x = ax;
                i = j;
                end = j;
                hyphenated = softHyphenEnd;
                if (j > 0 && j <= n && m.Breaks[j] == LineBreakOpportunity.Mandatory && j < n + 1)
                {
                    char last = t[j - 1];
                    if (last == ParagraphMeasurer.LineSeparator)
                    {
                        lineBreak = true;
                        break;
                    }
                    if (ParagraphMeasurer.IsPageOrColumnBreak(last))
                    {
                        pageBreak = true;
                        columnBreak = last == ParagraphMeasurer.ColumnBreakChar;
                        break;
                    }
                }
                // Look ahead: a soft hyphen only shows if the next atom does not fit.
                if (softHyphenEnd)
                {
                    int nj = NextOpportunity(m, j);
                    double nextVisible = x + VisibleWidth(m, j, nj);
                    if (nextVisible > maxX + Epsilon)
                    {
                        contentEnd = visibleX + hyphenW;
                        visibleEnd = j;
                        break;
                    }
                    hyphenated = false;
                }
            }
            hyphenated = hyphenated && end < n && end == i && t[end - 1] == '\u00AD';
        emit:
            if (!hyphenated && visibleEnd > s && visibleEnd <= n && visibleEnd > 0 && t[visibleEnd - 1] == '\u00AD')
            {
                visibleEnd--;
            }
            bool isLast = end >= n && !lineBreak && !pageBreak;
            (double asc, double desc) = LineMetrics(m, s, end, isLast);
            (double height, double baseline) = ApplySpacing(pf, asc, desc);
            return new FittedLine
            {
                Start = s,
                End = end,
                StartX = x0,
                MaxX = maxX,
                ContentEndX = contentEnd,
                TabWidths = tabs,
                EndsWithLineBreak = lineBreak,
                EndsWithPageBreak = pageBreak,
                EndsWithColumnBreak = columnBreak,
                IsLastLine = isLast,
                Hyphenated = hyphenated,
                VisibleEnd = Math.Max(s, visibleEnd),
                Ascent = asc,
                Descent = desc,
                Height = height,
                Baseline = baseline,
            };
        }

        private static bool IsTrailingSpace(char c) => c == ' ' || c == '\u3000' || c == '\u2002' || c == '\u2003';

        private static int NextOpportunity(MeasuredParagraph m, int i)
        {
            int n = m.Text.Length;
            int j = i + 1;
            while (j < n && m.Breaks[j] == LineBreakOpportunity.NoBreak)
            {
                j++;
            }
            return Math.Min(j, n);
        }

        private static double VisibleWidth(MeasuredParagraph m, int i, int j)
        {
            int e = j;
            while (e > i && (IsTrailingSpace(m.Text[e - 1]) || m.Text[e - 1] == ParagraphMeasurer.LineSeparator || ParagraphMeasurer.IsPageOrColumnBreak(m.Text[e - 1])))
            {
                e--;
            }
            return m.Width(i, e);
        }

        private static int EmergencyBreak(MeasuredParagraph m, int i, int j, double x, double maxX)
        {
            int best = -1;
            for (int k = i + 1; k <= j; k++)
            {
                if (k < m.Text.Length && !m.ClusterStart[k])
                {
                    continue;
                }
                if (x + m.Width(i, k) <= maxX + Epsilon)
                {
                    best = k;
                }
                else
                {
                    break;
                }
            }
            if (best < 0)
            {
                // At least one cluster per line.
                best = i + 1;
                while (best < m.Text.Length && !m.ClusterStart[best])
                {
                    best++;
                }
            }
            return best;
        }

        /// <summary>Index of the first character after the list label (tabs before it use the implicit hanging stop).</summary>
        private static int FirstTextIndex(MeasuredParagraph m)
        {
            if (m.Source.ListLabel is null)
            {
                return 0;
            }
            return m.Source.ListLabel.Text.Length + (m.Source.ListLabel.Suffix == ListLabelSuffix.Nothing ? 0 : 1);
        }

        /// <summary>Width of the tab at index k when the pen is at x. Positions are relative to the text area's left edge.</summary>
        internal static double TabWidth(MeasuredParagraph m, int k, double x, double maxX, double defaultTabStop, bool labelTab)
        {
            ParagraphFormat pf = m.Source.Format;
            const double MinStep = 0.01;
            TabStop? stop = null;
            if (m.PositionalTabs is not null && m.PositionalTabs.TryGetValue(k, out PositionalTab? ptab))
            {
                // A positional tab ignores tab stops: its target is a fixed point of the margin or indent area.
                double from = ptab.RelativeToIndent ? pf.LeftIndent : 0;
                double to = ptab.RelativeToIndent ? maxX : maxX + pf.RightIndent;
                double target = ptab.Alignment switch
                {
                    TabAlignment.Center => (from + to) / 2,
                    TabAlignment.Right => to,
                    _ => from,
                };
                stop = new TabStop(target, ptab.Alignment == TabAlignment.Decimal ? TabAlignment.Left : ptab.Alignment);
                if (stop.Value.Alignment == TabAlignment.Left)
                {
                    return Math.Max(0, target - x);
                }
            }
            double lastCustom = double.NegativeInfinity;
            foreach (TabStop ts in stop is null ? pf.TabStops : Array.Empty<TabStop>())
            {
                if (ts.Alignment == TabAlignment.Bar)
                {
                    continue;
                }
                lastCustom = Math.Max(lastCustom, ts.Position);
                if (ts.Position > x + MinStep && (stop is null || ts.Position < stop.Value.Position))
                {
                    stop = ts;
                }
            }
            // Word: a hanging indent acts as a tab stop for the first line.
            if (m.PositionalTabs?.ContainsKey(k) != true && pf.FirstLineIndent < 0 && pf.LeftIndent > x + MinStep && (stop is null || pf.LeftIndent < stop.Value.Position))
            {
                stop = new TabStop(pf.LeftIndent);
            }
            if (stop is null)
            {
                double interval = defaultTabStop > 0 ? defaultTabStop : 36;
                double from = Math.Max(x, double.IsNegativeInfinity(lastCustom) ? x : lastCustom);
                double next = (Math.Floor((from + MinStep) / interval) + 1) * interval;
                stop = new TabStop(next);
            }
            TabStop s = stop.Value;
            if (s.Alignment == TabAlignment.Left)
            {
                return Math.Max(0, s.Position - x);
            }
            // Width of the text that follows, up to the next tab or line/paragraph end.
            string t = m.Text;
            int e = k + 1;
            int decimalAt = -1;
            while (e < t.Length && t[e] != '\t' && t[e] != ParagraphMeasurer.LineSeparator && !ParagraphMeasurer.IsPageOrColumnBreak(t[e]))
            {
                if (decimalAt < 0 && (t[e] == '.' || t[e] == ','))
                {
                    decimalAt = e;
                }
                e++;
            }
            double following = m.Width(k + 1, e);
            double w = s.Alignment switch
            {
                TabAlignment.Right => s.Position - x - following,
                TabAlignment.Center => s.Position - x - (following / 2),
                TabAlignment.Decimal => s.Position - x - (decimalAt >= 0 ? m.Width(k + 1, decimalAt) : following),
                _ => s.Position - x,
            };
            return Math.Max(0, w);
        }

        private static (double Ascent, double Descent) LineMetrics(MeasuredParagraph m, int start, int end, bool includeMark)
        {
            double asc = 0, desc = 0;
            bool any = false;
            int segPrev = -1;
            for (int k = start; k < end; k++)
            {
                int si = m.SegmentOf[k];
                if (si == segPrev)
                {
                    continue;
                }
                segPrev = si;
                Segment seg = m.Segments[si];
                asc = Math.Max(asc, seg.Ascent);
                desc = Math.Max(desc, seg.Descent);
                any = true;
            }
            if (includeMark || !any)
            {
                asc = Math.Max(asc, m.MarkAscent);
                desc = Math.Max(desc, m.MarkDescent);
            }
            return (asc, desc);
        }

        /// <summary>Line box height and baseline offset under the paragraph's line-spacing rule.</summary>
        internal static (double Height, double Baseline) ApplySpacing(ParagraphFormat pf, double ascent, double descent)
        {
            double natural = ascent + descent;
            switch (pf.LineSpacingRule)
            {
                case LineSpacingRule.Exact:
                    return (pf.LineSpacing, pf.LineSpacing - descent);
                case LineSpacingRule.AtLeast:
                    double h = Math.Max(natural, pf.LineSpacing);
                    return (h, h - descent);
                default:
                    double mult = pf.LineSpacing > 0 ? pf.LineSpacing : 1;
                    return (natural * mult, ascent);
            }
        }
    }
}
