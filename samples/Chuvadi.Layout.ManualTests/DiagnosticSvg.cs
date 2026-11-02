using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Fonts;
using Chuvadi.Layout.Fonts.Internal;

namespace Chuvadi.Layout.ManualTests
{
    /// <summary>
    /// Diagnostic SVG writer for visual verification. Draws glyphs from their TrueType outlines (so shaped glyph
    /// ids render exactly); not part of the library. The production SVG/PNG/PDF path is Chuvadi.Layout.Pdf.
    /// </summary>
    internal static class DiagnosticSvg
    {
        public static string Render(LayoutDisplayList list, bool showLineBoxes = false, IReadOnlyList<LayoutBox>? boxes = null)
        {
            CultureInfo ci = CultureInfo.InvariantCulture;
            StringBuilder body = new StringBuilder();
            StringBuilder defs = new StringBuilder();
            Dictionary<FontFace, int> fontIds = new Dictionary<FontFace, int>(ReferenceEqualityComparer.Instance);
            HashSet<string> defined = new HashSet<string>();
            int clipId = 0;
            int openClips = 0;
            foreach (LayoutOp op in list)
            {
                switch (op)
                {
                    case GlyphRunOp run:
                        if (!fontIds.TryGetValue(run.Font, out int fid))
                        {
                            fid = fontIds.Count;
                            fontIds[run.Font] = fid;
                        }
                        GlyphOutlines outlines = GlyphOutlines.For(run.Font);
                        double s = run.FontSize / run.Font.UnitsPerEm;
                        string fill = Hex(run.Color);
                        string skew = run.SyntheticItalic ? " skewX(-12)" : string.Empty;
                        string stroke = run.SyntheticBold ? $" stroke=\"{fill}\" stroke-width=\"{(run.Font.UnitsPerEm / 40.0).ToString("0.##", ci)}\"" : string.Empty;
                        foreach (PositionedGlyph g in run.Glyphs)
                        {
                            string id = $"g{fid}_{g.GlyphId}";
                            if (defined.Add(id))
                            {
                                defs.Append("<path id=\"").Append(id).Append("\" d=\"").Append(outlines.PathData(g.GlyphId)).Append("\"/>\n");
                            }
                            double gx = run.X + g.X;
                            double gy = run.Y + g.Y;
                            body.Append(ci, $"<use href=\"#{id}\" fill=\"{fill}\"{stroke} transform=\"translate({gx:0.###},{gy:0.###}){skew} scale({(s * run.HorizontalScale):0.######},{(-s):0.######})\"/>\n");
                        }
                        break;
                    case FillRectOp r:
                        body.Append(ci, $"<rect x=\"{r.Rect.X:0.###}\" y=\"{r.Rect.Y:0.###}\" width=\"{r.Rect.Width:0.###}\" height=\"{r.Rect.Height:0.###}\" fill=\"{Hex(r.Color)}\"{Opacity(r.Color)}/>\n");
                        break;
                    case StrokeLineOp l:
                        string dash = l.Dash switch
                        {
                            LineDash.Dot => $" stroke-dasharray=\"{l.Width.ToString("0.##", ci)},{(l.Width * 2).ToString("0.##", ci)}\"",
                            LineDash.Dash => $" stroke-dasharray=\"{(l.Width * 4).ToString("0.##", ci)},{(l.Width * 3).ToString("0.##", ci)}\"",
                            LineDash.LongDash => $" stroke-dasharray=\"{(l.Width * 8).ToString("0.##", ci)},{(l.Width * 3).ToString("0.##", ci)}\"",
                            LineDash.DashDot or LineDash.DashDotDot => $" stroke-dasharray=\"{(l.Width * 4).ToString("0.##", ci)},{(l.Width * 2).ToString("0.##", ci)},{l.Width.ToString("0.##", ci)},{(l.Width * 2).ToString("0.##", ci)}\"",
                            _ => string.Empty,
                        };
                        body.Append(ci, $"<line x1=\"{l.X1:0.###}\" y1=\"{l.Y1:0.###}\" x2=\"{l.X2:0.###}\" y2=\"{l.Y2:0.###}\" stroke=\"{Hex(l.Color)}\" stroke-width=\"{l.Width:0.###}\"{dash}/>\n");
                        break;
                    case ImageOp img:
                        string mime = img.Image.Format switch
                        {
                            LayoutImageFormat.Png => "image/png",
                            LayoutImageFormat.Jpeg => "image/jpeg",
                            LayoutImageFormat.Gif => "image/gif",
                            _ => "image/bmp",
                        };
                        body.Append(ci, $"<image x=\"{img.Destination.X:0.###}\" y=\"{img.Destination.Y:0.###}\" width=\"{img.Destination.Width:0.###}\" height=\"{img.Destination.Height:0.###}\" preserveAspectRatio=\"none\" href=\"data:{mime};base64,{Convert.ToBase64String(img.Image.Data.Span)}\"/>\n");
                        break;
                    case PushClipOp c:
                        clipId++;
                        defs.Append(ci, $"<clipPath id=\"c{clipId}\"><rect x=\"{c.Rect.X:0.###}\" y=\"{c.Rect.Y:0.###}\" width=\"{c.Rect.Width:0.###}\" height=\"{c.Rect.Height:0.###}\"/></clipPath>\n");
                        body.Append(ci, $"<g clip-path=\"url(#c{clipId})\">\n");
                        openClips++;
                        break;
                    case PopClipOp:
                        if (openClips > 0)
                        {
                            body.Append("</g>\n");
                            openClips--;
                        }
                        break;
                    case PathOp p:
                        StringBuilder d = new StringBuilder();
                        foreach (PathSegment seg in p.Segments)
                        {
                            d.Append(seg.Kind switch
                            {
                                PathSegmentKind.MoveTo => string.Format(ci, "M{0:0.###} {1:0.###} ", seg.X, seg.Y),
                                PathSegmentKind.LineTo => string.Format(ci, "L{0:0.###} {1:0.###} ", seg.X, seg.Y),
                                PathSegmentKind.CubicTo => string.Format(ci, "C{0:0.###} {1:0.###} {2:0.###} {3:0.###} {4:0.###} {5:0.###} ", seg.C1X, seg.C1Y, seg.C2X, seg.C2Y, seg.X, seg.Y),
                                _ => "Z ",
                            });
                        }
                        body.Append(ci, $"<path d=\"{d}\" fill=\"{(p.Fill is LayoutColor f ? Hex(f) : "none")}\" stroke=\"{(p.Stroke is LayoutColor st ? Hex(st) : "none")}\" stroke-width=\"{p.StrokeWidth:0.###}\"/>\n");
                        break;
                }
            }
            while (openClips-- > 0)
            {
                body.Append("</g>\n");
            }
            if (showLineBoxes && boxes is not null)
            {
                foreach (LayoutBox b in boxes)
                {
                    string color = b.Kind switch
                    {
                        LayoutBoxKind.Line => "#4A90D9",
                        LayoutBoxKind.Paragraph => "#2BA84A",
                        LayoutBoxKind.TableCell or LayoutBoxKind.SheetCell => "#D98A4A",
                        _ => "#999999",
                    };
                    body.Append(ci, $"<rect x=\"{b.Rect.X:0.###}\" y=\"{b.Rect.Y:0.###}\" width=\"{b.Rect.Width:0.###}\" height=\"{b.Rect.Height:0.###}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"0.3\" stroke-opacity=\"0.6\"/>\n");
                }
            }
            StringBuilder svg = new StringBuilder();
            svg.Append(ci, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{list.WidthPt:0.###}pt\" height=\"{list.HeightPt:0.###}pt\" viewBox=\"0 0 {list.WidthPt:0.###} {list.HeightPt:0.###}\">\n");
            svg.Append("<rect width=\"100%\" height=\"100%\" fill=\"#FFFFFF\"/>\n");
            svg.Append("<defs>\n").Append(defs).Append("</defs>\n");
            svg.Append(body);
            svg.Append("</svg>\n");
            return svg.ToString();
        }

        private static string Hex(LayoutColor c) => "#" + c.R.ToString("X2", CultureInfo.InvariantCulture) + c.G.ToString("X2", CultureInfo.InvariantCulture) + c.B.ToString("X2", CultureInfo.InvariantCulture);

        private static string Opacity(LayoutColor c) => c.A == 255 ? string.Empty : $" fill-opacity=\"{(c.A / 255.0).ToString("0.###", CultureInfo.InvariantCulture)}\"";
    }
}
