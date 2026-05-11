// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8 — Graphics; §9 — Text; §7.8 — Content streams
// PHASE: Phase 2 — Chuvadi.Pdf.Rendering
// Public API: rasterize a PDF page to a PixelBuffer.

using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.Graphics;
using GraphicsPath = Chuvadi.Pdf.Graphics.Path;
using Chuvadi.Pdf.Images;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Rendering;

/// <summary>
/// Rasterizes a PDF page to a <see cref="PixelBuffer"/>.
/// </summary>
/// <remarks>
/// <see cref="PageRasterizer"/> is the top-level public API for page rendering.
/// It wires together all layers:
/// <list type="number">
///   <item>Decodes the page's content streams through their filter chains.</item>
///   <item>Tokenizes and interprets PDF graphics operators.</item>
///   <item>Fills paths using <see cref="ScanlineRasterizer"/>.</item>
///   <item>Strokes paths using <see cref="StrokeExpander"/>.</item>
///   <item>Renders text glyphs via <see cref="FontRenderer"/>.</item>
///   <item>Composites image XObjects from the page's Resources.</item>
/// </list>
///
/// PDF operators supported: path construction (m l c v y h re),
/// path painting (f F f* S s B B* b b* n), graphics state (q Q cm w
/// J j M g G rg RG k K cs CS sc SC), text (BT ET Tf Td TD Tm T* Tj TJ ' ''),
/// XObjects (Do). Unsupported operators are silently skipped.
///
/// PDF 32000-1:2008 §8 — Graphics model.
/// </remarks>
public sealed class PageRasterizer
{
    private readonly PdfObjectStore _objects;
    private readonly RenderOptions _options;
    private readonly ScanlineRasterizer _scanline;
    private readonly StrokeExpander _stroke;
    private readonly FilterPipeline _pipeline;
    private readonly Dictionary<string, FontRenderer> _fontCache;

    /// <summary>
    /// Initialises a <see cref="PageRasterizer"/> for a document's object store.
    /// </summary>
    /// <param name="objects">The document's object store.</param>
    /// <param name="options">Rendering options. Uses <see cref="RenderOptions.Default"/> when null.</param>
    public PageRasterizer(PdfObjectStore objects, RenderOptions? options = null)
    {
        _objects  = objects ?? throw new ArgumentNullException(nameof(objects));
        _options  = options ?? RenderOptions.Default;
        _scanline = new ScanlineRasterizer();
        _stroke   = new StrokeExpander();
        _pipeline = FilterRegistry.CreateDefaultPipeline();
        _fontCache = new Dictionary<string, FontRenderer>();
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Rasterizes a PDF page to a <see cref="PixelBuffer"/>.
    /// </summary>
    /// <param name="page">The page to rasterize.</param>
    /// <returns>
    /// A <see cref="PixelBuffer"/> in BGRA format containing the rendered page.
    /// </returns>
    public PixelBuffer Rasterize(PdfPage page)
    {
        if (page is null)
        {
            throw new ArgumentNullException(nameof(page));
        }

        double pageW = page.Width;
        double pageH = page.Height;

        (int pixW, int pixH) = _options.PixelSize(pageW, pageH);
        PixelBuffer buffer = new PixelBuffer(pixW, pixH);
        buffer.Clear(_options.Background);

        byte[] contentBytes = LoadContentBytes(page);

        if (contentBytes.Length == 0)
        {
            return buffer;
        }

        double scale = _options.Scale;

        // Build the page-to-device transform:
        // PDF origin is bottom-left, device origin is top-left.
        // x' = x * scale
        // y' = (pageH - y) * scale
        RenderState state = new RenderState(pageH, scale, pixH);
        PdfDictionary? resources = page.Resources;

        InterpretContentStream(contentBytes, state, buffer, resources);

        return buffer;
    }

    /// <summary>
    /// Rasterizes a page and encodes the result as PNG bytes.
    /// </summary>
    public byte[] RasterizeToPng(PdfPage page)
    {
        if (page is null)
        {
            throw new ArgumentNullException(nameof(page));
        }

        PixelBuffer buffer = Rasterize(page);
        ImageFrame frame = new ImageFrame(buffer, ImageColorFormat.Rgb24);

        using (MemoryStream ms = new MemoryStream())
        {
            PngEncoder.Encode(frame, ms);
            return ms.ToArray();
        }
    }

    // ── Content stream loading ────────────────────────────────────────────

    private byte[] LoadContentBytes(PdfPage page)
    {
        PdfPrimitive? contents = page.Contents;

        if (contents is null || contents is PdfNull)
        {
            return [];
        }

        PdfPrimitive resolved = _objects.Resolve(contents);

        if (resolved is PdfStream single)
        {
            return DecodeStream(single);
        }

        if (resolved is PdfArray array)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                for (int i = 0; i < array.Count; i++)
                {
                    PdfPrimitive item = _objects.Resolve(array[i]);

                    if (item is PdfStream s)
                    {
                        byte[] decoded = DecodeStream(s);
                        ms.Write(decoded, 0, decoded.Length);

                        if (i < array.Count - 1)
                        {
                            ms.WriteByte(32);
                        }
                    }
                }

                return ms.ToArray();
            }
        }

