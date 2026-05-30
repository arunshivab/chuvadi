// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8 (Graphics), §9 (Text), §9.4.3 (Text-showing operators)
// PHASE: Phase 2.1 — display-list intermediate
//        v2.1.2 — text-run word boundary correctness

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Fonts;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// Builds a <see cref="PageDisplayList"/> by walking a page's content stream
/// and translating each PDF operator to a <see cref="RenderOp"/>.
/// </summary>
public static class DisplayListBuilder
{
    /// <summary>Builds a display list for the given page.</summary>
    public static PageDisplayList Build(PdfDocument document, int pageIndex)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (pageIndex < 0 || pageIndex >= document.PageCount)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex));
        }
        PdfPage page = document.Pages[pageIndex];
        return new Builder(document).BuildPage(page);
    }

    private sealed class Builder
    {
        private readonly PdfDocument _doc;
        private readonly List<RenderOp> _ops = new();
        private readonly Dictionary<string, FontWidths> _widthsByKey = new();
        private readonly Dictionary<string, bool> _compositeByKey = new();
        // v2.1.2: collected for downstream renderers that embed font programs
        // (e.g. SvgRenderer emits CSS @font-face data URLs from these). Keys
        // match the resource-name used in TextOp.FontKey.
        private readonly Dictionary<string, PdfDictionary> _fontDictsByKey = new();

        // v2.1.8: graceful-degradation events accumulated during build,
        // surfaced on PageDisplayList.Diagnostics. Deduplicated by
        // (kind, message) so a single condition that fires per-character
        // (e.g. font resolution failure on every glyph of a 31-char string)
        // emits one diagnostic, not 31.
        private readonly List<RenderingDiagnostic> _diagnostics = new();
        private readonly HashSet<(DiagnosticKind, string)> _diagnosticKeys = new();

        private PdfDictionary? _resources;

        // ── v2.1.2: gap-tracking for word-boundary space insertion ───────────
        //
        // After each text emit on the same line, we record the text-matrix
        // X position that EmitText left things at (post-advance) and whether
        // the run ended with a space character. Before the next emit, we
        // compare the recorded X position to the current text-matrix X
        // position. A gap larger than a fraction of the space-width tells
        // us the PDF intends a word break, and we insert a synthetic space
        // glyph at the start of the next run so the extracted text contains
        // the space character.
        //
        // Guards:
        //   - skip if no previous run on this line
        //   - skip if the previous run ENDED with a space — otherwise we'd
        //     produce double spaces (the "Current  Job" symptom)
        //   - skip if the next run STARTS with a space — same reason
        //
        // The line break operators (Td, TD, Tm, T*, ', ") and BT reset
        // this tracking so we never insert a space across a line boundary.
        private bool _hasPrevRunOnLine;
        private double _prevRunEndX;   // post-emit text-matrix E (X translation)
        private double _prevRunEndY;   // post-emit text-matrix F — sanity check
        private bool _prevRunEndedWithSpace;
        // Gap threshold: a gap exceeding 30% of a space-width is treated as a
        // word boundary. PDFBox uses similar values, determined by trial and
        // error against real PDFs. PDF.js uses 0.1–0.25 depending on context.
        // 0.3 is conservative — we insert fewer spurious spaces and miss a
        // few legitimate breaks. Better than the opposite.
        private const double GapToleranceFraction = 0.3;

        internal Builder(PdfDocument doc) { _doc = doc; }

        internal PageDisplayList BuildPage(PdfPage page)
        {
            _resources = page.Resources;
            byte[] content = LoadContent(page);
            BuilderStateStack stack = new();
            Dispatch(content, stack);
            int rotation = 0;
            if (page.Dictionary.TryGetValue(PdfName.Intern("Rotate"), out PdfPrimitive? rv)
                && rv is PdfInteger ri) { rotation = ri.Value; }
            return new PageDisplayList(_ops, page.Width, page.Height, rotation, _fontDictsByKey, _diagnostics);
        }

        private byte[] LoadContent(PdfPage page)
        {
            if (!page.Dictionary.TryGetValue(PdfName.Intern("Contents"), out PdfPrimitive? cv))
            {
                return Array.Empty<byte>();
            }
            PdfPrimitive resolved = _doc.Objects.Resolve(cv);
            using MemoryStream merged = new();
            if (resolved is PdfStream s)
            {
                byte[] data = StreamDecodeHelper.Decode(s);
                merged.Write(data, 0, data.Length);
            }
            else if (resolved is PdfArray arr)
            {
                foreach (PdfPrimitive entry in arr)
                {
                    if (_doc.Objects.Resolve(entry) is PdfStream st)
                    {
                        byte[] data = StreamDecodeHelper.Decode(st);
                        merged.Write(data, 0, data.Length);
                        merged.WriteByte((byte)' ');
                    }
                }
            }
            return merged.ToArray();
        }

        private void Dispatch(byte[] content, BuilderStateStack stack)
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

                Execute(token.RawText, operands, stack);
                operands.Clear();
            }
        }

        private void Execute(string op, List<PdfToken> operands, BuilderStateStack stack)
        {
            BuilderState s = stack.Current;
            switch (op)
            {
                // ── State ─────────────────────────────────────────────────────
                case "q":
                    stack.Push();
                    _ops.Add(new TransformOp { Push = true, Ctm = s.Ctm });
                    break;
                case "Q":
                    stack.Pop();
                    _ops.Add(new TransformOp { Push = false, Ctm = stack.Current.Ctm });
                    break;
                case "cm":
                    if (operands.Count >= 6)
                    {
                        AffineMatrix m = new(
                            Num(operands[0]), Num(operands[1]),
                            Num(operands[2]), Num(operands[3]),
                            Num(operands[4]), Num(operands[5]));
                        s.Ctm = m.Multiply(s.Ctm);
                    }
                    break;

                // ── Stroke params ────────────────────────────────────────────
                case "w": if (operands.Count > 0) { s.LineWidth = Num(operands[0]); } break;
                case "J": if (operands.Count > 0) { s.LineCap = (LineCap)(int)Num(operands[0]); } break;
                case "j": if (operands.Count > 0) { s.LineJoin = (LineJoin)(int)Num(operands[0]); } break;
                case "M": if (operands.Count > 0) { s.MiterLimit = Num(operands[0]); } break;
                case "d": ParseDashArray(operands, s); break;

                // ── Color ────────────────────────────────────────────────────
                case "g": if (operands.Count > 0) { s.FillColor = PdfColor.Gray(Num(operands[0])); } break;
                case "G": if (operands.Count > 0) { s.StrokeColor = PdfColor.Gray(Num(operands[0])); } break;
                case "rg": if (operands.Count >= 3) { s.FillColor = PdfColor.Rgb(Num(operands[0]), Num(operands[1]), Num(operands[2])); } break;
                case "RG": if (operands.Count >= 3) { s.StrokeColor = PdfColor.Rgb(Num(operands[0]), Num(operands[1]), Num(operands[2])); } break;
                case "k": if (operands.Count >= 4) { s.FillColor = PdfColor.Cmyk(Num(operands[0]), Num(operands[1]), Num(operands[2]), Num(operands[3])); } break;
                case "K": if (operands.Count >= 4) { s.StrokeColor = PdfColor.Cmyk(Num(operands[0]), Num(operands[1]), Num(operands[2]), Num(operands[3])); } break;

                // ── Path construction ────────────────────────────────────────
                case "m":
                    if (operands.Count >= 2)
                    {
                        (double mx, double my) = s.Ctm.Apply(Num(operands[0]), Num(operands[1]));
                        s.AppendMoveTo(mx, my);
                    }
                    break;
                case "l":
                    if (operands.Count >= 2)
                    {
                        (double lx, double ly) = s.Ctm.Apply(Num(operands[0]), Num(operands[1]));
                        s.AppendLineTo(lx, ly);
                    }
                    break;
                case "c":
                    if (operands.Count >= 6)
                    {
                        (double x1, double y1) = s.Ctm.Apply(Num(operands[0]), Num(operands[1]));
                        (double x2, double y2) = s.Ctm.Apply(Num(operands[2]), Num(operands[3]));
                        (double x3, double y3) = s.Ctm.Apply(Num(operands[4]), Num(operands[5]));
                        s.AppendCubicTo(x1, y1, x2, y2, x3, y3);
                    }
                    break;
                case "v":
                    if (operands.Count >= 4)
                    {
                        (double vx2, double vy2) = s.Ctm.Apply(Num(operands[0]), Num(operands[1]));
                        (double vx3, double vy3) = s.Ctm.Apply(Num(operands[2]), Num(operands[3]));
                        s.AppendCubicTo(s.CurX, s.CurY, vx2, vy2, vx3, vy3);
                    }
                    break;
                case "y":
                    if (operands.Count >= 4)
                    {
                        (double yx1, double yy1) = s.Ctm.Apply(Num(operands[0]), Num(operands[1]));
                        (double yx3, double yy3) = s.Ctm.Apply(Num(operands[2]), Num(operands[3]));
                        s.AppendCubicTo(yx1, yy1, yx3, yy3, yx3, yy3);
                    }
                    break;
                case "h":
                    s.AppendClose();
                    break;
                case "re":
                    if (operands.Count >= 4)
                    {
                        double rx = Num(operands[0]);
                        double ry = Num(operands[1]);
                        double rw = Num(operands[2]);
                        double rh = Num(operands[3]);
                        (double p0x, double p0y) = s.Ctm.Apply(rx, ry);
                        (double p1x, double p1y) = s.Ctm.Apply(rx + rw, ry);
                        (double p2x, double p2y) = s.Ctm.Apply(rx + rw, ry + rh);
                        (double p3x, double p3y) = s.Ctm.Apply(rx, ry + rh);
                        s.AppendMoveTo(p0x, p0y);
                        s.AppendLineTo(p1x, p1y);
                        s.AppendLineTo(p2x, p2y);
                        s.AppendLineTo(p3x, p3y);
                        s.AppendClose();
                    }
                    break;

                // ── Path painting ────────────────────────────────────────────
                case "S": EmitPath(s, PaintMode.Stroke, FillRule.NonZero); break;
                case "s": s.AppendClose(); EmitPath(s, PaintMode.Stroke, FillRule.NonZero); break;
                case "f":
                case "F": EmitPath(s, PaintMode.Fill, FillRule.NonZero); break;
                case "f*": EmitPath(s, PaintMode.Fill, FillRule.EvenOdd); break;
                case "B": EmitPath(s, PaintMode.FillAndStroke, FillRule.NonZero); break;
                case "B*": EmitPath(s, PaintMode.FillAndStroke, FillRule.EvenOdd); break;
                case "b": s.AppendClose(); EmitPath(s, PaintMode.FillAndStroke, FillRule.NonZero); break;
                case "b*": s.AppendClose(); EmitPath(s, PaintMode.FillAndStroke, FillRule.EvenOdd); break;
                case "n": s.ResetPath(); break;

                // ── Clipping ──────────────────────────────────────────────────
                case "W":
                case "W*":
                    if (s.HasCurrentPath)
                    {
                        _ops.Add(new ClipOp
                        {
                            Geometry = s.CurrentPath,
                            FillRule = op == "W*" ? FillRule.EvenOdd : FillRule.NonZero,
                        });
                    }
                    break;

                // ── Text ──────────────────────────────────────────────────────
                case "BT":
                    s.TextMatrix = AffineMatrix.Identity;
                    s.TextLineMatrix = AffineMatrix.Identity;
                    ResetGapTracking();
                    break;
                case "ET":
                    ResetGapTracking();
                    break;
                case "Tf":
                    if (operands.Count >= 2)
                    {
                        s.FontKey = operands[0].RawText.TrimStart('/');
                        s.FontSize = Num(operands[1]);
                        s.BaseFont = ResolveBaseFont(s.FontKey);
                    }
                    break;
                case "Td":
                    if (operands.Count >= 2)
                    {
                        AffineMatrix t = new(1, 0, 0, 1, Num(operands[0]), Num(operands[1]));
                        s.TextLineMatrix = t.Multiply(s.TextLineMatrix);
                        s.TextMatrix = s.TextLineMatrix;
                        // Line-changing op: don't track gap across this boundary.
                        ResetGapTracking();
                    }
                    break;
                case "TD":
                    if (operands.Count >= 2)
                    {
                        double tdx = Num(operands[0]); double tdy = Num(operands[1]);
                        s.Leading = -tdy;
                        AffineMatrix t = new(1, 0, 0, 1, tdx, tdy);
                        s.TextLineMatrix = t.Multiply(s.TextLineMatrix);
                        s.TextMatrix = s.TextLineMatrix;
                        ResetGapTracking();
                    }
                    break;
                case "Tm":
                    if (operands.Count >= 6)
                    {
                        AffineMatrix tm = new(
                            Num(operands[0]), Num(operands[1]),
                            Num(operands[2]), Num(operands[3]),
                            Num(operands[4]), Num(operands[5]));
                        s.TextMatrix = tm;
                        s.TextLineMatrix = tm;
                        ResetGapTracking();
                    }
                    break;
                case "T*":
                    {
                        AffineMatrix t = new(1, 0, 0, 1, 0, -s.Leading);
                        s.TextLineMatrix = t.Multiply(s.TextLineMatrix);
                        s.TextMatrix = s.TextLineMatrix;
                        ResetGapTracking();
                    }
                    break;
                case "Tc": if (operands.Count > 0) { s.CharSpacing = Num(operands[0]); } break;
                case "Tw": if (operands.Count > 0) { s.WordSpacing = Num(operands[0]); } break;
                case "Tz": if (operands.Count > 0) { s.HorizontalScaling = Num(operands[0]); } break;
                case "TL": if (operands.Count > 0) { s.Leading = Num(operands[0]); } break;
                case "Tr": if (operands.Count > 0) { s.RenderingMode = (TextRenderingMode)(int)Num(operands[0]); } break;
                case "Ts": if (operands.Count > 0) { s.TextRise = Num(operands[0]); } break;

                case "Tj":
                    if (operands.Count > 0) { EmitText(operands[0], s); }
                    break;
                case "'":
                    {
                        AffineMatrix t = new(1, 0, 0, 1, 0, -s.Leading);
                        s.TextLineMatrix = t.Multiply(s.TextLineMatrix);
                        s.TextMatrix = s.TextLineMatrix;
                        ResetGapTracking();
                        if (operands.Count > 0) { EmitText(operands[0], s); }
                    }
                    break;
                case "\"":
                    if (operands.Count >= 3)
                    {
                        s.WordSpacing = Num(operands[0]);
                        s.CharSpacing = Num(operands[1]);
                        AffineMatrix t = new(1, 0, 0, 1, 0, -s.Leading);
                        s.TextLineMatrix = t.Multiply(s.TextLineMatrix);
                        s.TextMatrix = s.TextLineMatrix;
                        ResetGapTracking();
                        EmitText(operands[2], s);
                    }
                    break;
                case "TJ":
                    // v2.1.3 (fold): consecutive string literals separated by
                    // small (sub-space-width) numeric kerns are merged into a
                    // single TextOp so that downstream renderers using
                    // embedded fonts can let the font's natural hmtx drive
                    // glyph advance across an entire word, eliminating the
                    // inter-chunk gap caused by PDF /Widths and font hmtx
                    // disagreeing. Large kerns still break the fold so real
                    // word spaces survive as TextOp boundaries.
                    EmitTJ(operands, s);
                    break;

                // ── XObject ────────────────────────────────────────────────────
                case "Do":
                    if (operands.Count > 0)
                    {
                        EmitXObject(operands[0].RawText.TrimStart('/'), s);
                    }
                    break;

                // ── Ignored / pass-through ─────────────────────────────────────
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
                    break;
                default: break;
            }
        }

        private void EmitPath(BuilderState s, PaintMode mode, FillRule rule)
        {
            if (!s.HasCurrentPath) { return; }
            StrokeStyle? stroke = mode != PaintMode.Fill ? new StrokeStyle(
                LineWidth: s.LineWidth * Math.Max(Math.Abs(s.Ctm.A), Math.Abs(s.Ctm.D)),
                Cap: s.LineCap,
                Join: s.LineJoin,
                MiterLimit: s.MiterLimit,
                DashArray: s.DashArray,
                DashPhase: s.DashPhase) : null;
            _ops.Add(new PathOp
            {
                Geometry = s.CurrentPath,
                Mode = mode,
                FillRule = rule,
                FillColor = s.FillColor,
                StrokeColor = s.StrokeColor,
                Stroke = stroke,
            });
            s.ResetPath();
        }

        private void ResetGapTracking()
        {
            _hasPrevRunOnLine = false;
            _prevRunEndedWithSpace = false;
        }

        /// <summary>
        /// v2.1.2 helper (Bug 2): returns the synthetic glyph for a leading
        /// space to prepend to a run when the gap from the previous run on
        /// the same line exceeds the word-boundary threshold. Returns null
        /// when no space should be inserted (no previous run, line changed,
        /// gap below threshold, or the previous run already ended in space).
        /// </summary>
        private DisplayListGlyph? MaybeBuildLeadingSpace(BuilderState s, FontWidths widths)
        {
            if (!_hasPrevRunOnLine) { return null; }
            // v2.1.2 (issue B): if the previous run on this line ended with
            // a space character, the word boundary is already represented
            // in the extracted text. Inserting another space here produces
            // the double-space symptom ("Current  Job").
            if (_prevRunEndedWithSpace) { return null; }

            double curX = s.TextMatrix.E;
            double curY = s.TextMatrix.F;

            // If the line changed (Y differs significantly), gap tracking would
            // have been reset by Td/TD/Tm/T*/'. Belt-and-braces: also check Y.
            if (Math.Abs(curY - _prevRunEndY) > 0.01) { return null; }

            double gap = curX - _prevRunEndX;
            if (gap <= 0) { return null; }

            // Compute space-width in user-space points. Use the font's space
            // glyph width if available; otherwise fall back to 0.25 × FontSize.
            double spaceWidth1000 = widths.GetWidth(0x20);
            double spaceWidth = spaceWidth1000 > 0
                ? (spaceWidth1000 / 1000.0) * s.FontSize
                : 0.25 * s.FontSize;
            spaceWidth *= s.HorizontalScaling / 100.0;

            if (gap < spaceWidth * GapToleranceFraction) { return null; }

            // X=0, Advance=0: the synthetic space adds the character for
            // text extraction without affecting downstream glyph positioning.
            return new DisplayListGlyph(
                GlyphId: 0x20,
                Unicode: " ",
                X: 0,
                Y: 0,
                Advance: 0);
        }

        private void EmitText(PdfToken token, BuilderState s)
        {
            if (s.FontKey is null) { return; }
            byte[] bytes = StringExtractor.Extract(token);
            if (bytes.Length == 0) { return; }

            FontWidths widths = GetWidths(s.FontKey);
            bool composite = _compositeByKey.GetValueOrDefault(s.FontKey, false);

            // Decode all glyphs first so we can inspect the first character
            // before deciding whether to prepend a synthetic leading space.
            List<DisplayListGlyph> glyphs = new();
            double xAdvance = 0;
            int codeStep = composite ? 2 : 1;

            for (int i = 0; i + codeStep <= bytes.Length; i += codeStep)
            {
                int code = composite
                    ? ((bytes[i] << 8) | bytes[i + 1])
                    : bytes[i];

                string unicode = DecodeSingleCode(bytes, i, codeStep, s.FontKey);

                double rawWidth = widths.GetWidth(code);   // font units (1000ths em)
                double advance = (rawWidth / 1000.0) * s.FontSize
                                 + s.CharSpacing
                                 + (unicode == " " ? s.WordSpacing : 0.0);
                advance *= s.HorizontalScaling / 100.0;

                glyphs.Add(new DisplayListGlyph(
                    GlyphId: code,
                    Unicode: unicode,
                    X: xAdvance,
                    Y: 0,
                    Advance: advance));

                xAdvance += advance;
            }

            if (glyphs.Count == 0) { return; }

            // v2.1.2 (Bug 2): if the previous run on this line ended far
            // enough away to constitute a word break, AND the new run does
            // not already begin with a literal space character, prepend a
            // synthetic space so the extracted text has the word boundary.
            // The "already has space" guard prevents double-spacing when
            // the PDF supplied an explicit space at the start of the run.
            bool firstIsAlreadySpace = glyphs[0].Unicode == " ";
            if (!firstIsAlreadySpace)
            {
                DisplayListGlyph? leading = MaybeBuildLeadingSpace(s, widths);
                if (leading is not null)
                {
                    glyphs.Insert(0, leading.Value);
                }
            }

            AffineMatrix combined = s.TextMatrix.Multiply(s.Ctm);
            _ops.Add(new TextOp
            {
                FontKey = s.FontKey,
                BaseFont = s.BaseFont ?? "Helvetica",
                FontSize = s.FontSize,
                Glyphs = glyphs,
                Transform = combined,
                RenderingMode = s.RenderingMode,
                FillColor = s.FillColor,
                StrokeColor = s.StrokeColor,
            });

            // Advance text matrix by the total advance of this run.
            AffineMatrix step = new(1, 0, 0, 1, xAdvance, 0);
            s.TextMatrix = step.Multiply(s.TextMatrix);

            // v2.1.2 (Bug 2): record the end position so we can detect a gap
            // before the next emit on the same line. Also record whether this
            // run ended with a space — used to suppress double-space insertion
            // ("Current  Job") on the next run.
            _hasPrevRunOnLine = true;
            _prevRunEndX = s.TextMatrix.E;
            _prevRunEndY = s.TextMatrix.F;
            _prevRunEndedWithSpace = glyphs[glyphs.Count - 1].Unicode == " ";
        }

        /// <summary>
        /// v2.1.3 — TJ-array handler with sub-space-width kerning fold.
        /// </summary>
        /// <remarks>
        /// <para>
        /// PDF §9.4.3: a TJ array alternates between string literals (which
        /// show glyphs) and numeric kerns (which translate the text matrix
        /// horizontally). Word emits TJ arrays where one logical word becomes
        /// many tiny string literals separated by sub-point typographic kerns
        /// like <c>-8</c> or <c>-6</c>. Treating every literal as its own
        /// <see cref="TextOp"/> means downstream renderers see word fragments
        /// at independent anchor positions, and when the SVG renderer trusts
        /// an embedded font's hmtx instead of PDF <c>/Widths</c>, the
        /// fragment anchors and the font's glyph extents disagree by a
        /// fraction of an em — producing visible intra-word gaps.
        /// </para>
        /// <para>
        /// The fold buffers consecutive same-state string literals into a
        /// single <see cref="TextOp"/>. Small kerns (below
        /// <see cref="GapToleranceFraction"/> of the space width) are absorbed
        /// into the running cursor position so per-glyph X offsets within
        /// the fold include them; the renderer is then free to honour or
        /// ignore those offsets depending on whether the font is embedded.
        /// Large kerns flush the fold and start a fresh <see cref="TextOp"/>
        /// after the kern, so real word spaces remain encoded as TextOp
        /// boundaries.
        /// </para>
        /// </remarks>
        private void EmitTJ(List<PdfToken> operands, BuilderState s)
        {
            if (s.FontKey is null) { return; }

            FontWidths widths = GetWidths(s.FontKey);
            bool composite = _compositeByKey.GetValueOrDefault(s.FontKey, false);
            int codeStep = composite ? 2 : 1;

            // Break-the-fold threshold: a kern whose magnitude exceeds this
            // many user-space points starts a new TextOp after the kern.
            // Mirrors the word-boundary heuristic in MaybeBuildLeadingSpace.
            double spaceWidth1000 = widths.GetWidth(0x20);
            double spaceWidthPoints = spaceWidth1000 > 0
                ? (spaceWidth1000 / 1000.0) * s.FontSize
                : 0.25 * s.FontSize;
            spaceWidthPoints *= s.HorizontalScaling / 100.0;
            double breakThreshold = spaceWidthPoints * GapToleranceFraction;

            // Pending fold state.
            List<DisplayListGlyph> pending = new();
            AffineMatrix pendingTransform = AffineMatrix.Identity;
            double cursorX = 0;

            for (int idx = 0; idx < operands.Count; idx++)
            {
                PdfToken tok = operands[idx];
                if (tok.Type == PdfTokenType.LiteralString
                    || tok.Type == PdfTokenType.HexString)
                {
                    byte[] bytes = StringExtractor.Extract(tok);
                    if (bytes.Length == 0) { continue; }

                    bool startingNewFold = pending.Count == 0;
                    if (startingNewFold)
                    {
                        pendingTransform = s.TextMatrix.Multiply(s.Ctm);
                        cursorX = 0;
                    }

                    // Decode glyphs and accumulate into the pending fold.
                    List<DisplayListGlyph> decoded = new();
                    for (int i = 0; i + codeStep <= bytes.Length; i += codeStep)
                    {
                        int code = composite
                            ? ((bytes[i] << 8) | bytes[i + 1])
                            : bytes[i];
                        string unicode = DecodeSingleCode(bytes, i, codeStep, s.FontKey);
                        double rawWidth = widths.GetWidth(code);
                        double advance = (rawWidth / 1000.0) * s.FontSize
                                         + s.CharSpacing
                                         + (unicode == " " ? s.WordSpacing : 0.0);
                        advance *= s.HorizontalScaling / 100.0;

                        decoded.Add(new DisplayListGlyph(
                            GlyphId: code,
                            Unicode: unicode,
                            X: cursorX,
                            Y: 0,
                            Advance: advance));
                        cursorX += advance;
                    }

                    // On a fold START, optionally prepend a synthetic leading
                    // space for text-extraction word boundaries. The same
                    // logic as EmitText's leading-space prepend, but applied
                    // exactly once per fold rather than once per literal.
                    if (startingNewFold && decoded.Count > 0)
                    {
                        bool firstIsAlreadySpace = decoded[0].Unicode == " ";
                        if (!firstIsAlreadySpace)
                        {
                            DisplayListGlyph? leading = MaybeBuildLeadingSpace(s, widths);
                            if (leading is not null)
                            {
                                pending.Add(leading.Value);
                            }
                        }
                    }

                    pending.AddRange(decoded);
                }
                else if (tok.IsNumeric)
                {
                    double n = Num(tok);
                    // Negative n shifts text forward (right) in LTR per §9.4.3.
                    double tx = -(n / 1000.0) * s.FontSize * (s.HorizontalScaling / 100.0);

                    // v2.1.3 — lookahead: if this kern is immediately followed
                    // by a string literal whose first character is " ", treat
                    // the kern as small regardless of magnitude. Word emits
                    // a large positive shift before the space glyph to widen
                    // inter-word gaps; combined with the explicit space char
                    // that follows, the visible word break is doubled. By
                    // absorbing the kern, the embedded font's own space-glyph
                    // advance alone provides the visible word break. Without
                    // this rule the kern would flush the fold and produce
                    // either a missing space (when the snap shrink fires and
                    // over-corrects) or a too-wide space (when it doesn't).
                    bool nextIsLeadingSpace = false;
                    if (idx + 1 < operands.Count)
                    {
                        PdfToken nextTok = operands[idx + 1];
                        if (nextTok.Type == PdfTokenType.LiteralString
                            || nextTok.Type == PdfTokenType.HexString)
                        {
                            byte[] nextBytes = StringExtractor.Extract(nextTok);
                            if (nextBytes.Length >= codeStep)
                            {
                                string firstUnicode = DecodeSingleCode(
                                    nextBytes, 0, codeStep, s.FontKey);
                                nextIsLeadingSpace = firstUnicode == " ";
                            }
                        }
                    }

                    if (pending.Count == 0)
                    {
                        // No fold in progress — apply kern directly to text
                        // matrix, matching the pre-fold behaviour for leading
                        // or post-flush kerns.
                        AffineMatrix step = new(1, 0, 0, 1, tx, 0);
                        s.TextMatrix = step.Multiply(s.TextMatrix);
                    }
                    else if (Math.Abs(tx) >= breakThreshold && !nextIsLeadingSpace)
                    {
                        // Large kern that's NOT Word's kern-before-space
                        // idiom: flush, apply the kern, start a new fold.
                        FlushFold(pending, pendingTransform, cursorX, s);
                        pending = new List<DisplayListGlyph>();
                        AffineMatrix step = new(1, 0, 0, 1, tx, 0);
                        s.TextMatrix = step.Multiply(s.TextMatrix);
                        cursorX = 0;
                    }
                    else
                    {
                        // Small kern, or kern-before-space: absorb into
                        // the cursor so the following glyph(s) sit at the
                        // kerned position within the same fold.
                        cursorX += tx;
                    }
                }
            }

            // Final flush at end of TJ. If pending is empty, any trailing
            // kerns have already been applied directly to the text matrix.
            if (pending.Count > 0)
            {
                FlushFold(pending, pendingTransform, cursorX, s);
            }
        }

        /// <summary>
        /// v2.1.3 — emit a folded TextOp and update gap-tracking state.
        /// Advances <see cref="BuilderState.TextMatrix"/> by
        /// <paramref name="cursorX"/> (the total run width including any
        /// absorbed small kerns).
        /// </summary>
        private void FlushFold(List<DisplayListGlyph> pending,
            AffineMatrix transform, double cursorX, BuilderState s)
        {
            if (pending.Count == 0) { return; }

            _ops.Add(new TextOp
            {
                FontKey = s.FontKey!,
                BaseFont = s.BaseFont ?? "Helvetica",
                FontSize = s.FontSize,
                Glyphs = pending,
                Transform = transform,
                RenderingMode = s.RenderingMode,
                FillColor = s.FillColor,
                StrokeColor = s.StrokeColor,
            });

            AffineMatrix step = new(1, 0, 0, 1, cursorX, 0);
            s.TextMatrix = step.Multiply(s.TextMatrix);

            _hasPrevRunOnLine = true;
            _prevRunEndX = s.TextMatrix.E;
            _prevRunEndY = s.TextMatrix.F;
            _prevRunEndedWithSpace = pending[pending.Count - 1].Unicode == " ";
        }

        private FontWidths GetWidths(string fontKey)
        {
            if (_widthsByKey.TryGetValue(fontKey, out FontWidths? cached)) { return cached; }
            PdfDictionary? fontDict = ResolveFontDict(fontKey);
            if (fontDict is null)
            {
                FontWidths fallback = FontWidthsFallback();
                _widthsByKey[fontKey] = fallback;
                _compositeByKey[fontKey] = false;
                return fallback;
            }
            FontWidths fw = FontWidths.FromDictionary(fontDict, _doc.Objects);
            // Enable Standard 14 fallback if BaseFont is one of them — many PDFs
            // (and Chuvadi's Authoring module) omit /Widths for Standard 14 fonts.
            string? baseFont = ResolveBaseFont(fontKey);
            if (baseFont is not null) { fw.EnableStandard14Fallback(baseFont); }
            _widthsByKey[fontKey] = fw;
            _compositeByKey[fontKey] = fw.IsComposite;
            return fw;
        }

        private PdfDictionary? ResolveFontDict(string fontKey)
        {
            if (_fontDictsByKey.TryGetValue(fontKey, out PdfDictionary? cached))
            {
                return cached;
            }
            if (_resources is null) { return null; }
            if (!_resources.TryGetValue(PdfName.Intern("Font"), out PdfPrimitive? fonts))
            {
                return null;
            }
            PdfDictionary? fd = _doc.Objects.ResolveAs<PdfDictionary>(fonts);
            if (fd is null) { return null; }
            if (!fd.TryGetValue(PdfName.Intern(fontKey), out PdfPrimitive? fv)) { return null; }
            PdfDictionary? resolved = _doc.Objects.ResolveAs<PdfDictionary>(fv);
            if (resolved is not null)
            {
                _fontDictsByKey[fontKey] = resolved;
            }
            return resolved;
        }

        private static FontWidths FontWidthsFallback()
        {
            // Build a stub FontWidths via reflection-free constructor proxy:
            // a synthetic font dict with no /Widths gives the default 500 width.
            PdfDictionary empty = new();
            return FontWidths.FromDictionary(empty, NullResolver.Instance);
        }

        private string DecodeSingleCode(byte[] bytes, int offset, int codeStep, string fontKey)
        {
            // Decode just the bytes [offset, offset+codeStep) through PdfFont.
            byte[] slice = new byte[codeStep];
            System.Array.Copy(bytes, offset, slice, 0, codeStep);
            return DecodeText(slice, fontKey);
        }

        private string DecodeText(byte[] bytes, string fontKey)
        {
            if (_resources is null)
            {
                AddDiagnostic(DiagnosticKind.DecodeFallback,
                    $"Font '{fontKey}' could not be resolved: page has no /Resources. Falling back to Latin-1 decoding.");
                return TryLatin(bytes);
            }
            if (!_resources.TryGetValue(PdfName.Intern("Font"), out PdfPrimitive? fonts))
            {
                AddDiagnostic(DiagnosticKind.DecodeFallback,
                    $"Font '{fontKey}' could not be resolved: /Resources has no /Font entry. Falling back to Latin-1 decoding.");
                return TryLatin(bytes);
            }
            PdfDictionary? fd = _doc.Objects.ResolveAs<PdfDictionary>(fonts);
            if (fd is null)
            {
                AddDiagnostic(DiagnosticKind.DecodeFallback,
                    $"Font '{fontKey}' could not be resolved: /Resources/Font reference did not resolve to a dictionary. Falling back to Latin-1 decoding.");
                return TryLatin(bytes);
            }
            if (!fd.TryGetValue(PdfName.Intern(fontKey), out PdfPrimitive? fv))
            {
                AddDiagnostic(DiagnosticKind.DecodeFallback,
                    $"Font '{fontKey}' could not be resolved: /Font sub-dictionary has no entry for this key. Falling back to Latin-1 decoding.");
                return TryLatin(bytes);
            }
            PdfDictionary? font = _doc.Objects.ResolveAs<PdfDictionary>(fv);
            if (font is null)
            {
                AddDiagnostic(DiagnosticKind.DecodeFallback,
                    $"Font '{fontKey}' could not be resolved: the font reference did not resolve to a dictionary. Falling back to Latin-1 decoding.");
                return TryLatin(bytes);
            }
            try
            {
                PdfFont pf = PdfFont.FromDictionary(font, _doc.Objects);
                return pf.Decode(bytes);
            }
            catch (Exception ex)
            {
                AddDiagnostic(DiagnosticKind.DecodeFallback,
                    $"Font '{fontKey}' could not be resolved: PdfFont.FromDictionary threw {ex.GetType().Name}: {ex.Message}. Falling back to Latin-1 decoding.");
                return TryLatin(bytes);
            }
        }

        // v2.1.8: record a graceful-degradation event for downstream consumers.
        // Dedupes by (kind, message) so a per-character DecodeText fallback
        // emits one diagnostic per page, not one per glyph.
        private void AddDiagnostic(DiagnosticKind kind, string message)
        {
            if (_diagnosticKeys.Add((kind, message)))
            {
                _diagnostics.Add(new RenderingDiagnostic(kind, message));
            }
        }

        private static string TryLatin(byte[] bytes)
            => System.Text.Encoding.Latin1.GetString(bytes);

        private string? ResolveBaseFont(string fontKey)
        {
            if (_resources is null) { return null; }
            if (!_resources.TryGetValue(PdfName.Intern("Font"), out PdfPrimitive? fonts))
            {
                return null;
            }
            PdfDictionary? fd = _doc.Objects.ResolveAs<PdfDictionary>(fonts);
            if (fd is null) { return null; }
            if (!fd.TryGetValue(PdfName.Intern(fontKey), out PdfPrimitive? fv)) { return null; }
            PdfDictionary? font = _doc.Objects.ResolveAs<PdfDictionary>(fv);
            if (font is null) { return null; }
            if (font.TryGetValue(PdfName.Intern("BaseFont"), out PdfPrimitive? bv)
                && bv is PdfName bn)
            {
                string s = bn.Value;
                int plus = s.IndexOf('+');
                if (plus >= 0 && plus < s.Length - 1) { return s[(plus + 1)..]; }
                return s;
            }
            return null;
        }

        private void EmitXObject(string name, BuilderState s)
        {
            if (_resources is null) { return; }
            if (!_resources.TryGetValue(PdfName.Intern("XObject"), out PdfPrimitive? xv))
            {
                return;
            }
            PdfDictionary? xobjects = _doc.Objects.ResolveAs<PdfDictionary>(xv);
            if (xobjects is null) { return; }
            if (!xobjects.TryGetValue(PdfName.Intern(name), out PdfPrimitive? imgRef)) { return; }
            if (_doc.Objects.Resolve(imgRef) is not PdfStream stream) { return; }
            if (!stream.Dictionary.TryGetValue(PdfName.Intern("Subtype"), out PdfPrimitive? sub)
                || sub is not PdfName subName || subName.Value != "Image")
            {
                return;
            }

            int width = IntOf(stream.Dictionary, "Width", 0);
            int height = IntOf(stream.Dictionary, "Height", 0);
            int bpc = IntOf(stream.Dictionary, "BitsPerComponent", 8);
            if (width <= 0 || height <= 0) { return; }

            string? filterName = ExtractFilterName(stream.Dictionary);
            ImageFormat format;
            byte[] pixelData;
            PdfColorSpace cs = ExtractColorSpace(stream.Dictionary);

            if (filterName == "DCTDecode")
            {
                format = ImageFormat.Jpeg;
                pixelData = stream.RawBytes;
            }
            else
            {
                format = ImageFormat.Raw;
                try { pixelData = StreamDecodeHelper.Decode(stream); }
                catch { return; }
            }

            _ops.Add(new ImageOp
            {
                PixelData = pixelData,
                Format = format,
                Width = width,
                Height = height,
                BitsPerComponent = bpc,
                ColorSpace = cs,
                Transform = s.Ctm,
            });
        }

        private static int IntOf(PdfDictionary d, string key, int fallback)
        {
            if (d.TryGetValue(PdfName.Intern(key), out PdfPrimitive? v) && v is PdfInteger i)
            {
                return i.Value;
            }
            return fallback;
        }

        private static string? ExtractFilterName(PdfDictionary d)
        {
            if (!d.TryGetValue(PdfName.Intern("Filter"), out PdfPrimitive? f)) { return null; }
            return f switch
            {
                PdfName n => n.Value,
                PdfArray arr when arr.Count > 0 && arr[0] is PdfName n2 => n2.Value,
                _ => null,
            };
        }

        private static PdfColorSpace ExtractColorSpace(PdfDictionary d)
        {
            if (!d.TryGetValue(PdfName.Intern("ColorSpace"), out PdfPrimitive? cs)) { return PdfColorSpace.DeviceRgb; }
            if (cs is PdfName n)
            {
                return n.Value switch
                {
                    "DeviceGray" => PdfColorSpace.DeviceGray,
                    "DeviceRGB" => PdfColorSpace.DeviceRgb,
                    "DeviceCMYK" => PdfColorSpace.DeviceCmyk,
                    _ => PdfColorSpace.DeviceRgb,
                };
            }
            return PdfColorSpace.DeviceRgb;
        }

        private static void ParseDashArray(List<PdfToken> operands, BuilderState s)
        {
            List<double> values = new();
            double phase = 0;
            int phaseIndex = -1;
            for (int i = 0; i < operands.Count; i++)
            {
                if (operands[i].Type == PdfTokenType.ArrayStart || operands[i].Type == PdfTokenType.ArrayEnd) { continue; }
                if (operands[i].IsNumeric)
                {
                    if (phaseIndex < 0) { values.Add(Num(operands[i])); phaseIndex = i; }
                    else { phase = Num(operands[i]); }
                }
            }
            s.DashArray = values.Count > 0 ? values.ToArray() : null;
            s.DashPhase = phase;
        }

        private static double Num(PdfToken t)
            => double.Parse(t.RawText, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}

