using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.Engine.Flow
{
    /// <summary>Where a table continues on the next page: the row, and how far each of its cells got.</summary>
    internal sealed record TableContinuation(int Row, int[]? CellOffsets);

    /// <summary>Outcome of placing (part of) a table.</summary>
    internal readonly record struct TablePlacement(bool Placed, double Y, bool PageEnds, FlowPosition Next);

    /// <summary>Vertical content of a cell, in order.</summary>
    internal abstract class CellItem
    {
        /// <summary>Total height including gaps.</summary>
        public abstract double Height { get; }
    }

    /// <summary>A paragraph line inside a cell, with the paragraph's space before/after attached to its first/last line.</summary>
    internal sealed class CellLine : CellItem
    {
        public required MeasuredParagraph Paragraph { get; init; }
        public required FittedLine Line { get; init; }
        public required int LineIndex { get; init; }
        public required double GapBefore { get; init; }
        public required double GapAfter { get; init; }
        public required string Path { get; init; }

        public override double Height => GapBefore + Line.Height + GapAfter;
    }

    /// <summary>
    /// One row of a nested table inside a cell. A nested table is a run of these items, so the cell (and the outer
    /// row) can split between its rows across pages. A single nested row is not split.
    /// </summary>
    internal sealed class CellTableRow : CellItem
    {
        public required TableLayout Nested { get; init; }
        public required int RowIndex { get; init; }
        public required string Path { get; init; }

        public override double Height => Nested.Rows[RowIndex].Height;
    }

    internal sealed class CellLayout
    {
        public required FlowTableCell Cell { get; init; }
        public required int GridStart { get; init; }
        public required double X { get; init; }
        public required double Width { get; init; }
        public required double MarginLeft { get; init; }
        public required double MarginRight { get; init; }
        public required double MarginTop { get; init; }
        public required double MarginBottom { get; init; }
        public required List<CellItem> Items { get; init; }
        public required bool MergeContinue { get; init; }

        /// <summary>For a vertical-merge restart, the number of rows it covers.</summary>
        public int RowSpan { get; set; } = 1;

        public double ContentHeight
        {
            get
            {
                double h = 0;
                foreach (CellItem i in Items)
                {
                    h += i.Height;
                }
                return h;
            }
        }
    }

    internal sealed class RowLayout
    {
        public required FlowTableRow Row { get; init; }
        public required int Index { get; init; }
        public required List<CellLayout> Cells { get; init; }

        /// <summary>Height of the whole row (row-height rule and vertical merges applied).</summary>
        public double Height { get; set; }
    }

    /// <summary>
    /// A table measured for a width. Rows are measured on demand, in order, one row ahead of the row asked for (so
    /// a vertical merge ending at that row has already grown it); a long table's first page does not wait for the
    /// rest of the table.
    /// </summary>
    internal sealed class TableLayout
    {
        private readonly List<RowLayout> _rows = new List<RowLayout>();

        public required FlowTable Table { get; init; }
        public required double Width { get; init; }

        /// <summary>Offset of the table's left edge from the text area's left edge.</summary>
        public required double Offset { get; init; }

        public required double[] ColumnX { get; init; }
        public required int HeaderRowCount { get; init; }

        /// <summary>Measures the next row and appends it (set by the engine).</summary>
        internal required Action<List<RowLayout>, CancellationToken> BuildNext { get; init; }

        /// <summary>Cancellation for on-demand measuring, set by each caller before use.</summary>
        internal CancellationToken Token { get; set; }

        public int RowCount => Table.Rows.Count;

        /// <summary>Row <paramref name="index"/>, with its final height.</summary>
        public RowLayout Row(int index)
        {
            Ensure(Math.Min(RowCount, index + 2));
            return _rows[index];
        }

        /// <summary>Every row (measures the whole table).</summary>
        public IReadOnlyList<RowLayout> Rows
        {
            get
            {
                Ensure(RowCount);
                return _rows;
            }
        }

        public double TotalHeight
        {
            get
            {
                double h = 0;
                foreach (RowLayout r in Rows)
                {
                    h += r.Height;
                }
                return h;
            }
        }

        private void Ensure(int count)
        {
            while (_rows.Count < count)
            {
                BuildNext(_rows, Token);
            }
        }
    }

    /// <summary>A row (or part of one) placed on a page.</summary>
    internal sealed class PlacedRowFragment
    {
        public required RowLayout Row { get; init; }
        public required double Top { get; init; }
        public required double Height { get; init; }

        /// <summary>Per cell: the range of items drawn in this fragment.</summary>
        public required int[] From { get; init; }
        public required int[] To { get; init; }
        public required bool StartsRow { get; init; }
        public required bool EndsRow { get; init; }
    }

    /// <summary>A table slice placed on a page.</summary>
    internal sealed class PlacedTablePart : PlanItem
    {
        public required TableLayout Layout { get; init; }
        public required double Left { get; init; }
        public required List<PlacedRowFragment> Rows { get; init; }
        public required string Path { get; init; }
    }

    internal sealed partial class FlowEngine
    {
        private readonly Dictionary<(FlowTable, long), TableLayout> _tableCache = new Dictionary<(FlowTable, long), TableLayout>(new TableKeyComparer());

        private sealed class TableKeyComparer : IEqualityComparer<(FlowTable, long)>
        {
            public bool Equals((FlowTable, long) x, (FlowTable, long) y) => ReferenceEquals(x.Item1, y.Item1) && x.Item2 == y.Item2;

            public int GetHashCode((FlowTable, long) obj) => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Item1), obj.Item2);
        }

        /// <summary>Measures a table for an available width: grid columns, cells, items and row heights.</summary>
        internal TableLayout LayoutTable(FlowTable table, double areaWidth, string path, int depth, CancellationToken ct)
        {
            LayoutLimits.Check(nameof(LayoutLimits.MaxNestingDepth), _ctx.Limits.MaxNestingDepth, depth);
            long wkey = (long)Math.Round(areaWidth * 100);
            if (_tableCache.TryGetValue((table, wkey), out TableLayout? cached))
            {
                return cached;
            }
            TableFormat tf = table.Format;

            // Grid: given widths, or equal shares of the available width.
            int gridCount = table.ColumnWidths.Count;
            int maxCells = 0;
            foreach (FlowTableRow r in table.Rows)
            {
                int span = 0;
                foreach (FlowTableCell c in r.Cells)
                {
                    span += Math.Max(1, c.ColumnSpan);
                }
                maxCells = Math.Max(maxCells, span);
            }
            gridCount = Math.Max(gridCount, maxCells);
            double[] widths = new double[gridCount];
            double given = 0;
            int missing = 0;
            for (int i = 0; i < gridCount; i++)
            {
                double w = i < table.ColumnWidths.Count ? table.ColumnWidths[i] : 0;
                widths[i] = w;
                given += w;
                if (w <= 0)
                {
                    missing++;
                }
            }
            if (missing > 0)
            {
                double share = Math.Max(0, areaWidth - tf.Indent - given) / missing;
                for (int i = 0; i < gridCount; i++)
                {
                    if (widths[i] <= 0)
                    {
                        widths[i] = Math.Max(share, 12);
                    }
                }
            }
            double[] colX = new double[gridCount + 1];
            for (int i = 0; i < gridCount; i++)
            {
                colX[i + 1] = colX[i] + widths[i];
            }
            double tableWidth = colX[gridCount];
            double offset = tf.Alignment switch
            {
                ParagraphAlignment.Center => Math.Max(0, (areaWidth - tableWidth) / 2),
                ParagraphAlignment.Right => Math.Max(0, areaWidth - tableWidth),
                _ => tf.Indent,
            };

            int headerRows = 0;
            while (headerRows < table.Rows.Count && table.Rows[headerRows].IsHeader)
            {
                headerRows++;
            }
            Dictionary<int, CellLayout> openMerges = new Dictionary<int, CellLayout>();
            Dictionary<CellLayout, int> mergeStart = new Dictionary<CellLayout, int>(ReferenceEqualityComparer.Instance);

            // Measures row rows.Count. Vertical merges that end at the previous row (or at the last row) grow the
            // last spanned row when the merged content needs more room.
            void BuildNext(List<RowLayout> rows, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                int ri = rows.Count;
                FlowTableRow row = table.Rows[ri];
                List<CellLayout> cells = new List<CellLayout>();
                int g = 0;
                HashSet<int> continued = new HashSet<int>();
                for (int ci = 0; ci < row.Cells.Count; ci++)
                {
                    FlowTableCell cell = row.Cells[ci];
                    int span = Math.Max(1, cell.ColumnSpan);
                    int gEnd = Math.Min(gridCount, g + span);
                    double x = colX[Math.Min(g, gridCount)];
                    double w = colX[gEnd] - x;
                    double ml = cell.MarginLeft ?? tf.CellMarginLeft;
                    double mr = cell.MarginRight ?? tf.CellMarginRight;
                    double mt = cell.MarginTop ?? tf.CellMarginTop;
                    double mb = cell.MarginBottom ?? tf.CellMarginBottom;
                    // The horizontal border above a row takes room of its own: the content starts below it, so the row
                    // grows by the border's width (Word and LibreOffice; reference corpus d15). The last row also makes
                    // room for the table's bottom border.
                    BorderLine above = cell.BorderTop ?? (ri == 0 ? tf.BorderTop : tf.BorderInsideHorizontal);
                    if (above.IsVisible)
                    {
                        mt += above.Width;
                    }
                    if (ri == table.Rows.Count - 1 && (cell.BorderBottom ?? tf.BorderBottom) is BorderLine below && below.IsVisible)
                    {
                        mb += below.Width;
                    }
                    string cellPath = path + "/" + (ri + 1).ToString(CultureInfo.InvariantCulture) + "/" + (ci + 1).ToString(CultureInfo.InvariantCulture);
                    bool cont = cell.VerticalMerge == VerticalMerge.Continue && openMerges.ContainsKey(g);
                    List<CellItem> items = cont ? new List<CellItem>() : CellItems(cell.Blocks, Math.Max(1, w - ml - mr), cellPath, depth, token);
                    CellLayout cl = new CellLayout
                    {
                        Cell = cell,
                        GridStart = g,
                        X = x,
                        Width = w,
                        MarginLeft = ml,
                        MarginRight = mr,
                        MarginTop = mt,
                        MarginBottom = mb,
                        Items = items,
                        MergeContinue = cont,
                    };
                    if (cont)
                    {
                        openMerges[g].RowSpan++;
                        continued.Add(g);
                    }
                    cells.Add(cl);
                    g = gEnd;
                }
                // Merges not continued by this row ended at the previous row.
                List<int> ended = new List<int>();
                foreach (KeyValuePair<int, CellLayout> kv in openMerges)
                {
                    if (!continued.Contains(kv.Key))
                    {
                        ended.Add(kv.Key);
                    }
                }
                foreach (int key in ended)
                {
                    GrowMerge(rows, openMerges[key]);
                    openMerges.Remove(key);
                }
                foreach (CellLayout cl in cells)
                {
                    if (!cl.MergeContinue && cl.Cell.VerticalMerge == VerticalMerge.Restart)
                    {
                        openMerges[cl.GridStart] = cl;
                        mergeStart[cl] = ri;
                    }
                }
                RowLayout rl = new RowLayout { Row = row, Index = ri, Cells = cells };
                double content = 0;
                foreach (CellLayout cl in cells)
                {
                    if (!cl.MergeContinue && cl.Cell.VerticalMerge != VerticalMerge.Restart)
                    {
                        content = Math.Max(content, cl.ContentHeight + cl.MarginTop + cl.MarginBottom);
                    }
                    else
                    {
                        content = Math.Max(content, cl.MarginTop + cl.MarginBottom);
                    }
                }
                rl.Height = row.HeightRule switch
                {
                    RowHeightRule.Exact => row.Height,
                    RowHeightRule.AtLeast => Math.Max(row.Height, content),
                    _ => content,
                };
                if (rl.Height <= 0)
                {
                    rl.Height = Math.Max(content, 1);
                }
                rows.Add(rl);
                if (rows.Count == table.Rows.Count)
                {
                    foreach (CellLayout open in openMerges.Values)
                    {
                        GrowMerge(rows, open);
                    }
                    openMerges.Clear();
                }
            }

            void GrowMerge(List<RowLayout> rows, CellLayout cl)
            {
                int first = mergeStart[cl];
                int last = Math.Min(rows.Count - 1, first + cl.RowSpan - 1);
                double spanned = 0;
                for (int k = first; k <= last; k++)
                {
                    spanned += rows[k].Height;
                }
                double need = cl.ContentHeight + cl.MarginTop + cl.MarginBottom;
                if (last == table.Rows.Count - 1 && (cl.Cell.BorderBottom ?? tf.BorderBottom) is BorderLine below && below.IsVisible)
                {
                    need += below.Width; // a merge down to the last row also makes room for the table's bottom border
                }
                if (need > spanned && rows[last].Row.HeightRule != RowHeightRule.Exact)
                {
                    rows[last].Height += need - spanned;
                }
            }

            TableLayout layout = new TableLayout
            {
                Table = table,
                Width = tableWidth,
                Offset = offset,
                ColumnX = colX,
                HeaderRowCount = headerRows,
                BuildNext = BuildNext,
                Token = ct,
            };
            _tableCache[(table, wkey)] = layout;
            return layout;
        }

        private List<CellItem> CellItems(IList<FlowBlock> blocks, double width, string path, int depth, CancellationToken ct)
        {
            List<CellItem> items = new List<CellItem>();
            FlowParagraph? previous = null;
            for (int b = 0; b < blocks.Count; b++)
            {
                string bpath = path + "/" + (b + 1).ToString(CultureInfo.InvariantCulture);
                if (blocks[b] is FlowTable nested)
                {
                    TableLayout nl = LayoutTable(nested, width, bpath, depth + 1, ct);
                    nl.Token = ct;
                    for (int r = 0; r < nl.Rows.Count; r++)
                    {
                        items.Add(new CellTableRow { Nested = nl, RowIndex = r, Path = bpath });
                    }
                    previous = null;
                    continue;
                }
                FlowParagraph p = (FlowParagraph)blocks[b];
                (MeasuredParagraph m, List<FittedLine> lines) = Measure(-1, -1, p, width, bpath);
                FlowParagraph? next = b + 1 < blocks.Count ? blocks[b + 1] as FlowParagraph : null;
                double before = SpaceBefore(p, previous) + BorderTopSpace(p.Format);
                double after = SpaceAfter(p, next) + BorderBottomSpace(p.Format);
                for (int li = 0; li < lines.Count; li++)
                {
                    items.Add(new CellLine
                    {
                        Paragraph = m,
                        Line = lines[li],
                        LineIndex = li,
                        GapBefore = li == 0 ? before : 0,
                        GapAfter = li == lines.Count - 1 ? after : 0,
                        Path = bpath + ":" + (li + 1).ToString(CultureInfo.InvariantCulture),
                    });
                }
                previous = p;
            }
            return items;
        }

        private TablePlacement PlaceTable(FlowTable table, FlowPosition pos, PagePlan plan, double areaLeft, double areaWidth,
            double y, double bodyTop, double bodyBottom, bool first, string path, PageRecord record, CancellationToken ct)
        {
            TableLayout layout = LayoutTable(table, areaWidth, path, 1, ct);
            layout.Token = ct;
            FlowPosition after = new FlowPosition(pos.Section, pos.Block + 1, 0);
            if (layout.RowCount == 0)
            {
                return new TablePlacement(false, y, false, after);
            }
            TableContinuation cont = pos.Table ?? new TableContinuation(0, null);
            List<PlacedRowFragment> fragments = new List<PlacedRowFragment>();
            double yy = y;
            bool bodyPlaced = !first;
            bool continuing = cont.Row > 0 || cont.CellOffsets is not null;

            // Repeat header rows on continuation pages.
            if (continuing && layout.HeaderRowCount > 0 && cont.Row >= layout.HeaderRowCount)
            {
                for (int h = 0; h < layout.HeaderRowCount; h++)
                {
                    RowLayout hr = layout.Row(h);
                    if (yy + hr.Height > bodyBottom + Epsilon)
                    {
                        break;
                    }
                    fragments.Add(Fragment(hr, yy, hr.Height, null, null));
                    yy += hr.Height;
                }
            }

            for (int r = cont.Row; r < layout.RowCount; r++)
            {
                ct.ThrowIfCancellationRequested();
                RowLayout row = layout.Row(r);
                int[]? offsets = r == cont.Row ? cont.CellOffsets : null;
                double need = offsets is null ? row.Height : RemainingHeight(row, offsets);
                if (yy + need <= bodyBottom + Epsilon)
                {
                    fragments.Add(Fragment(row, yy, need, offsets, null));
                    yy += need;
                    bodyPlaced = true;
                    continue;
                }
                bool cantSplit = row.Row.CantSplit || row.Row.HeightRule == RowHeightRule.Exact || HasVerticalMerge(row);
                bool fitsOnFreshPage = need <= bodyBottom - bodyTop - HeaderHeight(layout, r);
                if (bodyPlaced && (cantSplit && fitsOnFreshPage || offsets is null && FitsNoItem(row, null, bodyBottom - yy)))
                {
                    return Finish(new TableContinuation(r, offsets));
                }
                if (cantSplit && bodyPlaced)
                {
                    return Finish(new TableContinuation(r, offsets));
                }
                // Split the row: each cell takes the items that fit.
                double room = bodyBottom - yy;
                int[] from = new int[row.Cells.Count];
                int[] to = new int[row.Cells.Count];
                bool any = false;
                bool done = true;
                double fragHeight = 0;
                for (int c = 0; c < row.Cells.Count; c++)
                {
                    CellLayout cl = row.Cells[c];
                    int start = offsets is null ? 0 : offsets[c];
                    from[c] = start;
                    double h = start == 0 ? cl.MarginTop : 0;
                    int k = start;
                    while (k < cl.Items.Count && h + cl.Items[k].Height <= room + Epsilon)
                    {
                        h += cl.Items[k].Height;
                        k++;
                    }
                    if (k == start && k < cl.Items.Count && !bodyPlaced)
                    {
                        // Force progress on a page with no body content yet.
                        h += cl.Items[k].Height;
                        k++;
                        _ctx.Warn(LayoutWarningCodes.ContentTallerThanPage, LayoutWarningSeverity.ContentLoss,
                            "A table cell item is taller than the page body and is clipped.", path);
                    }
                    to[c] = k;
                    any |= k > start;
                    done &= k >= cl.Items.Count;
                    fragHeight = Math.Max(fragHeight, h + (k >= cl.Items.Count ? cl.MarginBottom : 0));
                }
                if (!any)
                {
                    if (bodyPlaced)
                    {
                        return Finish(new TableContinuation(r, offsets));
                    }
                    fragments.Add(Fragment(row, yy, Math.Max(1, Math.Min(need, bodyBottom - yy)), offsets, null));
                    yy += Math.Max(1, Math.Min(need, bodyBottom - yy));
                    if (r + 1 >= layout.RowCount)
                    {
                        return Finish(null);
                    }
                    return Finish(new TableContinuation(r + 1, null));
                }
                fragHeight = Math.Max(fragHeight, 1);
                fragments.Add(new PlacedRowFragment
                {
                    Row = row,
                    Top = yy,
                    Height = fragHeight,
                    From = from,
                    To = to,
                    StartsRow = offsets is null,
                    EndsRow = done,
                });
                yy += fragHeight;
                bodyPlaced = true;
                if (done)
                {
                    continue;
                }
                return Finish(new TableContinuation(r, to));
            }
            AddPart();
            return new TablePlacement(true, yy, false, after);

            TablePlacement Finish(TableContinuation? next)
            {
                AddPart();
                if (next is null)
                {
                    return new TablePlacement(fragments.Count > 0, yy, true, after);
                }
                return new TablePlacement(fragments.Count > 0, yy, true, pos with { Line = 0, Table = next });
            }

            void AddPart()
            {
                if (fragments.Count > 0)
                {
                    plan.Items.Add(new PlacedTablePart { Layout = layout, Left = areaLeft + layout.Offset, Rows = fragments, Path = path });
                }
            }
        }

        private static double HeaderHeight(TableLayout layout, int row)
        {
            if (row < layout.HeaderRowCount)
            {
                return 0;
            }
            double h = 0;
            for (int i = 0; i < layout.HeaderRowCount; i++)
            {
                h += layout.Row(i).Height;
            }
            return h;
        }

        private static bool HasVerticalMerge(RowLayout row)
        {
            foreach (CellLayout c in row.Cells)
            {
                if (c.Cell.VerticalMerge != VerticalMerge.None)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>True when not even the first item of any cell fits in the remaining room (moving the row beats splitting it).</summary>
        private static bool FitsNoItem(RowLayout row, int[]? offsets, double room)
        {
            for (int c = 0; c < row.Cells.Count; c++)
            {
                CellLayout cl = row.Cells[c];
                int start = offsets is null ? 0 : offsets[c];
                if (start < cl.Items.Count && cl.MarginTop + cl.Items[start].Height <= room + Epsilon)
                {
                    return false;
                }
            }
            return true;
        }

        private static double RemainingHeight(RowLayout row, int[] offsets)
        {
            double h = 0;
            for (int c = 0; c < row.Cells.Count; c++)
            {
                CellLayout cl = row.Cells[c];
                double sum = 0;
                for (int k = offsets[c]; k < cl.Items.Count; k++)
                {
                    sum += cl.Items[k].Height;
                }
                h = Math.Max(h, sum + cl.MarginBottom);
            }
            return Math.Max(h, 1);
        }

        private static PlacedRowFragment Fragment(RowLayout row, double top, double height, int[]? from, int[]? to)
        {
            int n = row.Cells.Count;
            int[] f = new int[n];
            int[] t = new int[n];
            for (int c = 0; c < n; c++)
            {
                f[c] = from is null ? 0 : from[c];
                t[c] = to is null ? row.Cells[c].Items.Count : to[c];
            }
            return new PlacedRowFragment { Row = row, Top = top, Height = height, From = f, To = t, StartsRow = from is null, EndsRow = true };
        }

        // ---- Emission ----

        internal void EmitTablePart(PlacedTablePart part, List<LayoutOp> ops, List<LayoutBox> boxes, StringBuilder text, CancellationToken ct)
        {
            EmitTable(part.Layout, part.Left, part.Rows, ops, boxes, text, part.Path, ct);
        }

        private void EmitTable(TableLayout layout, double left, List<PlacedRowFragment> rows, List<LayoutOp> ops, List<LayoutBox> boxes, StringBuilder text,
            string path, CancellationToken ct)
        {
            if (rows.Count == 0)
            {
                return;
            }
            TableFormat tf = layout.Table.Format;
            double top = rows[0].Top;
            double bottom = rows[^1].Top + rows[^1].Height;
            boxes.Add(new LayoutBox(LayoutBoxKind.Table, new LayoutRect(left, top, layout.Width, bottom - top), path));

            // Shading.
            foreach (PlacedRowFragment fr in rows)
            {
                foreach (CellLayout cl in fr.Row.Cells)
                {
                    if (cl.MergeContinue)
                    {
                        continue;
                    }
                    if (cl.Cell.Background is LayoutColor bg && !bg.IsTransparent)
                    {
                        ops.Add(new FillRectOp(CellRect(cl, fr, rows, left), bg));
                    }
                }
            }

            // Content.
            foreach (PlacedRowFragment fr in rows)
            {
                bool firstCell = true;
                for (int c = 0; c < fr.Row.Cells.Count; c++)
                {
                    ct.ThrowIfCancellationRequested();
                    CellLayout cl = fr.Row.Cells[c];
                    if (cl.MergeContinue)
                    {
                        continue;
                    }
                    LayoutRect r = CellRect(cl, fr, rows, left);
                    string cellPath = path + "/" + (fr.Row.Index + 1).ToString(CultureInfo.InvariantCulture) + "/" + (c + 1).ToString(CultureInfo.InvariantCulture);
                    boxes.Add(new LayoutBox(LayoutBoxKind.TableCell, r, cellPath));
                    double contentH = 0;
                    for (int k = fr.From[c]; k < fr.To[c]; k++)
                    {
                        contentH += cl.Items[k].Height;
                    }
                    double y = r.Y + (fr.From[c] == 0 ? cl.MarginTop : 0);
                    double slack = r.Height - contentH - (fr.From[c] == 0 ? cl.MarginTop : 0) - cl.MarginBottom;
                    if (fr.StartsRow && fr.EndsRow && slack > 0)
                    {
                        y += cl.Cell.VerticalAlignment switch
                        {
                            CellVerticalAlignment.Center => slack / 2,
                            CellVerticalAlignment.Bottom => slack,
                            _ => 0,
                        };
                    }
                    if (!firstCell)
                    {
                        text.Append('\t');
                    }
                    firstCell = false;
                    StringBuilder cellText = new StringBuilder();
                    bool clip = fr.Row.Row.HeightRule == RowHeightRule.Exact;
                    if (clip)
                    {
                        ops.Add(new PushClipOp(r));
                    }
                    // Paragraph shading and borders inside the cell.
                    List<(MeasuredParagraph, FittedLine, int, double)> decorated = new List<(MeasuredParagraph, FittedLine, int, double)>();
                    double dy = y;
                    for (int k = fr.From[c]; k < fr.To[c]; k++)
                    {
                        if (cl.Items[k] is CellLine dl)
                        {
                            decorated.Add((dl.Paragraph, dl.Line, dl.LineIndex, dy + dl.GapBefore));
                        }
                        dy += cl.Items[k].Height;
                    }
                    EmitDecorations(decorated, r.X + cl.MarginLeft, Math.Max(1, cl.Width - cl.MarginLeft - cl.MarginRight), true, true, ops);
                    for (int k = fr.From[c]; k < fr.To[c]; k++)
                    {
                        CellItem item = cl.Items[k];
                        if (item is CellTableRow nestedRow)
                        {
                            // The run of this nested table's rows in this fragment, drawn as one table part.
                            List<PlacedRowFragment> nestedRows = new List<PlacedRowFragment>();
                            int k2 = k;
                            while (k2 < fr.To[c] && cl.Items[k2] is CellTableRow nr && ReferenceEquals(nr.Nested, nestedRow.Nested))
                            {
                                RowLayout rl = nr.Nested.Rows[nr.RowIndex];
                                nestedRows.Add(Fragment(rl, y, rl.Height, null, null));
                                y += rl.Height;
                                k2++;
                            }
                            EmitTable(nestedRow.Nested, r.X + cl.MarginLeft + nestedRow.Nested.Offset, nestedRows, ops, boxes, cellText, nestedRow.Path, ct);
                            k = k2 - 1;
                            continue;
                        }
                        if (item is CellLine line)
                        {
                            FlowEmitter.EmitLine(new PlacedLine
                            {
                                Paragraph = line.Paragraph,
                                Line = line.Line,
                                LineIndex = line.LineIndex,
                                AreaLeft = r.X + cl.MarginLeft,
                                AreaWidth = Math.Max(1, cl.Width - cl.MarginLeft - cl.MarginRight),
                                Top = y + line.GapBefore,
                                Path = line.Path,
                            }, _ctx, ops, boxes, cellText);
                        }
                        y += item.Height;
                    }
                    if (clip)
                    {
                        ops.Add(PopClipOp.Instance);
                    }
                    text.Append(cellText.ToString().TrimEnd('\n').Replace('\n', ' '));
                }
                text.Append('\n');
            }

            // Borders: each cell draws its right and bottom edges; the first column draws its left edge and the
            // first row on the page its top edge. Cell borders win over table borders.
            for (int fi = 0; fi < rows.Count; fi++)
            {
                PlacedRowFragment fr = rows[fi];
                bool firstOnPage = fi == 0;
                bool lastOfTable = fr.EndsRow && fr.Row.Index == layout.RowCount - 1;
                for (int c = 0; c < fr.Row.Cells.Count; c++)
                {
                    CellLayout cl = fr.Row.Cells[c];
                    LayoutRect r = new LayoutRect(left + cl.X, fr.Top, cl.Width, fr.Height);
                    bool firstCol = cl.GridStart == 0;
                    bool lastCol = c == fr.Row.Cells.Count - 1;
                    BorderLine topB = fr.Row.Index == 0 ? cl.Cell.BorderTop ?? tf.BorderTop
                        : cl.Cell.BorderTop ?? (tf.BorderInsideHorizontal.IsVisible ? tf.BorderInsideHorizontal : (firstOnPage ? tf.BorderTop : BorderLine.None));
                    BorderLine bottomB = lastOfTable ? cl.Cell.BorderBottom ?? tf.BorderBottom
                        : cl.Cell.BorderBottom ?? (tf.BorderInsideHorizontal.IsVisible || fi < rows.Count - 1 ? tf.BorderInsideHorizontal : tf.BorderBottom);
                    BorderLine leftB = firstCol ? cl.Cell.BorderLeft ?? tf.BorderLeft : cl.Cell.BorderLeft ?? tf.BorderInsideVertical;
                    BorderLine rightB = lastCol ? cl.Cell.BorderRight ?? tf.BorderRight : cl.Cell.BorderRight ?? tf.BorderInsideVertical;
                    if (firstOnPage && !cl.MergeContinue)
                    {
                        Stroke(topB, r.X, r.Y, r.Right, r.Y, ops);
                    }
                    bool mergeOpenBelow = (cl.Cell.VerticalMerge == VerticalMerge.Restart || cl.MergeContinue) && !NextRowEndsMerge(layout, fr.Row.Index, cl.GridStart);
                    if (!mergeOpenBelow || fi == rows.Count - 1)
                    {
                        Stroke(bottomB, r.X, r.Bottom, r.Right, r.Bottom, ops);
                    }
                    if (firstCol)
                    {
                        Stroke(leftB, r.X, r.Y, r.X, r.Bottom, ops);
                    }
                    Stroke(rightB, r.Right, r.Y, r.Right, r.Bottom, ops);
                }
            }
        }

        private static bool NextRowEndsMerge(TableLayout layout, int rowIndex, int gridStart)
        {
            if (rowIndex + 1 >= layout.RowCount)
            {
                return true;
            }
            foreach (CellLayout c in layout.Row(rowIndex + 1).Cells)
            {
                if (c.GridStart == gridStart)
                {
                    return !c.MergeContinue;
                }
            }
            return true;
        }

        private static void Stroke(BorderLine b, double x1, double y1, double x2, double y2, List<LayoutOp> ops)
        {
            if (!b.IsVisible)
            {
                return;
            }
            if (b.Double)
            {
                bool horizontal = Math.Abs(y1 - y2) < 0.001;
                double o = b.Width;
                ops.Add(new StrokeLineOp(x1 - (horizontal ? 0 : o), y1 - (horizontal ? o : 0), x2 - (horizontal ? 0 : o), y2 - (horizontal ? o : 0), b.Width, b.Color, b.Dash));
                ops.Add(new StrokeLineOp(x1 + (horizontal ? 0 : o), y1 + (horizontal ? o : 0), x2 + (horizontal ? 0 : o), y2 + (horizontal ? o : 0), b.Width, b.Color, b.Dash));
                return;
            }
            ops.Add(new StrokeLineOp(x1, y1, x2, y2, b.Width, b.Color, b.Dash));
        }

        /// <summary>The cell's rectangle on the page: a vertical-merge restart spans its placed rows.</summary>
        private static LayoutRect CellRect(CellLayout cl, PlacedRowFragment fr, List<PlacedRowFragment> rows, double left)
        {
            double height = fr.Height;
            if (cl.Cell.VerticalMerge == VerticalMerge.Restart && !cl.MergeContinue && cl.RowSpan > 1)
            {
                int idx = rows.IndexOf(fr);
                int last = fr.Row.Index + cl.RowSpan - 1;
                for (int k = idx + 1; k < rows.Count && rows[k].Row.Index <= last; k++)
                {
                    height += rows[k].Height;
                }
            }
            return new LayoutRect(left + cl.X, fr.Top, cl.Width, height);
        }
    }
}
