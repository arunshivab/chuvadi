using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Chuvadi.Layout.Fonts;

namespace Chuvadi.Layout.Drawing
{
    /// <summary>How the root &lt;svg&gt; element is sized.</summary>
    public enum LayoutSvgSize
    {
        /// <summary>width and height in points (the page's physical size).</summary>
        Points,

        /// <summary>width="100%" and no height; the viewBox keeps the aspect ratio (for responsive hosts).</summary>
        Responsive,
    }

    /// <summary>Settings for <see cref="LayoutSvg"/>. Every member has a safe default.</summary>
    public sealed class LayoutSvgOptions
    {
        private string _idPrefix = "p";
        private int _precision = 3;

        /// <summary>
        /// Prefix for every id in the SVG (glyph definitions, clip paths). Give each page its own prefix when several
        /// pages are inlined into one HTML document. Letters, digits, '-' and '_'; must start with a letter. Default "p".
        /// </summary>
        public string IdPrefix
        {
            get => _idPrefix;
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                if (value.Length == 0 || !char.IsAsciiLetter(value[0]))
                {
                    throw new ArgumentException("IdPrefix must start with an ASCII letter.", nameof(value));
                }
                foreach (char c in value)
                {
                    if (!char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')
                    {
                        throw new ArgumentException("IdPrefix may contain only ASCII letters, digits, '-' and '_'.", nameof(value));
                    }
                }
                _idPrefix = value;
            }
        }

        /// <summary>
        /// Adds an invisible text layer (the source text of every glyph run, stretched over the run) so text can be
        /// selected, copied and found with the host's search when the SVG is shown inline. Default true.
        /// </summary>
        public bool SelectableText { get; set; } = true;

        /// <summary>Page background; null leaves it transparent. Default white.</summary>
        public LayoutColor? Background { get; set; } = LayoutColor.White;

        /// <summary>How the root element is sized. Default <see cref="LayoutSvgSize.Points"/>.</summary>
        public LayoutSvgSize Size { get; set; } = LayoutSvgSize.Points;

        /// <summary>Decimal places for coordinates, 0 to 6. Default 3.</summary>
        public int Precision
        {
            get => _precision;
            set => _precision = value is >= 0 and <= 6 ? value : throw new ArgumentOutOfRangeException(nameof(value), "Precision is 0 to 6.");
        }

        /// <summary>Optional &lt;title&gt; of the SVG (shown as the tooltip and read by screen readers).</summary>
        public string? Title { get; set; }

        /// <summary>
        /// Returns the href for an image. Null (the default) embeds the image bytes as a data: URI. A host that serves
        /// images itself (for example a desktop app's web-view virtual host) can return its own URL; returning null for an
        /// image embeds that image.
        /// </summary>
        public Func<LayoutImage, string?>? ImageHref { get; set; }

        /// <summary>Emit link areas as &lt;a&gt; elements. Default true.</summary>
        public bool IncludeLinks { get; set; } = true;

        /// <summary>
        /// URI schemes allowed in external links (compared case-insensitively). Links with any other scheme
        /// (javascript:, data:, file: ...) are dropped with warning SvgLinkDropped. Default http, https, mailto.
        /// </summary>
        public ISet<string> AllowedUriSchemes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "http", "https", "mailto" };

        /// <summary>Returns the href for a link to a page (0-based index); null drops the link. Default "#page-N" (1-based).</summary>
        public Func<int, string?>? PageLinkHref { get; set; } = index => "#page-" + (index + 1).ToString(CultureInfo.InvariantCulture);

        /// <summary>Returns the href for a link to a bookmark that has no resolved page; null (the default) drops it.</summary>
        public Func<string, string?>? AnchorHref { get; set; }

        /// <summary>Optional target attribute for external links (for example "_blank"); rel="noopener noreferrer" is added with it.</summary>
        public string? ExternalLinkTarget { get; set; }