        return [];
    }

    private byte[] DecodeStream(PdfStream stream)
    {
        if (!stream.IsFiltered)
        {
            return stream.RawBytes;
        }

        PdfPrimitive? filter = stream.Filter;

        if (filter is PdfName filterName)
        {
            string resolved = FilterRegistry.ResolveAlias(filterName.Value);
            return _pipeline.Decode(resolved, stream.RawBytes, null);
        }

        if (filter is PdfArray filterArray)
        {
            byte[] data = stream.RawBytes;

            for (int i = 0; i < filterArray.Count; i++)
            {
                PdfName? fn = filterArray.GetAs<PdfName>(i);

                if (fn is null)
                {
                    continue;
                }

                string resolved = FilterRegistry.ResolveAlias(fn.Value);
                data = _pipeline.Decode(resolved, data, null);
            }

            return data;
        }

        return stream.RawBytes;
    }

    // ── Content stream interpreter ────────────────────────────────────────

    private void InterpretContentStream(
        byte[] content,
        RenderState state,
        PixelBuffer buffer,
        PdfDictionary? resources)
    {
        using (MemoryStream ms = new MemoryStream(content))
        using (PdfTokenizer tokenizer = new PdfTokenizer(ms))
        {
            List<PdfToken> operands = new List<PdfToken>();

            while (true)
            {
                PdfToken token = tokenizer.Read();

                if (token.IsEndOfStream)
                {
                    break;
                }

                if (token.Type == PdfTokenType.ArrayStart)
                {
                    // Collect array inline
                    List<PdfToken> arrTokens = new List<PdfToken>();

                    while (true)
                    {
                        PdfToken inner = tokenizer.Read();

                        if (inner.IsEndOfStream || inner.Type == PdfTokenType.ArrayEnd)
                        {
                            break;
                        }

                        arrTokens.Add(inner);
                    }

                    operands.Add(new PdfToken(PdfTokenType.ArrayStart, [], 0));
                    operands.AddRange(arrTokens);
                    operands.Add(new PdfToken(PdfTokenType.ArrayEnd, [], 0));
                    continue;
                }

                if (token.Type != PdfTokenType.Keyword)
                {
                    operands.Add(token);
                    continue;
                }

                string op = token.RawText;
                ExecuteOperator(op, operands, state, buffer, resources);
                operands.Clear();
            }
        }
    }

    private void ExecuteOperator(
        string op,
        List<PdfToken> operands,
        RenderState state,
        PixelBuffer buffer,
        PdfDictionary? resources)
    {
        switch (op)
        {
            // ── Graphics state ─────────────────────────────────────────────
            case "q": state.Push(); break;
            case "Q": state.Pop(); break;
            case "cm": ApplyCm(operands, state); break;
            case "w":  if (operands.Count > 0) { state.LineWidth = ParseDouble(operands[0]); } break;
            case "J":  if (operands.Count > 0) { state.LineCap   = (LineCap)ParseInt(operands[0]); } break;
            case "j":  if (operands.Count > 0) { state.LineJoin  = (LineJoin)ParseInt(operands[0]); } break;
            case "M":  if (operands.Count > 0) { state.MiterLimit = ParseDouble(operands[0]); } break;

            // ── Colour operators ───────────────────────────────────────────
            case "g":  state.FillColor   = ColorF.FromGray((float)ParseDouble(operands, 0)); break;
            case "G":  state.StrokeColor = ColorF.FromGray((float)ParseDouble(operands, 0)); break;
            case "rg": state.FillColor   = ColorF.FromRgb(
                                               (float)ParseDouble(operands, 0),
                                               (float)ParseDouble(operands, 1),
                                               (float)ParseDouble(operands, 2)); break;
            case "RG": state.StrokeColor = ColorF.FromRgb(
                                               (float)ParseDouble(operands, 0),
                                               (float)ParseDouble(operands, 1),
                                               (float)ParseDouble(operands, 2)); break;
            case "k":  state.FillColor   = ColorF.FromCmyk(
                                               (float)ParseDouble(operands, 0),
                                               (float)ParseDouble(operands, 1),
                                               (float)ParseDouble(operands, 2),
                                               (float)ParseDouble(operands, 3)); break;
            case "K":  state.StrokeColor = ColorF.FromCmyk(
                                               (float)ParseDouble(operands, 0),
                                               (float)ParseDouble(operands, 1),
                                               (float)ParseDouble(operands, 2),
                                               (float)ParseDouble(operands, 3)); break;
            case "sc":
            case "scn": ApplyScColor(operands, state, stroke: false); break;
            case "SC":
            case "SCN": ApplyScColor(operands, state, stroke: true); break;

            // ── GraphicsPath construction ──────────────────────────────────────────
            case "m":  if (operands.Count >= 2) { state.CurrentPath.MoveTo(ParseDouble(operands[0]), ParseDouble(operands[1])); } break;
            case "l":  if (operands.Count >= 2) { state.CurrentPath.LineTo(ParseDouble(operands[0]), ParseDouble(operands[1])); } break;
            case "c":  if (operands.Count >= 6) { state.CurrentPath.CubicBezierTo(ParsePoint(operands, 0), ParsePoint(operands, 2), ParsePoint(operands, 4)); } break;
            case "v":  if (operands.Count >= 4) { ApplyV(operands, state); } break;
            case "y":  if (operands.Count >= 4) { ApplyY(operands, state); } break;
            case "h":  state.CurrentPath.ClosePath(); break;
            case "re": if (operands.Count >= 4) { ApplyRe(operands, state); } break;

            // ── GraphicsPath painting ──────────────────────────────────────────────
            case "f":
            case "F":  PaintFill(state, buffer, FillRule.NonZeroWinding); state.ClearPath(); break;
            case "f*": PaintFill(state, buffer, FillRule.EvenOdd);        state.ClearPath(); break;
            case "S":  PaintStroke(state, buffer); state.ClearPath(); break;
            case "s":  state.CurrentPath.ClosePath(); PaintStroke(state, buffer); state.ClearPath(); break;
            case "B":  PaintFill(state, buffer, FillRule.NonZeroWinding); PaintStroke(state, buffer); state.ClearPath(); break;
            case "B*": PaintFill(state, buffer, FillRule.EvenOdd);        PaintStroke(state, buffer); state.ClearPath(); break;
            case "b":  state.CurrentPath.ClosePath(); PaintFill(state, buffer, FillRule.NonZeroWinding); PaintStroke(state, buffer); state.ClearPath(); break;
            case "b*": state.CurrentPath.ClosePath(); PaintFill(state, buffer, FillRule.EvenOdd);        PaintStroke(state, buffer); state.ClearPath(); break;
            case "n":  state.ClearPath(); break;
            case "W":
            case "W*": state.ClearPath(); break; // Clipping not implemented in Phase 2

            // ── Text ───────────────────────────────────────────────────────
            case "BT": state.InText = true; state.ResetTextState(); break;
            case "ET": state.InText = false; break;
            case "Tf": ApplyTf(operands, state, resources); break;
            case "Td": ApplyTd(operands, state); break;
            case "TD": ApplyTD(operands, state); break;
            case "Tm": ApplyTm(operands, state); break;
            case "T*": state.MoveToNextLine(); break;
            case "Tj": if (operands.Count > 0) { PaintText(ExtractString(operands[0]), state, buffer); } break;
            case "TJ": PaintTJ(operands, state, buffer); break;
            case "'":  state.MoveToNextLine(); if (operands.Count > 0) { PaintText(ExtractString(operands[0]), state, buffer); } break;
            case "\"": if (operands.Count >= 3) { ApplyWordCharSpacing(operands, state); state.MoveToNextLine(); PaintText(ExtractString(operands[2]), state, buffer); } break;

            // ── XObjects ───────────────────────────────────────────────────
            case "Do": if (operands.Count > 0) { PaintXObject(ExtractName(operands[0]), state, buffer, resources); } break;
        }
    }

    // ── Graphics state helpers ────────────────────────────────────────────

    private static void ApplyCm(List<PdfToken> operands, RenderState state)
    {
        if (operands.Count < 6)
        {
            return;
        }

        Transform ctm = new Transform(
            ParseDouble(operands[0]), ParseDouble(operands[1]),
            ParseDouble(operands[2]), ParseDouble(operands[3]),
            ParseDouble(operands[4]), ParseDouble(operands[5]));

        state.Ctm = ctm.Multiply(state.Ctm);
    }

    private static void ApplyScColor(List<PdfToken> operands, RenderState state, bool stroke)
    {
        if (operands.Count == 1)
        {
            float g = (float)ParseDouble(operands[0]);

            if (stroke)
            {
                state.StrokeColor = ColorF.FromGray(g);
            }
            else
            {
                state.FillColor = ColorF.FromGray(g);
            }
        }
        else if (operands.Count >= 3)
        {
            float r = (float)ParseDouble(operands[0]);
            float g = (float)ParseDouble(operands[1]);
            float bv = (float)ParseDouble(operands[2]);

            if (stroke)
            {
                state.StrokeColor = ColorF.FromRgb(r, g, bv);
            }
            else
            {
                state.FillColor = ColorF.FromRgb(r, g, bv);
            }
        }
    }

    // ── GraphicsPath construction helpers ─────────────────────────────────────────

    private static void ApplyV(List<PdfToken> operands, RenderState state)
    {
        // v: cubic bezier, first control point = current point
        PointF cp = state.CurrentPath.Count > 0
            ? new PointF(ParseDouble(operands[0]), ParseDouble(operands[1]))
            : PointF.Zero;
        state.CurrentPath.CubicBezierTo(
            cp,
            ParsePoint(operands, 0),
            ParsePoint(operands, 2));
    }

    private static void ApplyY(List<PdfToken> operands, RenderState state)
    {
        // y: cubic bezier, second control point = endpoint
        PointF ep = ParsePoint(operands, 2);
        state.CurrentPath.CubicBezierTo(
            ParsePoint(operands, 0),
            ep,
            ep);
    }

    private static void ApplyRe(List<PdfToken> operands, RenderState state)
    {
        double x = ParseDouble(operands[0]);
        double y = ParseDouble(operands[1]);
        double w = ParseDouble(operands[2]);
        double h = ParseDouble(operands[3]);
        state.CurrentPath.Rectangle(x, y, w, h);
    }

    // ── GraphicsPath painting ─────────────────────────────────────────────────────

    private void PaintFill(RenderState state, PixelBuffer buffer, FillRule rule)
    {
        GraphicsPath transformed = TransformPath(state.CurrentPath, state);
        PathFlattener flattener = new PathFlattener(_options.FlatnessTolerance);
        List<List<PointF>> subPaths = flattener.Flatten(transformed);
        _scanline.Fill(buffer, subPaths, state.FillColor, rule);
    }

    private void PaintStroke(RenderState state, PixelBuffer buffer)
    {
        GraphicsPath transformed = TransformPath(state.CurrentPath, state);
        PathFlattener flattener = new PathFlattener(_options.FlatnessTolerance);
        List<List<PointF>> subPaths = flattener.Flatten(transformed);

        StrokeStyle style = new StrokeStyle
        {
            Width     = state.LineWidth * _options.Scale,
            Cap       = state.LineCap,
            Join      = state.LineJoin,
            MiterLimit = state.MiterLimit,
            Color     = state.StrokeColor,
        };

        List<List<PointF>> filled = _stroke.Expand(subPaths, style);
        _scanline.Fill(buffer, filled, state.StrokeColor, FillRule.NonZeroWinding);
    }

    private GraphicsPath TransformPath(GraphicsPath source, RenderState state)
    {
        // Apply CTM + page-to-device transform
        GraphicsPath result = new GraphicsPath();
        double scale = _options.Scale;
        double pageH = state.PageHeight;

        foreach (PathSegment seg in source.Segments)
        {
            switch (seg.Kind)
            {
                case PathSegmentKind.MoveTo:
                    result.MoveTo(ToDevice(seg.P0, state.Ctm, scale, pageH));
                    break;
                case PathSegmentKind.LineTo:
                    result.LineTo(ToDevice(seg.P0, state.Ctm, scale, pageH));
                    break;
                case PathSegmentKind.CubicBezierTo:
                    result.CubicBezierTo(
                        ToDevice(seg.P0, state.Ctm, scale, pageH),
                        ToDevice(seg.P1, state.Ctm, scale, pageH),
                        ToDevice(seg.P2, state.Ctm, scale, pageH));
                    break;
                case PathSegmentKind.ClosePath:
                    result.ClosePath();
                    break;
            }
        }

        return result;
    }

    private static PointF ToDevice(PointF p, Transform ctm, double scale, double pageH)
    {
        PointF transformed = ctm.TransformPoint(p);
        return new PointF(
            transformed.X * scale,
            (pageH - transformed.Y) * scale);
    }

    // ── Text painting ──────────────────────────────────────────────────────

    private static void ApplyTf(
        List<PdfToken> operands, RenderState state, PdfDictionary? resources)
    {
        if (operands.Count < 2)
        {
            return;
        }

        state.FontName = ExtractName(operands[0]);
        state.FontSize = ParseDouble(operands[1]);
        state.FontResourceKey = resources;
    }

    private static void ApplyTd(List<PdfToken> operands, RenderState state)
    {
        if (operands.Count < 2)
        {
            return;
        }

        state.TextLineX += ParseDouble(operands[0]);
        state.TextLineY += ParseDouble(operands[1]);
        state.TextX = state.TextLineX;
        state.TextY = state.TextLineY;
    }

    private static void ApplyTD(List<PdfToken> operands, RenderState state)
    {
        if (operands.Count < 2)
        {
            return;
        }

        double tx = ParseDouble(operands[0]);
        double ty = ParseDouble(operands[1]);
        state.Leading = -ty;
        state.TextLineX += tx;
        state.TextLineY += ty;
        state.TextX = state.TextLineX;
        state.TextY = state.TextLineY;
    }

    private static void ApplyTm(List<PdfToken> operands, RenderState state)
    {
        if (operands.Count < 6)
        {
            return;
        }

        state.TextMatrix = new Transform(
            ParseDouble(operands[0]), ParseDouble(operands[1]),
            ParseDouble(operands[2]), ParseDouble(operands[3]),
            ParseDouble(operands[4]), ParseDouble(operands[5]));
        state.TextX = ParseDouble(operands[4]);
        state.TextY = ParseDouble(operands[5]);
        state.TextLineX = state.TextX;
        state.TextLineY = state.TextY;
    }

    private static void ApplyWordCharSpacing(List<PdfToken> operands, RenderState state)
    {
        if (operands.Count >= 2)
        {
            state.WordSpacing = ParseDouble(operands[0]);
            state.CharSpacing = ParseDouble(operands[1]);
        }
    }

    private void PaintText(string text, RenderState state, PixelBuffer buffer)
    {
        if (string.IsNullOrEmpty(text) || state.FontSize <= 0)
        {
            return;
        }

        FontRenderer? renderer = GetFontRenderer(state);

        if (renderer is null)
        {
            // No font available — skip rendering but still advance
            double approxAdvance = state.FontSize * 0.6 * text.Length;
            state.TextX += approxAdvance;
            return;
        }

        double scale = _options.Scale;
        double pageH = state.PageHeight;

        foreach (char c in text)
        {
            GlyphOutline scaled = renderer.GetGlyphOutlineForChar(c).Scale(state.FontSize);

            if (!scaled.IsEmpty)
            {
                // Place glyph at current text position, flipped to device space
                double gx = state.TextX * scale;
                double gy = (pageH - state.TextY) * scale;

                PaintGlyph(scaled.Outline, gx, gy, state.FillColor, buffer);
            }

            double advance = scaled.Metrics.AdvanceWidthAt(state.FontSize);
            state.TextX += advance + state.CharSpacing;

            if (c == ' ')
            {
                state.TextX += state.WordSpacing;
            }
        }
    }

    private void PaintGlyph(GraphicsPath glyphPath, double originX, double originY,
        ColorF color, PixelBuffer buffer)
    {
        // Translate glyph path to device origin, flip Y (glyph Y increases up, device Y down)
        GraphicsPath devicePath = new GraphicsPath();

        foreach (PathSegment seg in glyphPath.Segments)
        {
            switch (seg.Kind)
            {
                case PathSegmentKind.MoveTo:
                    devicePath.MoveTo(originX + seg.P0.X, originY - seg.P0.Y);
                    break;
                case PathSegmentKind.LineTo:
                    devicePath.LineTo(originX + seg.P0.X, originY - seg.P0.Y);
                    break;
                case PathSegmentKind.CubicBezierTo:
                    devicePath.CubicBezierTo(
                        new PointF(originX + seg.P0.X, originY - seg.P0.Y),
                        new PointF(originX + seg.P1.X, originY - seg.P1.Y),
                        new PointF(originX + seg.P2.X, originY - seg.P2.Y));
                    break;
                case PathSegmentKind.ClosePath:
                    devicePath.ClosePath();
                    break;
            }
        }

        PathFlattener flattener = new PathFlattener(_options.FlatnessTolerance);
        List<List<PointF>> subPaths = flattener.Flatten(devicePath);
        _scanline.Fill(buffer, subPaths, color, FillRule.NonZeroWinding);
    }

    private void PaintTJ(List<PdfToken> operands, RenderState state, PixelBuffer buffer)
    {
        // TJ: array of strings and numbers
        // numbers are glyph displacements in thousandths of a text unit
        bool inArray = false;

        foreach (PdfToken token in operands)
        {
            if (token.Type == PdfTokenType.ArrayStart)
            {
                inArray = true;
                continue;
            }

            if (token.Type == PdfTokenType.ArrayEnd)
            {
                inArray = false;
                continue;
            }

            if (!inArray)
            {
                continue;
            }

            if (token.Type == PdfTokenType.LiteralString || token.Type == PdfTokenType.HexString)
            {
                PaintText(ExtractString(token), state, buffer);
            }
            else if (token.Type == PdfTokenType.Integer || token.Type == PdfTokenType.Real)
            {
                // Negative = advance forward; positive = move back
                double displacement = ParseDouble(token) / -1000.0;
                state.TextX += displacement * state.FontSize;
            }
        }
    }

    // ── XObject painting ───────────────────────────────────────────────────

    private void PaintXObject(
        string name, RenderState state, PixelBuffer buffer, PdfDictionary? resources)
    {
        if (resources is null || string.IsNullOrEmpty(name))
        {
            return;
        }

        if (!resources.TryGetValue(PdfName.Intern("XObject"), out PdfPrimitive? xobjDict))
        {
            return;
        }

        PdfDictionary? xObjects = _objects.ResolveAs<PdfDictionary>(xobjDict ?? PdfNull.Value);

        if (xObjects is null)
        {
            return;
        }

        if (!xObjects.TryGetValue(PdfName.Intern(name), out PdfPrimitive? xobjRef))
        {
            return;
        }

        PdfStream? xobjStream = _objects.ResolveAs<PdfStream>(xobjRef ?? PdfNull.Value);

        if (xobjStream is null)
        {
            return;
        }

        if (!xobjStream.Dictionary.TryGetValue(PdfName.Intern("Subtype"), out PdfPrimitive? subtypePrim))
        {
            return;
        }

        if (subtypePrim is not PdfName subtype)
        {
            return;
        }

        if (subtype.Value == "Image")
        {
            PaintImageXObject(xobjStream, state, buffer);
        }
    }

    private void PaintImageXObject(PdfStream xobjStream, RenderState state, PixelBuffer buffer)
    {
        byte[] imageBytes = DecodeStream(xobjStream);

        ImageFrame? frame = null;

        try
        {
            // Try JPEG first, then PNG
            if (imageBytes.Length > 2 &&
                imageBytes[0] == 0xFF && imageBytes[1] == 0xD8)
            {
                frame = JpegDecoder.Decode(imageBytes);
            }
            else if (imageBytes.Length > 8 &&
                     imageBytes[0] == 137 && imageBytes[1] == 80)
            {
                frame = PngDecoder.Decode(imageBytes);
            }
        }
        catch (ImageException)
        {
            return; // Skip undecodeable image XObjects
        }

        if (frame is null)
        {
            return;
        }

        // Get image dimensions from the XObject dictionary
        if (!xobjStream.Dictionary.TryGetValue(PdfName.Intern("Width"), out PdfPrimitive? wPrim) ||
            !xobjStream.Dictionary.TryGetValue(PdfName.Intern("Height"), out PdfPrimitive? hPrim))
        {
            return;
        }

        if (wPrim is not PdfInteger imgW || hPrim is not PdfInteger imgH)
        {
            return;
        }

        // Place image using CTM (maps 1×1 unit square to image position/size)
        double scale = _options.Scale;
        double pageH = state.PageHeight;

        double destX = state.Ctm.E * scale;
        double destY = (pageH - state.Ctm.F) * scale;
        double destW = state.Ctm.A * scale;
        double destH = Math.Abs(state.Ctm.D) * scale;

        if (destW <= 0 || destH <= 0)
        {
            return;
        }

        CompositeImage(frame, buffer, destX, destY - destH, destW, destH);
    }

    private static void CompositeImage(
        ImageFrame frame, PixelBuffer buffer,
        double x, double y, double w, double h)
    {
        int dstX0 = Math.Max(0, (int)Math.Round(x));
        int dstY0 = Math.Max(0, (int)Math.Round(y));
        int dstX1 = Math.Min(buffer.Width  - 1, (int)Math.Round(x + w));
        int dstY1 = Math.Min(buffer.Height - 1, (int)Math.Round(y + h));

        for (int py = dstY0; py <= dstY1; py++)
        {
            for (int px = dstX0; px <= dstX1; px++)
            {
                double srcFracX = (px - x) / w;
                double srcFracY = (py - y) / h;
                int srcX = (int)(srcFracX * frame.Width);
                int srcY = (int)(srcFracY * frame.Height);
                srcX = Math.Max(0, Math.Min(frame.Width  - 1, srcX));
                srcY = Math.Max(0, Math.Min(frame.Height - 1, srcY));

                (byte sb, byte sg, byte sr, byte sa) = frame.Pixels.GetPixelBgra(srcX, srcY);
                buffer.SetPixelBgra(px, py, sb, sg, sr, sa);
            }
        }
    }

    // ── Font loading ───────────────────────────────────────────────────────

    private FontRenderer? GetFontRenderer(RenderState state)
    {
        if (string.IsNullOrEmpty(state.FontName))
        {
            return null;
        }

        if (_fontCache.TryGetValue(state.FontName, out FontRenderer? cached))
        {
            return cached;
        }

        // Try to load font from page resources
        PdfDictionary? resources = state.FontResourceKey;

        if (resources is null)
        {
            return null;
        }

        if (!resources.TryGetValue(PdfName.Intern("Font"), out PdfPrimitive? fontDict))
        {
            return null;
        }

        PdfDictionary? fonts = _objects.ResolveAs<PdfDictionary>(fontDict ?? PdfNull.Value);

        if (fonts is null)
        {
            return null;
        }

        if (!fonts.TryGetValue(PdfName.Intern(state.FontName), out PdfPrimitive? fontRef))
        {
            return null;
        }

        // Try to get embedded font stream
        byte[]? fontBytes = ExtractFontBytes(fontRef ?? PdfNull.Value);

        if (fontBytes is null)
        {
            return null;
        }

        try
        {
            FontRenderer renderer = new FontRenderer(fontBytes);
            _fontCache[state.FontName] = renderer;
            return renderer;
        }
        catch (Fonts.Rendering.FontRenderingException)
        {
            return null;
        }
    }

    private byte[]? ExtractFontBytes(PdfPrimitive fontRef)
    {
        PdfDictionary? fontDict = _objects.ResolveAs<PdfDictionary>(fontRef);

        if (fontDict is null)
        {
            return null;
        }

        // Look for FontDescriptor → FontFile/FontFile2/FontFile3
        if (!fontDict.TryGetValue(PdfName.Intern("FontDescriptor"), out PdfPrimitive? fdRef))
        {
            return null;
        }

        PdfDictionary? fd = _objects.ResolveAs<PdfDictionary>(fdRef ?? PdfNull.Value);

        if (fd is null)
        {
            return null;
        }

        string[] fontFileKeys = ["FontFile2", "FontFile", "FontFile3"];

        foreach (string key in fontFileKeys)
        {
            if (!fd.TryGetValue(PdfName.Intern(key), out PdfPrimitive? ffRef))
            {
                continue;
            }

            PdfStream? fontStream = _objects.ResolveAs<PdfStream>(ffRef ?? PdfNull.Value);

            if (fontStream is not null)
            {
                return DecodeStream(fontStream);
            }
        }

        return null;
    }

    // ── Token parsing helpers ──────────────────────────────────────────────

    private static double ParseDouble(PdfToken token)
    {
        if (double.TryParse(token.RawText,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out double v))
        {
            return v;
        }

        return 0;
    }

    private static double ParseDouble(List<PdfToken> tokens, int index)
    {
        return index < tokens.Count ? ParseDouble(tokens[index]) : 0;
    }

    private static int ParseInt(PdfToken token)
    {
        return (int)ParseDouble(token);
    }

    private static PointF ParsePoint(List<PdfToken> tokens, int startIndex)
    {
        return new PointF(ParseDouble(tokens, startIndex), ParseDouble(tokens, startIndex + 1));
    }

    private static string ExtractString(PdfToken token)
    {
        if (token.Type == PdfTokenType.LiteralString)
        {
            // Strip outer parentheses
            string raw = token.RawText;

            if (raw.Length >= 2 && raw[0] == '(' && raw[raw.Length - 1] == ')')
            {
                return raw.Substring(1, raw.Length - 2);
            }

            return raw;
        }

        if (token.Type == PdfTokenType.HexString)
        {
            string raw = token.RawText;

            if (raw.Length >= 2 && raw[0] == '<' && raw[raw.Length - 1] == '>')
            {
                string hex = raw.Substring(1, raw.Length - 2);
                System.Text.StringBuilder sb = new System.Text.StringBuilder(hex.Length / 2);

                for (int i = 0; i + 1 < hex.Length; i += 2)
                {
                    if (byte.TryParse(hex.Substring(i, 2),
                        System.Globalization.NumberStyles.HexNumber, null, out byte b))
                    {
                        sb.Append((char)b);
                    }
                }

                return sb.ToString();
            }
        }

        return string.Empty;
    }

    private static string ExtractName(PdfToken token)
    {
        string raw = token.RawText;

        if (raw.StartsWith("/", StringComparison.Ordinal))
        {
            return raw.Substring(1);
        }

        return raw;
    }
}

