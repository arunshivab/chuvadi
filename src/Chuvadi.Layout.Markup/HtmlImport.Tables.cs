using System;
using System.Collections.Generic;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Engine;
using Chuvadi.Layout.Model;
using Chuvadi.Markup.Html;

namespace Chuvadi.Layout.Markup
{
    internal sealed partial class HtmlImport
    {
        /// <summary>A table's cells placed on its grid (HTML's table model: row groups, col/rowspan).</summary>
        private sealed class Grid
        {
            public readonly List<GridRow> Rows = new List<GridRow>();
            public readonly List<HtmlElement> Captions = new List<HtmlElement>();
            public int Columns;
        }

        private sealed class GridRow
        {
            public HtmlElement? Tr;
            public HtmlElement? Group;
            public bool Header;
            public readonly List<GridCell> Cells = new List<GridCell>();
        }

        private sealed class GridCell
        {
            public required HtmlElement Element;
            public int Row;
            public int Column;
            public int ColSpan;
            public int RowSpan;
        }

        private readonly Dictionary<HtmlElement, Grid> _grids = new Dictionary<HtmlElement, Grid>(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<HtmlElement, (double Min, double Max)> _measures = new Dictionary<HtmlElement, (double, double)>(ReferenceEqualityComparer.Instance);

        private Grid BuildGrid(HtmlElement table)
        {
            if (_grids.TryGetValue(table, out Grid? cached))
            {
                return cached;
            }
            Grid g = new Grid();
            List<GridRow> head = new List<GridRow>(), body = new List<GridRow>(), foot = new List<GridRow>();
            foreach (HtmlElement child in table.Children)
            {
                if (child.Namespace != HtmlNamespace.Html)
                {
                    continue;
                }
                switch (child.LocalName)
                {
                    case "caption":
                        g.Captions.Add(child);
                        break;
                    case "thead":
                        AddRows(child, head, true);
                        break;
                    case "tbody":
                        AddRows(child, body, false);
                        break;
                    case "tfoot":
                        AddRows(child, foot, false);
                        break;
                    case "tr":
                        body.Add(new GridRow { Tr = child });
                        break;
                }
            }
            g.Rows.AddRange(head);
            g.Rows.AddRange(body);
            g.Rows.AddRange(foot);

            // Place the cells: each cell takes the first free column, and its spans block the columns below and to the right.
            HashSet<long> taken = new HashSet<long>();
            int total = g.Rows.Count;
            for (int r = 0; r < total; r++)
            {
                GridRow row = g.Rows[r];
                if (row.Tr is null)
                {
                    continue;
                }
                int col = 0;
                foreach (HtmlElement cell in row.Tr.Children)
                {
                    if (cell.Namespace != HtmlNamespace.Html || cell.LocalName is not ("td" or "th"))
                    {
                        continue;
                    }
                    while (taken.Contains(((long)r << 20) | (uint)col))
                    {
                        col++;
                    }
                    int colSpan = Span(cell.GetAttribute("colspan"), 1, 1000, 1);
                    int rowSpan = Span(cell.GetAttribute("rowspan"), 0, 65534, 1);
                    if (rowSpan == 0)
                    {
                        rowSpan = total - r;
                    }
                    rowSpan = Math.Max(1, Math.Min(rowSpan, total - r));
                    Limits.Check(nameof(LayoutLimits.MaxCellsPerSheet), _lo.Limits.MaxCellsPerSheet, (long)(col + colSpan) * total);
                    for (int dr = 0; dr < rowSpan; dr++)
                    {
                        for (int dc = 0; dc < colSpan; dc++)
                        {
                            taken.Add(((long)(r + dr) << 20) | (uint)(col + dc));
                        }
                    }
                    row.Cells.Add(new GridCell { Element = cell, Row = r, Column = col, ColSpan = colSpan, RowSpan = rowSpan });
                    col += colSpan;
                    g.Columns = Math.Max(g.Columns, col);
                }
            }
            _grids[table] = g;
            return g;
        }

        private static void AddRows(HtmlElement group, List<GridRow> into, bool header)
        {
            foreach (HtmlElement tr in group.Children)
            {
                if (tr.Namespace == HtmlNamespace.Html && tr.LocalName == "tr")
                {
                    into.Add(new GridRow { Tr = tr, Group = group, Header = header });
                }
            }
        }

        private static int Span(string? value, int min, int max, int fallback)
        {
            if (value is null)
            {
                return fallback;
            }
            string v = value.Trim();
            int end = 0;
            while (end < v.Length && char.IsDigit(v[end]))
            {
                end++;
            }
            return end > 0 && int.TryParse(v.AsSpan(0, Math.Min(end, 9)), out int n) ? Math.Clamp(n, min, max) : fallback;
        }

        // ---- Styles along the table's element chain ----

        private HtmlStyle RowGroupStyle(GridRow row, HtmlStyle table, double avail) =>
            row.Group is HtmlElement g ? Style(g, table, avail) : table;

        private HtmlStyle RowStyle(GridRow row, HtmlStyle table, double avail) =>
            row.Tr is HtmlElement tr ? Style(tr, RowGroupStyle(row, table, avail), avail) : table;

        private static double CellPadding(HtmlElement table, HtmlStyle tableStyle)
        {
            if (HtmlCss.TryAttributeLength(table.GetAttribute("cellpadding"), null, out double p, out _))
            {
                return p;
            }
            return 1 * HtmlCss.PxToPt;
        }

        private static int TableBorder(HtmlElement table)
        {
            string? b = table.GetAttribute("border");
            if (b is null)
            {
                return 0;
            }
            return b.Trim().Length == 0 ? 1 : Span(b, 0, 100, 1);
        }

        // ---- Measuring (an approximation of the CSS automatic table layout) ----

        private static double CharWidth(char ch, CharacterFormat f)
        {
            if (ch >= 0x2E80 && ch < 0xFFEF)
            {
                return f.FontSize;
            }
            double w = f.FontSize * (f.GenericFamily == Fonts.GenericFontFamily.Monospace ? 0.6 : 0.5);
            return f.Bold ? w * 1.05 : w;
        }

        private struct MeasureState
        {
            public double Line;
            public double Word;
            public double Min;
            public double Max;
            public bool Space;

            public void EndLine()
            {
                Max = Math.Max(Max, Line);
                Line = 0;
                Word = 0;
                Space = true;
            }
        }

        /// <summary>The minimum and preferred widths of an element's content, in points.</summary>
        private (double Min, double Max) MeasureContent(HtmlElement e, HtmlStyle s, double avail, int depth)
        {
            if (_measures.TryGetValue(e, out (double, double) cached))
            {
                return cached;
            }
            MeasureState m = new MeasureState { Space = true };
            Measure(e, s, avail, ref m, depth);
            m.EndLine();
            (double, double) result = (m.Min, Math.Max(m.Min, m.Max));
            _measures[e] = result;
            return result;
        }

        private void Measure(HtmlNode node, HtmlStyle s, double avail, ref MeasureState m, int depth)
        {
            for (HtmlNode? child = node.FirstChild; child is not null; child = child.NextSibling)
            {
                _ct.ThrowIfCancellationRequested();
                if (child is HtmlText t)
                {
                    if (s.Char.FontSize <= 0)
                    {
                        continue;
                    }
                    bool nowrap = s.WhiteSpace is WhiteSpaceMode.NoWrap or WhiteSpaceMode.Pre;
                    foreach (char ch in t.Data)
                    {
                        if (ch == '\n' && (s.Preserves || s.WhiteSpace == WhiteSpaceMode.PreLine))
                        {
                            m.EndLine();
                            continue;
                        }
                        if (ch is ' ' or '\t' or '\n' or '\r' or '\f')
                        {
                            if (!m.Space || s.Preserves)
                            {
                                m.Line += s.Char.FontSize * 0.25;
                                if (!nowrap)
                                {
                                    m.Word = 0;
                                }
                                else
                                {
                                    m.Word += s.Char.FontSize * 0.25;
                                }
                            }
                            m.Space = true;
                            continue;
                        }
                        double w = CharWidth(ch, s.Char);
                        m.Line += w;
                        m.Word = ch >= 0x2E80 && !nowrap ? w : m.Word + w;
                        m.Min = Math.Max(m.Min, m.Word);
                        m.Space = false;
                    }
                    continue;
                }
                if (child is not HtmlElement e || e.Namespace != HtmlNamespace.Html || s_skipped.Contains(e.LocalName))
                {
                    continue;
                }
                HtmlStyle cs = Style(e, s, avail);
                if (cs.Hidden || cs.Display == HtmlDisplay.None)
                {
                    continue;
                }
                switch (e.LocalName)
                {
                    case "br":
                        m.EndLine();
                        continue;
                    case "img":
                    {
                        (double w, _) = ImageSize(e, cs, ImageData(e), avail);
                        bool percent = cs.Width is string sw && sw.EndsWith('%');
                        if (cs.Display != HtmlDisplay.Inline)
                        {
                            m.EndLine();
                        }
                        m.Line += w;
                        m.Word += w;
                        m.Min = Math.Max(m.Min, percent ? 0 : m.Word);
                        m.Space = false;
                        if (cs.Display != HtmlDisplay.Inline)
                        {
                            m.EndLine();
                        }
                        continue;
                    }
                    case "table":
                    {
                        m.EndLine();
                        (double tmin, double tmax) = MeasureTable(e, cs, avail, depth + 1);
                        double extra = cs.MarginLeft + cs.MarginRight;
                        m.Min = Math.Max(m.Min, tmin + extra);
                        m.Max = Math.Max(m.Max, tmax + extra);
                        m.EndLine();
                        continue;
                    }
                }
                if (cs.Display == HtmlDisplay.Inline)
                {
                    Measure(e, cs, avail, ref m, depth);
                    continue;
                }
                m.EndLine();
                double side = cs.MarginLeft + cs.MarginRight + (cs.PaddingLeft ?? 0) + (cs.PaddingRight ?? 0)
                    + (cs.BorderLeft.IsVisible ? cs.BorderLeft.Width : 0) + (cs.BorderRight.IsVisible ? cs.BorderRight.Width : 0);
                MeasureState inner = new MeasureState { Space = true };
                Measure(e, cs, avail, ref inner, depth);
                inner.EndLine();
                double min = inner.Min, max = Math.Max(inner.Min, inner.Max);
                if (cs.Width is string bw && !bw.EndsWith('%') && HtmlCss.TryLength(bw, cs.Char.FontSize, _s.BodySize, null, out double fixedW) && fixedW > 0)
                {
                    min = Math.Max(min, fixedW);
                    max = Math.Max(min, fixedW);
                }
                m.Min = Math.Max(m.Min, min + side);
                m.Max = Math.Max(m.Max, max + side);
                m.EndLine();
            }
        }

        /// <summary>Per-column minimum, preferred, fixed and percentage widths of a table.</summary>
        private sealed class ColumnInfo
        {
            public double[] Min = Array.Empty<double>();
            public double[] Max = Array.Empty<double>();
            public double[] Fixed = Array.Empty<double>();
            public double[] Percent = Array.Empty<double>();
            public double Spacing;
        }

        private readonly Dictionary<HtmlElement, ColumnInfo> _columns = new Dictionary<HtmlElement, ColumnInfo>(ReferenceEqualityComparer.Instance);

        private ColumnInfo Columns(HtmlElement table, HtmlStyle ts, double avail, int depth)
        {
            if (_columns.TryGetValue(table, out ColumnInfo? cached))
            {
                return cached;
            }
            Grid g = BuildGrid(table);
            int n = g.Columns;
            ColumnInfo info = new ColumnInfo { Min = new double[n], Max = new double[n], Fixed = new double[n], Percent = new double[n] };
            info.Spacing = CellSpacing(table);
            double pad = CellPadding(table, ts);
            int border = TableBorder(table);
            List<(GridCell Cell, double Min, double Max)> spanning = new List<(GridCell, double, double)>();
            foreach (GridRow row in g.Rows)
            {
                HtmlStyle rs = RowStyle(row, ts, avail);
                if (rs.Hidden || rs.Display == HtmlDisplay.None)
                {
                    continue;
                }
                foreach (GridCell cell in row.Cells)
                {
                    HtmlStyle cs = Style(cell.Element, rs, avail);
                    if (cs.Hidden || cs.Display == HtmlDisplay.None)
                    {
                        continue;
                    }
                    (double min, double max) = depth > _lo.Limits.MaxNestingDepth ? (0, 0) : MeasureContent(cell.Element, cs, avail, depth);
                    double edges = (cs.PaddingLeft ?? pad) + (cs.PaddingRight ?? pad) + (border > 0 ? 1.5 : 0)
                        + (cs.BorderLeft.IsVisible ? cs.BorderLeft.Width : 0) + (cs.BorderRight.IsVisible ? cs.BorderRight.Width : 0);
                    min += edges;
                    max += edges;
                    double fixedWidth = 0, percent = 0;
                    if (cs.Width is string w)
                    {
                        if (w.EndsWith('%'))
                        {
                            HtmlCss.TryLength(w, cs.Char.FontSize, _s.BodySize, 100, out percent);
                        }
                        else if (HtmlCss.TryLength(w, cs.Char.FontSize, _s.BodySize, null, out double fw) && fw > 0)
                        {
                            fixedWidth = fw + (cs.PaddingLeft ?? pad) + (cs.PaddingRight ?? pad);
                            max = Math.Max(min, fixedWidth);
                        }
                    }
                    if (cell.ColSpan == 1)
                    {
                        int c = cell.Column;
                        info.Min[c] = Math.Max(info.Min[c], min);
                        info.Max[c] = Math.Max(info.Max[c], max);
                        info.Fixed[c] = Math.Max(info.Fixed[c], fixedWidth);
                        info.Percent[c] = Math.Max(info.Percent[c], percent);
                    }
                    else
                    {
                        spanning.Add((cell, min, max));
                    }
                }
            }
            // A spanning cell widens the columns it covers evenly when they are too narrow for it.
            foreach ((GridCell cell, double min, double max) in spanning)
            {
                double haveMin = 0, haveMax = 0;
                for (int c = cell.Column; c < cell.Column + cell.ColSpan && c < n; c++)
                {
                    haveMin += info.Min[c];
                    haveMax += info.Max[c];
                }
                double gaps = info.Spacing * (cell.ColSpan - 1);
                for (int c = cell.Column; c < cell.Column + cell.ColSpan && c < n; c++)
                {
                    if (min - gaps > haveMin)
                    {
                        info.Min[c] += (min - gaps - haveMin) / cell.ColSpan;
                    }
                    if (max - gaps > haveMax)
                    {
                        info.Max[c] += (max - gaps - haveMax) / cell.ColSpan;
                    }
                    info.Max[c] = Math.Max(info.Max[c], info.Min[c]);
                }
            }
            _columns[table] = info;
            return info;
        }

        private static double CellSpacing(HtmlElement table)
        {
            if (HtmlCss.TryAttributeLength(table.GetAttribute("cellspacing"), null, out double s, out _))
            {
                return s;
            }
            return TableBorder(table) > 0 ? 2 * HtmlCss.PxToPt : 0;
        }

        private (double Min, double Max) MeasureTable(HtmlElement table, HtmlStyle ts, double avail, int depth)
        {
            ColumnInfo info = Columns(table, ts, avail, depth);
            double min = 0, max = 0;
            for (int c = 0; c < info.Min.Length; c++)
            {
                min += info.Min[c];
                max += Math.Max(info.Max[c], info.Fixed[c]);
            }
            double spacing = info.Spacing * (info.Min.Length + 1);
            min += spacing;
            max += spacing;
            if (ts.Width is string w && !w.EndsWith('%') && HtmlCss.TryLength(w, ts.Char.FontSize, _s.BodySize, null, out double fixedW) && fixedW > 0)
            {
                max = Math.Max(min, fixedW);
                min = Math.Max(min, fixedW);
            }
            return (min, max);
        }

        /// <summary>Column widths for an available width: the table's width, then minimums, fixed and percentage columns, then the rest by preference.</summary>
        private double[] ColumnWidths(HtmlElement table, HtmlStyle ts, double avail, int depth)
        {
            ColumnInfo info = Columns(table, ts, avail, depth);
            int n = info.Min.Length;
            double spacing = info.Spacing * (n + 1);
            double room = Math.Max(n * 3, avail - spacing);
            double sumMin = 0, sumMax = 0;
            for (int c = 0; c < n; c++)
            {
                sumMin += info.Min[c];
                sumMax += Math.Max(info.Max[c], info.Fixed[c]);
            }
            double target;
            if (ts.Width is string w && HtmlCss.TryLength(w, ts.Char.FontSize, _s.BodySize, avail, out double tw) && tw > 0)
            {
                target = Math.Min(Math.Max(tw - spacing, sumMin), room);
            }
            else
            {
                target = Math.Min(room, Math.Max(sumMax, sumMin));
            }
            double[] widths = new double[n];
            bool[] auto = new bool[n];
            double used = 0;
            for (int c = 0; c < n; c++)
            {
                widths[c] = info.Min[c];
                if (info.Percent[c] > 0)
                {
                    widths[c] = Math.Max(widths[c], info.Percent[c] * target / 100);
                }
                else if (info.Fixed[c] > 0)
                {
                    widths[c] = Math.Max(widths[c], info.Fixed[c]);
                }
                else
                {
                    auto[c] = true;
                }
                used += widths[c];
            }
            double rest = target - used;
            if (rest > 0.01)
            {
                // Auto columns grow toward their preferred width.
                double want = 0;
                for (int c = 0; c < n; c++)
                {
                    if (auto[c])
                    {
                        want += Math.Max(0, info.Max[c] - widths[c]);
                    }
                }
                if (want > 0)
                {
                    double give = Math.Min(rest, want);
                    for (int c = 0; c < n; c++)
                    {
                        if (auto[c])
                        {
                            widths[c] += give * Math.Max(0, info.Max[c] - widths[c]) / want;
                        }
                    }
                    rest -= give;
                }
            }
            if (rest > 0.01)
            {
                // Any space left (a table wider than its content) goes to the auto columns, else to all.
                double basis = 0;
                int count = 0;
                for (int c = 0; c < n; c++)
                {
                    if (auto[c])
                    {
                        basis += widths[c];
                        count++;
                    }
                }
                bool all = count == 0;
                if (all)
                {
                    basis = 0;
                    for (int c = 0; c < n; c++)
                    {
                        basis += widths[c];
                    }
                }
                for (int c = 0; c < n; c++)
                {
                    if (all || auto[c])
                    {
                        widths[c] += basis > 0 ? rest * widths[c] / basis : rest / (all ? n : count);
                    }
                }
            }
            double total = 0;
            for (int c = 0; c < n; c++)
            {
                total += widths[c];
            }
            if (total > room && total > 0)
            {
                for (int c = 0; c < n; c++)
                {
                    widths[c] *= room / total;
                }
            }
            for (int c = 0; c < n; c++)
            {
                widths[c] = Math.Max(widths[c], 1);
            }
            return widths;
        }

        // ---- Building ----

        private void Table(HtmlElement table, HtmlStyle ts, Ctx c, Container k)
        {
            EndParagraph(k);
            FlushLabel(k);
            int depth = c.TableDepth + 1;
            double avail = Math.Max(36, k.Width - Cap(c.Left, k.Width) - Cap(c.Right, k.Width));
            Grid g = BuildGrid(table);
            Ctx tc = c with { Style = ts };
            if (depth > _lo.Limits.MaxNestingDepth)
            {
                _warn.Add(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.Fidelity,
                    "Tables nested deeper than LayoutLimits.MaxNestingDepth are laid out as paragraphs (their content is kept).");
                Flatten(g, ts, tc, k);
                return;
            }
            if (g.Columns == 1 && (depth > 1 || ContainsTable(table)))
            {
                // A one-column table nested in a table, or wrapping other tables, is page layout (mail wrappers and
                // containers): its cells become blocks, so long content splits across pages like any text.
                FlattenLayout(table, ts, g, c, k);
                return;
            }
            foreach (HtmlElement caption in g.Captions)
            {
                HtmlStyle cs = Style(caption, ts, avail);
                if (!cs.Hidden && cs.Display != HtmlDisplay.None)
                {
                    Block(caption, cs, tc, k);
                }
            }
            if (g.Columns == 0 || g.Rows.Count == 0)
            {
                return;
            }
            k.Pending = Math.Max(k.Pending, ts.MarginTop);
            if (ts.BreakBefore)
            {
                k.BreakNext = true;
            }
            double[] widths = ColumnWidths(table, ts, avail, depth);
            double pad = CellPadding(table, ts);
            int border = TableBorder(table);
            double spacing = CellSpacing(table);
            LayoutColor ruleColor = _s.RuleColor;
            BorderLine cellLine = border > 0 ? new BorderLine(0.75, ruleColor) : BorderLine.None;
            BorderLine outer = border > 0 ? new BorderLine(border * HtmlCss.PxToPt, ruleColor) : BorderLine.None;
            bool centred = (ts.MarginLeftAuto && ts.MarginRightAuto) || c.Style.LegacyCenter;
            double total = spacing * (widths.Length + 1);
            foreach (double w in widths)
            {
                total += w;
            }
            FlowTable ft = new FlowTable
            {
                Format = new Model.TableFormat
                {
                    Indent = Cap(c.Left, k.Width) + (centred ? 0 : ts.MarginLeft),
                    Alignment = centred ? ParagraphAlignment.Center : ParagraphAlignment.Left,
                    CellMarginLeft = pad,
                    CellMarginRight = pad,
                    CellMarginTop = pad,
                    CellMarginBottom = pad,
                    CellSpacing = spacing,
                    BorderTop = ts.BorderTop.IsVisible ? ts.BorderTop : outer,
                    BorderBottom = ts.BorderBottom.IsVisible ? ts.BorderBottom : outer,
                    BorderLeft = ts.BorderLeft.IsVisible ? ts.BorderLeft : outer,
                    BorderRight = ts.BorderRight.IsVisible ? ts.BorderRight : outer,
                    BorderInsideHorizontal = cellLine,
                    BorderInsideVertical = cellLine,
                },
            };
            foreach (double w in widths)
            {
                ft.ColumnWidths.Add(w);
            }
            LayoutColor? tableBg = _o.PrintBackgrounds ? ts.Background : null;
            // Cells covered by a rowspan from above: (row, column) → the spanning cell.
            Dictionary<long, GridCell> covered = new Dictionary<long, GridCell>();
            for (int r = 0; r < g.Rows.Count; r++)
            {
                GridRow row = g.Rows[r];
                HtmlStyle rs = RowStyle(row, ts, avail);
                FlowTableRow fr = new FlowTableRow { IsHeader = row.Header };
                if (rs.Hidden || rs.Display == HtmlDisplay.None)
                {
                    continue;
                }
                if (TryHeight(rs, out double rowHeight))
                {
                    fr.Height = rowHeight;
                    fr.HeightRule = RowHeightRule.AtLeast;
                }
                LayoutColor? rowBg = _o.PrintBackgrounds ? rs.Background ?? (row.Group is not null ? RowGroupStyle(row, ts, avail).Background : null) ?? tableBg : null;
                Dictionary<int, GridCell> starts = new Dictionary<int, GridCell>();
                foreach (GridCell cell in row.Cells)
                {
                    starts[cell.Column] = cell;
                }
                int col = 0;
                while (col < g.Columns)
                {
                    if (starts.TryGetValue(col, out GridCell? cell))
                    {
                        HtmlStyle cs = Style(cell.Element, rs, avail);
                        double width = 0;
                        for (int i = col; i < col + cell.ColSpan && i < widths.Length; i++)
                        {
                            width += widths[i];
                        }
                        width += spacing * (cell.ColSpan - 1);
                        FlowTableCell fc = Cell(cell, cs, rs, c, depth, width, pad, rowBg, border > 0 ? cellLine : BorderLine.None);
                        if (fc.Blocks.Count == 0 || cs.Hidden || cs.Display == HtmlDisplay.None)
                        {
                            fc.Blocks.Clear();
                            fc.Blocks.Add(EmptyCellParagraph(cs));
                        }
                        fr.Cells.Add(fc);
                        if (cell.RowSpan > 1)
                        {
                            for (int dr = 1; dr < cell.RowSpan; dr++)
                            {
                                covered[((long)(r + dr) << 20) | (uint)col] = cell;
                            }
                        }
                        // A row height set on a cell applies to the row.
                        if (TryHeight(cs, out double cellHeight) && cell.RowSpan == 1 && cellHeight > fr.Height)
                        {
                            fr.Height = cellHeight;
                            fr.HeightRule = RowHeightRule.AtLeast;
                        }
                        col += cell.ColSpan;
                    }
                    else if (covered.TryGetValue(((long)r << 20) | (uint)col, out GridCell? above))
                    {
                        FlowTableCell placeholder = new FlowTableCell { ColumnSpan = above.ColSpan, VerticalMerge = VerticalMerge.Continue };
                        placeholder.Blocks.Add(new FlowParagraph { MarkFormat = ts.Char.FontSize > 0 ? ts.Char : ts.Char with { FontSize = 1 } });
                        fr.Cells.Add(placeholder);
                        col += above.ColSpan;
                    }
                    else
                    {
                        // A missing cell: nothing is drawn there.
                        FlowTableCell empty = new FlowTableCell
                        {
                            BorderTop = BorderLine.None,
                            BorderBottom = BorderLine.None,
                            BorderLeft = BorderLine.None,
                            BorderRight = BorderLine.None,
                            Background = rowBg,
                        };
                        empty.Blocks.Add(EmptyCellParagraph(rs));
                        fr.Cells.Add(empty);
                        col++;
                    }
                }
                ft.Rows.Add(fr);
            }
            if (ft.Rows.Count == 0)
            {
                return;
            }
            // Margins collapse around the table as around any block.
            if (k.Blocks.Count > 0 && k.Blocks[^1] is FlowParagraph prev)
            {
                prev.Format = prev.Format with { SpaceAfter = Math.Max(prev.Format.SpaceAfter, k.Pending) };
            }
            else if (k.Pending > 0 && !(k.IsBody && k.Blocks.Count == 0))
            {
                k.Blocks.Add(new FlowParagraph
                {
                    Format = new ParagraphFormat { LineSpacingRule = LineSpacingRule.Exact, LineSpacing = k.Pending },
                    MarkFormat = new CharacterFormat { FontSize = 1 },
                });
            }
            k.Pending = 0;
            if (k.BreakNext)
            {
                k.Blocks.Add(new FlowParagraph
                {
                    Format = new ParagraphFormat { PageBreakBefore = true, LineSpacingRule = LineSpacingRule.Exact, LineSpacing = 1 },
                    MarkFormat = new CharacterFormat { FontSize = 1 },
                });
                k.BreakNext = false;
            }
            k.Blocks.Add(ft);
            k.Pending = ts.MarginBottom;
            if (ts.BreakAfter)
            {
                k.BreakNext = true;
            }
        }

        private static FlowParagraph EmptyCellParagraph(HtmlStyle s)
        {
            if (s.Char.FontSize > 1)
            {
                return new FlowParagraph { MarkFormat = s.Char };
            }
            // font-size 0 cells (mail spacers) take no line height.
            return new FlowParagraph
            {
                Format = new ParagraphFormat { LineSpacingRule = LineSpacingRule.Exact, LineSpacing = 0.75 },
                MarkFormat = s.Char with { FontSize = 1 },
            };
        }

        private FlowTableCell Cell(GridCell cell, HtmlStyle cs, HtmlStyle rs, Ctx c, int depth, double width, double pad, LayoutColor? rowBg, BorderLine attrBorder)
        {
            FlowTableCell fc = new FlowTableCell
            {
                ColumnSpan = cell.ColSpan,
                VerticalMerge = cell.RowSpan > 1 ? VerticalMerge.Restart : VerticalMerge.None,
                VerticalAlignment = cs.VAlign ?? rs.VAlign ?? CellVerticalAlignment.Center,
                Background = _o.PrintBackgrounds ? cs.Background ?? rowBg : null,
                MarginLeft = cs.PaddingLeft,
                MarginRight = cs.PaddingRight,
                MarginTop = cs.PaddingTop,
                MarginBottom = cs.PaddingBottom,
            };
            if (cs.BorderTop.IsVisible)
            {
                fc.BorderTop = cs.BorderTop;
            }
            if (cs.BorderBottom.IsVisible)
            {
                fc.BorderBottom = cs.BorderBottom;
            }
            if (cs.BorderLeft.IsVisible)
            {
                fc.BorderLeft = cs.BorderLeft;
            }
            if (cs.BorderRight.IsVisible)
            {
                fc.BorderRight = cs.BorderRight;
            }
            if (cs.Hidden || cs.Display == HtmlDisplay.None)
            {
                return fc;
            }
            double inner = Math.Max(6, width - (cs.PaddingLeft ?? pad) - (cs.PaddingRight ?? pad));
            Container ck = new Container(fc.Blocks, inner, false);
            Ctx cc = new Ctx(cs, 0, 0, BorderLine.None, BorderLine.None, null, null, 0, depth);
            Walk(cell.Element, cc, ck);
            EndParagraph(ck);
            FlushLabel(ck);
            return fc;
        }

        private static bool ContainsTable(HtmlElement table)
        {
            foreach (HtmlElement e in table.DescendantElements())
            {
                if (e.LocalName == "table" && e.Namespace == HtmlNamespace.Html)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// A layout table as blocks: the table's width and centring become indents, its background and borders those of
        /// the region, and each cell a block with the cell's padding, background and alignment.
        /// </summary>
        private void FlattenLayout(HtmlElement table, HtmlStyle ts, Grid g, Ctx c, Container k)
        {
            double avail = Math.Max(36, k.Width - Cap(c.Left, k.Width) - Cap(c.Right, k.Width));
            HtmlStyle region = ts.Clone();
            region.Display = HtmlDisplay.Block;
            double width = avail;
            if (ts.Width is string w && HtmlCss.TryLength(w, ts.Char.FontSize, _s.BodySize, avail, out double tw) && tw > 0 && tw < avail)
            {
                width = tw;
            }
            double free = avail - width;
            bool centred = (ts.MarginLeftAuto && ts.MarginRightAuto) || c.Style.LegacyCenter;
            region.MarginLeft = centred ? free / 2 : ts.MarginLeft;
            region.MarginRight = centred ? free / 2 : Math.Max(0, free - ts.MarginLeft);
            if (TableBorder(table) is int b && b > 0 && !ts.BorderTop.IsVisible)
            {
                for (int i = 0; i < 4; i++)
                {
                    region.BorderWidth[i] = b * HtmlCss.PxToPt;
                    region.BorderStyle[i] = "solid";
                    region.BorderColor[i] = _s.RuleColor;
                }
            }
            double pad = CellPadding(table, ts);
            BlockWith(table, region, c, k, (inner, kk) =>
            {
                foreach (HtmlElement caption in g.Captions)
                {
                    HtmlStyle cs = Style(caption, ts, avail);
                    if (!cs.Hidden && cs.Display != HtmlDisplay.None)
                    {
                        Block(caption, cs, inner, kk);
                    }
                }
                foreach (GridRow row in g.Rows)
                {
                    HtmlStyle rs = RowStyle(row, ts, avail);
                    if (rs.Hidden || rs.Display == HtmlDisplay.None)
                    {
                        continue;
                    }
                    LayoutColor? rowBg = rs.Background ?? (row.Group is not null ? RowGroupStyle(row, ts, avail).Background : null);
                    foreach (GridCell cell in row.Cells)
                    {
                        HtmlStyle cs = Style(cell.Element, rs, avail);
                        if (cs.Hidden || cs.Display == HtmlDisplay.None)
                        {
                            continue;
                        }
                        HtmlStyle box = cs.Clone();
                        box.Display = HtmlDisplay.Block;
                        box.PaddingTop ??= pad;
                        box.PaddingBottom ??= pad;
                        box.PaddingLeft ??= pad;
                        box.PaddingRight ??= pad;
                        box.Background ??= rowBg;
                        box.Height ??= rs.Height;
                        Block(cell.Element, box, inner, kk);
                    }
                }
            });
        }

        /// <summary>A table too deeply nested to lay out as a table: its cells become blocks, in reading order.</summary>
        private void Flatten(Grid g, HtmlStyle ts, Ctx c, Container k)
        {
            double avail = Math.Max(18, k.Width - c.Left - c.Right);
            foreach (GridRow row in g.Rows)
            {
                HtmlStyle rs = RowStyle(row, ts, avail);
                if (rs.Hidden || rs.Display == HtmlDisplay.None)
                {
                    continue;
                }
                foreach (GridCell cell in row.Cells)
                {
                    HtmlStyle cs = Style(cell.Element, rs, avail);
                    if (!cs.Hidden && cs.Display != HtmlDisplay.None)
                    {
                        EndParagraph(k);
                        Walk(cell.Element, c with { Style = cs }, k);
                        EndParagraph(k);
                    }
                }
            }
        }
    }
}
