// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008
// PHASE: Phase 2.1 — SVG renderer over display list

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Rendering.DisplayList;

namespace Chuvadi.Pdf.Svg;

/// <summary>
/// Renders a <see cref="PageDisplayList"/> to SVG.
/// </summary>
/// <remarks>
/// <para>
/// This is the Phase 2.1 architectural pivot: SVG output no longer walks the
/// PDF content stream directly. Instead, <see cref="DisplayListBuilder"/>
/// produces a neutral <see cref="PageDisplayList"/>, and this renderer turns
/// it into SVG. The same display list also feeds the WPF renderer (Phase 2.1
/// Stage 11) and any future output adapters (software rasterizer, etc.).
/// </para>
/// <para>
/// Coordinate system: PDF uses bottom-left origin, SVG uses top-left. The
/// output wraps content in a single <c>&lt;g transform="matrix(1 0 0 -1 0 H)"&gt;</c>
/// outer group so PDF-native coordinates flow through directly. Text elements
/// receive a local counter-flip to read upright.
/// </para>
/// </remarks>
public sealed class SvgRenderer
{
    private readonly SvgExportOptions _opts;

    /// <summary>Initialises a renderer with default options.</summary>
    public SvgRenderer() : this(new SvgExportOptions()) { }

    /// <summary>Initialises a renderer with the given options.</summary>
    public SvgRenderer(SvgExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _opts = options;
    }

    /// <summary>Renders one page of <paramref name="document"/> to an SVG string.</summary>
    public string RenderPage(PdfDocument document, int pageIndex)
    {
        ArgumentNullException.ThrowIfNull(document);
        PageDisplayList list = DisplayListBuilder.Build(document, pageIndex);
        return Render(list);
    }

    /// <summary>Renders one page of <paramref name="document"/> to UTF-8 bytes.</summary>
    public byte[] RenderPageBytes(PdfDocument document, int pageIndex)
        => Encoding.UTF8.GetBytes(RenderPage(document, pageIndex));

