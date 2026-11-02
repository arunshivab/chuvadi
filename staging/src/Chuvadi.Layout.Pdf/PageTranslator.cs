using System;
using System.Collections.Generic;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Fonts;
using Chuvadi.Pdf.Authoring;
using Chuvadi.Pdf.Graphics;
using LayoutPathSegment = Chuvadi.Layout.Drawing.PathSegment;
using LayoutPathSegmentKind = Chuvadi.Layout.Drawing.PathSegmentKind;
using PdfShapedGlyph = Chuvadi.Pdf.Text.Shaping.ShapedGlyph;

namespace Chuvadi.Layout.Pdf
{
    /// <summary>
    /// Draws a <see cref="LayoutDisplayList"/> onto a chuvadi-pdf <see cref="PageBuilder"/> (Chuvadi.Pdf 3.26.0):
    /// clips are real PDF clips, translucent colours are drawn in opacity scopes, dashes are line styles, links are
    /// link annotations, glyph runs carry their source text (ToUnicode or /ActualText) and the text matrix (character
    /// scaling, synthetic italic and bold), and images are cropped. Fonts are embedded per face (TrueType, CFF and
    /// collection faces alike) and subset.
    /// </summary>
    internal sealed class PageTranslator
    {
        private readonly PdfDocumentBuilder _builder;
        private readonly Dictionary<FontFace, string?> _fontNames;
        private readonly Action<LayoutWarning>? _warn;
        private readonly HashSet<string> _warned;

        /// <summary>The slant of synthetic italic: tan(about 11 degrees), as Word draws it.</summary>
        internal const double SyntheticItalicSkew = 0.2;

        /// <summary>The outline stroke of synthetic bold, as a fraction of the font size.</summary>
        internal const double SyntheticBoldStroke = 0.03;

        public PageTranslator(PdfDocumentBuilder builder, Dictionary<FontFace, string?> fontNames, HashSet<string> warned, Action<LayoutWarning>? warn)
        {
            _builder = builder;
            _fontNames = fontNames;
            _warned = warned;
            _warn = warn;
        }

        public void Draw(LayoutDisplayList list, PageBuilder page)
        {
            int clips = 0;
            foreach (LayoutOp op in list)
            {
                switch (op)
                {
                    case PushClipOp push:
                        page.PushClip(push.Rect.X, push.Rect.Y, Math.Max(0, push.Rect.Width), Math.Max(0, push.Rect.Height));
                        clips++;
                        break;
                    case PopClipOp:
                        if (clips > 0)
                        {
                            page.PopClip();
                            clips--;
                        }
                        break;
                    case GlyphRunOp run:
                        DrawGlyphs(run, page);
                        break;
                    case FillRectOp fill:
                        if (fill.Color.A == 0 || fill.Rect.Width <= 0 || fill.Rect.Height <= 0)
                        {
                            break;
                        }
                        Translucent(page, fill.Color.A, () => page.DrawRectangle(fill.Rect.X, fill.Rect.Y, fill.Rect.Width, fill.Rect.Height, fill: ToColor(fill.Color)));
                        break;
                    case StrokeLineOp line:
                        DrawLine(line, page);
                        break;
                    case PathOp path:
                        DrawPath(path, page);
                        break;
                    case ImageOp image:
                        DrawImage(image, page);
                        break;
                    case LinkOp link:
                        DrawLink(link, page);
                        break;
                }
            }
            while (clips-- > 0)
            {
                page.PopClip();
            }
        }

        /// <summary>Runs <paramref name="draw"/> in an opacity scope when <paramref name="alpha"/> is below 255.</summary>
        private static void Translucent(PageBuilder page, byte alpha, Action draw)
        {
            if (alpha >= 255)
            {
                draw();
                return;
            }
            page.PushOpacity(alpha / 255.0);
            draw();
            page.PopOpacity();
        }