internal static class StreamDecodeHelper
{
    private static readonly Chuvadi.Pdf.Filters.FilterPipeline Pipeline
        = Chuvadi.Pdf.Filters.FilterRegistry.CreateDefaultPipeline();

    internal static byte[] Decode(PdfStream stream)
    {
        if (!stream.IsFiltered) { return stream.RawBytes; }
        PdfPrimitive? filter = stream.Filter;
        if (filter is PdfName fn)
        {
            string resolved = Chuvadi.Pdf.Filters.FilterRegistry.ResolveAlias(fn.Value);
            return Pipeline.Decode(resolved, stream.RawBytes, null);
        }
        if (filter is PdfArray fa)
        {
            byte[] data = stream.RawBytes;
            for (int i = 0; i < fa.Count; i++)
            {
                if (fa[i] is PdfName n)
                {
                    string resolved = Chuvadi.Pdf.Filters.FilterRegistry.ResolveAlias(n.Value);
                    data = Pipeline.Decode(resolved, data, null);
                }
            }
            return data;
        }
        return stream.RawBytes;
    }
}

internal static class StringExtractor
{
    internal static byte[] Extract(PdfToken token)
    {
        if (token.Type == PdfTokenType.LiteralString) { return UnescapeLiteral(token.RawBytes); }
        if (token.Type == PdfTokenType.HexString) { return UnescapeHex(token.RawBytes); }
        return token.RawBytes;
    }

