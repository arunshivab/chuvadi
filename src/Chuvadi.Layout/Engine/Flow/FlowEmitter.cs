using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Fonts;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.Engine.Flow
{
    /// <summary>Turns a <see cref="PagePlan"/> into a <see cref="LaidOutPage"/>.</summary>
    internal static class FlowEmitter
    {
        public static LaidOutPage Emit(FlowEngine engine, LayoutContext ctx, PagePlan plan, PageRecord record, int index, CancellationToken ct)
        {
            List<LayoutOp> ops = new List<LayoutOp>();
            List<LayoutBox> boxes = new List<LayoutBox>();
            StringBuilder text = new StringBuilder();
            bool provisional = false;

            // Watermark, images behind text, then decorations, then content, then images in front of text.
            engine.EmitWatermark(plan, record, ops);
            foreach (PlanItem item in plan.Items)
            {
                if (item is PlacedFloatingImage behind && behind.Behind)
                {
                    EmitFloating(engine, behind, record, ctx, ops, boxes, text, ct);
                }
            }
            foreach (PlanItem item in plan.Items)
            {
                if (item is PlacedParagraphSlice slice)
                {
                    EmitParagraphDecoration(slice, ops);
                    boxes.Add(new LayoutBox(LayoutBoxKind.Paragraph, slice.Rect, slice.Path));
                }
            }
            engine.EmitHeadersFooters(plan, record, ops, boxes, ref provisional, ct);
            foreach (PlanItem item in plan.Items)
            {
                ct.ThrowIfCancellationRequested();
                switch (item)
                {
                    case PlacedLine line:
                        EmitLine(line, ctx, ops, boxes, text);
                        break;
                    case PlacedTablePart table:
                        engine.EmitTablePart(table, ops, boxes, text, ct);
                        break;
                    case PlacedColumnRule rule:
                        ops.Add(new StrokeLineOp(rule.X, rule.Top, rule.X, rule.Bottom, FlowEngine.ColumnSeparatorWidth, LayoutColor.Black));
                        break;
                }
            }

            foreach (PlanItem item in plan.Items)
            {
                if (item is PlacedFloatingImage front && !front.Behind)
                {
                    EmitFloating(engine, front, record, ctx, ops, boxes, text, ct);
                }
            }
            engine.EmitFootnotes(plan, record, ops, boxes, text, ct);

            string label = FlowEngine.FormatPageNumber(record.PageNumber, engine.SectionNumberFormat(record.Section));
            return new LaidOutPage(index, new LayoutDisplayList(ops, plan.Geometry.Width, plan.Geometry.Height), boxes,
                text.ToString().TrimEnd('\n'), provisional, label, null);
        }

        private static void EmitFloating(FlowEngine engine, PlacedFloatingImage f, PageRecord record, LayoutContext ctx, List<LayoutOp> ops, List<LayoutBox> boxes,
            StringBuilder text, CancellationToken ct)
        {
            if (f.TextBox is FlowTextBox box)
            {
                engine.EmitTextBox(box, f.Rect, record, ops, text, f.Path, ct);
                boxes.Add(new LayoutBox(LayoutBoxKind.TextBox, f.Rect, f.Path));
                return;
            }
            EmitImageOrPlaceholder(f.Image!, f.Rect, ctx, ops, f.Path, f.Crop);
            boxes.Add(new LayoutBox(LayoutBoxKind.Image, f.Rect, f.Path));
        }

        internal static void EmitParagraphDecoration(PlacedParagraphSlice slice, List<LayoutOp> ops)
        {
            ParagraphFormat f = slice.Paragraph.Format;
            LayoutRect r = slice.Rect;
            bool top = f.BorderTop.IsVisible && slice.IsFirstSlice;
            bool bottom = f.BorderBottom.IsVisible && slice.IsLastSlice;
            // The bordered box: the lines plus each border's spacing; borders are drawn just outside it.
            double outerTop = r.Y - (top ? f.BorderTop.Space : 0);
            double outerBottom = r.Bottom + (bottom ? f.BorderBottom.Space : 0);
            double outerLeft = r.X - (f.BorderLeft.IsVisible ? f.BorderLeft.Space : 0);
            double outerRight = r.Right + (f.BorderRight.IsVisible ? f.BorderRight.Space : 0);
            if (f.Background is LayoutColor bg && !bg.IsTransparent && outerBottom > outerTop)
            {
                ops.Add(new FillRectOp(new LayoutRect(outerLeft, outerTop, outerRight - outerLeft, outerBottom - outerTop), bg));
            }
            double lineTop = outerTop - (top ? f.BorderTop.Width / 2 : 0);
            double lineBottom = outerBottom + (bottom ? f.BorderBottom.Width / 2 : 0);
            double xl = outerLeft - (f.BorderLeft.IsVisible ? f.BorderLeft.Width / 2 : 0);
            double xr = outerRight + (f.BorderRight.IsVisible ? f.BorderRight.Width / 2 : 0);
            if (top)
            {
                ops.Add(new StrokeLineOp(xl - (f.BorderLeft.Width / 2), lineTop, xr + (f.BorderRight.Width / 2), lineTop, f.BorderTop.Width, f.BorderTop.Color, f.BorderTop.Dash));
            }
            if (bottom)
            {
                ops.Add(new StrokeLineOp(xl - (f.BorderLeft.Width / 2), lineBottom, xr + (f.BorderRight.Width / 2), lineBottom, f.BorderBottom.Width, f.BorderBottom.Color, f.BorderBottom.Dash));
            }
            if (f.BorderLeft.IsVisible)
            {
                ops.Add(new StrokeLineOp(xl, lineTop, xl, lineBottom, f.BorderLeft.Width, f.BorderLeft.Color, f.BorderLeft.Dash));
            }
            if (f.BorderRight.IsVisible)
            {
                ops.Add(new StrokeLineOp(xr, lineTop, xr, lineBottom, f.BorderRight.Width, f.BorderRight.Color, f.BorderRight.Dash));
            }
        }

        /// <summary>
        /// Emits one line. Field values, page-dependent ones included, are part of the measured text: they were
        /// evaluated, shaped and fitted with the rest of the paragraph (D-90).
        /// </summary>
        internal static void EmitLine(PlacedLine pl, LayoutContext ctx, List<LayoutOp> ops, List<LayoutBox> boxes, StringBuilder text)
        {
            MeasuredParagraph m = pl.Paragraph;
            FittedLine line = pl.Line;
            ParagraphFormat pf = m.Source.Format;
            string t = m.Text;
            int start = line.Start;
            int visibleEnd = line.VisibleEnd;

            // Alignment.
            double available = line.MaxX - line.StartX;
            double content = line.ContentEndX - line.StartX;
            double slack = Math.Max(0, available - content);
            double offset = 0;
            double perGap = 0;
            HashSet<int>? stretch = null;
            switch (pf.Alignment)
            {
                case ParagraphAlignment.Center:
                    offset = slack / 2;
                    break;
                case ParagraphAlignment.Right:
                    offset = slack;
                    break;
                case ParagraphAlignment.Justify:
                    if (!line.IsLastLine && !line.EndsWithPageBreak)
                    {
                        stretch = JustifiableSpaces(t, start, visibleEnd);
                        perGap = stretch.Count > 0 ? slack / stretch.Count : 0;
                    }
                    break;
                case ParagraphAlignment.Distribute:
                    stretch = new HashSet<int>();
                    for (int k = start; k < visibleEnd - 1; k++)
                    {
                        if (m.ClusterStart[k] && t[k] != '\t')
                        {
                            stretch.Add(k);
                        }
                    }
                    perGap = stretch.Count > 0 ? slack / stretch.Count : 0;
                    if (stretch.Count == 0)
                    {
                        offset = slack / 2;
                    }
                    break;
            }

            // Pen positions for every index in the line.
            double x0 = pl.AreaLeft + line.StartX + offset;
            int end = line.End;
            double[] pos = new double[end - start + 1];
            double x = x0;
            for (int k = start; k < end; k++)
            {
                pos[k - start] = x;
                char c = t[k];
                if (c == '\t')
                {
                    x += line.TabWidths.TryGetValue(k, out double tw) ? tw : 0;
                }
                else
                {
                    x += m.Advance[k];
                }
                if (stretch is not null && stretch.Contains(k))
                {
                    x += perGap;
                }
            }
            pos[end - start] = x;

            double baseline = pl.Top + line.Baseline;
            double lineTop = pl.Top;
            double lineBottom = pl.Top + line.Height;

            // Background highlights behind the line's text.
            int prevSeg = -1;
            for (int k = start; k < visibleEnd; k++)
            {
                int si = m.SegmentOf[k];
                if (si == prevSeg)
                {
                    continue;
                }
                prevSeg = si;
                Segment seg = m.Segments[si];
                if (seg.Format.Background is LayoutColor bg && !bg.IsTransparent)
                {
                    int a = Math.Max(seg.Start, start);
                    int b = Math.Min(seg.End, visibleEnd);
                    double top = baseline + seg.BaselineOffset - (seg.Ascent + Math.Min(0, seg.BaselineOffset));
                    double h = seg.Ascent + seg.Descent;
                    ops.Add(new FillRectOp(new LayoutRect(pos[a - start], Math.Max(lineTop, top), pos[b - start] - pos[a - start], h), bg));
                }
            }

            StringBuilder lineText = new StringBuilder();
            prevSeg = -1;
            for (int k = start; k < Math.Max(visibleEnd, start); k++)
            {
                int si = m.SegmentOf[k];
                if (si == prevSeg)
                {
                    continue;
                }
                prevSeg = si;
                Segment seg = m.Segments[si];
                int a = Math.Max(seg.Start, start);
                int b = Math.Min(seg.End, visibleEnd);
                if (b <= a)
                {
                    continue;
                }
                double segX = pos[a - start];
                double segRight = pos[b - start];
                switch (seg.Kind)
                {
                    case SegmentKind.Text:
                        EmitGlyphs(seg, m, a, b, pos, start, baseline, ops);
                        AppendText(lineText, t, a, b);
                        break;
                    case SegmentKind.Tab:
                        lineText.Append('\t');
                        EmitTabLeaders(seg, m, pf, a, b, pos, start, baseline, pl.AreaLeft + offset, ops);
                        break;
                    case SegmentKind.Image:
                        EmitImage(seg, ctx, segX, baseline, ops, boxes, pl.Path);
                        lineText.Append('\uFFFC');
                        break;
                }
                EmitDecorations(seg, segX, segRight, baseline, ops);
                if (seg.HyperlinkUri is not null || seg.HyperlinkAnchor is not null)
                {
                    ops.Add(new LinkOp(new LayoutRect(segX, lineTop, segRight - segX, lineBottom - lineTop), seg.HyperlinkUri, seg.HyperlinkAnchor));
                }
            }

            if (line.Hyphenated && visibleEnd > 0 && m.SoftHyphens.TryGetValue(visibleEnd - 1, out var hyphen))
            {
                Segment seg = m.Segments[m.SegmentOf[visibleEnd - 1]];
                double hx = pos[visibleEnd - start];
                ops.Add(new GlyphRunOp
                {
                    Font = seg.Font!.Face,
                    FontSize = seg.FontSize,
                    X = hx,
                    Y = baseline + seg.BaselineOffset,
                    Glyphs = new[] { new PositionedGlyph(hyphen.Glyph.GlyphId, 0, 0, hyphen.Width, 0) },
                    Text = "-",
                    Color = seg.Format.Color,
                    SyntheticBold = seg.Font.SyntheticBold,
                    SyntheticItalic = seg.Font.SyntheticItalic,
                    HorizontalScale = seg.HorizontalScale,
                });
                lineText.Append('-');
            }

            double right = Math.Max(pos[Math.Max(visibleEnd, start) - start], x0);
            boxes.Add(new LayoutBox(LayoutBoxKind.Line, new LayoutRect(x0, lineTop, right - x0, line.Height), pl.Path));
            text.Append(lineText);
            text.Append('\n');
        }

        private static void AppendText(StringBuilder sb, string t, int a, int b)
        {
            for (int k = a; k < b; k++)
            {
                char c = t[k];
                if (c == '\u00AD' || c == ParagraphMeasurer.LineSeparator || ParagraphMeasurer.IsPageOrColumnBreak(c))
                {
                    continue;
                }
                sb.Append(c);
            }
        }

        private static HashSet<int> JustifiableSpaces(string t, int start, int visibleEnd)
        {
            int lastTab = start - 1;
            for (int k = start; k < visibleEnd; k++)
            {
                if (t[k] == '\t')
                {
                    lastTab = k;
                }
            }
            HashSet<int> set = new HashSet<int>();
            for (int k = lastTab + 1; k < visibleEnd; k++)
            {
                if (t[k] == ' ')
                {
                    set.Add(k);
                }
            }
            return set;
        }

        private static void EmitGlyphs(Segment seg, MeasuredParagraph m, int a, int b, double[] pos, int lineStart, double baseline,
            List<LayoutOp> ops)
        {
            FontResolution font = seg.Font!;
            List<PositionedGlyph> glyphs = new List<PositionedGlyph>();
            double origin = pos[a - lineStart];
            int lastCluster = -1;
            double clusterPen = 0;
            foreach (MGlyph g in seg.Glyphs)
            {
                if (g.Cluster < a || g.Cluster >= b)
                {
                    continue;
                }
                if (g.Cluster != lastCluster)
                {
                    lastCluster = g.Cluster;
                    clusterPen = pos[g.Cluster - lineStart] - origin;
                }
                glyphs.Add(new PositionedGlyph(g.GlyphId, clusterPen + g.XOffset, g.YOffset, g.Advance, g.Cluster - a));
                clusterPen += g.Advance;
            }
            if (glyphs.Count == 0)
            {
                return;
            }
            ops.Add(Run(seg, font, origin, baseline, glyphs, m.Text.Substring(a, b - a)));
        }

        private static GlyphRunOp Run(Segment seg, FontResolution font, double x, double baseline, IReadOnlyList<PositionedGlyph> glyphs, string text)
            => new GlyphRunOp
            {
                Font = font.Face,
                FontSize = seg.FontSize,
                X = x,
                Y = baseline + seg.BaselineOffset,
                Glyphs = glyphs,
                Text = text,
                Color = seg.Format.Color,
                SyntheticBold = font.SyntheticBold,
                SyntheticItalic = font.SyntheticItalic,
                HorizontalScale = seg.HorizontalScale,
            };

        private static void EmitTabLeaders(Segment seg, MeasuredParagraph m, ParagraphFormat pf, int a, int b, double[] pos, int lineStart, double baseline, double areaLeft, List<LayoutOp> ops)
        {
            for (int k = a; k < b; k++)
            {
                double x1 = pos[k - lineStart];
                double x2 = pos[k + 1 - lineStart];
                TabLeader leader = m.PositionalTabs is not null && m.PositionalTabs.TryGetValue(k, out PositionalTab? ptab) ? ptab.Leader : LeaderAt(pf, x1 - areaLeft);
                if (leader == TabLeader.None || x2 - x1 < 1 || seg.Font is null)
                {
                    continue;
                }
                char ch = leader switch
                {
                    TabLeader.Dot => '.',
                    TabLeader.Hyphen => '-',
                    TabLeader.Underscore or TabLeader.Heavy => '_',
                    TabLeader.MiddleDot => '\u00B7',
                    _ => '.',
                };
                FontFace face = seg.Font.Face;
                int g = face.GetGlyphId(ch);
                double adv = face.GetAdvance(g) * seg.FontSize / face.UnitsPerEm;
                if (adv <= 0)
                {
                    continue;
                }
                // Leaders align to a grid of the leader advance, leaving a small gap before the text.
                List<PositionedGlyph> glyphs = new List<PositionedGlyph>();
                double gx = Math.Ceiling((x1 + (adv / 2)) / adv) * adv;
                while (gx + adv <= x2 - (adv / 2))
                {
                    glyphs.Add(new PositionedGlyph(g, gx - x1, 0, adv, 0));
                    gx += adv;
                }
                if (glyphs.Count > 0)
                {
                    ops.Add(new GlyphRunOp
                    {
                        Font = face,
                        FontSize = seg.FontSize,
                        X = x1,
                        Y = baseline,
                        Glyphs = glyphs,
                        Text = new string(ch, glyphs.Count),
                        Color = seg.Format.Color,
                    });
                }
            }
        }

        /// <summary>
        /// The leader of the tab stop a tab starting at <paramref name="rel"/> goes to: the first custom stop after it,
        /// as <see cref="LineFitter.TabWidth"/> chooses it (a hanging indent's implicit stop and default stops have none).
        /// </summary>
        private static TabLeader LeaderAt(ParagraphFormat pf, double rel)
        {
            const double MinStep = 0.01;
            TabStop? stop = null;
            foreach (TabStop ts in pf.TabStops)
            {
                if (ts.Alignment != TabAlignment.Bar && ts.Position > rel + MinStep && (stop is null || ts.Position < stop.Value.Position))
                {
                    stop = ts;
                }
            }
            if (stop is null || (pf.FirstLineIndent < 0 && pf.LeftIndent > rel + MinStep && pf.LeftIndent < stop.Value.Position))
            {
                return TabLeader.None;
            }
            return stop.Value.Leader;
        }

        private static void EmitDecorations(Segment seg, double x1, double x2, double baseline, List<LayoutOp> ops)
        {
            CharacterFormat f = seg.Format;
            if (seg.Font is null || x2 <= x1 || seg.Kind == SegmentKind.LineBreak || seg.Kind == SegmentKind.PageBreak)
            {
                return;
            }
            FontFace face = seg.Font.Face;
            double unit = seg.FontSize / face.UnitsPerEm;
            double by = baseline + seg.BaselineOffset;
            if (f.Underline != UnderlineStyle.None)
            {
                double thick = Math.Max(0.5, face.UnderlineThickness * unit);
                bool heavy = f.Underline is UnderlineStyle.Thick or UnderlineStyle.DottedHeavy or UnderlineStyle.DashHeavy
                    or UnderlineStyle.DashLongHeavy or UnderlineStyle.DotDashHeavy or UnderlineStyle.DotDotDashHeavy or UnderlineStyle.WavyHeavy;
                if (heavy)
                {
                    thick *= 2;
                }
                double uy = by - (face.UnderlinePosition * unit) + (thick / 2);
                LineDash dash = f.Underline switch
                {
                    UnderlineStyle.Dotted or UnderlineStyle.DottedHeavy => LineDash.Dot,
                    UnderlineStyle.Dash or UnderlineStyle.DashHeavy => LineDash.Dash,
                    UnderlineStyle.DashLong or UnderlineStyle.DashLongHeavy => LineDash.LongDash,
                    UnderlineStyle.DotDash or UnderlineStyle.DotDashHeavy => LineDash.DashDot,
                    UnderlineStyle.DotDotDash or UnderlineStyle.DotDotDashHeavy => LineDash.DashDotDot,
                    _ => LineDash.Solid,
                };
                LayoutColor color = f.UnderlineColor ?? f.Color;
                ops.Add(new StrokeLineOp(x1, uy, x2, uy, thick, color, dash));
                if (f.Underline is UnderlineStyle.Double or UnderlineStyle.WavyDouble)
                {
                    ops.Add(new StrokeLineOp(x1, uy + (thick * 2), x2, uy + (thick * 2), thick, color, dash));
                }
            }
            if (f.Strikethrough || f.DoubleStrikethrough)
            {
                double thick = Math.Max(0.5, face.StrikeoutSize * unit);
                double sy = by - (face.StrikeoutPosition * unit) + (thick / 2);
                if (f.DoubleStrikethrough)
                {
                    ops.Add(new StrokeLineOp(x1, sy - thick, x2, sy - thick, thick, f.Color));
                    ops.Add(new StrokeLineOp(x1, sy + thick, x2, sy + thick, thick, f.Color));
                }
                else
                {
                    ops.Add(new StrokeLineOp(x1, sy, x2, sy, thick, f.Color));
                }
            }
        }

        private static void EmitImage(Segment seg, LayoutContext ctx, double x, double baseline, List<LayoutOp> ops, List<LayoutBox> boxes, string path)
        {
            if (seg.TextBox is TextBoxInline tb)
            {
                LayoutRect box = new LayoutRect(x, baseline - tb.Height, tb.Width, tb.Height);
                EmitInlineTextBox(tb.Box, box, ctx, ops, boxes, path);
                return;
            }
            ImageInline img = seg.Image!;
            LayoutRect r = new LayoutRect(x, baseline - img.Height, img.Width, img.Height);
            EmitImageOrPlaceholder(img.Image, r, ctx, ops, path, img.Crop);
            boxes.Add(new LayoutBox(LayoutBoxKind.Image, r, path));
        }

        /// <summary>Field results inside an inline text box: the cached results (the box is laid out with its line).</summary>
        private sealed class CachedFieldValues : IFieldValues
        {
            public static readonly CachedFieldValues Instance = new CachedFieldValues();

            public string Evaluate(FieldInline field) => field.CachedResult ?? string.Empty;
        }

        /// <summary>
        /// An inline text box: fill, its paragraphs inside the insets (clipped to the box), outline. Tables inside an
        /// inline text box are not laid out (a warning says so); floating text boxes lay out tables too.
        /// </summary>
        internal static void EmitInlineTextBox(FlowTextBox box, LayoutRect r, LayoutContext ctx, List<LayoutOp> ops, List<LayoutBox> boxes, string path)
        {
            EmitTextBoxFrame(box, r, ops, outline: false);
            double width = Math.Max(1, r.Width - box.InsetLeft - box.InsetRight);
            List<(MeasuredParagraph M, FittedLine Line, int Index, double Top)> lines = new List<(MeasuredParagraph, FittedLine, int, double)>();
            double y = 0;
            foreach (FlowBlock b in box.Blocks)
            {
                if (b is not FlowParagraph p)
                {
                    ctx.Warn(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.ContentLoss, "A table inside an inline text box is not laid out.", path);
                    continue;
                }
                MeasuredParagraph m = ParagraphMeasurer.Measure(p, ctx, CachedFieldValues.Instance);
                List<FittedLine> fitted = LineFitter.Fit(m, width, 36);
                y += p.Format.SpaceBefore;
                for (int i = 0; i < fitted.Count; i++)
                {
                    lines.Add((m, fitted[i], i, y));
                    y += fitted[i].Height;
                }
                y += p.Format.SpaceAfter;
            }
            double top = TextBoxContentTop(box, r, y);
            ops.Add(new PushClipOp(r));
            StringBuilder ignore = new StringBuilder();
            List<LayoutBox> noBoxes = new List<LayoutBox>();
            foreach ((MeasuredParagraph m, FittedLine line, int index, double ly) in lines)
            {
                EmitLine(new PlacedLine { Paragraph = m, Line = line, LineIndex = index, AreaLeft = r.X + box.InsetLeft, AreaWidth = width, Top = top + ly, Path = path }, ctx, ops, noBoxes, ignore);
            }
            ops.Add(PopClipOp.Instance);
            EmitTextBoxFrame(box, r, ops, outline: true);
            boxes.Add(new LayoutBox(LayoutBoxKind.TextBox, r, path));
        }

        /// <summary>Top of a text box's content for its vertical alignment.</summary>
        internal static double TextBoxContentTop(FlowTextBox box, LayoutRect r, double contentHeight)
        {
            double inner = r.Height - box.InsetTop - box.InsetBottom;
            return box.VerticalAlignment switch
            {
                TextBoxVerticalAlignment.Center => r.Y + box.InsetTop + ((inner - contentHeight) / 2),
                TextBoxVerticalAlignment.Bottom => r.Y + box.InsetTop + inner - contentHeight,
                _ => r.Y + box.InsetTop,
            };
        }

        /// <summary>A text box's fill (before its text) or outline (after it).</summary>
        internal static void EmitTextBoxFrame(FlowTextBox box, LayoutRect r, List<LayoutOp> ops, bool outline)
        {
            if (!outline)
            {
                if (box.Fill is LayoutColor fill && !fill.IsTransparent)
                {
                    ops.Add(new FillRectOp(r, fill));
                }
                return;
            }
            if (box.Line is LayoutColor line && !line.IsTransparent && box.LineWidth > 0)
            {
                double w = box.LineWidth;
                ops.Add(new StrokeLineOp(r.X, r.Y, r.Right, r.Y, w, line));
                ops.Add(new StrokeLineOp(r.Right, r.Y, r.Right, r.Bottom, w, line));
                ops.Add(new StrokeLineOp(r.Right, r.Bottom, r.X, r.Bottom, w, line));
                ops.Add(new StrokeLineOp(r.X, r.Bottom, r.X, r.Y, w, line));
            }
        }

        internal static void EmitImageOrPlaceholder(LayoutImage image, LayoutRect r, LayoutContext ctx, List<LayoutOp> ops, string path, ImageCrop? crop = null)
        {
            if (image.Format is LayoutImageFormat.Png or LayoutImageFormat.Jpeg or LayoutImageFormat.Gif or LayoutImageFormat.Bmp)
            {
                ops.Add(new ImageOp(image, r, crop?.ToSourceRect(image)));
                return;
            }
            ctx.Warn(LayoutWarningCodes.ImagePlaceholder, LayoutWarningSeverity.ContentLoss,
                $"{image.Format} images are not rendered; a placeholder box is drawn at the declared size.", path);
            LayoutColor grey = new LayoutColor(0xA0, 0xA0, 0xA0);
            ops.Add(new FillRectOp(r, new LayoutColor(0xF2, 0xF2, 0xF2)));
            ops.Add(new StrokeLineOp(r.X, r.Y, r.Right, r.Y, 0.5, grey));
            ops.Add(new StrokeLineOp(r.Right, r.Y, r.Right, r.Bottom, 0.5, grey));
            ops.Add(new StrokeLineOp(r.Right, r.Bottom, r.X, r.Bottom, 0.5, grey));
            ops.Add(new StrokeLineOp(r.X, r.Bottom, r.X, r.Y, 0.5, grey));
            ops.Add(new StrokeLineOp(r.X, r.Y, r.Right, r.Bottom, 0.5, grey));
            ops.Add(new StrokeLineOp(r.X, r.Bottom, r.Right, r.Y, 0.5, grey));
        }
    }
}
