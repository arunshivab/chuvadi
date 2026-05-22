// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8 (Graphics), §9 (Text), §11 (Transparency)
// PHASE: Phase 2.0 — SVG export

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Svg;

/// <summary>
/// Translates PDF page content streams to SVG.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the structure of <c>Chuvadi.Pdf.Rendering.PageRasterizer</c>:
/// walks the content stream's operator tokens, maintaining a graphics
/// state stack, but emits SVG elements via <see cref="SvgWriter"/> rather
/// than rasterizing to a pixel buffer.
/// </para>
/// <para>
/// Coordinate system: SVG uses top-left origin (Y down); PDF uses
/// bottom-left (Y up). The export wraps page content in a single
/// <c>&lt;g transform="matrix(1 0 0 -1 0 H)"&gt;</c> outer group so PDF-native
/// coordinates flow through directly. Text elements receive a local
/// counter-flip so glyphs read upright.
/// </para>
/// <para>
/// As of Phase 2.1, this class is obsolete; new code should use
/// <see cref="SvgRenderer"/>, which renders a neutral
/// <see cref="Chuvadi.Pdf.Rendering.DisplayList.PageDisplayList"/> and
/// shares its pipeline with other output adapters.
/// </para>
/// </remarks>
[System.Obsolete("Use SvgRenderer instead. SvgExporter walks the PDF content stream directly; SvgRenderer consumes the neutral PageDisplayList built by DisplayListBuilder, allowing the same intermediate representation to feed other output adapters (WPF, etc.).", error: false)]
public static class SvgExporter
{
    /// <summary>Exports a page to an SVG string.</summary>
    public static string ExportPage(PdfDocument document, int pageIndex, SvgExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (pageIndex < 0 || pageIndex >= document.PageCount)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex));
        }
        return new Exporter(document, options ?? new SvgExportOptions()).Export(pageIndex);
    }

    /// <summary>Exports a page to a byte array (UTF-8).</summary>
    public static byte[] ExportPageBytes(PdfDocument document, int pageIndex, SvgExportOptions? options = null)
        => Encoding.UTF8.GetBytes(ExportPage(document, pageIndex, options));

    /// <summary>Exports a page directly to a stream.</summary>
    public static void ExportPage(PdfDocument document, int pageIndex, Stream output,
        SvgExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        byte[] bytes = ExportPageBytes(document, pageIndex, options);
        output.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Enumerates SVG exports for all pages.</summary>
    public static IEnumerable<string> ExportPages(PdfDocument document, SvgExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        Exporter ex = new(document, options ?? new SvgExportOptions());
        for (int i = 0; i < document.PageCount; i++)
        {
            yield return ex.Export(i);
        }
    }

    // ── Internal workhorse ────────────────────────────────────────────────

    private sealed class Exporter
    {
        private readonly PdfDocument _doc;
        private readonly SvgExportOptions _opts;
        private readonly Dictionary<string, string> _fontFamilyByKey = new();
        private readonly HashSet<string> _emittedFontFaces = new();

        internal Exporter(PdfDocument doc, SvgExportOptions opts)
        {
            _doc = doc;
            _opts = opts;
        }

        internal string Export(int pageIndex)
        {
            PdfPage page = _doc.Pages[pageIndex];
            double width = page.Width;
            double height = page.Height;

            SvgWriter w = new(_opts.Precision);
            w.StartSvg(width, height);
            w.OpenPageFlip(height);

            byte[] content = ReadContentStream(page);
            PdfDictionary? resources = page.Resources;

            SvgStateStack stack = new();
            DispatchOperators(content, stack, w, resources);

            w.CloseGroup();   // close the page flip group
            return w.ToSvgString();
        }

        private byte[] ReadContentStream(PdfPage page)
        {
            // Resolve /Contents which may be a single stream or an array.
            if (!page.Dictionary.TryGetValue(PdfName.Intern("Contents"), out PdfPrimitive? contentsVal))
            {
                return Array.Empty<byte>();
            }
            PdfPrimitive resolved = _doc.Objects.Resolve(contentsVal);
            using MemoryStream merged = new();
            if (resolved is PdfStream singleStream)
            {
                byte[] data = StreamDecoder.Decode(singleStream);
                merged.Write(data, 0, data.Length);
            }
            else if (resolved is PdfArray arr)
            {
                foreach (PdfPrimitive entry in arr)
                {
                    if (_doc.Objects.Resolve(entry) is PdfStream s)
                    {
                        byte[] data = StreamDecoder.Decode(s);
                        merged.Write(data, 0, data.Length);
                        merged.WriteByte((byte)' ');   // separator
                    }
                }
            }
            return merged.ToArray();
        }

        private void DispatchOperators(byte[] content, SvgStateStack stack,
            SvgWriter w, PdfDictionary? resources)
        {
            using MemoryStream ms = new(content);
            using PdfTokenizer tokenizer = new(ms);
            List<PdfToken> operands = new();

            while (true)
            {
                PdfToken token = tokenizer.Read();
                if (token.IsEndOfStream) { break; }

                if (token.Type == PdfTokenType.ArrayStart)
                {
                    // Collect array contents — used by TJ.
                    operands.Add(token);
                    while (true)
                    {
                        PdfToken inner = tokenizer.Read();
                        if (inner.IsEndOfStream || inner.Type == PdfTokenType.ArrayEnd)
                        {
                            operands.Add(new PdfToken(PdfTokenType.ArrayEnd, Array.Empty<byte>(), 0));
                            break;
                        }
                        operands.Add(inner);
                    }
                    continue;
                }

                if (token.Type != PdfTokenType.Keyword)
                {
                    operands.Add(token);
                    continue;
                }

                string op = token.RawText;
                Execute(op, operands, stack, w, resources);
                operands.Clear();
            }
        }

        private void Execute(string op, List<PdfToken> operands, SvgStateStack stack,
            SvgWriter w, PdfDictionary? resources)
        {
            SvgGraphicsState s = stack.Current;

            switch (op)
            {
                // ── Graphics state ───────────────────────────────────────
                case "q": stack.Push(); w.OpenGroup(); break;
                case "Q": stack.Pop(); w.CloseGroup(); break;
                case "cm":
                    if (operands.Count >= 6)
                    {
                        Mat2x3 m = new(
                            Num(operands[0]), Num(operands[1]),
                            Num(operands[2]), Num(operands[3]),
                            Num(operands[4]), Num(operands[5]));
                        s.Ctm = m.Multiply(s.Ctm);
                        // We don't emit a separate transform per cm — the path
                        // operators apply CTM internally before emission. This
                        // keeps SVG output flatter and easier for browsers.
                    }
                    break;

                // ── Stroke parameters ────────────────────────────────────
                case "w": if (operands.Count > 0) { s.LineWidth = Num(operands[0]); } break;
                case "J": if (operands.Count > 0) { s.LineCap = (int)Num(operands[0]); } break;
                case "j": if (operands.Count > 0) { s.LineJoin = (int)Num(operands[0]); } break;
                case "M": if (operands.Count > 0) { s.MiterLimit = Num(operands[0]); } break;
                case "d":
                    if (operands.Count >= 2) { s.DashArray = ParseDashArray(operands); }
                    break;

                // ── Color ────────────────────────────────────────────────
                case "g":
                    if (operands.Count > 0)
                    {
                        double g = Num(operands[0]);
                        s.FillColor = $"rgb({(int)(g * 255)},{(int)(g * 255)},{(int)(g * 255)})";
                    }
                    break;
                case "G":
                    if (operands.Count > 0)
                    {
                        double g = Num(operands[0]);
                        s.StrokeColor = $"rgb({(int)(g * 255)},{(int)(g * 255)},{(int)(g * 255)})";
                    }
                    break;
                case "rg":
                    if (operands.Count >= 3)
                    {
                        s.FillColor = $"rgb({(int)(Num(operands[0]) * 255)},{(int)(Num(operands[1]) * 255)},{(int)(Num(operands[2]) * 255)})";
                    }
                    break;
                case "RG":
                    if (operands.Count >= 3)
                    {
                        s.StrokeColor = $"rgb({(int)(Num(operands[0]) * 255)},{(int)(Num(operands[1]) * 255)},{(int)(Num(operands[2]) * 255)})";
                    }
                    break;
                case "k":
                    if (operands.Count >= 4)
                    {
                        s.FillColor = CmykToRgb(Num(operands[0]), Num(operands[1]), Num(operands[2]), Num(operands[3]));
                    }
                    break;
                case "K":
                    if (operands.Count >= 4)
                    {
                        s.StrokeColor = CmykToRgb(Num(operands[0]), Num(operands[1]), Num(operands[2]), Num(operands[3]));
                    }
                    break;

                // ── Path construction (apply CTM to each coordinate) ────
                case "m":
                    if (operands.Count >= 2)
                    {
                        (double x, double y) = ApplyMat(s.Ctm, Num(operands[0]), Num(operands[1]));
                        s.AppendPath(FormatInvariant("M {0:0.####} {1:0.####}", x, y));
                    }
                    break;
                case "l":
                    if (operands.Count >= 2)
                    {
                        (double x, double y) = ApplyMat(s.Ctm, Num(operands[0]), Num(operands[1]));
                        s.AppendPath(FormatInvariant("L {0:0.####} {1:0.####}", x, y));
                    }
                    break;
                case "c":
                    if (operands.Count >= 6)
                    {
                        (double x1, double y1) = ApplyMat(s.Ctm, Num(operands[0]), Num(operands[1]));
                        (double x2, double y2) = ApplyMat(s.Ctm, Num(operands[2]), Num(operands[3]));
                        (double x3, double y3) = ApplyMat(s.Ctm, Num(operands[4]), Num(operands[5]));
                        s.AppendPath(FormatInvariant(
                            "C {0:0.####} {1:0.####} {2:0.####} {3:0.####} {4:0.####} {5:0.####}",
                            x1, y1, x2, y2, x3, y3));
                    }
                    break;
                case "v":   // v: first control = current point
                case "y":   // y: second control = endpoint
                    // SVG doesn't have direct equivalents — emit as full cubic.
                    // For v1 simplicity, we approximate v and y by emitting C with
                    // the appropriate control point reused.
                    if (operands.Count >= 4)
                    {
                        (double xA, double yA) = ApplyMat(s.Ctm, Num(operands[0]), Num(operands[1]));
                        (double xB, double yB) = ApplyMat(s.Ctm, Num(operands[2]), Num(operands[3]));
                        if (op == "v")
                        {
                            // v: control1 implicit = current point. Use S (smooth curveto).
                            s.AppendPath(FormatInvariant("S {0:0.####} {1:0.####} {2:0.####} {3:0.####}", xA, yA, xB, yB));
                        }
                        else
                        {
                            // y: control2 = endpoint
                            s.AppendPath(FormatInvariant("C {0:0.####} {1:0.####} {2:0.####} {3:0.####} {2:0.####} {3:0.####}", xA, yA, xB, yB));
                        }
                    }
                    break;
                case "h":
                    s.AppendPath("Z");
                    break;
                case "re":
                    if (operands.Count >= 4)
                    {
                        double x0 = Num(operands[0]);
                        double y0 = Num(operands[1]);
                        double width = Num(operands[2]);
                        double height = Num(operands[3]);
                        (double rx0, double ry0) = ApplyMat(s.Ctm, x0, y0);
                        (double rx1, double ry1) = ApplyMat(s.Ctm, x0 + width, y0);
                        (double rx2, double ry2) = ApplyMat(s.Ctm, x0 + width, y0 + height);
                        (double rx3, double ry3) = ApplyMat(s.Ctm, x0, y0 + height);
                        s.AppendPath(FormatInvariant(
                            "M {0:0.####} {1:0.####} L {2:0.####} {3:0.####} L {4:0.####} {5:0.####} L {6:0.####} {7:0.####} Z",
                            rx0, ry0, rx1, ry1, rx2, ry2, rx3, ry3));
                    }
                    break;

                // ── Path painting ────────────────────────────────────────
                case "S": EmitPathFromState(s, w, fill: false, stroke: true, fillRule: null); break;
                case "s": s.AppendPath("Z"); EmitPathFromState(s, w, fill: false, stroke: true, fillRule: null); break;
                case "f":
                case "F": EmitPathFromState(s, w, fill: true, stroke: false, fillRule: "nonzero"); break;
                case "f*": EmitPathFromState(s, w, fill: true, stroke: false, fillRule: "evenodd"); break;
                case "B": EmitPathFromState(s, w, fill: true, stroke: true, fillRule: "nonzero"); break;
                case "B*": EmitPathFromState(s, w, fill: true, stroke: true, fillRule: "evenodd"); break;
                case "b": s.AppendPath("Z"); EmitPathFromState(s, w, fill: true, stroke: true, fillRule: "nonzero"); break;
                case "b*": s.AppendPath("Z"); EmitPathFromState(s, w, fill: true, stroke: true, fillRule: "evenodd"); break;
                case "n":
                    // End path without painting. Clear without emit.
                    s.TakePath();
                    break;

                // ── Clipping ─────────────────────────────────────────────
                case "W":
                case "W*":
                    // Clip is applied with the NEXT path-painting operator (which then
                    // either paints + clips or, with 'n', just clips). v1 simplification:
                    // emit a clipPath using the current path, register it, and assume the
                    // next group benefits. Full spec compliance pending.
                    if (s.HasCurrentPath)
                    {
                        string clipPathData = s.CurrentPath.ToString();
                        string rule = op == "W*" ? "evenodd" : "nonzero";
                        // We can't apply mid-group easily; capture but don't emit yet.
                        // For v1, simplification: ignore clip ops to avoid breaking output.
                        _ = w.AddClipPath(clipPathData, rule);
                    }
                    break;

                // ── Text — handled in TextDispatcher (added next batch) ──
                case "BT":
                case "ET":
                case "Tf":
                case "Td":
                case "TD":
                case "Tm":
                case "T*":
                case "Tj":
                case "TJ":
                case "'":
                case "\"":
                case "Tc":
                case "Tw":
                case "Tz":
                case "TL":
                case "Tr":
                case "Ts":
                    TextDispatcher.Dispatch(op, operands, s, w, resources, _doc, _opts,
                        _fontFamilyByKey, _emittedFontFaces);
                    break;

                // ── XObjects ─────────────────────────────────────────────
                case "Do":
                    if (operands.Count > 0)
                    {
                        string name = operands[0].RawText.TrimStart('/');
                        ImageDispatcher.DrawXObject(name, s, w, resources, _doc);
                    }
                    break;

                // ── Ignored / pass-through ───────────────────────────────
                case "BMC":
                case "BDC":
                case "EMC":
                case "gs":
                case "i":
                case "CS":
                case "cs":
                case "sc":
                case "SC":
                case "scn":
                case "SCN":
                case "ri":
                case "sh":
                    // Pattern/shading/special colorspaces: noop in v1.
                    break;

                default:
                    // Unknown operator — silently skip rather than fail the whole page.
                    break;
            }
        }

        private static void EmitPathFromState(SvgGraphicsState s, SvgWriter w,
            bool fill, bool stroke, string? fillRule)
        {
            if (!s.HasCurrentPath) { return; }
            string d = s.TakePath();
            string? fillColor = fill ? s.FillColor : null;
            string? strokeColor = stroke ? s.StrokeColor : null;
            // Effective stroke width: CTM scale factor × LineWidth.
            double effLineWidth = s.LineWidth * Math.Max(
                Math.Abs(s.Ctm.A), Math.Abs(s.Ctm.D));
            string? extra = null;
            if (s.DashArray is not null) { extra = $"stroke-dasharray=\"{s.DashArray}\""; }
            w.EmitPath(d, fillColor, strokeColor, effLineWidth, fillRule, extra);
        }

        private static (double X, double Y) ApplyMat(Mat2x3 m, double x, double y)
            => (m.A * x + m.C * y + m.E, m.B * x + m.D * y + m.F);

        private static double Num(PdfToken t)
            => double.Parse(t.RawText, NumberStyles.Float, CultureInfo.InvariantCulture);

        private static string FormatInvariant(string fmt, params object[] args)
            => string.Format(CultureInfo.InvariantCulture, fmt, args);

        private static string ParseDashArray(List<PdfToken> operands)
        {
            // operands: [ ... ] phase — collect numeric tokens between ArrayStart and ArrayEnd.
            StringBuilder sb = new();
            for (int i = 0; i < operands.Count; i++)
            {
                PdfToken t = operands[i];
                if (t.Type == PdfTokenType.Integer || t.Type == PdfTokenType.Real)
                {
                    if (i == operands.Count - 1) { continue; }   // skip phase
                    if (sb.Length > 0) { sb.Append(','); }
                    sb.Append(t.RawText);
                }
            }
            return sb.ToString();
        }

        private static string CmykToRgb(double c, double m, double y, double k)
        {
            double r = (1 - c) * (1 - k);
            double g = (1 - m) * (1 - k);
            double b = (1 - y) * (1 - k);
            return $"rgb({(int)(r * 255)},{(int)(g * 255)},{(int)(b * 255)})";
        }
    }
}