    private static byte[] UnescapeLiteral(byte[] raw)
    {
        int start = 0, end = raw.Length;
        if (end > 0 && raw[0] == (byte)'(') { start = 1; }
        if (end > start && raw[end - 1] == (byte)')') { end--; }
        List<byte> result = new(end - start);
        for (int i = start; i < end; i++)
        {
            byte b = raw[i];
            if (b == (byte)'\\' && i + 1 < end)
            {
                byte next = raw[++i];
                switch (next)
                {
                    case (byte)'n': result.Add((byte)'\n'); break;
                    case (byte)'r': result.Add((byte)'\r'); break;
                    case (byte)'t': result.Add((byte)'\t'); break;
                    case (byte)'b': result.Add(0x08); break;
                    case (byte)'f': result.Add(0x0C); break;
                    case (byte)'(': result.Add((byte)'('); break;
                    case (byte)')': result.Add((byte)')'); break;
                    case (byte)'\\': result.Add((byte)'\\'); break;
                    default:
                        if (next >= (byte)'0' && next <= (byte)'7')
                        {
                            int v = next - (byte)'0';
                            int digits = 1;
                            while (digits < 3 && i + 1 < end
                                && raw[i + 1] >= (byte)'0' && raw[i + 1] <= (byte)'7')
                            {
                                v = v * 8 + (raw[++i] - (byte)'0');
                                digits++;
                            }
                            result.Add((byte)v);
                        }
                        else { result.Add(next); }
                        break;
                }
            }
            else { result.Add(b); }
        }
        return result.ToArray();
    }

    private static byte[] UnescapeHex(byte[] raw)
    {
        int start = 0, end = raw.Length;
        if (end > 0 && raw[0] == (byte)'<') { start = 1; }
        if (end > start && raw[end - 1] == (byte)'>') { end--; }
        List<byte> result = new();
        int pending = -1;
        for (int i = start; i < end; i++)
        {
            int v = HexVal(raw[i]);
            if (v < 0) { continue; }
            if (pending < 0) { pending = v; }
            else { result.Add((byte)((pending << 4) | v)); pending = -1; }
        }
        if (pending >= 0) { result.Add((byte)(pending << 4)); }
        return result.ToArray();
    }

    private static int HexVal(byte b)
    {
        if (b >= (byte)'0' && b <= (byte)'9') { return b - (byte)'0'; }
        if (b >= (byte)'A' && b <= (byte)'F') { return 10 + b - (byte)'A'; }
        if (b >= (byte)'a' && b <= (byte)'f') { return 10 + b - (byte)'a'; }
        return -1;
    }
}
