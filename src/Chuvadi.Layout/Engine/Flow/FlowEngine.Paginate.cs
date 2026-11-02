using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.Engine.Flow
{
    internal sealed partial class FlowEngine
    {
        /// <summary>Horizontal text area of a section on a page (mirror margins and gutter applied).</summary>
        internal (double Left, double Width) TextArea(PageGeometry g, int pageNumber)
        {
            double left = g.MarginLeft;
            double right = g.MarginRight;
            if (g.MirrorMargins && pageNumber % 2 == 0)
            {
                (left, right) = (right, left);
            }
            if (!g.GutterAtTop)
            {
                if (g.MirrorMargins && pageNumber % 2 == 0)
                {
                    right += g.Gutter;
                }
                else
                {
                    left += g.Gutter;
                }
            }
            return (left, Math.Max(1, g.Width - left - right));
        }

        /// <summary>A floating object placed before the flow of a page because it overlaps text above its anchor.</summary>
        private sealed record Preseed(AnchoredObjectInline Anchor, LayoutRect Rect, string Path);

        /// <summary>
        /// Lays out one page from its checkpoint. Produces placements; ops are emitted separately.
        /// A floating object positioned relative to the page or margin can reach above its anchor paragraph; Word
        /// then reflows the text above it too. The page is laid out again with such objects placed first, as long as
        /// their anchors still land on the page (at most three extra passes).
        /// </summary>
        internal PagePlan Paginate(PageRecord record, CancellationToken ct)
        {
            List<Preseed>? preseed = null;
            for (int pass = 0; ; pass++)
            {
                PagePlan plan = PaginateOnce(record, ct, preseed, out List<Preseed> late, out HashSet<AnchoredObjectInline> reached);
                if (pass >= 3)
                {
                    return plan;
                }
                List<Preseed> next = new List<Preseed>();
                bool changed = false;
                if (preseed is not null)
                {
                    foreach (Preseed ps in preseed)
                    {
                        if (reached.Contains(ps.Anchor))
                        {
                            next.Add(ps);
                        }
                        else
                        {
                            changed = true;
                        }
                    }
                }
                foreach (Preseed l in late)
                {
                    next.Add(l);
                    changed = true;
                }
                if (!changed)
                {
                    return plan;
                }
                preseed = next;
            }
        }

        private PagePlan PaginateOnce(PageRecord record, CancellationToken ct, List<Preseed>? preseed, out List<Preseed> late, out HashSet<AnchoredObjectInline> reached)
        {
            late = new List<Preseed>();
            reached = new HashSet<AnchoredObjectInline>(ReferenceEqualityComparer.Instance);
            PagePlan plan = new PagePlan();
            FlowPosition pos = record.Start;
            int pageSection = Math.Min(pos.Section, _doc.Sections.Count - 1);
            PageGeometry pageGeom = _doc.Sections[pageSection].Page;
            plan.Section = pageSection;
            plan.Geometry = pageGeom;
            (double bodyTop, double bodyBottom) = BodyArea(record, pageGeom);
            plan.FootnoteBottom = bodyBottom;
            double y = bodyTop;
            bool first = true;
            PageStartReason reason = record.Reason;
            FlowParagraph? previousParagraph = null;
            // Vertical bands that text must avoid (top-and-bottom wrapping) and objects text flows beside.
            List<(double Top, double Bottom)> bands = new List<(double, double)>();
            List<WrapExclusion> exclusions = new List<WrapExclusion>();
            HashSet<AnchoredObjectInline> seeded = new HashSet<AnchoredObjectInline>(ReferenceEqualityComparer.Instance);
            if (preseed is not null)
            {
                foreach (Preseed ps in preseed)
                {
                    seeded.Add(ps.Anchor);
                    plan.Items.Add(Placed(ps.Anchor, ps.Rect, false, ps.Path));
                    AddWrap(ps.Anchor, ps.Rect, bands, exclusions, ps.Path);
                }
            }

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (pos.Section >= _doc.Sections.Count)
                {
                    plan.Finished = true;
                    plan.End = pos;
                    return plan;
                }
                FlowSection section = _doc.Sections[pos.Section];
                if (pos.Block >= section.Blocks.Count)
                {
                    int nextSection = pos.Section + 1;
                    if (nextSection >= _doc.Sections.Count)
                    {
                        plan.Finished = true;
                        plan.End = new FlowPosition(nextSection, 0, 0);
                        return plan;
                    }
                    FlowSection ns = _doc.Sections[nextSection];
                    pos = new FlowPosition(nextSection, 0, 0);
                    bool continuous = ns.Start == SectionStart.Continuous && ns.Page.SameSizeAs(pageGeom);
                    if (!continuous)
                    {
                        plan.End = pos;
                        plan.EndReason = PageStartReason.SectionBreak;
                        return plan;
                    }
                    continue;
                }

                (double areaLeft, double areaWidth) = TextArea(section.Page, record.PageNumber);
                FlowBlock block = section.Blocks[pos.Block];
                string path = (pos.Section + 1).ToString(CultureInfo.InvariantCulture) + "/" + (pos.Block + 1).ToString(CultureInfo.InvariantCulture);

                if (block is FlowTable table)
                {
                    y = SkipFloats(bands, exclusions, y, 1, path);
                    TablePlacement tp = PlaceTable(table, pos, plan, areaLeft, areaWidth, y, bodyTop, bodyBottom, first, path, record, ct);
                    if (tp.Placed)
                    {
                        first = false;
                    }
                    y = tp.Y;
                    previousParagraph = null;
                    if (tp.PageEnds)
                    {
                        plan.End = tp.Next;
                        plan.EndReason = PageStartReason.Flow;
                        return plan;
                    }
                    pos = tp.Next;
                    continue;
                }

                FlowParagraph p = (FlowParagraph)block;
                (MeasuredParagraph m, List<FittedLine> cached) = Measure(pos.Section, pos.Block, p, areaWidth);
                ParagraphFormat pf = p.Format;
                bool atStart = pos.Line == 0 && pos.TextStart is null;
                double spaceBefore = 0;
                if (atStart)
                {
                    if (pf.PageBreakBefore && !first)
                    {
                        plan.End = pos;
                        plan.EndReason = PageStartReason.PageBreak;
                        return plan;
                    }
                    spaceBefore = SpaceBefore(p, previousParagraph);
                    if (first && reason == PageStartReason.Flow)
                    {
                        spaceBefore = 0; // Word suppresses space before at the top of a page reached by normal flow.
                    }
                    if (!first && pf.KeepWithNext)
                    {
                        double chain = KeepChainHeight(pos, areaWidth, spaceBefore);
                        if (chain <= bodyBottom - bodyTop && y + chain > bodyBottom + Epsilon)
                        {
                            plan.End = pos;
                            plan.EndReason = PageStartReason.Flow;
                            return plan;
                        }
                    }
                }

                // Top and bottom borders occupy their width plus spacing (Word adds them to the paragraph's height).
                double borderTop = atStart && pf.BorderTop.IsVisible ? pf.BorderTop.Space + pf.BorderTop.Width : 0;
                double borderBottom = pf.BorderBottom.IsVisible ? pf.BorderBottom.Space + pf.BorderBottom.Width : 0;
                spaceBefore += borderTop;
                // Floating images anchored in this paragraph (placed with its first line).
                int bandsBefore = bands.Count;
                int exclusionsBefore = exclusions.Count;
                int itemsBefore = plan.Items.Count;
                List<Preseed> candidates = new List<Preseed>();
                if (atStart)
                {
                    PlaceAnchors(p, plan, bands, exclusions, seeded, candidates, pageGeom, areaLeft, areaWidth, y + spaceBefore, bodyTop, bodyBottom, path, record);
                }
                double yy = y + spaceBefore;

                // Lines and their tops. Text beside floating objects (or a paragraph continued from such text) is fitted
                // row by row; otherwise the cached full-width lines are used.
                List<FittedLine> lines;
                List<double> tops = new List<double>();
                int startLine;
                bool dynamic = pos.TextStart is not null;
                if (!dynamic)
                {
                    foreach (WrapExclusion e in exclusions)
                    {
                        if (e.Bottom > yy + Epsilon)
                        {
                            dynamic = true;
                            break;
                        }
                    }
                }
                if (dynamic)
                {
                    int startChar = pos.TextStart ?? cached[Math.Min(pos.Line, cached.Count - 1)].Start;
                    List<(FittedLine Line, double Top)> rows = FitAround(m, startChar, atStart, yy, areaLeft, areaWidth, bands, exclusions, bodyBottom);
                    lines = new List<FittedLine>(rows.Count);
                    foreach ((FittedLine l, double t) in rows)
                    {
                        lines.Add(l);
                        tops.Add(t);
                    }
                    startLine = 0;
                }
                else
                {
                    lines = cached;
                    startLine = pos.Line;
                    double cursor = yy;
                    for (int k2 = startLine; k2 < lines.Count; k2++)
                    {
                        double t = SkipBands(bands, cursor, lines[k2].Height);
                        if (t + lines[k2].Height > bodyBottom + Epsilon)
                        {
                            break;
                        }
                        tops.Add(t);
                        cursor = t + lines[k2].Height;
                    }
                }
                int total = lines.Count;
                int k = startLine;
                // A line with footnote references fits only if its notes fit at the foot of the page too.
                List<FlowNote> pendingNotes = new List<FlowNote>();
                double pendingNoteHeight = 0;
                while (k < total && k - startLine < tops.Count)
                {
                    double add = NewNoteHeight(m, lines[k], plan, pendingNotes, record, areaWidth, out List<FlowNote>? lineNotes);
                    if (tops[k - startLine] + lines[k].Height > bodyBottom - pendingNoteHeight - add + Epsilon)
                    {
                        break;
                    }
                    if (lineNotes is not null)
                    {
                        pendingNotes.AddRange(lineNotes);
                    }
                    pendingNoteHeight += add;
                    k++;
                }
                // Never split a row of pieces across pages.
                while (k > startLine && k < total && lines[k].SameRow)
                {
                    k--;
                }
                int fit = k - startLine;
                int remaining = total - startLine;
                int place;
                bool pageEnds;
                if (fit == remaining)
                {
                    place = fit;
                    pageEnds = false;
                }
                else
                {
                    if (pf.KeepLinesTogether && atStart && !first && ParagraphHeight(lines, startLine) <= bodyBottom - bodyTop)
                    {
                        RollBack(plan, bands, exclusions, itemsBefore, bandsBefore, exclusionsBefore);
                        plan.End = pos;
                        plan.EndReason = PageStartReason.Flow;
                        return plan;
                    }
                    place = fit;
                    if (pf.WidowControl && Rows(lines, startLine, total) >= 2)
                    {
                        if (atStart && Rows(lines, startLine, startLine + place) == 1)
                        {
                            place = 0; // orphan: do not leave the first line alone at the bottom
                        }
                        if (place > 0 && Rows(lines, startLine + place, total) == 1)
                        {
                            place = BackOneRow(lines, startLine, place); // widow: do not carry the last line alone to the next page
                            if (atStart && Rows(lines, startLine, startLine + place) == 1)
                            {
                                place = 0;
                            }
                        }
                    }
                    if (place == 0 && first)
                    {
                        place = Math.Max(1, fit);
                        while (startLine + place < total && lines[startLine + place].SameRow)
                        {
                            place++;
                        }
                        if (fit == 0 && lines[startLine].Height > bodyBottom - bodyTop)
                        {
                            _ctx.Warn(LayoutWarningCodes.ContentTallerThanPage, LayoutWarningSeverity.ContentLoss,
                                "A line is taller than the page body and is clipped.", path);
                        }
                    }
                    if (place == 0)
                    {
                        RollBack(plan, bands, exclusions, itemsBefore, bandsBefore, exclusionsBefore);
                        plan.End = pos;
                        plan.EndReason = PageStartReason.Flow;
                        return plan;
                    }
                    pageEnds = true;
                }

                // The paragraph's first line is on this page: its anchored objects are final.
                if (atStart)
                {
                    foreach (FlowInline inline in p.Inlines)
                    {
                        if (inline is AnchoredObjectInline anchored)
                        {
                            reached.Add(anchored);
                        }
                    }
                    if (!first)
                    {
                        late.AddRange(candidates);
                    }
                }

                // Place lines, honouring page breaks inside the paragraph.
                double lineTop = yy;
                double sliceTop = place > 0 && tops.Count > 0 ? tops[0] : lineTop;
                double sliceBottom = lineTop;
                int placedEnd = startLine + place;
                bool pageBreak = false;
                for (int li = startLine; li < placedEnd; li++)
                {
                    FittedLine line = lines[li];
                    lineTop = li - startLine < tops.Count ? tops[li - startLine] : sliceBottom;
                    plan.Items.Add(new PlacedLine
                    {
                        Paragraph = m,
                        Line = line,
                        LineIndex = li,
                        AreaLeft = areaLeft,
                        AreaWidth = areaWidth,
                        Top = lineTop,
                        Path = path + ":" + (li + 1).ToString(CultureInfo.InvariantCulture),
                    });
                    sliceBottom = Math.Max(sliceBottom, lineTop + line.Height);
                    if (line.EndsWithPageBreak)
                    {
                        placedEnd = li + 1;
                        pageBreak = true;
                        if (!dynamic && placedEnd == total - 1 && lines[placedEnd].Start >= m.Text.Length)
                        {
                            // Only the paragraph mark follows the page break: it stays with the break (Word 2013 and
                            // later, LibreOffice) instead of starting the next page with an empty line.
                            placedEnd = total;
                        }
                        break;
                    }
                }
                lineTop = sliceBottom;
                for (int li = startLine; li < placedEnd; li++)
                {
                    // Footnotes of the placed lines take their room at the foot of this page.
                    bodyBottom -= NewNoteHeight(m, lines[li], plan, null, record, areaWidth, out List<FlowNote>? placedNotes);
                    if (placedNotes is not null)
                    {
                        plan.Footnotes.AddRange(placedNotes);
                    }
                }
                bool lastSlice = placedEnd == total && (dynamic ? lines[total - 1].IsLastLine : true);
                plan.Items.Add(new PlacedParagraphSlice
                {
                    Paragraph = p,
                    Rect = new LayoutRect(areaLeft + pf.LeftIndent, sliceTop, Math.Max(0, areaWidth - pf.LeftIndent - pf.RightIndent), lineTop - sliceTop),
                    IsFirstSlice = atStart,
                    IsLastSlice = lastSlice,
                    Path = path,
                });
                first = false;
                if (pageBreak)
                {
                    plan.End = lastSlice ? new FlowPosition(pos.Section, pos.Block + 1, 0) : Continue(pos, lines, placedEnd, dynamic);
                    plan.EndReason = PageStartReason.PageBreak;
                    return plan;
                }
                if (pageEnds)
                {
                    plan.End = Continue(pos, lines, placedEnd, dynamic);
                    plan.EndReason = PageStartReason.Flow;
                    return plan;
                }
                y = lineTop + borderBottom + SpaceAfter(p, NextParagraph(pos));
                previousParagraph = p;
                pos = new FlowPosition(pos.Section, pos.Block + 1, 0);
                if (y >= bodyBottom - Epsilon)
                {
                    plan.End = pos;
                    plan.EndReason = PageStartReason.Flow;
                    return plan;
                }
            }
        }

        /// <summary>Where a paragraph continues on the next page: a cached line index, or a character offset for text fitted around objects.</summary>
        private static FlowPosition Continue(FlowPosition pos, List<FittedLine> lines, int placedEnd, bool dynamic)
            => dynamic
                ? new FlowPosition(pos.Section, pos.Block, 0, null, lines[placedEnd].Start)
                : new FlowPosition(pos.Section, pos.Block, placedEnd);

        /// <summary>Number of rows (pieces sharing a row count once) in lines[from, to).</summary>
        private static int Rows(List<FittedLine> lines, int from, int to)
        {
            int n = 0;
            for (int i = from; i < to && i < lines.Count; i++)
            {
                if (!lines[i].SameRow)
                {
                    n++;
                }
            }
            return n;
        }

        /// <summary>Moves the split point back by one whole row.</summary>
        private static int BackOneRow(List<FittedLine> lines, int startLine, int place)
        {
            place--;
            while (place > 0 && lines[startLine + place].SameRow)
            {
                place--;
            }
            return place;
        }

        /// <summary>Moves a box of height h starting at y below any band it would overlap.</summary>
        private static double SkipBands(List<(double Top, double Bottom)> bands, double y, double h)
        {
            bool moved = true;
            int guard = 0;
            while (moved && guard++ < 64)
            {
                moved = false;
                foreach ((double top, double bottom) in bands)
                {
                    if (y < bottom - Epsilon && y + h > top + Epsilon)
                    {
                        y = bottom;
                        moved = true;
                    }
                }
            }
            return y;
        }

        private static void RollBack(PagePlan plan, List<(double Top, double Bottom)> bands, List<WrapExclusion> exclusions, int items, int bandCount, int exclusionCount)
        {
            if (exclusions.Count > exclusionCount)
            {
                exclusions.RemoveRange(exclusionCount, exclusions.Count - exclusionCount);
            }
            if (plan.Items.Count > items)
            {
                plan.Items.RemoveRange(items, plan.Items.Count - items);
            }
            if (bands.Count > bandCount)
            {
                bands.RemoveRange(bandCount, bands.Count - bandCount);
            }
        }

        /// <summary>
        /// Positions the paragraph's anchored images and records how they affect text. Objects already placed for this
        /// pass (<paramref name="seeded"/>) are skipped. Page- or margin-relative objects that reach above the
        /// paragraph are reported in <paramref name="candidates"/> so the page can be laid out again with them first.
        /// </summary>
        private void PlaceAnchors(FlowParagraph p, PagePlan plan, List<(double Top, double Bottom)> bands, List<WrapExclusion> exclusions,
            HashSet<AnchoredObjectInline> seeded, List<Preseed> candidates, PageGeometry page,
            double areaLeft, double areaWidth, double paragraphTop, double bodyTop, double bodyBottom, string path, PageRecord record)
        {
            foreach (FlowInline inline in p.Inlines)
            {
                if (inline is not AnchoredObjectInline a || seeded.Contains(a))
                {
                    continue;
                }
                (double baseX, double spanX) = a.HorizontalRelativeTo switch
                {
                    HorizontalAnchor.Page => (0.0, page.Width),
                    _ => (areaLeft, areaWidth),
                };
                (double baseY, double spanY) = a.VerticalRelativeTo switch
                {
                    VerticalAnchor.Page => (0.0, page.Height),
                    VerticalAnchor.Margin => (page.MarginTop, page.Height - page.MarginTop - page.MarginBottom),
                    _ => (paragraphTop, 0.0),
                };
                double x = a.HorizontalAlignment switch
                {
                    FloatAlignment.Start => baseX,
                    FloatAlignment.Center => baseX + ((spanX - a.Width) / 2),
                    FloatAlignment.End => baseX + spanX - a.Width,
                    _ => baseX + a.OffsetX,
                };
                double height = ObjectHeight(a, record);
                double yy = a.VerticalAlignment switch
                {
                    FloatAlignment.Start => baseY,
                    FloatAlignment.Center => baseY + ((spanY - height) / 2),
                    FloatAlignment.End => baseY + spanY - height,
                    _ => baseY + a.OffsetY,
                };
                LayoutRect rect = new LayoutRect(x, yy, a.Width, height);
                bool behind = a.Wrap == FloatWrap.BehindText;
                plan.Items.Add(Placed(a, rect, behind, path));
                AddWrap(a, rect, bands, exclusions, path);
                bool affectsText = a.Wrap is FloatWrap.TopAndBottom or FloatWrap.Square or FloatWrap.Tight or FloatWrap.Through;
                bool fixedPosition = a.VerticalRelativeTo is VerticalAnchor.Page or VerticalAnchor.Margin;
                if (affectsText && fixedPosition && rect.Y - a.DistanceTop < paragraphTop - Epsilon && rect.Bottom + a.DistanceBottom > bodyTop + Epsilon)
                {
                    candidates.Add(new Preseed(a, rect, path));
                }
            }
        }

        /// <summary>The plan item that draws a floating picture or text box.</summary>
        private static PlacedFloatingImage Placed(AnchoredObjectInline a, LayoutRect rect, bool behind, string path) => a switch
        {
            AnchoredImageInline img => new PlacedFloatingImage { Image = img.Image, Crop = img.Crop, Rect = rect, Behind = behind, Path = path },
            AnchoredTextBoxInline tb => new PlacedFloatingImage { TextBox = tb.Box, Rect = rect, Behind = behind, Path = path },
            _ => throw new ArgumentException("Unknown anchored object.", nameof(a)),
        };

        /// <summary>Height of a floating object: a text box that fits its text grows to hold it.</summary>
        private double ObjectHeight(AnchoredObjectInline a, PageRecord record)
            => a is AnchoredTextBoxInline { Box.AutoFit: true } tb ? Math.Max(a.Height, TextBoxNeeds(tb.Box, a.Width, record)) : a.Height;

        private static double ParagraphHeight(List<FittedLine> lines, int from)
        {
            double h = 0;
            for (int i = from; i < lines.Count; i++)
            {
                h += lines[i].Height;
            }
            return h;
        }

        private static double SpaceBefore(FlowParagraph p, FlowParagraph? previous)
        {
            ParagraphFormat f = p.Format;
            if (f.ContextualSpacing && previous is not null && f.StyleId is not null && f.StyleId == previous.Format.StyleId)
            {
                return 0;
            }
            if (previous is not null)
            {
                // Space between two paragraphs is the larger of the first's space after and the second's space before,
                // not their sum: the previous paragraph has already added its space after, so only the excess is added.
                return Math.Max(0, f.SpaceBefore - SpaceAfter(previous, p));
            }
            return f.SpaceBefore;
        }

        private static double SpaceAfter(FlowParagraph p, FlowParagraph? next)
        {
            ParagraphFormat f = p.Format;
            if (f.ContextualSpacing && next is not null && f.StyleId is not null && f.StyleId == next.Format.StyleId)
            {
                return 0;
            }
            return f.SpaceAfter;
        }

        private FlowParagraph? NextParagraph(FlowPosition pos)
        {
            FlowSection s = _doc.Sections[pos.Section];
            return pos.Block + 1 < s.Blocks.Count ? s.Blocks[pos.Block + 1] as FlowParagraph : null;
        }

        /// <summary>Height of a keep-with-next chain starting at pos: the chained paragraphs plus the first line of the one after.</summary>
        private double KeepChainHeight(FlowPosition pos, double width, double firstSpaceBefore)
        {
            FlowSection s = _doc.Sections[pos.Section];
            double h = firstSpaceBefore;
            int b = pos.Block;
            FlowParagraph? prev = null;
            int guard = 0;
            while (b < s.Blocks.Count && guard++ < 64)
            {
                if (s.Blocks[b] is not FlowParagraph p)
                {
                    break;
                }
                List<FittedLine> lines = Measure(pos.Section, b, p, width).Lines;
                if (b > pos.Block)
                {
                    h += SpaceBefore(p, prev);
                }
                if (!p.Format.KeepWithNext)
                {
                    h += lines.Count > 0 ? lines[0].Height : 0;
                    return h;
                }
                h += ParagraphHeight(lines, 0) + SpaceAfter(p, NextParagraph(new FlowPosition(pos.Section, b, 0)));
                prev = p;
                b++;
            }
            return h;
        }
    }
}
