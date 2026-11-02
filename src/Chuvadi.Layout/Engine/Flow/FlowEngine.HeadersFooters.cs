using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.Engine.Flow
{
    internal sealed partial class FlowEngine
    {
        /// <summary>Field values for one page: PAGE, NUMPAGES and SECTIONPAGES use that page's record.</summary>
        private sealed class PageFieldValues : IFieldValues
        {
            private readonly FlowEngine _engine;
            private readonly PageRecord _page;

            public PageFieldValues(FlowEngine engine, PageRecord page)
            {
                _engine = engine;
                _page = page;
            }

            /// <summary>A NUMPAGES or SECTIONPAGES value was computed before the page count was final.</summary>
            public bool UsedProvisionalCount { get; private set; }

            public string Evaluate(FieldInline field)
            {
                if ((field.Kind == FieldKind.NumPages || field.Kind == FieldKind.SectionPages) && !_engine.IsPageCountFinal)
                {
                    UsedProvisionalCount = true;
                }
                return _engine.EvaluateField(field, _page);
            }
        }

        /// <summary>A header or footer laid out for one page.</summary>
        private sealed class HeaderFooterLayout
        {
            public List<(MeasuredParagraph Paragraph, FittedLine Line, int Index, double Top)> Lines { get; } = new List<(MeasuredParagraph, FittedLine, int, double)>();
            public List<(TableLayout Table, double Top)> Tables { get; } = new List<(TableLayout, double)>();
            public double Height { get; set; }
            public bool Provisional { get; set; }
        }

        /// <summary>The header and footer blocks that apply to a page (OOXML inheritance from earlier sections).</summary>
        private (IList<FlowBlock>? Header, IList<FlowBlock>? Footer) PartsFor(PageRecord record)
        {
            HeaderFooterSet? flags = null;
            for (int s = record.Section; s >= 0 && flags is null; s--)
            {
                flags = _doc.Sections[s].HeadersFooters;
            }
            if (flags is null)
            {
                return (null, null);
            }
            bool first = flags.DifferentFirstPage && record.SectionPageIndex == 0;
            bool even = flags.DifferentOddEven && record.PageNumber % 2 == 0;
            IList<FlowBlock>? header = Resolve(record.Section, h => first ? h.FirstPageHeader : even ? h.EvenPageHeader : h.DefaultHeader);
            IList<FlowBlock>? footer = Resolve(record.Section, h => first ? h.FirstPageFooter : even ? h.EvenPageFooter : h.DefaultFooter);
            return (header, footer);
        }

        private IList<FlowBlock>? Resolve(int section, Func<HeaderFooterSet, IList<FlowBlock>?> pick)
        {
            for (int s = section; s >= 0; s--)
            {
                HeaderFooterSet? set = _doc.Sections[s].HeadersFooters;
                if (set is not null && pick(set) is IList<FlowBlock> blocks)
                {
                    return blocks;
                }
            }
            return null;
        }

        private HeaderFooterLayout LayoutHeaderFooter(IList<FlowBlock> blocks, double width, PageRecord record, string path, CancellationToken ct)
        {
            HeaderFooterLayout hf = new HeaderFooterLayout();
            PageFieldValues fields = new PageFieldValues(this, record);
            double y = 0;
            FlowParagraph? previous = null;
            for (int b = 0; b < blocks.Count; b++)
            {
                ct.ThrowIfCancellationRequested();
                if (blocks[b] is FlowTable table)
                {
                    TableLayout tl = LayoutTable(table, width, path + "/" + (b + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), 1, ct);
                    tl.Token = ct;
                    hf.Tables.Add((tl, y));
                    y += tl.TotalHeight;
                    previous = null;
                    continue;
                }
                FlowParagraph p = (FlowParagraph)blocks[b];
                MeasuredParagraph m = ParagraphMeasurer.Measure(p, _ctx, fields);
                List<FittedLine> lines = LineFitter.Fit(m, width, _doc.DefaultTabStop);
                y += SpaceBefore(p, previous) + BorderTopSpace(p.Format);
                for (int i = 0; i < lines.Count; i++)
                {
                    hf.Lines.Add((m, lines[i], i, y));
                    y += lines[i].Height;
                }
                y += BorderBottomSpace(p.Format);
                FlowParagraph? next = b + 1 < blocks.Count ? blocks[b + 1] as FlowParagraph : null;
                if (next is not null)
                {
                    y += SpaceAfter(p, next);
                }
                previous = p;
            }
            hf.Height = y;
            hf.Provisional = fields.UsedProvisionalCount;
            return hf;
        }

        /// <summary>
        /// Vertical extent of the body on a page. A header taller than the space between its distance and the top
        /// margin pushes the body down; a tall footer pushes the body bottom up (as Word does).
        /// </summary>
        internal (double Top, double Bottom) BodyArea(PageRecord record, PageGeometry g)
        {
            double top = g.MarginTop + (g.GutterAtTop ? g.Gutter : 0);
            double bottom = g.Height - g.MarginBottom;
            (IList<FlowBlock>? header, IList<FlowBlock>? footer) = PartsFor(record);
            (double _, double width) = TextArea(g, record.PageNumber);
            if (header is not null && header.Count > 0)
            {
                double h = LayoutHeaderFooter(header, width, record, "header", CancellationToken.None).Height;
                top = Math.Max(top, g.HeaderDistance + h);
            }
            if (footer is not null && footer.Count > 0)
            {
                double h = LayoutHeaderFooter(footer, width, record, "footer", CancellationToken.None).Height;
                bottom = Math.Min(bottom, g.Height - g.FooterDistance - h);
            }
            return (top, Math.Max(top + 1, bottom));
        }

        internal static double BorderTopSpace(ParagraphFormat f) => f.BorderTop.IsVisible ? f.BorderTop.Space + f.BorderTop.Width : 0;

        internal static double BorderBottomSpace(ParagraphFormat f) => f.BorderBottom.IsVisible ? f.BorderBottom.Space + f.BorderBottom.Width : 0;

        /// <summary>
        /// Emits paragraph shading and borders for consecutive lines (grouped by paragraph). The first group may be a
        /// continuation and the last may continue; <paramref name="firstStarts"/>/<paramref name="lastEnds"/> say whether
        /// those groups include the paragraph's first/last line.
        /// </summary>
        internal static void EmitDecorations(List<(MeasuredParagraph Paragraph, FittedLine Line, int Index, double Top)> lines, double left, double width,
            bool firstStarts, bool lastEnds, List<LayoutOp> ops)
        {
            int i = 0;
            while (i < lines.Count)
            {
                MeasuredParagraph m = lines[i].Paragraph;
                int j = i;
                while (j + 1 < lines.Count && ReferenceEquals(lines[j + 1].Paragraph, m))
                {
                    j++;
                }
                ParagraphFormat f = m.Source.Format;
                if ((f.Background is LayoutColor bg && !bg.IsTransparent) || f.BorderTop.IsVisible || f.BorderBottom.IsVisible || f.BorderLeft.IsVisible || f.BorderRight.IsVisible)
                {
                    double top = lines[i].Top;
                    double bottom = lines[j].Top + lines[j].Line.Height;
                    bool isFirst = lines[i].Index == 0;
                    bool isLast = lines[j].Line.IsLastLine || lines[j].Index == m.Text.Length;
                    FlowEmitter.EmitParagraphDecoration(new PlacedParagraphSlice
                    {
                        Paragraph = m.Source,
                        Rect = new LayoutRect(left + f.LeftIndent, top, Math.Max(0, width - f.LeftIndent - f.RightIndent), bottom - top),
                        IsFirstSlice = isFirst,
                        IsLastSlice = isLast,
                        Path = string.Empty,
                    }, ops);
                }
                i = j + 1;
            }
        }

        internal PageNumberFormat SectionNumberFormat(int section) => _doc.Sections[section].PageNumberFormat;

        internal void EmitHeadersFooters(PagePlan plan, PageRecord record, List<LayoutOp> ops, List<LayoutBox> boxes, ref bool provisional, CancellationToken ct)
        {
            if (record.Blank || _doc.Sections.Count == 0)
            {
                return;
            }
            PageGeometry g = _doc.Sections[record.Section].Page;
            (IList<FlowBlock>? header, IList<FlowBlock>? footer) = PartsFor(record);
            (double left, double width) = TextArea(g, record.PageNumber);
            StringBuilder ignore = new StringBuilder();
            if (header is not null && header.Count > 0)
            {
                HeaderFooterLayout hf = LayoutHeaderFooter(header, width, record, "header", ct);
                EmitHeaderFooter(hf, left, width, g.HeaderDistance, ops, ignore, "header", ct);
                boxes.Add(new LayoutBox(LayoutBoxKind.Header, new LayoutRect(left, g.HeaderDistance, width, hf.Height), "header"));
                provisional |= hf.Provisional;
            }
            if (footer is not null && footer.Count > 0)
            {
                HeaderFooterLayout hf = LayoutHeaderFooter(footer, width, record, "footer", ct);
                double top = g.Height - g.FooterDistance - hf.Height;
                EmitHeaderFooter(hf, left, width, top, ops, ignore, "footer", ct);
                boxes.Add(new LayoutBox(LayoutBoxKind.Footer, new LayoutRect(left, top, width, hf.Height), "footer"));
                provisional |= hf.Provisional;
            }
            if (provisional)
            {
                _ctx.Warn(LayoutWarningCodes.ProvisionalPageCount, LayoutWarningSeverity.Info,
                    "A page-count field was drawn with the estimated page count; call ResolvePageCount before rendering for exact values.");
            }
        }

        private void EmitHeaderFooter(HeaderFooterLayout hf, double left, double width, double top, List<LayoutOp> ops, StringBuilder text, string path, CancellationToken ct)
        {
            List<LayoutBox> noBoxes = new List<LayoutBox>();
            EmitDecorations(hf.Lines.ConvertAll(l => (l.Paragraph, l.Line, l.Index, top + l.Top)), left, width, true, true, ops);
            foreach ((MeasuredParagraph m, FittedLine line, int index, double y) in hf.Lines)
            {
                FlowEmitter.EmitLine(new PlacedLine { Paragraph = m, Line = line, LineIndex = index, AreaLeft = left, AreaWidth = width, Top = top + y, Path = path }, _ctx, ops, noBoxes, text);
            }
            foreach ((TableLayout table, double y) in hf.Tables)
            {
                List<PlacedRowFragment> rows = new List<PlacedRowFragment>();
                double ry = top + y;
                foreach (RowLayout r in table.Rows)
                {
                    rows.Add(Fragment(r, ry, r.Height, null, null));
                    ry += r.Height;
                }
                EmitTable(table, left + table.Offset, rows, ops, noBoxes, text, path, ct);
            }
        }
    }
}