        /// <summary>The PDF font name of a face, registering it on first use; null when the writer cannot embed it.</summary>
        private string? FontName(FontFace face)
        {
            if (!_fontNames.TryGetValue(face, out string? name))
            {
                name = "LF" + _fontNames.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                try
                {
                    _builder.AddFont(name, PdfFaces.Program(face), face.FaceIndex);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Warn("PdfFontNotEmbedded", LayoutWarningSeverity.ContentLoss,
                        $"'{face.FullName}' could not be embedded by the PDF writer ({ex.Message}); its text is omitted.");
                    name = null;
                }
                _fontNames[face] = name;
            }
            return name;
        }

        private void DrawGlyphs(GlyphRunOp run, PageBuilder page)
        {
            if (run.Glyphs.Count == 0 || run.FontSize <= 0 || run.Color.A == 0)
            {
                return;
            }
            if (FontName(run.Font) is not string font)
            {
                return;
            }
            double size = run.FontSize;
            double scale = run.HorizontalScale > 0 ? run.HorizontalScale : 1;
            GlyphRunStyle style = new GlyphRunStyle
            {
                HorizontalScale = scale,
                Skew = run.SyntheticItalic ? SyntheticItalicSkew : 0,
                StrokeWidth = run.SyntheticBold ? size * SyntheticBoldStroke : 0,
            };

            // Layout's positions already include the character scaling; the writer scales advances itself, so the
            // advances passed are unscaled: the distance from one glyph's origin to the next, divided by the scale.
            double toThousandths = 1000.0 / size;
            PdfShapedGlyph[] shaped = new PdfShapedGlyph[run.Glyphs.Count];
            int[] clusters = new int[run.Glyphs.Count];
            bool clustersValid = true;
            for (int i = 0; i < run.Glyphs.Count; i++)
            {
                PositionedGlyph g = run.Glyphs[i];
                double advance = i + 1 < run.Glyphs.Count ? run.Glyphs[i + 1].X - g.X : g.Advance;
                shaped[i] = new PdfShapedGlyph(g.GlyphId, advance / scale * toThousandths, 0, -g.Y * toThousandths, i);
                clusters[i] = g.Cluster;
                clustersValid &= g.Cluster >= 0 && g.Cluster <= run.Text.Length;
            }
            double x = run.X + run.Glyphs[0].X;
            double top = run.Y - size;
            Translucent(page, run.Color.A, () =>
            {
                if (clustersValid && run.Text.Length > 0)
                {
                    page.DrawShapedRun(shaped, run.Text, clusters, x, top, font, size, ToColor(run.Color), style);
                }
                else
                {
                    Warn("PdfTextNotExtractable", LayoutWarningSeverity.Fidelity, "A glyph run had no source text mapping; it is drawn but does not extract as text.");
                    page.DrawShapedRun(shaped, run.Text.Length > 0 ? run.Text : " ", new int[shaped.Length], x, top, font, size, ToColor(run.Color), style);
                }
            });
        }

        private static void DrawLine(StrokeLineOp line, PageBuilder page)
        {
            if (line.Color.A == 0 || line.Width <= 0)
            {
                return;
            }
            double unit = Math.Max(line.Width, 0.5);
            double[] pattern = line.Dash switch
            {
                LineDash.Dot => new[] { 1.0, 2.0 },
                LineDash.Dash => new[] { 4.0, 3.0 },
                LineDash.LongDash => new[] { 8.0, 3.0 },
                LineDash.DashDot => new[] { 4.0, 2.0, 1.0, 2.0 },
                LineDash.DashDotDot => new[] { 4.0, 2.0, 1.0, 2.0, 1.0, 2.0 },
                _ => Array.Empty<double>(),
            };
            for (int i = 0; i < pattern.Length; i++)
            {
                pattern[i] *= unit;
            }
            LineStyle style = new LineStyle { Width = line.Width, DashPattern = pattern };
            Translucent(page, line.Color.A, () => page.DrawLine(line.X1, line.Y1, line.X2, line.Y2, ToColor(line.Color), style));
        }

