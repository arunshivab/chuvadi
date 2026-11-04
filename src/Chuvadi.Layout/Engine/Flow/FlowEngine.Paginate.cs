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
        /// <param name="Anchor">The anchored picture or text box (AnchoredObjectInline), or the floating table (FlowTable).</param>
        /// <param name="Rect">Where it goes (for a table, its top-left corner and width; the height comes from laying it out).</param>
        /// <param name="Path">Its path for warnings and boxes.</param>
        /// <param name="TablePos">For a floating table: its position in the flow.</param>
        /// <param name="AreaWidth">For a floating table: the width it was laid out in.</param>
        private sealed record Preseed(object Anchor, LayoutRect Rect, string Path, FlowPosition TablePos = default, double AreaWidth = 0);

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
                PagePlan plan = PaginateOnce(record, ct, preseed, out List<Preseed> late, out HashSet<object> reached);
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

        /// <summary>Why a column stopped taking content.</summary>
        private enum ColumnEnd
        {
            /// <summary>The column is full (or a keep rule moved content on): the next column, or the next page.</summary>
            Full,

            /// <summary>A column break: the next column, or the next page after the last column.</summary>
            ColumnBreak,

            /// <summary>A page break (or page-break-before): the next page.</summary>
            PageBreak,

            /// <summary>The next section starts on a new page.</summary>
            SectionBreak,

            /// <summary>The next section starts in the next column (w:type="nextColumn").</summary>
            NextColumnSection,

            /// <summary>The next section starts with a continuous break and a different column set: a new region below.</summary>
            ContinuousSection,

            /// <summary>The end of the document.</summary>
            Finished,

            /// <summary>Region only: every column of the region is used; the page ends.</summary>
            Overflow,
        }

        /// <summary>Mutable state of the page being laid out, shared by its regions and columns.</summary>
        private sealed class PageFill
        {
            public required PagePlan Plan { get; init; }
            public required List<(double Top, double Bottom)> Bands { get; init; }
            public required List<WrapExclusion> Exclusions { get; init; }
            public required HashSet<object> Seeded { get; init; }
            public required List<Preseed> Late { get; init; }
            public required HashSet<object> Reached { get; init; }

            /// <summary>Floating tables placed on the page: their boxes and whether they let others overlap them.</summary>
            public List<(LayoutRect Rect, bool AllowOverlap)> FloatingTables { get; init; } = new List<(LayoutRect, bool)>();
            public required PageRecord Record { get; init; }
            public required PageGeometry PageGeom { get; init; }
            public required double BodyTop { get; init; }

            /// <summary>Bottom of the body; footnotes placed on the page raise it.</summary>
            public double BodyBottom { get; set; }

            /// <summary>Nothing has been placed on the page yet.</summary>
            public bool FirstOnPage { get; set; } = true;

            /// <summary>A copy for a trial layout (column balancing): later changes to either do not affect the other.</summary>
            public PageFill Clone()
            {
                PagePlan p = new PagePlan { Section = Plan.Section, Geometry = Plan.Geometry, FootnoteBottom = Plan.FootnoteBottom };
                p.Items.AddRange(Plan.Items);
                p.Footnotes.AddRange(Plan.Footnotes);
                p.FootnoteAreas.AddRange(Plan.FootnoteAreas);
                return new PageFill
                {
                    Plan = p,
                    Bands = new List<(double, double)>(Bands),
                    Exclusions = new List<WrapExclusion>(Exclusions),
                    Seeded = new HashSet<object>(Seeded, ReferenceEqualityComparer.Instance),
                    Late = new List<Preseed>(Late),
                    Reached = new HashSet<object>(Reached, ReferenceEqualityComparer.Instance),
                    FloatingTables = new List<(LayoutRect, bool)>(FloatingTables),
                    Record = Record,
                    PageGeom = PageGeom,
                    BodyTop = BodyTop,
                    BodyBottom = BodyBottom,
                    FirstOnPage = FirstOnPage,
                };
            }
        }

        /// <summary>The end of one column: why, where the flow continues, and how far down it reached.</summary>
        /// <param name="End">Why the column ended.</param>
        /// <param name="Next">Where the flow continues.</param>
        /// <param name="Y">The flow position after the column's content (space after included).</param>
        /// <param name="ContentBottom">Bottom of the last line or table placed in the column.</param>
        /// <param name="Placed">Anything was placed in the column.</param>
        private readonly record struct ColumnResult(ColumnEnd End, FlowPosition Next, double Y, double ContentBottom, bool Placed);

        /// <summary>The end of one region (a set of columns between a top and a bottom).</summary>
        private sealed record RegionResult(ColumnEnd End, FlowPosition Next, PageStartReason NextReason, double Y, double[] ContentBottoms, bool[] Placed, int ColumnsUsed);

        /// <summary>Most columns a section can have (the schema allows at most 45 w:col elements).</summary>
        internal const int MaxColumns = 45;

        /// <summary>Width of the line between columns (Word draws about 0.75 pt).</summary>
        internal const double ColumnSeparatorWidth = 0.75;

        /// <summary>The columns of a section on a page, in absolute page coordinates (one entry for a one-column section).</summary>
        internal List<(double Left, double Width)> ColumnsOf(FlowSection s, int sectionIndex, int pageNumber)
        {
            (double left, double width) = TextArea(s.Page, pageNumber);
            List<(double Left, double Width)> cols = new List<(double, double)>();
            string where = "section " + (sectionIndex + 1).ToString(CultureInfo.InvariantCulture);
            if (s.ColumnWidths.Count > 0)
            {
                double x = left;
                int n = Math.Min(s.ColumnWidths.Count, MaxColumns);
                if (s.ColumnWidths.Count > MaxColumns)
                {
                    _ctx.Warn(LayoutWarningCodes.ColumnTooNarrow, LayoutWarningSeverity.Fidelity,
                        $"Section {sectionIndex + 1} has {s.ColumnWidths.Count} columns; the first {MaxColumns} are laid out.", where);
                }
                for (int i = 0; i < n; i++)
                {
                    FlowColumn c = s.ColumnWidths[i];
                    double w = c.Width;
                    if (!(w >= 1))
                    {
                        _ctx.Warn(LayoutWarningCodes.ColumnTooNarrow, LayoutWarningSeverity.Fidelity,
                            $"Column {i + 1} of section {sectionIndex + 1} is narrower than 1 pt; it is laid out 1 pt wide.", where);
                        w = 1;
                    }
                    cols.Add((x, w));
                    x += w + (c.SpaceAfter > 0 ? c.SpaceAfter : 0);
                }
                return cols;
            }
            int count = Math.Max(1, s.ColumnCount);
            if (count == 1)
            {
                cols.Add((left, width));
                return cols;
            }
            if (count > MaxColumns)
            {
                _ctx.Warn(LayoutWarningCodes.ColumnTooNarrow, LayoutWarningSeverity.Fidelity,
                    $"Section {sectionIndex + 1} has {count} columns; {MaxColumns} are laid out.", where);
                count = MaxColumns;
            }
            double gap = s.ColumnSpacing > 0 ? s.ColumnSpacing : 0;
            double cw = (width - (gap * (count - 1))) / count;
            if (!(cw >= 1))
            {
                _ctx.Warn(LayoutWarningCodes.ColumnTooNarrow, LayoutWarningSeverity.Fidelity,
                    $"The {count} columns of section {sectionIndex + 1} are narrower than 1 pt; they are laid out 1 pt wide.", where);
                cw = 1;
            }
            for (int i = 0; i < count; i++)
            {
                cols.Add((left + (i * (cw + gap)), cw));
            }
            return cols;
        }

        /// <summary>The column set of a section is the same as another's (a next-column section then stays in the region).</summary>
        private bool SameColumns(FlowSection a, int ai, FlowSection b, int bi, int pageNumber)
        {
            List<(double Left, double Width)> x = ColumnsOf(a, ai, pageNumber);
            List<(double Left, double Width)> y = ColumnsOf(b, bi, pageNumber);
            if (x.Count != y.Count)
            {
                return false;
            }
            for (int i = 0; i < x.Count; i++)
            {
                if (Math.Abs(x[i].Left - y[i].Left) > Epsilon || Math.Abs(x[i].Width - y[i].Width) > Epsilon)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Lays out one page: a stack of regions. A region is one column set from a top down to the foot of the body;
        /// text fills its columns left to right, then the page ends. A continuous section break with a different column
        /// set ends the region and starts a new one below it. A region ended that way is balanced first (Word's rule;
        /// the standard is silent): it gets the smallest height at which its content still fits its columns.
        /// </summary>
        private PagePlan PaginateOnce(PageRecord record, CancellationToken ct, List<Preseed>? preseed, out List<Preseed> late, out HashSet<object> reached)
        {
            late = new List<Preseed>();
            reached = new HashSet<object>(ReferenceEqualityComparer.Instance);
            PagePlan plan = new PagePlan();
            FlowPosition pos = record.Start;
            int pageSection = Math.Min(pos.Section, _doc.Sections.Count - 1);
            PageGeometry pageGeom = _doc.Sections[pageSection].Page;
            plan.Section = pageSection;
            plan.Geometry = pageGeom;
            (double bodyTop, double bodyBottom) = BodyArea(record, pageGeom);
            plan.FootnoteBottom = bodyBottom;
            PageFill f = new PageFill
            {
                Plan = plan,
                Bands = new List<(double, double)>(),
                Exclusions = new List<WrapExclusion>(),
                Seeded = new HashSet<object>(ReferenceEqualityComparer.Instance),
                Late = late,
                Reached = reached,
                Record = record,
                PageGeom = pageGeom,
                BodyTop = bodyTop,
                BodyBottom = bodyBottom,
            };
            if (preseed is not null)
            {
                foreach (Preseed ps in preseed)
                {
                    f.Seeded.Add(ps.Anchor);
                    if (ps.Anchor is FlowTable ft && ft.Floating is FloatingTablePosition fp)
                    {
                        // A table positioned on the page or margin that reaches above its paragraph: placed first, whole.
                        double seededTop = ClearOfFloatingTables(f, fp, ps.Rect.X, ps.Rect.Width, ps.Rect.Y, ps.Rect.Height);
                        PlaceFloatingTable(f, ft, fp, ps.TablePos, ps.Rect.X, ps.Rect.Width, ps.AreaWidth, seededTop, f.BodyTop, double.MaxValue, false, ps.Path, ct);
                        continue;
                    }
                    AnchoredObjectInline a = (AnchoredObjectInline)ps.Anchor;
                    plan.Items.Add(Placed(a, ps.Rect, false, ps.Path));
                    AddWrap(a, ps.Rect, f.Bands, f.Exclusions, ps.Path);
                }
            }

            double top = bodyTop;
            PageStartReason reason = record.Reason;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (pos.Section >= _doc.Sections.Count)
                {
                    plan.Finished = true;
                    plan.End = pos;
                    return plan;
                }
                int si = pos.Section;
                FlowSection section = _doc.Sections[si];
                List<(double Left, double Width)> cols = ColumnsOf(section, si, record.PageNumber);
                bool single = cols.Count == 1;
                double? limit = null;
                if (!single && section.BalanceColumns)
                {
                    limit = BalancedBottom(f, pos, reason, cols, top, ct);
                }
                RegionResult rr = FillRegion(f, pos, reason, cols, single, top, limit, ct);
                if (section.ColumnSeparator && !single)
                {
                    AddColumnSeparators(plan, cols, rr, top);
                }
                pos = rr.Next;
                switch (rr.End)
                {
                    case ColumnEnd.Finished:
                        plan.Finished = true;
                        plan.End = pos;
                        return plan;
                    case ColumnEnd.ContinuousSection:
                        top = rr.Y;
                        reason = PageStartReason.SectionBreak;
                        if (top >= f.BodyBottom - Epsilon)
                        {
                            plan.End = pos;
                            plan.EndReason = PageStartReason.Flow;
                            return plan;
                        }
                        continue;
                    case ColumnEnd.PageBreak:
                        plan.End = pos;
                        plan.EndReason = PageStartReason.PageBreak;
                        return plan;
                    case ColumnEnd.SectionBreak:
                        plan.End = pos;
                        plan.EndReason = PageStartReason.SectionBreak;
                        return plan;
                    default:
                        plan.End = pos;
                        plan.EndReason = rr.NextReason;
                        return plan;
                }
            }
        }

        /// <summary>
        /// The bottom that balances a region ended by a continuous section break: the smallest one (to 0.25 pt) at which
        /// all of its content still fits its columns. Null when the region does not end on this page that way.
        /// </summary>
        private double? BalancedBottom(PageFill f, FlowPosition pos, PageStartReason reason, List<(double Left, double Width)> cols, double top, CancellationToken ct)
        {
            RegionResult full = FillRegion(f.Clone(), pos, reason, cols, false, top, null, ct);
            if (full.End != ColumnEnd.ContinuousSection)
            {
                return null;
            }
            double hi = f.BodyBottom;
            double lo = top;
            while (hi - lo > 0.25)
            {
                ct.ThrowIfCancellationRequested();
                double mid = (lo + hi) / 2;
                RegionResult trial = FillRegion(f.Clone(), pos, reason, cols, false, top, mid, ct);
                if (trial.End == ColumnEnd.ContinuousSection)
                {
                    hi = mid;
                }
                else
                {
                    lo = mid;
                }
            }
            return hi;
        }

        /// <summary>Lines in the middle of the gaps before each column that has content, from the region top to the bottom of its longest column.</summary>
        private static void AddColumnSeparators(PagePlan plan, List<(double Left, double Width)> cols, RegionResult rr, double top)
        {
            double bottom = top;
            for (int c = 0; c < rr.ColumnsUsed; c++)
            {
                if (rr.Placed[c])
                {
                    bottom = Math.Max(bottom, rr.ContentBottoms[c]);
                }
            }
            if (bottom <= top + Epsilon)
            {
                return;
            }
            for (int c = 1; c < rr.ColumnsUsed; c++)
            {
                if (!rr.Placed[c])
                {
                    continue;
                }
                double x = (cols[c - 1].Left + cols[c - 1].Width + cols[c].Left) / 2;
                plan.Items.Add(new PlacedColumnRule { X = x, Top = top, Bottom = bottom });
            }
        }

        /// <summary>Fills the columns of one region in order.</summary>
        private RegionResult FillRegion(PageFill f, FlowPosition pos, PageStartReason reason, List<(double Left, double Width)> cols, bool single, double top,
            double? limit, CancellationToken ct)
        {
            double[] bottoms = new double[cols.Count];
            bool[] placed = new bool[cols.Count];
            double maxY = top;
            for (int c = 0; c < cols.Count; c++)
            {
                ColumnResult r = FillColumn(f, pos, reason, cols[c], single, top, limit, ct);
                bottoms[c] = r.ContentBottom;
                placed[c] = r.Placed;
                maxY = Math.Max(maxY, r.Y);
                pos = r.Next;
                switch (r.End)
                {
                    case ColumnEnd.Full:
                        reason = PageStartReason.Flow;
                        break;
                    case ColumnEnd.ColumnBreak:
                        reason = PageStartReason.PageBreak;
                        break;
                    case ColumnEnd.NextColumnSection:
                        int prev = Math.Max(0, pos.Section - 1);
                        if (single || pos.Section >= _doc.Sections.Count
                            || !SameColumns(_doc.Sections[prev], prev, _doc.Sections[pos.Section], pos.Section, f.Record.PageNumber))
                        {
                            // No next column of the same set on this page: the section starts on the next page.
                            return new RegionResult(ColumnEnd.SectionBreak, pos, PageStartReason.SectionBreak, maxY, bottoms, placed, c + 1);
                        }
                        reason = PageStartReason.SectionBreak;
                        break;
                    default:
                        return new RegionResult(r.End, pos, reason, maxY, bottoms, placed, c + 1);
                }
            }
            return new RegionResult(ColumnEnd.Overflow, pos, reason, maxY, bottoms, placed, cols.Count);
        }

        /// <summary>
        /// Fills one column (in a one-column section, the whole text area) from <paramref name="top"/> down to the foot of
        /// the body, or <paramref name="limit"/> when the region is balanced.
        /// </summary>
        private ColumnResult FillColumn(PageFill f, FlowPosition pos, PageStartReason reason, (double Left, double Width) column, bool single, double top,
            double? limit, CancellationToken ct)
        {
            PagePlan plan = f.Plan;
            PageRecord record = f.Record;
            PageGeometry pageGeom = f.PageGeom;
            List<(double Top, double Bottom)> bands = f.Bands;
            List<WrapExclusion> exclusions = f.Exclusions;
            double bodyTop = top;
            double bodyBottom = limit is double l0 ? Math.Min(l0, f.BodyBottom) : f.BodyBottom;
            // At the top of a column that starts at the top of the body, something must be placed (a fresh column cannot do better).
            bool canForce = top <= f.BodyTop + Epsilon && limit is null;
            double y = top;
            bool first = true;
            bool placedAny = false;
            double contentBottom = top;
            FlowParagraph? previousParagraph = null;
            HashSet<object> seeded = f.Seeded;

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (pos.Section >= _doc.Sections.Count)
                {
                    return new ColumnResult(ColumnEnd.Finished, pos, y, contentBottom, placedAny);
                }
                FlowSection section = _doc.Sections[pos.Section];
                if (pos.Block >= section.Blocks.Count)
                {
                    int nextSection = pos.Section + 1;
                    if (nextSection >= _doc.Sections.Count)
                    {
                        return new ColumnResult(ColumnEnd.Finished, new FlowPosition(nextSection, 0, 0), y, contentBottom, placedAny);
                    }
                    FlowSection ns = _doc.Sections[nextSection];
                    pos = new FlowPosition(nextSection, 0, 0);
                    bool sameSize = ns.Page.SameSizeAs(pageGeom);
                    if (ns.Start == SectionStart.Continuous && sameSize)
                    {
                        if (single && ColumnsOf(ns, nextSection, record.PageNumber).Count == 1)
                        {
                            continue; // one column on both sides: the flow simply continues
                        }
                        return new ColumnResult(ColumnEnd.ContinuousSection, pos, y, contentBottom, placedAny);
                    }
                    if (ns.Start == SectionStart.NextColumn && sameSize)
                    {
                        return new ColumnResult(ColumnEnd.NextColumnSection, pos, y, contentBottom, placedAny);
                    }
                    return new ColumnResult(ColumnEnd.SectionBreak, pos, y, contentBottom, placedAny);
                }

                // The margins' text area (for objects positioned relative to the margins) and the column.
                (double marginLeft, double marginWidth) = TextArea(section.Page, record.PageNumber);
                (double areaLeft, double areaWidth) = single ? (marginLeft, marginWidth) : column;
                FlowBlock block = section.Blocks[pos.Block];
                string path = (pos.Section + 1).ToString(CultureInfo.InvariantCulture) + "/" + (pos.Block + 1).ToString(CultureInfo.InvariantCulture);

                if (block is FlowTable floating && floating.Floating is FloatingTablePosition fpos)
                {
                    if (seeded.Contains(floating) && pos.Table is null)
                    {
                        f.Reached.Add(floating); // placed first on this page (it reaches above its paragraph)
                        pos = new FlowPosition(pos.Section, pos.Block + 1, 0);
                        continue;
                    }
                    bool pageWasEmpty = f.FirstOnPage;
                    FloatingTableResult fr = FloatTable(f, floating, fpos, pos, column, single, y, top, bodyBottom, first && canForce, path, ct);
                    if (fr.Placed)
                    {
                        first = false;
                        placedAny = true;
                        f.FirstOnPage = false;
                        contentBottom = Math.Max(contentBottom, fr.Bottom);
                    }
                    if (fr.Late is Preseed lateTable && !pageWasEmpty)
                    {
                        f.Late.Add(lateTable);
                    }
                    if (fr.ColumnEnds)
                    {
                        return new ColumnResult(ColumnEnd.Full, fr.Next, y, contentBottom, placedAny);
                    }
                    pos = fr.Next; // the text that follows flows beside the table: y does not move
                    continue;
                }

                if (block is FlowTable table)
                {
                    y = SkipFloats(bands, exclusions, y, 1, path);
                    TablePlacement tp = PlaceTable(table, pos, plan, areaLeft, areaWidth, y, bodyTop, bodyBottom, first && canForce, path, record, ct);
                    if (tp.Placed)
                    {
                        first = false;
                        placedAny = true;
                        f.FirstOnPage = false;
                        contentBottom = Math.Max(contentBottom, tp.Y);
                    }
                    y = tp.Y;
                    previousParagraph = null;
                    if (tp.PageEnds)
                    {
                        return new ColumnResult(ColumnEnd.Full, tp.Next, y, contentBottom, placedAny);
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
                    if (pf.PageBreakBefore && !f.FirstOnPage)
                    {
                        return new ColumnResult(ColumnEnd.PageBreak, pos, y, contentBottom, placedAny);
                    }
                    spaceBefore = SpaceBefore(p, previousParagraph);
                    if (first && reason == PageStartReason.Flow)
                    {
                        spaceBefore = 0; // Word suppresses space before at the top of a page (or column) reached by normal flow.
                    }
                    if (!(first && canForce) && pf.KeepWithNext)
                    {
                        double chain = KeepChainHeight(pos, areaWidth, spaceBefore);
                        if (chain <= bodyBottom - bodyTop && y + chain > bodyBottom + Epsilon)
                        {
                            return new ColumnResult(ColumnEnd.Full, pos, y, contentBottom, placedAny);
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
                    PlaceAnchors(p, plan, bands, exclusions, seeded, candidates, pageGeom, marginLeft, marginWidth, areaLeft, areaWidth,
                        y + spaceBefore, f.BodyTop, bodyBottom, path, record);
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
                    foreach ((FittedLine ln, double t) in rows)
                    {
                        lines.Add(ln);
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
                bool columnEnds;
                if (fit == remaining)
                {
                    place = fit;
                    columnEnds = false;
                }
                else
                {
                    if (pf.KeepLinesTogether && atStart && !(first && canForce) && ParagraphHeight(lines, startLine) <= bodyBottom - bodyTop)
                    {
                        RollBack(plan, bands, exclusions, itemsBefore, bandsBefore, exclusionsBefore);
                        return new ColumnResult(ColumnEnd.Full, pos, y, contentBottom, placedAny);
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
                    if (place == 0 && first && canForce)
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
                        return new ColumnResult(ColumnEnd.Full, pos, y, contentBottom, placedAny);
                    }
                    columnEnds = true;
                }

                // The paragraph's first line is on this page: its anchored objects are final.
                if (atStart)
                {
                    foreach (FlowInline inline in p.Inlines)
                    {
                        if (inline is AnchoredObjectInline anchored)
                        {
                            f.Reached.Add(anchored);
                        }
                    }
                    if (!f.FirstOnPage)
                    {
                        f.Late.AddRange(candidates);
                    }
                }

                // Place lines, honouring page and column breaks inside the paragraph.
                double lineTop = yy;
                double sliceTop = place > 0 && tops.Count > 0 ? tops[0] : lineTop;
                double sliceBottom = lineTop;
                int placedEnd = startLine + place;
                bool pageBreak = false;
                bool columnBreak = false;
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
                        columnBreak = line.EndsWithColumnBreak;
                        if (!columnBreak && !dynamic && placedEnd == total - 1 && lines[placedEnd].Start >= m.Text.Length)
                        {
                            // Only the paragraph mark follows the page break: it stays with the break (Word 2013 and later,
                            // LibreOffice) instead of starting the next page with an empty line. After a column break Word
                            // carries the mark to the next column as an empty line (calibration K2, K6), so it is not kept.
                            placedEnd = total;
                        }
                        break;
                    }
                }
                lineTop = sliceBottom;
                for (int li = startLine; li < placedEnd; li++)
                {
                    // Footnotes of the placed lines take their room at the foot of this page.
                    double noteRoom = NewNoteHeight(m, lines[li], plan, null, record, areaWidth, out List<FlowNote>? placedNotes);
                    if (noteRoom > 0)
                    {
                        f.BodyBottom -= noteRoom;
                        bodyBottom -= noteRoom;
                    }
                    if (placedNotes is not null)
                    {
                        plan.Footnotes.AddRange(placedNotes);
                        foreach (FlowNote _ in placedNotes)
                        {
                            plan.FootnoteAreas.Add(single ? null : (areaLeft, areaWidth));
                        }
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
                placedAny = true;
                f.FirstOnPage = false;
                contentBottom = Math.Max(contentBottom, lineTop);
                if (pageBreak)
                {
                    FlowPosition next = lastSlice ? new FlowPosition(pos.Section, pos.Block + 1, 0) : Continue(pos, lines, placedEnd, dynamic || !single);
                    return new ColumnResult(columnBreak ? ColumnEnd.ColumnBreak : ColumnEnd.PageBreak, next, lineTop, contentBottom, placedAny);
                }
                if (columnEnds)
                {
                    return new ColumnResult(ColumnEnd.Full, Continue(pos, lines, placedEnd, dynamic || !single), lineTop, contentBottom, placedAny);
                }
                y = lineTop + borderBottom + SpaceAfter(p, NextParagraph(pos));
                previousParagraph = p;
                pos = new FlowPosition(pos.Section, pos.Block + 1, 0);
                if (y >= bodyBottom - Epsilon)
                {
                    return new ColumnResult(ColumnEnd.Full, pos, y, contentBottom, placedAny);
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
            HashSet<object> seeded, List<Preseed> candidates, PageGeometry page,
            double areaLeft, double areaWidth, double columnLeft, double columnWidth, double paragraphTop, double bodyTop, double bodyBottom, string path, PageRecord record)
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
                    HorizontalAnchor.Margin => (areaLeft, areaWidth),
                    _ => (columnLeft, columnWidth), // the column the anchor paragraph is in (the margins in one column)
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