// ── Render state ──────────────────────────────────────────────────────────

/// <summary>
/// Internal mutable graphics state for the rendering pipeline.
/// </summary>
internal sealed class RenderState
{
    private readonly Stack<RenderStateSnapshot> _stack;

    internal RenderState(double pageHeight, double scale, int pixelHeight)
    {
        PageHeight  = pageHeight;
        Scale       = scale;
        PixelHeight = pixelHeight;
        _stack = new Stack<RenderStateSnapshot>();
        CurrentPath = new GraphicsPath();
        Ctm = Transform.Identity;
        TextMatrix = Transform.Identity;
        FillColor   = ColorF.Black;
        StrokeColor = ColorF.Black;
        LineWidth   = 1.0;
        LineCap     = LineCap.Butt;
        LineJoin    = LineJoin.Miter;
        MiterLimit  = 10.0;
        FontSize    = 12.0;
        FontName    = string.Empty;
        Leading     = 0;
        CharSpacing = 0;
        WordSpacing = 0;
    }

    internal double PageHeight  { get; }
    internal double Scale       { get; }
    internal int    PixelHeight { get; }

    // Graphics state
    internal Transform Ctm        { get; set; }
    internal ColorF FillColor     { get; set; }
    internal ColorF StrokeColor   { get; set; }
    internal double LineWidth     { get; set; }
    internal LineCap LineCap      { get; set; }
    internal LineJoin LineJoin    { get; set; }
    internal double MiterLimit    { get; set; }

