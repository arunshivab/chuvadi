using System;
using System.Collections.Generic;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.Engine.Flow
{
    /// <summary>
    /// Area beside which text flows (square, tight and through wrapping). Coordinates are absolute page points.
    /// </summary>
    internal sealed class WrapExclusion
    {
        /// <summary>Vertical extent text must respect, wrap distances included.</summary>
        public required double Top { get; init; }

        public required double Bottom { get; init; }

        /// <summary>Horizontal extent of the object (or polygon bounding box), without the wrap distances.</summary>
        public required double Left { get; init; }

        public required double Right { get; init; }

        public required double DistanceLeft { get; init; }

        public required double DistanceRight { get; init; }

        /// <summary>Wrap contour in absolute coordinates; null for a rectangle.</summary>
        public WrapPoint[]? Polygon { get; init; }

        public required FloatWrapSide Side { get; init; }

        public bool Overlaps(double top, double height) => top < Bottom - 0.01 && top + height > Top + 0.01;

        /// <summary>Horizontal span the object blocks over the band [top, top + height], distances included; null if none.</summary>
        public (double Left, double Right)? BlockedSpan(double top, double height)
        {
            if (!Overlaps(top, height))
            {
                return null;
            }
            double l = Left, r = Right;
            if (Polygon is { Length: >= 3 } poly)
            {
                double y0 = top, y1 = top + height;
                double min = double.PositiveInfinity, max = double.NegativeInfinity;
                for (int i = 0; i < poly.Length; i++)
                {
                    WrapPoint a = poly[i];
                    WrapPoint b = poly[(i + 1) % poly.Length];
                    double lo = Math.Min(a.Y, b.Y), hi = Math.Max(a.Y, b.Y);
                    if (hi < y0 || lo > y1)
                    {
                        continue;
                    }
                    if (Math.Abs(b.Y - a.Y) < 1e-9)
                    {
                        min = Math.Min(min, Math.Min(a.X, b.X));
                        max = Math.Max(max, Math.Max(a.X, b.X));
                        continue;
                    }
                    // Clip the edge to the band and take the x at both clipped ends.
                    double ya = Math.Clamp(a.Y, y0, y1);
                    double yb = Math.Clamp(b.Y, y0, y1);
                    double xa = a.X + ((b.X - a.X) * (ya - a.Y) / (b.Y - a.Y));
                    double xb = a.X + ((b.X - a.X) * (yb - a.Y) / (b.Y - a.Y));
                    min = Math.Min(min, Math.Min(xa, xb));
                    max = Math.Max(max, Math.Max(xa, xb));
                }
                if (double.IsInfinity(min))
                {
                    return null;
                }
                l = min;
                r = max;
            }
            return (l - DistanceLeft, r + DistanceRight);
        }
    }

    internal sealed partial class FlowEngine
    {
        /// <summary>
        /// Narrowest space beside a floating object that text is placed in, in points. Word's exact threshold is not
        /// documented; 18 pt is calibrated against the acceptance corpus as it grows (DESIGN.md section 10).
        /// </summary>
        internal const double MinWrapSegmentWidth = 18;

        /// <summary>Free horizontal spaces of the text area over a band, in absolute coordinates, left to right.</summary>
        internal static List<(double Left, double Right)> FreeSegments(List<WrapExclusion> exclusions, double top, double height, double areaLeft, double areaWidth)
        {
            double areaRight = areaLeft + areaWidth;
            List<(double Left, double Right)> blocked = new List<(double, double)>();
            foreach (WrapExclusion e in exclusions)
            {
                if (e.BlockedSpan(top, height) is not (double bl, double br))
                {
                    continue;
                }
                FloatWrapSide side = e.Side;
                if (side == FloatWrapSide.Largest)
                {
                    double leftSpace = Math.Max(0, bl - areaLeft);
                    double rightSpace = Math.Max(0, areaRight - br);
                    side = leftSpace >= rightSpace ? FloatWrapSide.Left : FloatWrapSide.Right;
                }
                switch (side)
                {
                    case FloatWrapSide.Left:
                        blocked.Add((bl, double.PositiveInfinity));
                        break;
                    case FloatWrapSide.Right:
                        blocked.Add((double.NegativeInfinity, br));
                        break;
                    default:
                        blocked.Add((bl, br));
                        break;
                }
            }
            blocked.Sort((a, b) => a.Left.CompareTo(b.Left));
            List<(double Left, double Right)> free = new List<(double, double)>();
            double x = areaLeft;
            foreach ((double l, double r) in blocked)
            {
                if (l > x)
                {
                    free.Add((x, Math.Min(l, areaRight)));
                }
                x = Math.Max(x, r);
                if (x >= areaRight)
                {
                    break;
                }
            }
            if (x < areaRight)
            {
                free.Add((x, areaRight));
            }
            free.RemoveAll(f => f.Right - f.Left <= 0);
            return free;
        }

        /// <summary>
        /// Fits a paragraph's lines from <paramref name="startChar"/> at <paramref name="y"/>, flowing text beside
        /// floating objects and below top-and-bottom bands. Each row may hold several pieces (both-sides wrapping);
        /// pieces after the first are marked <see cref="FittedLine.SameRow"/>. Fitting stops two rows below
        /// <paramref name="stopBelow"/> (enough for widow control).
        /// </summary>
        internal List<(FittedLine Line, double Top)> FitAround(MeasuredParagraph m, int startChar, bool first, double y,
            double areaLeft, double areaWidth, List<(double Top, double Bottom)> bands, List<WrapExclusion> exclusions, double stopBelow)
        {
            ParagraphFormat pf = m.Source.Format;
            int n = m.Text.Length;
            double tab = _doc.DefaultTabStop;
            double fullMax = areaWidth - pf.RightIndent;
            List<(FittedLine, double)> result = new List<(FittedLine, double)>();
            List<WrapExclusion> hit = new List<WrapExclusion>();
            int s = startChar;
            int rowsBelow = 0;
            int guard = 0;
            while (guard++ < 1_000_000)
            {
                double x0 = pf.LeftIndent + (first ? pf.FirstLineIndent : 0);
                FittedLine full = LineFitter.FitLine(m, s, first, x0, fullMax, tab, true)!;
                double t = SkipBands(bands, y, full.Height);
                hit.Clear();
                foreach (WrapExclusion e in exclusions)
                {
                    if (e.Overlaps(t, full.Height))
                    {
                        hit.Add(e);
                    }
                }
                FittedLine last;
                double rowHeight;
                if (hit.Count == 0)
                {
                    result.Add((full, t));
                    last = full;
                    rowHeight = full.Height;
                }
                else
                {
                    bool placed = false;
                    last = full;
                    rowHeight = 0;
                    foreach ((double segL, double segR) in FreeSegments(hit, t, full.Height, areaLeft, areaWidth))
                    {
                        double left = Math.Max(x0, segL - areaLeft);
                        double right = Math.Min(fullMax, segR - areaLeft);
                        if (right - left < MinWrapSegmentWidth)
                        {
                            continue;
                        }
                        FittedLine? piece = LineFitter.FitLine(m, s, first, left, right, tab, false);
                        if (piece is null)
                        {
                            continue;
                        }
                        if (placed)
                        {
                            piece = piece with { SameRow = true };
                        }
                        result.Add((piece, t));
                        placed = true;
                        last = piece;
                        rowHeight = Math.Max(rowHeight, piece.Height);
                        s = piece.End;
                        first = false;
                        if (piece.IsLastLine || piece.EndsWithLineBreak || piece.EndsWithPageBreak || s >= n)
                        {
                            break;
                        }
                    }
                    if (!placed)
                    {
                        // No room beside the objects on this row: continue below the nearest one.
                        double next = double.PositiveInfinity;
                        foreach (WrapExclusion e in hit)
                        {
                            next = Math.Min(next, e.Bottom);
                        }
                        y = Math.Max(next, t + 0.5);
                        continue;
                    }
                }
                if (last.IsLastLine)
                {
                    break;
                }
                s = last.End;
                first = false;
                y = t + rowHeight;
                if (t >= stopBelow && ++rowsBelow >= 2)
                {
                    break;
                }
            }
            return result;
        }

        /// <summary>Records how a floating object's wrapping affects text on the page.</summary>
        private void AddWrap(AnchoredObjectInline a, LayoutRect rect, List<(double Top, double Bottom)> bands, List<WrapExclusion> exclusions, string path)
        {
            switch (a.Wrap)
            {
                case FloatWrap.TopAndBottom:
                    bands.Add((rect.Y - a.DistanceTop, rect.Bottom + a.DistanceBottom));
                    break;
                case FloatWrap.Square:
                    exclusions.Add(new WrapExclusion
                    {
                        Top = rect.Y - a.DistanceTop,
                        Bottom = rect.Bottom + a.DistanceBottom,
                        Left = rect.X,
                        Right = rect.Right,
                        DistanceLeft = a.DistanceLeft,
                        DistanceRight = a.DistanceRight,
                        Side = a.WrapSide,
                    });
                    break;
                case FloatWrap.Tight:
                case FloatWrap.Through:
                    if (a.Wrap == FloatWrap.Through)
                    {
                        _ctx.Warn(LayoutWarningCodes.TextWrapApproximated, LayoutWarningSeverity.Fidelity,
                            "Through wrapping is laid out as tight: text is not placed in gaps inside the wrap polygon.", path);
                    }
                    WrapPoint[]? poly = null;
                    double top = rect.Y, bottom = rect.Bottom, left = rect.X, right = rect.Right;
                    if (a.WrapPolygon is { Count: >= 3 } wp)
                    {
                        poly = new WrapPoint[wp.Count];
                        top = double.PositiveInfinity;
                        bottom = double.NegativeInfinity;
                        left = double.PositiveInfinity;
                        right = double.NegativeInfinity;
                        for (int i = 0; i < wp.Count; i++)
                        {
                            WrapPoint p = new WrapPoint(rect.X + wp[i].X, rect.Y + wp[i].Y);
                            poly[i] = p;
                            top = Math.Min(top, p.Y);
                            bottom = Math.Max(bottom, p.Y);
                            left = Math.Min(left, p.X);
                            right = Math.Max(right, p.X);
                        }
                    }
                    exclusions.Add(new WrapExclusion
                    {
                        // Word ignores distT/distB for tight and through wrapping.
                        Top = top,
                        Bottom = bottom,
                        Left = left,
                        Right = right,
                        DistanceLeft = a.DistanceLeft,
                        DistanceRight = a.DistanceRight,
                        Polygon = poly,
                        Side = a.WrapSide,
                    });
                    break;
            }
        }

        /// <summary>Moves a block (table) of height h below bands and beside-wrap objects; tables do not flow beside objects.</summary>
        private double SkipFloats(List<(double Top, double Bottom)> bands, List<WrapExclusion> exclusions, double y, double h, string path)
        {
            if (exclusions.Count == 0)
            {
                return SkipBands(bands, y, h);
            }
            List<(double Top, double Bottom)> all = new List<(double, double)>(bands);
            bool hit = false;
            foreach (WrapExclusion e in exclusions)
            {
                all.Add((e.Top, e.Bottom));
                hit |= e.Overlaps(y, h);
            }
            if (hit)
            {
                _ctx.Warn(LayoutWarningCodes.TextWrapApproximated, LayoutWarningSeverity.Fidelity,
                    "A table next to a floating object is placed below it; tables do not flow beside floating objects.", path);
            }
            return SkipBands(all, y, h);
        }
    }
}