    /// <summary>Renders one page of <paramref name="document"/> to <paramref name="output"/>.</summary>
    public void RenderPage(PdfDocument document, int pageIndex, Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        byte[] bytes = RenderPageBytes(document, pageIndex);
        output.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Enumerates SVG renders for all pages of <paramref name="document"/>.</summary>
    public IEnumerable<string> RenderPages(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        for (int i = 0; i < document.PageCount; i++)
        {
            yield return RenderPage(document, i);
        }
    }

    /// <summary>Renders a pre-built <see cref="PageDisplayList"/> to an SVG string.</summary>
    public string Render(PageDisplayList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        SvgWriter w = new(_opts.Precision);
        w.StartSvg(list.MediaWidth, list.MediaHeight);
        w.OpenPageFlip(list.MediaHeight);

        foreach (RenderOp op in list)
        {
            switch (op)
            {
                case PathOp p: EmitPath(p, w); break;
                case TextOp t: EmitText(t, w); break;
                case ImageOp i: EmitImage(i, w); break;
                case ClipOp c: EmitClip(c, w); break;
                case TransformOp xf:
                    if (xf.Push) { w.OpenGroup(); } else { w.CloseGroup(); }
                    break;
                case OpacityOp op2:
                    if (op2.Push)
                    {
                        string attrs = $"opacity=\"{op2.Alpha.ToString("0.###", CultureInfo.InvariantCulture)}\"";
                        if (op2.Isolated) { attrs += " style=\"isolation:isolate\""; }
                        w.OpenGroup(extraAttrs: attrs);
                    }
                    else { w.CloseGroup(); }
                    break;
                case BlendModeOp bm:
                    if (bm.Push)
                    {
                        w.OpenGroup(extraAttrs: $"style=\"mix-blend-mode:{BlendModeCss(bm.Mode)}\"");
                    }
                    else { w.CloseGroup(); }
                    break;
                default: break;
            }
        }

        w.CloseGroup();
        return w.ToSvgString();
    }

    // ── Op emitters ──────────────────────────────────────────────────────

    private void EmitPath(PathOp op, SvgWriter w)
    {
        string d = PathToSvg(op.Geometry, _opts.Precision);
        bool fill = op.Mode is PaintMode.Fill or PaintMode.FillAndStroke;
        bool stroke = op.Mode is PaintMode.Stroke or PaintMode.FillAndStroke;
        string? fillCol = fill ? SrgbCss(op.FillColor) : null;
        string? strokeCol = stroke ? SrgbCss(op.StrokeColor) : null;
        double strokeWidth = op.Stroke?.LineWidth ?? 0;
        string fillRule = op.FillRule == FillRule.EvenOdd ? "evenodd" : "nonzero";
        string? extra = null;
        if (stroke && op.Stroke?.DashArray is { Length: > 0 } da)
        {
            StringBuilder ds = new();
            for (int i = 0; i < da.Length; i++)
            {
                if (i > 0) { ds.Append(','); }
                ds.Append(da[i].ToString("0.##", CultureInfo.InvariantCulture));
            }
            extra = $"stroke-dasharray=\"{ds}\"";
        }
        w.EmitPath(d, fillCol, strokeCol, strokeWidth, fillRule, extra);
    }

    private void EmitText(TextOp op, SvgWriter w)
    {
        if (op.Glyphs.Count == 0) { return; }
        // Build the visible text from glyph Unicodes.
        StringBuilder sb = new();
        foreach (DisplayListGlyph g in op.Glyphs) { sb.Append(g.Unicode); }
        string text = sb.ToString();
        if (text.Length == 0) { return; }

        AffineMatrix localFlip = new(1, 0, 0, -1, 0, 0);
        AffineMatrix combined = localFlip.Multiply(op.Transform);
        string transform = combined.ToSvgMatrix();
        string family = CssFamilyFor(op.BaseFont);
        string fill = SrgbCss(op.FillColor);
        w.EmitText(text, 0, 0, family, op.FontSize, fill, transform);
    }

    private void EmitImage(ImageOp op, SvgWriter w)
    {
        string? dataUrl = ImageEncoder.BuildDataUrl(op);
        if (dataUrl is null) { return; }
        string transform = op.Transform.ToSvgMatrix();
        w.OpenGroup(transform);
        w.EmitImage(dataUrl, 0, 0, 1, 1);
        w.CloseGroup();
    }

    private void EmitClip(ClipOp op, SvgWriter w)
    {
        string d = PathToSvg(op.Geometry, _opts.Precision);
        string rule = op.FillRule == FillRule.EvenOdd ? "evenodd" : "nonzero";
        _ = w.AddClipPath(d, rule);
        // Application of the clip to subsequent content is not implemented in
        // this pass — Stage 2 v1 limitation; tracked for Phase 2.2.
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string PathToSvg(PathGeometry g, int precision)
    {
        string fmt = "0." + new string('#', precision);
        StringBuilder sb = new();
        for (int i = 0; i < g.Segments.Count; i++)
        {
            if (i > 0) { sb.Append(' '); }
            PathSegment seg = g.Segments[i];
            switch (seg.Command)
            {
                case PathCommand.MoveTo:
                    sb.Append("M ").Append(seg.X1.ToString(fmt, CultureInfo.InvariantCulture))
                      .Append(' ').Append(seg.Y1.ToString(fmt, CultureInfo.InvariantCulture));
                    break;
                case PathCommand.LineTo:
                    sb.Append("L ").Append(seg.X1.ToString(fmt, CultureInfo.InvariantCulture))
                      .Append(' ').Append(seg.Y1.ToString(fmt, CultureInfo.InvariantCulture));
                    break;
                case PathCommand.CubicTo:
                    sb.Append("C ")
                      .Append(seg.X1.ToString(fmt, CultureInfo.InvariantCulture)).Append(' ')
                      .Append(seg.Y1.ToString(fmt, CultureInfo.InvariantCulture)).Append(' ')
                      .Append(seg.X2.ToString(fmt, CultureInfo.InvariantCulture)).Append(' ')
                      .Append(seg.Y2.ToString(fmt, CultureInfo.InvariantCulture)).Append(' ')
                      .Append(seg.X3.ToString(fmt, CultureInfo.InvariantCulture)).Append(' ')
                      .Append(seg.Y3.ToString(fmt, CultureInfo.InvariantCulture));
                    break;
                case PathCommand.Close:
                    sb.Append('Z');
                    break;
                default: break;
            }
        }
        return sb.ToString();
    }

    private static string SrgbCss(PdfColor color)
    {
        (double r, double g, double b) = color.ToSrgb();
        return $"rgb({(int)Math.Round(r * 255)},{(int)Math.Round(g * 255)},{(int)Math.Round(b * 255)})";
    }

    private static string CssFamilyFor(string baseFont)
    {
        if (baseFont.StartsWith("Helvetica", StringComparison.OrdinalIgnoreCase)
            || baseFont.StartsWith("Arial", StringComparison.OrdinalIgnoreCase))
        {
            return "Helvetica, Arial, sans-serif";
        }
        if (baseFont.StartsWith("Times", StringComparison.OrdinalIgnoreCase))
        {
            return "Times, \"Times New Roman\", serif";
        }
        if (baseFont.StartsWith("Courier", StringComparison.OrdinalIgnoreCase))
        {
            return "Courier, \"Courier New\", monospace";
        }
        if (baseFont.Equals("Symbol", StringComparison.OrdinalIgnoreCase)) { return "Symbol"; }
        if (baseFont.Equals("ZapfDingbats", StringComparison.OrdinalIgnoreCase))
        {
            return "\"Zapf Dingbats\"";
        }
        return "sans-serif";
    }

    private static string BlendModeCss(PdfBlendMode mode) => mode switch
    {
        PdfBlendMode.Multiply => "multiply",
        PdfBlendMode.Screen => "screen",
        PdfBlendMode.Overlay => "overlay",
        PdfBlendMode.Darken => "darken",
        PdfBlendMode.Lighten => "lighten",
        PdfBlendMode.ColorDodge => "color-dodge",
        PdfBlendMode.ColorBurn => "color-burn",
        PdfBlendMode.HardLight => "hard-light",
        PdfBlendMode.SoftLight => "soft-light",
        PdfBlendMode.Difference => "difference",
        PdfBlendMode.Exclusion => "exclusion",
        _ => "normal",
    };
}