        private static void DrawPath(PathOp op, PageBuilder page)
        {
            Path path = new Path();
            foreach (LayoutPathSegment s in op.Segments)
            {
                switch (s.Kind)
                {
                    case LayoutPathSegmentKind.MoveTo:
                        path.MoveTo(s.X, s.Y);
                        break;
                    case LayoutPathSegmentKind.LineTo:
                        path.LineTo(s.X, s.Y);
                        break;
                    case LayoutPathSegmentKind.CubicTo:
                        path.CubicBezierTo(new PointF(s.C1X, s.C1Y), new PointF(s.C2X, s.C2Y), new PointF(s.X, s.Y));
                        break;
                    case LayoutPathSegmentKind.Close:
                        path.ClosePath();
                        break;
                }
            }
            if (op.Fill is LayoutColor f && f.A > 0)
            {
                Translucent(page, f.A, () => page.DrawPath(path, ToColor(f), null, op.StrokeWidth));
            }
            if (op.Stroke is LayoutColor st && st.A > 0 && op.StrokeWidth > 0)
            {
                Translucent(page, st.A, () => page.DrawPath(path, null, ToColor(st), op.StrokeWidth));
            }
        }

        private void DrawImage(ImageOp op, PageBuilder page)
        {
            LayoutRect d = op.Destination;
            if (d.Width <= 0 || d.Height <= 0)
            {
                return;
            }
            if (op.Image.Format is LayoutImageFormat.Png or LayoutImageFormat.Jpeg or LayoutImageFormat.Bmp or LayoutImageFormat.Tiff)
            {
                try
                {
                    byte[] data = op.Image.Data.ToArray();
                    if (op.SourceCrop is LayoutRect c && c.Width > 0 && c.Height > 0)
                    {
                        page.DrawImage(data, d.X, d.Y, d.Width, d.Height, new ImageCrop(c.X, c.Y, c.Width, c.Height));
                    }
                    else
                    {
                        page.DrawImage(data, d.X, d.Y, d.Width, d.Height);
                    }
                    return;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Warn("PdfImageUnreadable", LayoutWarningSeverity.ContentLoss, "An image could not be decoded by the PDF writer: " + ex.Message);
                }
            }
            else
            {
                Warn("PdfImageFormat", LayoutWarningSeverity.ContentLoss, $"{op.Image.Format} images are not supported by the PDF writer; a placeholder box is drawn.");
            }
            Color grey = Color.FromBytes(0xA0, 0xA0, 0xA0);
            page.DrawRectangle(d.X, d.Y, d.Width, d.Height, fill: Color.FromBytes(0xF2, 0xF2, 0xF2), stroke: grey, strokeWidth: 0.5);
        }

        private void DrawLink(LinkOp link, PageBuilder page)
        {
            LayoutRect r = link.Rect;
            if (r.Width <= 0 || r.Height <= 0)
            {
                return;
            }
            if (link.Uri is string uri)
            {
                if (Uri.TryCreate(uri, UriKind.Absolute, out Uri? target))
                {
                    page.AddLink(r.X, r.Y, r.Width, r.Height, target);
                }
                else
                {
                    Warn("PdfLinkInvalid", LayoutWarningSeverity.Fidelity, "A link whose target is not an absolute URI was not emitted.");
                }
                return;
            }
            if (link.TargetPage is int pageIndex && pageIndex >= 0)
            {
                page.AddLink(r.X, r.Y, r.Width, r.Height, pageIndex, 0);
                return;
            }
            if (link.Anchor is not null)
            {
                Warn("PdfInternalLink", LayoutWarningSeverity.Fidelity, "Internal (bookmark) links without a resolved target page are not emitted by the PDF writer.");
            }
        }

        private void Warn(string code, LayoutWarningSeverity severity, string message)
        {
            if (_warned.Add(code))
            {
                _warn?.Invoke(new LayoutWarning(code, severity, message));
            }
        }

        private static Color ToColor(LayoutColor c) => Color.FromBytes(c.R, c.G, c.B);
    }
}