    // Current path
    internal GraphicsPath CurrentPath { get; private set; }

    // Text state
    internal bool InText           { get; set; }
    internal Transform TextMatrix  { get; set; }
    internal double TextX          { get; set; }
    internal double TextY          { get; set; }
    internal double TextLineX      { get; set; }
    internal double TextLineY      { get; set; }
    internal double FontSize       { get; set; }
    internal string FontName       { get; set; }
    internal double Leading        { get; set; }
    internal double CharSpacing    { get; set; }
    internal double WordSpacing    { get; set; }
    internal PdfDictionary? FontResourceKey { get; set; }

    internal void Push()
    {
        _stack.Push(new RenderStateSnapshot(Ctm, FillColor, StrokeColor,
            LineWidth, LineCap, LineJoin, MiterLimit));
    }

    internal void Pop()
    {
        if (_stack.Count == 0)
        {
            return;
        }

        RenderStateSnapshot snap = _stack.Pop();
        Ctm         = snap.Ctm;
        FillColor   = snap.FillColor;
        StrokeColor = snap.StrokeColor;
        LineWidth   = snap.LineWidth;
        LineCap     = snap.LineCap;
        LineJoin    = snap.LineJoin;
        MiterLimit  = snap.MiterLimit;
    }

    internal void ClearPath()
    {
        CurrentPath = new GraphicsPath();
    }

    internal void ResetTextState()
    {
        TextMatrix  = Transform.Identity;
        TextX       = 0;
        TextY       = 0;
        TextLineX   = 0;
        TextLineY   = 0;
        CharSpacing = 0;
        WordSpacing = 0;
    }

    internal void MoveToNextLine()
    {
        TextLineY -= Leading;
        TextX = TextLineX;
        TextY = TextLineY;
    }
}

internal readonly struct RenderStateSnapshot
{
    internal RenderStateSnapshot(
        Transform ctm, ColorF fill, ColorF stroke,
        double lineWidth, LineCap cap, LineJoin join, double miter)
    {
        Ctm         = ctm;
        FillColor   = fill;
        StrokeColor = stroke;
        LineWidth   = lineWidth;
        LineCap     = cap;
        LineJoin    = join;
        MiterLimit  = miter;
    }

    internal Transform Ctm        { get; }
    internal ColorF FillColor     { get; }
    internal ColorF StrokeColor   { get; }
    internal double LineWidth     { get; }
    internal LineCap LineCap      { get; }
    internal LineJoin LineJoin    { get; }
    internal double MiterLimit    { get; }
}
