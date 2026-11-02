using System;
using System.Collections.Generic;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.Engine.Grid
{
    /// <summary>Conditional-format icons (simplified vector artwork) and right-to-left mirroring.</summary>
    internal sealed partial class GridEngine
    {
        private static readonly LayoutColor IconRed = new LayoutColor(0xE0, 0x3C, 0x31);
        private static readonly LayoutColor IconYellow = new LayoutColor(0xF4, 0xC4, 0x30);
        private static readonly LayoutColor IconGreen = new LayoutColor(0x34, 0xA8, 0x53);
        private static readonly LayoutColor IconGray = new LayoutColor(0x80, 0x80, 0x80);
        private static readonly LayoutColor IconBlack = new LayoutColor(0x26, 0x26, 0x26);
        private static readonly LayoutColor IconPink = new LayoutColor(0xF2, 0x9C, 0x9C);
        private static readonly LayoutColor IconLightGray = new LayoutColor(0xD9, 0xD9, 0xD9);
        private static readonly LayoutColor IconGold = new LayoutColor(0xF4, 0xB0, 0x30);

        /// <summary>
        /// Draws an icon at the cell's left edge, vertically centred, 12 pt (16 px) at 100 % like Excel, shrunk to fit
        /// short rows. The artwork is simplified (arrows, circles, bars, stars, triangles), coloured the way Excel's sets are.
        /// </summary>
        private void EmitIcon(GridIcon icon, LayoutRect cell, double s, List<LayoutOp> ops, string sheetName)
        {
            double size = Math.Min(12 * s, Math.Min(cell.Height, cell.Width) - (2 * s));
            if (size <= 1)
            {
                return;
            }
            _ctx.Warn(LayoutWarningCodes.IconSetApproximated, LayoutWarningSeverity.Fidelity,
                "Conditional-format icons are drawn as simplified vector icons.", sheetName);
            LayoutRect r = new LayoutRect(cell.X + (2 * s), cell.Y + ((cell.Height - size) / 2), size, size);
            string set = icon.IconSet ?? string.Empty;
            int n = Math.Max(1, icon.Count);
            int i = Math.Clamp(icon.Index, 0, n - 1);
            double t = n == 1 ? 1 : (double)i / (n - 1); // 0 = lowest values, 1 = highest

            if (set.Contains("Arrows", StringComparison.OrdinalIgnoreCase))
            {
                LayoutColor c = set.Contains("Gray", StringComparison.OrdinalIgnoreCase) ? IconGray : Ramp(t);
                Arrow(r, -90 + (180 * t), c, ops); // down (lowest) through right to up (highest)
            }
            else if (set.Contains("Triangles", StringComparison.OrdinalIgnoreCase))
            {
                if (i == 0)
                {
                    Polygon(ops, IconRed, (r.X, r.Y + (r.Height * 0.25)), (r.Right, r.Y + (r.Height * 0.25)), (r.X + (r.Width / 2), r.Bottom - (r.Height * 0.15)));
                }
                else if (i == n - 1)
                {
                    Polygon(ops, IconGreen, (r.X, r.Bottom - (r.Height * 0.25)), (r.Right, r.Bottom - (r.Height * 0.25)), (r.X + (r.Width / 2), r.Y + (r.Height * 0.15)));
                }
                else
                {
                    ops.Add(new FillRectOp(new LayoutRect(r.X + (r.Width * 0.15), r.Y + (r.Height * 0.42), r.Width * 0.7, r.Height * 0.16), IconYellow));
                }
            }
            else if (set.Contains("Stars", StringComparison.OrdinalIgnoreCase))
            {
                Star(r, IconLightGray, ops);
                if (t > 0)
                {
                    ops.Add(new PushClipOp(new LayoutRect(r.X, r.Y, r.Width * t, r.Height)));
                    Star(r, IconGold, ops);
                    ops.Add(PopClipOp.Instance);
                }
            }
            else if (set.Contains("Rating", StringComparison.OrdinalIgnoreCase) || set.Contains("Boxes", StringComparison.OrdinalIgnoreCase))
            {
                // Signal bars: the first icon has one filled bar, the last has all.
                int bars = n;
                double gap = r.Width * 0.08;
                double bw = (r.Width - (gap * (bars - 1))) / bars;
                for (int b = 0; b < bars; b++)
                {
                    double h = r.Height * (b + 1) / bars;
                    ops.Add(new FillRectOp(new LayoutRect(r.X + (b * (bw + gap)), r.Bottom - h, bw, h), b <= i ? new LayoutColor(0x3A, 0x6E, 0xC6) : IconLightGray));
                }
            }
            else if (set.Contains("Quarters", StringComparison.OrdinalIgnoreCase))
            {
                ops.Add(new PathOp { Segments = Ellipse(r), Fill = LayoutColor.White, Stroke = IconGray, StrokeWidth = Math.Max(0.25, 0.75 * s) });
                if (t > 0)
                {
                    ops.Add(new PathOp { Segments = Pie(r, t), Fill = IconGray });
                }
            }
            else
            {
                // Traffic lights, signs, symbols, flags and anything else: a coloured disc.
                LayoutColor c;
                if (set.StartsWith("4RedToBlack", StringComparison.OrdinalIgnoreCase))
                {
                    c = i switch { 0 => IconBlack, 1 => IconGray, 2 => IconPink, _ => IconRed };
                }
                else if (set.StartsWith("4TrafficLights", StringComparison.OrdinalIgnoreCase))
                {
                    c = i switch { 0 => IconBlack, 1 => IconRed, 2 => IconYellow, _ => IconGreen };
                }
                else
                {
                    c = Ramp(t);
                }
                ops.Add(new PathOp { Segments = Ellipse(r), Fill = c });
            }
        }

        /// <summary>Red (low) through yellow to green (high).</summary>
        private static LayoutColor Ramp(double t) => t < 0.34 ? IconRed : t < 0.67 ? IconYellow : IconGreen;

        private static void Polygon(List<LayoutOp> ops, LayoutColor fill, params (double X, double Y)[] pts)
        {
            List<PathSegment> seg = new List<PathSegment> { new PathSegment(PathSegmentKind.MoveTo, pts[0].X, pts[0].Y) };
            for (int k = 1; k < pts.Length; k++)
            {
                seg.Add(new PathSegment(PathSegmentKind.LineTo, pts[k].X, pts[k].Y));
            }
            seg.Add(new PathSegment(PathSegmentKind.Close));
            ops.Add(new PathOp { Segments = seg, Fill = fill });
        }

        /// <summary>An arrow centred in <paramref name="r"/> pointing at <paramref name="degrees"/> (0 = right, 90 = up).</summary>
        private static void Arrow(LayoutRect r, double degrees, LayoutColor fill, List<LayoutOp> ops)
        {
            // Arrow outline in unit space pointing right, then rotated and scaled.
            (double X, double Y)[] unit =
            {
                (-0.45, -0.12), (0.05, -0.12), (0.05, -0.35), (0.45, 0), (0.05, 0.35), (0.05, 0.12), (-0.45, 0.12),
            };
            double a = degrees * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
            double cx = r.X + (r.Width / 2), cy = r.Y + (r.Height / 2);
            (double X, double Y)[] pts = new (double, double)[unit.Length];
            for (int k = 0; k < unit.Length; k++)
            {
                double ux = unit[k].X * r.Width, uy = unit[k].Y * r.Height;
                pts[k] = (cx + (ux * cos) - (uy * sin), cy - ((ux * sin) + (uy * cos))); // y down on the page
            }
            Polygon(ops, fill, pts);
        }

        private static void Star(LayoutRect r, LayoutColor fill, List<LayoutOp> ops)
        {
            (double X, double Y)[] pts = new (double, double)[10];
            double cx = r.X + (r.Width / 2), cy = r.Y + (r.Height * 0.53);
            for (int k = 0; k < 10; k++)
            {
                double rad = (k % 2 == 0 ? 0.5 : 0.2) * r.Width;
                double a = (-90 + (k * 36)) * Math.PI / 180;
                pts[k] = (cx + (rad * Math.Cos(a)), cy + (rad * Math.Sin(a)));
            }
            Polygon(ops, fill, pts);
        }

        /// <summary>A pie slice from 12 o'clock clockwise covering <paramref name="fraction"/> of the circle (lines approximate the arc).</summary>
        private static List<PathSegment> Pie(LayoutRect r, double fraction)
        {
            double cx = r.X + (r.Width / 2), cy = r.Y + (r.Height / 2), rx = r.Width / 2, ry = r.Height / 2;
            List<PathSegment> seg = new List<PathSegment> { new PathSegment(PathSegmentKind.MoveTo, cx, cy) };
            int steps = Math.Max(2, (int)Math.Ceiling(fraction * 48));
            for (int k = 0; k <= steps; k++)
            {
                double a = (-90 + (360 * fraction * k / steps)) * Math.PI / 180;
                seg.Add(new PathSegment(PathSegmentKind.LineTo, cx + (rx * Math.Cos(a)), cy + (ry * Math.Sin(a))));
            }
            seg.Add(new PathSegment(PathSegmentKind.Close));
            return seg;
        }

        /// <summary>Mirrors every op and box about the vertical line x = axisSum / 2 (positions only; glyphs and images keep their orientation).</summary>
        private static void MirrorHorizontally(List<LayoutOp> ops, List<LayoutBox> boxes, double axisSum)
        {
            LayoutRect M(LayoutRect r) => new LayoutRect(axisSum - r.X - r.Width, r.Y, r.Width, r.Height);
            for (int k = 0; k < ops.Count; k++)
            {
                ops[k] = ops[k] switch
                {
                    GlyphRunOp g => new GlyphRunOp
                    {
                        Font = g.Font,
                        FontSize = g.FontSize,
                        X = axisSum - g.X - g.Width,
                        Y = g.Y,
                        Glyphs = g.Glyphs,
                        Text = g.Text,
                        Color = g.Color,
                        SyntheticBold = g.SyntheticBold,
                        SyntheticItalic = g.SyntheticItalic,
                        HorizontalScale = g.HorizontalScale,
                    },
                    FillRectOp f => new FillRectOp(M(f.Rect), f.Color),
                    StrokeLineOp l => new StrokeLineOp(axisSum - l.X1, l.Y1, axisSum - l.X2, l.Y2, l.Width, l.Color, l.Dash),
                    PathOp p => new PathOp
                    {
                        Segments = MirrorSegments(p.Segments, axisSum),
                        Fill = p.Fill,
                        Stroke = p.Stroke,
                        StrokeWidth = p.StrokeWidth,
                    },
                    ImageOp i => new ImageOp(i.Image, M(i.Destination), i.SourceCrop),
                    PushClipOp c => new PushClipOp(M(c.Rect)),
                    LinkOp a => new LinkOp(M(a.Rect), a.Uri, a.Anchor, a.TargetPage),
                    LayoutOp other => other,
                };
            }
            for (int k = 0; k < boxes.Count; k++)
            {
                boxes[k] = boxes[k] with { Rect = M(boxes[k].Rect) };
            }
        }

        private static List<PathSegment> MirrorSegments(IReadOnlyList<PathSegment> segments, double axisSum)
        {
            List<PathSegment> m = new List<PathSegment>(segments.Count);
            foreach (PathSegment seg in segments)
            {
                m.Add(seg.Kind == PathSegmentKind.Close ? seg : seg with { X = axisSum - seg.X, C1X = axisSum - seg.C1X, C2X = axisSum - seg.C2X });
            }
            return m;
        }
    }
}