        /// <summary>Receives each output limitation once per call (codes SvgCffFontAsText, SvgImageFormat, SvgLinkDropped).</summary>
        public Action<LayoutWarning>? Warning { get; set; }
    }

    /// <summary>
    /// Writes a laid-out page as a standalone SVG 1.1 document, in pure BCL and entirely in memory. Glyphs are drawn
    /// from their TrueType outlines (each distinct glyph is one &lt;path&gt; in &lt;defs&gt;, placed with &lt;use&gt;), so the
    /// page looks the same in every browser and WebView2 with no font installed and no font files exposed. An invisible
    /// text layer carries the source text for selection and search. The output contains no script, no event
    /// attributes, no external references (unless <see cref="LayoutSvgOptions.ImageHref"/> or link options add them),
    /// and only link schemes from <see cref="LayoutSvgOptions.AllowedUriSchemes"/>.
    /// </summary>
    public static class LayoutSvg
    {
        /// <summary>Warning: a CFF (.otf) face has no TrueType outlines; its runs are drawn as SVG text in the face's family.</summary>
        public const string CffFontAsText = "SvgCffFontAsText";

        /// <summary>Warning: an image format browsers cannot show (TIFF, EMF, WMF, unknown) was drawn as a placeholder box.</summary>
        public const string ImageFormat = "SvgImageFormat";

        /// <summary>Warning: a link was dropped (disallowed scheme, or no href for an internal target).</summary>
        public const string LinkDropped = "SvgLinkDropped";

        /// <summary>The page as an SVG document.</summary>
        public static string ToSvg(LaidOutPage page, LayoutSvgOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(page);
            return ToSvg(page.DisplayList, options, cancellationToken);
        }

        /// <summary>A display list as an SVG document.</summary>
        public static string ToSvg(LayoutDisplayList list, LayoutSvgOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(list);
            return new Writer(list, options ?? new LayoutSvgOptions(), cancellationToken).Run();
        }

        /// <summary>Writes the page as an SVG document to <paramref name="output"/> (UTF-8, no BOM).</summary>
        public static void Write(LaidOutPage page, Stream output, LayoutSvgOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(output);
            string svg = ToSvg(page, options, cancellationToken);
            byte[] bytes = new UTF8Encoding(false).GetBytes(svg);
            output.Write(bytes, 0, bytes.Length);
        }

        /// <summary>Writes the page as an SVG document to <paramref name="output"/>.</summary>
        public static void Write(LaidOutPage page, TextWriter output, LayoutSvgOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(output);
            output.Write(ToSvg(page, options, cancellationToken));
        }

        private sealed class Writer
        {
            private readonly LayoutDisplayList _list;
            private readonly LayoutSvgOptions _o;
            private readonly CancellationToken _ct;
            private readonly string _fmt;
            private readonly string _p;
            private readonly StringBuilder _defs = new StringBuilder();
            private readonly StringBuilder _body = new StringBuilder();
            private readonly StringBuilder _links = new StringBuilder();
            private readonly Dictionary<FontFace, int> _fontIds = new Dictionary<FontFace, int>(ReferenceEqualityComparer.Instance);
            private readonly HashSet<long> _defined = new HashSet<long>();
            private readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);
            private int _clipId;
            private int _openClips;

            public Writer(LayoutDisplayList list, LayoutSvgOptions options, CancellationToken ct)
            {
                _list = list;
                _o = options;
                _ct = ct;
                _fmt = options.Precision == 0 ? "0" : "0." + new string('#', options.Precision);
                _p = options.IdPrefix;
            }

            public string Run()
            {
                for (int i = 0; i < _list.Count; i++)
                {
                    if ((i & 255) == 0)
                    {
                        _ct.ThrowIfCancellationRequested();
                    }
                    switch (_list[i])
                    {
                        case GlyphRunOp run:
                            Glyphs(run);
                            break;
                        case FillRectOp r:
                            _body.Append("<rect").Append(Rect(r.Rect)).Append(" fill=\"").Append(Hex(r.Color)).Append('"').Append(Alpha("fill-opacity", r.Color)).Append("/>\n");
                            break;
                        case StrokeLineOp l:
                            Line(l);
                            break;
                        case PathOp p:
                            Path(p);
                            break;
                        case ImageOp img:
                            Image(img);
                            break;
                        case PushClipOp c:
                            _clipId++;
                            _defs.Append("<clipPath id=\"").Append(_p).Append('c').Append(_clipId).Append("\"><rect").Append(Rect(c.Rect)).Append("/></clipPath>\n");
                            _body.Append("<g clip-path=\"url(#").Append(_p).Append('c').Append(_clipId).Append(")\">\n");
                            _openClips++;
                            break;
                        case PopClipOp:
                            if (_openClips > 0)
                            {
                                _body.Append("</g>\n");
                                _openClips--;
                            }
                            break;
                        case LinkOp link:
                            Link(link);
                            break;
                    }
                }
                while (_openClips > 0)
                {
                    _body.Append("</g>\n");
                    _openClips--;
                }
                _ct.ThrowIfCancellationRequested();

                StringBuilder svg = new StringBuilder(_defs.Length + _body.Length + _links.Length + 512);
                string w = N(_list.WidthPt);
                string h = N(_list.HeightPt);
                svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" version=\"1.1\"");
                if (_o.Size == LayoutSvgSize.Points)
                {
                    svg.Append(" width=\"").Append(w).Append("pt\" height=\"").Append(h).Append("pt\"");
                }
                else
                {
                    svg.Append(" width=\"100%\"");
                }
                svg.Append(" viewBox=\"0 0 ").Append(w).Append(' ').Append(h).Append("\">\n");
                if (!string.IsNullOrEmpty(_o.Title))
                {
                    svg.Append("<title>").Append(Escape(_o.Title)).Append("</title>\n");
                }
                if (_defs.Length > 0)
                {
                    svg.Append("<defs>\n").Append(_defs).Append("</defs>\n");
                }
                if (_o.Background is LayoutColor bg)
                {
                    svg.Append("<rect width=\"").Append(w).Append("\" height=\"").Append(h).Append("\" fill=\"").Append(Hex(bg)).Append('"').Append(Alpha("fill-opacity", bg)).Append("/>\n");
                }
                svg.Append(_body);
                svg.Append(_links);
                svg.Append("</svg>\n");
                return svg.ToString();
            }

            private void Glyphs(GlyphRunOp run)
            {
                if (run.Glyphs.Count == 0)
                {
                    return;
                }
                GlyphOutlines outlines = GlyphOutlines.For(run.Font);
                string fill = Hex(run.Color);
                string alpha = Alpha("fill-opacity", run.Color);
                if (!outlines.HasOutlines)
                {
                    // CFF outlines are not read: draw the text in the face's family (the viewer's font, if any).
                    Warn(CffFontAsText, LayoutWarningSeverity.Fidelity,
                        $"Face '{run.Font.FullName}' has CFF outlines; its text is drawn as SVG text in that family, so it shows only where the font is installed.");
                    _body.Append("<text x=\"").Append(N(run.X)).Append("\" y=\"").Append(N(run.Y)).Append("\" font-size=\"").Append(N(run.FontSize))
                        .Append("\" font-family=\"").Append(Escape(run.Font.FamilyName)).Append(", sans-serif\" fill=\"").Append(fill).Append('"').Append(alpha);
                    if (run.Width > 0)
                    {
                        _body.Append(" textLength=\"").Append(N(run.Width)).Append("\" lengthAdjust=\"spacingAndGlyphs\"");
                    }
                    _body.Append(" xml:space=\"preserve\">").Append(Escape(Clean(run.Text))).Append("</text>\n");
                    return;
                }
                if (!_fontIds.TryGetValue(run.Font, out int fid))
                {
                    fid = _fontIds.Count;
                    _fontIds[run.Font] = fid;
                }
                double s = run.FontSize / run.Font.UnitsPerEm;
                string sx = N6(s * run.HorizontalScale);
                string sy = N6(-s);
                string skew = run.SyntheticItalic ? " skewX(-12)" : string.Empty;
                bool open = false;
                foreach (PositionedGlyph g in run.Glyphs)
                {
                    string d = outlines.PathData(g.GlyphId);
                    if (d.Length == 0)
                    {
                        continue; // space and other blank glyphs
                    }
                    if (!open)
                    {
                        _body.Append("<g fill=\"").Append(fill).Append('"').Append(alpha);
                        if (run.SyntheticBold)
                        {
                            _body.Append(" stroke=\"").Append(fill).Append('"').Append(Alpha("stroke-opacity", run.Color))
                                .Append(" stroke-width=\"").Append(N(run.Font.UnitsPerEm / 40.0)).Append("\" stroke-linejoin=\"round\"");
                        }
                        _body.Append(">\n");
                        open = true;
                    }
                    long key = ((long)fid << 32) | (uint)g.GlyphId;
                    string id = _p + "g" + fid.ToString(CultureInfo.InvariantCulture) + "_" + g.GlyphId.ToString(CultureInfo.InvariantCulture);
                    if (_defined.Add(key))
                    {
                        _defs.Append("<path id=\"").Append(id).Append("\" d=\"").Append(d).Append("\"/>\n");
                    }
                    _body.Append("<use xlink:href=\"#").Append(id).Append("\" transform=\"translate(").Append(N(run.X + g.X)).Append(',').Append(N(run.Y + g.Y)).Append(')')
                        .Append(skew).Append(" scale(").Append(sx).Append(',').Append(sy).Append(")\"/>\n");
                }
                if (open)
                {
                    _body.Append("</g>\n");
                }
                if (_o.SelectableText)
                {
                    TextLayer(run);
                }
            }

            /// <summary>Invisible, selectable source text stretched over the run.</summary>
            private void TextLayer(GlyphRunOp run)
            {
                string text = Clean(run.Text).TrimEnd('\n', '\r');
                if (text.Length == 0 || run.Width <= 0)
                {
                    return;
                }
                bool rtl = run.Glyphs.Count > 1 && run.Glyphs[0].Cluster > run.Glyphs[^1].Cluster;
                _body.Append("<text x=\"").Append(N(rtl ? run.X + run.Width : run.X)).Append("\" y=\"").Append(N(run.Y))
                    .Append("\" font-size=\"").Append(N(run.FontSize)).Append("\" font-family=\"sans-serif\" fill=\"#000\" fill-opacity=\"0\"")
                    .Append(" textLength=\"").Append(N(run.Width)).Append("\" lengthAdjust=\"spacingAndGlyphs\"");
                if (rtl)
                {
                    _body.Append(" direction=\"rtl\" unicode-bidi=\"embed\"");
                }
                _body.Append(" xml:space=\"preserve\">").Append(Escape(text)).Append("</text>\n");
            }

            private void Line(StrokeLineOp l)
            {
                _body.Append("<line x1=\"").Append(N(l.X1)).Append("\" y1=\"").Append(N(l.Y1)).Append("\" x2=\"").Append(N(l.X2)).Append("\" y2=\"").Append(N(l.Y2))
                    .Append("\" stroke=\"").Append(Hex(l.Color)).Append('"').Append(Alpha("stroke-opacity", l.Color)).Append(" stroke-width=\"").Append(N(l.Width)).Append('"');
                double w = l.Width;
                string? dash = l.Dash switch
                {
                    LineDash.Dot => N(w) + "," + N(w * 2),
                    LineDash.Dash => N(w * 4) + "," + N(w * 3),
                    LineDash.LongDash => N(w * 8) + "," + N(w * 3),
                    LineDash.DashDot => N(w * 4) + "," + N(w * 2) + "," + N(w) + "," + N(w * 2),
                    LineDash.DashDotDot => N(w * 4) + "," + N(w * 2) + "," + N(w) + "," + N(w * 2) + "," + N(w) + "," + N(w * 2),
                    _ => null,
                };
                if (dash is not null)
                {
                    _body.Append(" stroke-dasharray=\"").Append(dash).Append('"');
                }
                _body.Append("/>\n");
            }

            private void Path(PathOp p)
            {
                StringBuilder d = new StringBuilder();
                foreach (PathSegment seg in p.Segments)
                {
                    switch (seg.Kind)
                    {
                        case PathSegmentKind.MoveTo:
                            d.Append('M').Append(N(seg.X)).Append(' ').Append(N(seg.Y)).Append(' ');
                            break;
                        case PathSegmentKind.LineTo:
                            d.Append('L').Append(N(seg.X)).Append(' ').Append(N(seg.Y)).Append(' ');
                            break;
                        case PathSegmentKind.CubicTo:
                            d.Append('C').Append(N(seg.C1X)).Append(' ').Append(N(seg.C1Y)).Append(' ').Append(N(seg.C2X)).Append(' ').Append(N(seg.C2Y))
                                .Append(' ').Append(N(seg.X)).Append(' ').Append(N(seg.Y)).Append(' ');
                            break;
                        default:
                            d.Append("Z ");
                            break;
                    }
                }
                if (d.Length == 0)
                {
                    return;
                }
                _body.Append("<path d=\"").Append(d.ToString().TrimEnd()).Append('"');
                if (p.Fill is LayoutColor f)
                {
                    _body.Append(" fill=\"").Append(Hex(f)).Append('"').Append(Alpha("fill-opacity", f));
                }
                else
                {
                    _body.Append(" fill=\"none\"");
                }
                if (p.Stroke is LayoutColor st)
                {
                    _body.Append(" stroke=\"").Append(Hex(st)).Append('"').Append(Alpha("stroke-opacity", st)).Append(" stroke-width=\"").Append(N(p.StrokeWidth)).Append('"');
                }
                _body.Append("/>\n");
            }

            private void Image(ImageOp op)
            {
                LayoutImage image = op.Image;
                string? mime = image.Format switch
                {
                    LayoutImageFormat.Png => "image/png",
                    LayoutImageFormat.Jpeg => "image/jpeg",
                    LayoutImageFormat.Gif => "image/gif",
                    LayoutImageFormat.Bmp => "image/bmp",
                    LayoutImageFormat.Svg => "image/svg+xml",
                    _ => null,
                };
                LayoutRect dst = op.Destination;
                if (mime is null)
                {
                    Warn(ImageFormat, LayoutWarningSeverity.ContentLoss, $"{image.Format} images cannot be shown in SVG viewers; a placeholder box is drawn.");
                    _body.Append("<rect").Append(Rect(dst)).Append(" fill=\"#F2F2F2\" stroke=\"#A6A6A6\" stroke-width=\"0.75\"/>\n");
                    return;
                }
                string? href = _o.ImageHref?.Invoke(image);
                href ??= "data:" + mime + ";base64," + Convert.ToBase64String(image.Data.Span);
                string escaped = Escape(href);
                if (op.SourceCrop is LayoutRect crop && image.PixelWidth > 0 && image.PixelHeight > 0 && crop.Width > 0 && crop.Height > 0)
                {
                    // A nested viewport shows the crop rectangle of the full-size image, stretched to the destination.
                    _body.Append("<svg").Append(Rect(dst)).Append(" viewBox=\"").Append(N(crop.X)).Append(' ').Append(N(crop.Y)).Append(' ').Append(N(crop.Width)).Append(' ').Append(N(crop.Height))
                        .Append("\" preserveAspectRatio=\"none\" overflow=\"hidden\"><image width=\"").Append(image.PixelWidth.ToString(CultureInfo.InvariantCulture))
                        .Append("\" height=\"").Append(image.PixelHeight.ToString(CultureInfo.InvariantCulture)).Append("\" preserveAspectRatio=\"none\" xlink:href=\"").Append(escaped).Append("\"/></svg>\n");
                    return;
                }
                _body.Append("<image").Append(Rect(dst)).Append(" preserveAspectRatio=\"none\" xlink:href=\"").Append(escaped).Append("\"/>\n");
            }

            private void Link(LinkOp link)
            {
                if (!_o.IncludeLinks)
                {
                    return;
                }
                string? href = null;
                bool external = false;
                if (link.Uri is string uri)
                {
                    if (System.Uri.TryCreate(uri.Trim(), UriKind.Absolute, out Uri? parsed) && _o.AllowedUriSchemes.Contains(parsed.Scheme))
                    {
                        href = parsed.AbsoluteUri;
                        external = true;
                    }
                    else if (uri.StartsWith('#'))
                    {
                        href = _o.AnchorHref?.Invoke(uri.Substring(1));
                    }
                    else
                    {
                        Warn(LinkDropped, LayoutWarningSeverity.Fidelity, "A link with a scheme outside LayoutSvgOptions.AllowedUriSchemes was dropped.");
                        return;
                    }
                }
                else if (link.TargetPage is int page)
                {
                    href = _o.PageLinkHref?.Invoke(page);
                }
                else if (link.Anchor is string anchor)
                {
                    href = _o.AnchorHref?.Invoke(anchor);
                }
                if (href is null)
                {
                    Warn(LinkDropped, LayoutWarningSeverity.Fidelity, "An internal link has no href (LayoutSvgOptions.PageLinkHref / AnchorHref) and was dropped.");
                    return;
                }
                _links.Append("<a xlink:href=\"").Append(Escape(href)).Append('"');
                if (external && !string.IsNullOrEmpty(_o.ExternalLinkTarget))
                {
                    _links.Append(" target=\"").Append(Escape(_o.ExternalLinkTarget)).Append("\" rel=\"noopener noreferrer\"");
                }
                _links.Append("><rect").Append(Rect(link.Rect)).Append(" fill=\"#000\" fill-opacity=\"0\" pointer-events=\"all\"/></a>\n");
            }

            private void Warn(string code, LayoutWarningSeverity severity, string message)
            {
                if (_warned.Add(code))
                {
                    _o.Warning?.Invoke(new LayoutWarning(code, severity, message));
                }
            }

            private string Rect(LayoutRect r)
                => " x=\"" + N(r.X) + "\" y=\"" + N(r.Y) + "\" width=\"" + N(Math.Max(0, r.Width)) + "\" height=\"" + N(Math.Max(0, r.Height)) + "\"";

            private string N(double v)
            {
                if (double.IsNaN(v) || double.IsInfinity(v))
                {
                    return "0";
                }
                string s = v.ToString(_fmt, CultureInfo.InvariantCulture);
                return s == "-0" ? "0" : s;
            }

            private static string N6(double v) => double.IsFinite(v) ? v.ToString("0.######", CultureInfo.InvariantCulture) : "0";

            private static string Hex(LayoutColor c)
                => "#" + c.R.ToString("X2", CultureInfo.InvariantCulture) + c.G.ToString("X2", CultureInfo.InvariantCulture) + c.B.ToString("X2", CultureInfo.InvariantCulture);

            private static string Alpha(string attribute, LayoutColor c)
                => c.A == 255 ? string.Empty : " " + attribute + "=\"" + (c.A / 255.0).ToString("0.###", CultureInfo.InvariantCulture) + "\"";
        }

        /// <summary>Removes characters XML 1.0 cannot carry: controls other than tab/newline become spaces, lone surrogates U+FFFD.</summary>
        internal static string Clean(string text)
        {
            StringBuilder? sb = null;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                char? replace = null;
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    {
                        sb?.Append(c).Append(text[i + 1]);
                        i++;
                        continue;
                    }
                    replace = '\uFFFD';
                }
                else if (char.IsLowSurrogate(c))
                {
                    replace = '\uFFFD';
                }
                else if ((c < 0x20 && c != '\n') || c == '\uFFFE' || c == '\uFFFF')
                {
                    replace = ' ';
                }
                if (replace is char r)
                {
                    sb ??= new StringBuilder(text, 0, i, text.Length);
                    sb.Append(r);
                }
                else
                {
                    sb?.Append(c);
                }
            }
            return sb?.ToString() ?? text;
        }

        /// <summary>XML-escapes text and attribute values.</summary>
        internal static string Escape(string s)
        {
            if (s.AsSpan().IndexOfAny("<>&\"'") < 0)
            {
                return s;
            }
            StringBuilder sb = new StringBuilder(s.Length + 16);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '&': sb.Append("&amp;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\'': sb.Append("&#39;"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}
