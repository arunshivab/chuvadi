using System;
using System.Collections.Generic;
using System.Globalization;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Fonts;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.Engine.Flow
{
    /// <summary>Section watermarks: WordArt-style text stretched to a box, drawn as glyph outline paths so it can rotate.</summary>
    internal sealed partial class FlowEngine
    {
        /// <summary>Draws the section's watermark behind everything else on the page.</summary>
        internal void EmitWatermark(PagePlan plan, PageRecord record, List<LayoutOp> ops)
        {
            if (record.Blank || record.Section >= _doc.Sections.Count || _doc.Sections[record.Section].Watermark is not FlowWatermark wm
                || string.IsNullOrWhiteSpace(wm.Text) || !(wm.Opacity > 0))
            {
                return;
            }
            PageGeometry g = plan.Geometry;
            (double left, double contentWidth) = TextArea(g, record.PageNumber);
            double top = g.MarginTop;
            double contentHeight = Math.Max(36, g.Height - g.MarginTop - g.MarginBottom);
            contentWidth = Math.Max(36, contentWidth);
            double width = wm.Width is double w && w > 0 ? w : contentWidth;
            double height = wm.Height is double h && h > 0 ? h : Math.Min(width * 3 / Math.Max(1, wm.Text.Length), contentHeight / 2);

            // Shape the text at a nominal size, then stretch its ink box to the watermark box (WordArt "fit shape").
            const double nominal = 100;
            FlowParagraph p = FlowParagraph.Of(wm.Text.Replace('\n', ' '), new CharacterFormat { FontFamily = wm.FontFamily, FontSize = nominal });
            MeasuredParagraph m = ParagraphMeasurer.Measure(p, _ctx, this);
            List<(FontFace Face, int Glyph, double X, double Y, double Scale)> glyphs = new List<(FontFace, int, double, double, double)>();
            double pen = 0, ascent = 0, descent = 0;
            foreach (Segment seg in m.Segments)
            {
                if (seg.Font is not FontResolution font || seg.Glyphs.Length == 0)
                {
                    continue;
                }
                ascent = Math.Max(ascent, seg.Ascent);
                descent = Math.Max(descent, seg.Descent);
                double scale = seg.FontSize / font.Face.UnitsPerEm;
                int lastCluster = -1;
                double clusterPen = pen;
                foreach (MGlyph mg in seg.Glyphs)
                {
                    if (mg.Cluster != lastCluster)
                    {
                        lastCluster = mg.Cluster;
                        clusterPen = pen;
                    }
                    glyphs.Add((font.Face, mg.GlyphId, clusterPen + mg.XOffset, mg.YOffset, scale));
                    clusterPen += mg.Advance;
                    pen += mg.Advance;
                }
            }
            if (glyphs.Count == 0 || pen <= 0 || ascent + descent <= 0)
            {
                return;
            }
            double sx = width / pen;
            double sy = height / (ascent + descent);
            double cx = left + (contentWidth / 2);
            double cy = top + (contentHeight / 2);
            double angle = wm.Diagonal ? Math.PI / 4 : 0; // counter-clockwise on the page (VML rotation 315 degrees)
            double cos = Math.Cos(angle), sin = Math.Sin(angle);
            LayoutColor fill = wm.Color with { A = (byte)Math.Round(Math.Clamp(wm.Opacity, 0, 1) * 255) };

            // Box-local point (text space: x from the left, y down from the ascent line) to page coordinates.
            (double X, double Y) Map(double tx, double ty)
            {
                double bx = (tx * sx) - (width / 2);
                double by = (ty * sy) - (height / 2);
                return (cx + (bx * cos) + (by * sin), cy - (bx * sin) + (by * cos));
            }

            foreach ((FontFace face, int gid, double gx, double gy, double scale) in glyphs)
            {
                GlyphOutlines outlines = GlyphOutlines.For(face);
                if (!outlines.HasOutlines)
                {
                    _ctx.Warn(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.ContentLoss,
                        $"The watermark font '{face.FullName}' has CFF outlines, which cannot be drawn rotated; the watermark is not drawn.");
                    return;
                }
                List<PathSegment> segments = ToPage(outlines.PathData(gid), (fx, fy) => Map(gx + (fx * scale), ascent + gy - (fy * scale)));
                if (segments.Count > 0)
                {
                    ops.Add(new PathOp { Segments = segments, Fill = fill });
                }
            }
        }

        /// <summary>
        /// Converts glyph outline path data (M, L, Q, Z in font units, as written by <see cref="GlyphOutlines"/>) to page
        /// path segments; quadratic curves become the equivalent cubics.
        /// </summary>
        internal static List<PathSegment> ToPage(string data, Func<double, double, (double X, double Y)> map)
        {
            List<PathSegment> result = new List<PathSegment>();
            int i = 0;
            double curX = 0, curY = 0;
            while (i < data.Length)
            {
                char cmd = data[i++];
                switch (cmd)
                {
                    case 'M':
                    case 'L':
                    {
                        double x = Number(data, ref i), y = Number(data, ref i);
                        (double px, double py) = map(x, y);
                        result.Add(new PathSegment(cmd == 'M' ? PathSegmentKind.MoveTo : PathSegmentKind.LineTo, px, py));
                        curX = x;
                        curY = y;
                        break;
                    }
                    case 'Q':
                    {
                        double qx = Number(data, ref i), qy = Number(data, ref i), x = Number(data, ref i), y = Number(data, ref i);
                        double c1x = curX + (2.0 / 3 * (qx - curX)), c1y = curY + (2.0 / 3 * (qy - curY));
                        double c2x = x + (2.0 / 3 * (qx - x)), c2y = y + (2.0 / 3 * (qy - y));
                        (double a1, double b1) = map(c1x, c1y);
                        (double a2, double b2) = map(c2x, c2y);
                        (double px, double py) = map(x, y);
                        result.Add(new PathSegment(PathSegmentKind.CubicTo, px, py, a1, b1, a2, b2));
                        curX = x;
                        curY = y;
                        break;
                    }
                    case 'Z':
                        result.Add(new PathSegment(PathSegmentKind.Close));
                        break;
                }
            }
            return result;
        }

        private static double Number(string s, ref int i)
        {
            while (i < s.Length && s[i] == ' ')
            {
                i++;
            }
            int start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == '-' || s[i] == 'e' || s[i] == 'E'))
            {
                i++;
            }
            return double.Parse(s.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}
