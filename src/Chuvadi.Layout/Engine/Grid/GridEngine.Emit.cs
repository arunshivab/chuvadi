using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Engine.Flow;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.Engine.Grid
{
    internal sealed partial class GridEngine
    {
        private static readonly LayoutColor GridlineColor = new LayoutColor(0, 0, 0);
        private const double GridlineWidth = 0.25;

        /// <summary>A row or column placed on the page: index and its start/size in page points.</summary>
        private readonly record struct Track(int Index, double Start, double Size);

        public LaidOutPage BuildPage(int index, CancellationToken ct)
        {
            GridPageRecord rec = Record(index);
            GridSheet sheet = _book.Sheets[rec.Sheet];
            GridGeometry geo = _geometry[rec.Sheet];
            GridPageSetup ps = sheet.PageSetup;
            double s = rec.Scale;
            (double pw, double ph) = Printable(ps);
            double headingW = ps.PrintHeadings ? HeadingColumnWidth(geo, new CellRange(rec.RowFirst, rec.ColumnFirst, rec.RowLast, rec.ColumnLast)) * s : 0;
            double headingH = ps.PrintHeadings ? sheet.DefaultRowHeight * s : 0;

            // Tracks: print titles first, then the band.
            List<Track> cols = new List<Track>();
            List<Track> rows = new List<Track>();
            double x = 0;
            if (rec.TitleColumns && sheet.PrintTitleColumns is (int tc0, int tc1))
            {
                for (int c = tc0; c <= tc1; c++)
                {
                    double w = geo.ColumnWidth(c) * s;
                    cols.Add(new Track(c, x, w));
                    x += w;
                }
            }
            for (int c = rec.ColumnFirst; c <= rec.ColumnLast; c++)
            {
                double w = geo.ColumnWidth(c) * s;
                cols.Add(new Track(c, x, w));
                x += w;
            }
            double y = 0;
            if (rec.TitleRows && sheet.PrintTitleRows is (int tr0, int tr1))
            {
                for (int r = tr0; r <= tr1; r++)
                {
                    double h = geo.RowHeight(r) * s;
                    rows.Add(new Track(r, y, h));
                    y += h;
                }
            }
            for (int r = rec.RowFirst; r <= rec.RowLast; r++)
            {
                ct.ThrowIfCancellationRequested();
                double h = geo.RowHeight(r) * s;
                rows.Add(new Track(r, y, h));
                y += h;
            }
            double contentW = headingW + x;
            double contentH = headingH + y;
            double ox = ps.MarginLeft + (ps.CenterHorizontally ? Math.Max(0, (pw - contentW) / 2) : 0);
            double oy = ps.MarginTop + (ps.CenterVertically ? Math.Max(0, (ph - contentH) / 2) : 0);
            double gx = ox + headingW;
            double gy = oy + headingH;

            Dictionary<int, Track> colByIndex = new Dictionary<int, Track>();
            foreach (Track t in cols)
            {
                colByIndex.TryAdd(t.Index, t);
            }
            Dictionary<int, Track> rowByIndex = new Dictionary<int, Track>();
            foreach (Track t in rows)
            {
                rowByIndex.TryAdd(t.Index, t);
            }

            List<LayoutOp> ops = new List<LayoutOp>();
            List<LayoutBox> boxes = new List<LayoutBox>();
            StringBuilder text = new StringBuilder();

            // Merged ranges visible on this page, clipped to the visible tracks.
            List<(CellRange Range, LayoutRect Rect)> merges = new List<(CellRange, LayoutRect)>();
            foreach (CellRange m in sheet.MergedRanges)
            {
                LayoutRect? r = SpanRect(m, cols, rows, gx, gy);
                if (r is LayoutRect rr)
                {
                    merges.Add((m, rr));
                }
            }

            // 1. Fills.
            foreach (Track row in rows)
            {
                if (row.Size <= 0)
                {
                    continue;
                }
                foreach (Track col in cols)
                {
                    if (col.Size <= 0)
                    {
                        continue;
                    }
                    GridCell? cell = sheet.GetCell(row.Index, col.Index);
                    if (cell?.Style.Fill is LayoutColor fill && !fill.IsTransparent && !InMerge(sheet, row.Index, col.Index))
                    {
                        ops.Add(new FillRectOp(new LayoutRect(gx + col.Start, gy + row.Start, col.Size, row.Size), fill));
                    }
                }
            }
            foreach ((CellRange m, LayoutRect r) in merges)
            {
                GridCell? anchor = sheet.GetCell(m.FirstRow, m.FirstColumn);
                if (anchor?.Style.Fill is LayoutColor fill && !fill.IsTransparent)
                {
                    ops.Add(new FillRectOp(r, fill));
                }
            }

            // Data bars (conditional formatting), over the fill and under the text.
            foreach (Track row in rows)
            {
                if (row.Size <= 0)
                {
                    continue;
                }
                foreach (Track col in cols)
                {
                    if (col.Size > 0 && sheet.GetCell(row.Index, col.Index) is { DataBar: GridDataBar bar })
                    {
                        EmitDataBar(bar, new LayoutRect(gx + col.Start, gy + row.Start, col.Size, row.Size), s, ops);
                    }
                }
            }

            // Icons (conditional formatting) at the left of the cell, under the text.
            foreach (Track row in rows)
            {
                if (row.Size <= 0)
                {
                    continue;
                }
                foreach (Track col in cols)
                {
                    if (col.Size > 0 && sheet.GetCell(row.Index, col.Index) is { Icon: GridIcon icon })
                    {
                        EmitIcon(icon, new LayoutRect(gx + col.Start, gy + row.Start, col.Size, row.Size), s, ops, sheet.Name);
                    }
                }
            }

            // 2. Gridlines (not inside merges).
            if (ps.PrintGridlines)
            {
                EmitGridlines(sheet, cols, rows, gx, gy, x, y, merges, ops);
            }

            // 3. Headings.
            if (ps.PrintHeadings)
            {
                EmitHeadings(sheet, geo, cols, rows, ox, oy, headingW, headingH, s, ops);
            }

            // 4. Cell text.
            foreach (Track row in rows)
            {
                if (row.Size <= 0)
                {
                    continue;
                }
                EmitContinuedText(sheet, geo, rec.ColumnFirst, row, cols, gx, gy, s, ops);
                bool firstInRow = true;
                foreach (Track col in cols)
                {
                    GridCell? cell = sheet.GetCell(row.Index, col.Index);
                    if (cell is null)
                    {
                        continue;
                    }
                    LayoutRect cellRect = new LayoutRect(gx + col.Start, gy + row.Start, col.Size, row.Size);
                    bool merged = false;
                    foreach ((CellRange m, LayoutRect r) in merges)
                    {
                        if (m.Contains(row.Index, col.Index))
                        {
                            merged = true;
                            if (m.FirstRow == row.Index && m.FirstColumn == col.Index)
                            {
                                cellRect = r;
                                merged = false;
                            }
                            break;
                        }
                    }
                    if (merged || col.Size <= 0)
                    {
                        continue;
                    }
                    string path = sheet.Name + "!" + CellAddress.Format(row.Index, col.Index);
                    LayoutRect textRect = cellRect;
                    if (cell.Icon is not null)
                    {
                        // Excel keeps the value clear of the icon at the left edge.
                        double iconSpace = Math.Min(14 * s, cellRect.Width / 2);
                        textRect = new LayoutRect(cellRect.X + iconSpace, cellRect.Y, cellRect.Width - iconSpace, cellRect.Height);
                    }
                    EmitCellText(sheet, geo, cell, row.Index, col.Index, textRect, cols, gx, s, ops, path);
                    boxes.Add(new LayoutBox(LayoutBoxKind.SheetCell, cellRect, path));
                    if (cell.DisplayText.Length > 0)
                    {
                        if (!firstInRow)
                        {
                            text.Append('\t');
                        }
                        text.Append(cell.Runs is null ? cell.DisplayText : Concat(cell.Runs));
                        firstInRow = false;
                    }
                }
                if (!firstInRow)
                {
                    text.Append('\n');
                }
            }

            // 5. Borders on top, then diagonals.
            EmitBorders(sheet, cols, rows, gx, gy, merges, ops);
            EmitDiagonals(sheet, cols, rows, gx, gy, merges, ops);

            // Pictures, shapes and charts print over the cells.
            EmitDrawings(sheet, geo, rec, cols, rows, gx, gy, s, ops, boxes);

            // Right-to-left sheets: mirror the printed block (headings, cells, drawings) about its own centre line.
            // Glyphs keep their shapes; only positions move. Headers and footers are not mirrored.
            if (sheet.RightToLeft)
            {
                MirrorHorizontally(ops, boxes, ox + ox + contentW);
                _ctx.Warn(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.Fidelity,
                    "Right-to-left sheet: columns are printed from right to left. Cell contents are mirrored with their cells, so explicit left and right alignments are mirrored too.", sheet.Name);
            }

            // 6. Header and footer.
            bool provisional = false;
            EmitHeaderFooter(sheet, rec, index, s, ops, boxes);

            return new LaidOutPage(index, new LayoutDisplayList(ops, ps.PageWidth, ps.PageHeight), boxes, text.ToString().TrimEnd('\n'),
                provisional, rec.PageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), sheet.Name);
        }

        /// <summary>
        /// A data bar as Excel 2010+ draws it: inset 2 px from the cell edges, its length a share of the cell width,
        /// from the left edge (negative values: from the right edge). Gradient bars are drawn solid.
        /// </summary>
        private static void EmitDataBar(GridDataBar bar, LayoutRect cell, double s, List<LayoutOp> ops)
        {
            double inset = GridGeometry.PixelsToPoints(2) * s;
            double h = cell.Height - (2 * inset);
            double w = Math.Clamp(bar.Length, 0, 1) * (cell.Width - (2 * inset));
            if (h <= 0 || w <= 0)
            {
                return;
            }
            double x = bar.Negative ? cell.Right - inset - w : cell.X + inset;
            LayoutRect r = new LayoutRect(x, cell.Y + inset, w, h);
            ops.Add(new FillRectOp(r, bar.Color));
            if (bar.BorderColor is LayoutColor bc)
            {
                ops.Add(new StrokeLineOp(r.X, r.Y, r.Right, r.Y, 0.75 * s, bc));
                ops.Add(new StrokeLineOp(r.Right, r.Y, r.Right, r.Bottom, 0.75 * s, bc));
                ops.Add(new StrokeLineOp(r.Right, r.Bottom, r.X, r.Bottom, 0.75 * s, bc));
                ops.Add(new StrokeLineOp(r.X, r.Bottom, r.X, r.Y, 0.75 * s, bc));
            }
        }

        /// <summary>Diagonal borders, across the whole merged area for a merge's anchor cell.</summary>
        private static void EmitDiagonals(GridSheet sheet, List<Track> cols, List<Track> rows, double gx, double gy,
            List<(CellRange Range, LayoutRect Rect)> merges, List<LayoutOp> ops)
        {
            foreach (Track row in rows)
            {
                foreach (Track col in cols)
                {
                    GridCell? cell = sheet.GetCell(row.Index, col.Index);
                    if (cell is null || (!cell.Style.BorderDiagonalUp.IsVisible && !cell.Style.BorderDiagonalDown.IsVisible))
                    {
                        continue;
                    }
                    LayoutRect r = new LayoutRect(gx + col.Start, gy + row.Start, col.Size, row.Size);
                    bool skip = false;
                    foreach ((CellRange m, LayoutRect mr) in merges)
                    {
                        if (m.Contains(row.Index, col.Index))
                        {
                            skip = !(m.FirstRow == row.Index && m.FirstColumn == col.Index);
                            r = mr;
                            break;
                        }
                    }
                    if (skip || r.Width <= 0 || r.Height <= 0)
                    {
                        continue;
                    }
                    if (cell.Style.BorderDiagonalUp.IsVisible)
                    {
                        Edge(cell.Style.BorderDiagonalUp, r.X, r.Bottom, r.Right, r.Y, ops);
                    }
                    if (cell.Style.BorderDiagonalDown.IsVisible)
                    {
                        Edge(cell.Style.BorderDiagonalDown, r.X, r.Y, r.Right, r.Bottom, ops);
                    }
                }
            }
        }

        private static string Concat(IReadOnlyList<(string Text, CharacterFormat Format)> runs)
        {
            StringBuilder sb = new StringBuilder();
            foreach ((string t, CharacterFormat _) in runs)
            {
                sb.Append(t);
            }
            return sb.ToString();
        }

        private static LayoutRect? SpanRect(CellRange m, List<Track> cols, List<Track> rows, double gx, double gy)
        {
            double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue;
            foreach (Track c in cols)
            {
                if (c.Index >= m.FirstColumn && c.Index <= m.LastColumn)
                {
                    x0 = Math.Min(x0, c.Start);
                    x1 = Math.Max(x1, c.Start + c.Size);
                }
            }
            foreach (Track r in rows)
            {
                if (r.Index >= m.FirstRow && r.Index <= m.LastRow)
                {
                    y0 = Math.Min(y0, r.Start);
                    y1 = Math.Max(y1, r.Start + r.Size);
                }
            }
            if (x1 < x0 || y1 < y0)
            {
                return null;
            }
            return new LayoutRect(gx + x0, gy + y0, x1 - x0, y1 - y0);
        }

        private static void EmitGridlines(GridSheet sheet, List<Track> cols, List<Track> rows, double gx, double gy, double w, double h,
            List<(CellRange Range, LayoutRect Rect)> merges, List<LayoutOp> ops)
        {
            // Horizontal lines at each row edge, split around merges that straddle the edge.
            for (int i = 0; i <= rows.Count; i++)
            {
                double yy = gy + (i < rows.Count ? rows[i].Start : h);
                double segStart = gx;
                foreach (Track c in cols)
                {
                    bool inside = i > 0 && i < rows.Count && MergeStraddles(merges, rows[i - 1].Index, rows[i].Index, c.Index, true);
                    if (inside)
                    {
                        double cx = gx + c.Start;
                        if (cx > segStart)
                        {
                            ops.Add(new StrokeLineOp(segStart, yy, cx, yy, GridlineWidth, GridlineColor));
                        }
                        segStart = cx + c.Size;
                    }
                }
                if (gx + w > segStart)
                {
                    ops.Add(new StrokeLineOp(segStart, yy, gx + w, yy, GridlineWidth, GridlineColor));
                }
            }
            for (int j = 0; j <= cols.Count; j++)
            {
                double xx = gx + (j < cols.Count ? cols[j].Start : w);
                double segStart = gy;
                foreach (Track r in rows)
                {
                    bool inside = j > 0 && j < cols.Count && MergeStraddles(merges, cols[j - 1].Index, cols[j].Index, r.Index, false);
                    if (inside)
                    {
                        double ry = gy + r.Start;
                        if (ry > segStart)
                        {
                            ops.Add(new StrokeLineOp(xx, segStart, xx, ry, GridlineWidth, GridlineColor));
                        }
                        segStart = ry + r.Size;
                    }
                }
                if (gy + h > segStart)
                {
                    ops.Add(new StrokeLineOp(xx, segStart, xx, gy + h, GridlineWidth, GridlineColor));
                }
            }
        }

        private static bool MergeStraddles(List<(CellRange Range, LayoutRect Rect)> merges, int before, int after, int other, bool horizontalEdge)
        {
            foreach ((CellRange m, LayoutRect _) in merges)
            {
                if (horizontalEdge)
                {
                    if (other >= m.FirstColumn && other <= m.LastColumn && before >= m.FirstRow && after <= m.LastRow)
                    {
                        return true;
                    }
                }
                else if (other >= m.FirstRow && other <= m.LastRow && before >= m.FirstColumn && after <= m.LastColumn)
                {
                    return true;
                }
            }
            return false;
        }

        private void EmitHeadings(GridSheet sheet, GridGeometry geo, List<Track> cols, List<Track> rows, double ox, double oy, double hw, double hh, double s,
            List<LayoutOp> ops)
        {
            CharacterFormat f = sheet.NormalFont with { FontSize = sheet.NormalFont.FontSize * s };
            double gx = ox + hw;
            double gy = oy + hh;
            foreach (Track c in cols)
            {
                if (c.Size <= 0)
                {
                    continue;
                }
                LayoutRect r = new LayoutRect(gx + c.Start, oy, c.Size, hh);
                Box(r, ops);
                CenteredText(CellAddress.ColumnName(c.Index), f, r, ops);
            }
            foreach (Track row in rows)
            {
                if (row.Size <= 0)
                {
                    continue;
                }
                LayoutRect r = new LayoutRect(ox, gy + row.Start, hw, row.Size);
                Box(r, ops);
                CenteredText((row.Index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), f, r, ops);
            }
            Box(new LayoutRect(ox, oy, hw, hh), ops);
        }

        private static void Box(LayoutRect r, List<LayoutOp> ops)
        {
            ops.Add(new StrokeLineOp(r.X, r.Y, r.Right, r.Y, GridlineWidth, GridlineColor));
            ops.Add(new StrokeLineOp(r.Right, r.Y, r.Right, r.Bottom, GridlineWidth, GridlineColor));
            ops.Add(new StrokeLineOp(r.Right, r.Bottom, r.X, r.Bottom, GridlineWidth, GridlineColor));
            ops.Add(new StrokeLineOp(r.X, r.Bottom, r.X, r.Y, GridlineWidth, GridlineColor));
        }

        private void CenteredText(string text, CharacterFormat f, LayoutRect r, List<LayoutOp> ops)
        {
            FlowParagraph p = FlowParagraph.Of(text, f, new ParagraphFormat { Alignment = ParagraphAlignment.Center });
            (MeasuredParagraph m, List<FittedLine> lines) = MeasureCell(p, r.Width);
            if (lines.Count == 0)
            {
                return;
            }
            FittedLine line = lines[0];
            double top = r.Y + ((r.Height - line.Height) / 2);
            FlowEmitter.EmitLine(new PlacedLine { Paragraph = m, Line = line, LineIndex = 0, AreaLeft = r.X, AreaWidth = r.Width, Top = top, Path = string.Empty },
                _ctx, ops, new List<LayoutBox>(), new StringBuilder());
        }

        /// <summary>The paragraph Layout uses for a cell's text: cell font (scaled), alignment and indent.</summary>
        private FlowParagraph CellParagraph(GridCell cell, double scale, double width, GridGeometry geo)
        {
            GridCellStyle st = cell.Style;
            ParagraphAlignment align = EffectiveAlignment(cell) switch
            {
                CellHorizontalAlignment.Center or CellHorizontalAlignment.CenterContinuous => ParagraphAlignment.Center,
                CellHorizontalAlignment.Right => ParagraphAlignment.Right,
                CellHorizontalAlignment.Justify or CellHorizontalAlignment.Distributed => st.WrapText ? ParagraphAlignment.Justify : ParagraphAlignment.Left,
                _ => ParagraphAlignment.Left,
            };
            double indent = st.Indent > 0 ? GridGeometry.PixelsToPoints(st.Indent * 3 * geo.MaxDigitWidth) * scale : 0;
            ParagraphFormat pf = new ParagraphFormat
            {
                Alignment = align,
                LeftIndent = align == ParagraphAlignment.Right ? 0 : indent,
                RightIndent = align == ParagraphAlignment.Right ? indent : 0,
                WidowControl = false,
            };
            FlowParagraph p = new FlowParagraph { Format = pf, MarkFormat = Scaled(st.Font, scale) };
            if (cell.Runs is not null)
            {
                foreach ((string t, CharacterFormat f) in cell.Runs)
                {
                    p.Inlines.Add(new TextRun(t, Scaled(f, scale)) { HyperlinkUri = cell.HyperlinkUri, HyperlinkAnchor = cell.HyperlinkLocation });
                }
            }
            else if (cell.FillCharacter is char fill && cell.FillIndex is int at && at <= cell.DisplayText.Length)
            {
                // Repeat-fill: the text before the fill at the left edge, the rest flush right, the fill between
                // (a right tab at the cell's inner edge, with the fill character as its leader).
                TabLeader leader = fill switch
                {
                    '.' => TabLeader.Dot,
                    '-' => TabLeader.Hyphen,
                    '_' => TabLeader.Underscore,
                    '\u00B7' => TabLeader.MiddleDot,
                    ' ' => TabLeader.None,
                    _ => TabLeader.Dot, // drawn with the fill character itself (LeaderCharacter)
                };
                char? custom = leader == TabLeader.Dot && fill != '.' ? fill : null;
                p.Format = pf with { Alignment = ParagraphAlignment.Left, LeftIndent = 0, RightIndent = 0, TabStops = new[] { new TabStop(width, TabAlignment.Right, leader, custom) } };
                CharacterFormat f = Scaled(st.Font, scale);
                if (at > 0)
                {
                    p.Inlines.Add(new TextRun(cell.DisplayText[..at], f) { HyperlinkUri = cell.HyperlinkUri, HyperlinkAnchor = cell.HyperlinkLocation });
                }
                p.Inlines.Add(new TabInline(f));
                if (at < cell.DisplayText.Length)
                {
                    p.Inlines.Add(new TextRun(cell.DisplayText[at..], f) { HyperlinkUri = cell.HyperlinkUri, HyperlinkAnchor = cell.HyperlinkLocation });
                }
            }
            else
            {
                p.Inlines.Add(new TextRun(cell.DisplayText, Scaled(st.Font, scale)) { HyperlinkUri = cell.HyperlinkUri, HyperlinkAnchor = cell.HyperlinkLocation });
            }
            return p;
        }

        private static CharacterFormat Scaled(CharacterFormat f, double scale) => scale == 1 ? f : f with
        {
            FontSize = f.FontSize * scale,
            ComplexScriptFontSize = f.ComplexScriptFontSize * scale,
            CharacterSpacing = f.CharacterSpacing * scale,
        };

        internal static CellHorizontalAlignment EffectiveAlignment(GridCell cell)
        {
            if (cell.Style.HorizontalAlignment != CellHorizontalAlignment.General)
            {
                return cell.Style.HorizontalAlignment;
            }
            return cell.Kind switch
            {
                CellValueKind.Number => CellHorizontalAlignment.Right,
                CellValueKind.Boolean or CellValueKind.Error => CellHorizontalAlignment.Center,
                _ => CellHorizontalAlignment.Left,
            };
        }

        /// <summary>
        /// F11 (standard silent; follows Excel): text that overflows from a cell left of this page's first column, through
        /// empty cells, continues on this page. The cell is laid out where it would be if the grid went on to the left, and
        /// only the part on this page is drawn. Not with print-title columns, which sit between the two.
        /// </summary>
        private void EmitContinuedText(GridSheet sheet, GridGeometry geo, int firstColumn, Track row, List<Track> cols, double gx, double gy, double s, List<LayoutOp> ops)
        {
            const int MaxScan = 256;
            if (firstColumn <= 0 || cols.Count == 0 || cols[0].Index != firstColumn || !IsEmpty(sheet, row.Index, firstColumn))
            {
                return;
            }
            int c = firstColumn - 1;
            while (c >= 0 && firstColumn - c <= MaxScan && IsEmpty(sheet, row.Index, c))
            {
                c--;
            }
            if (c < 0 || firstColumn - c > MaxScan || InMerge(sheet, row.Index, c) || sheet.GetCell(row.Index, c) is not GridCell cell)
            {
                return;
            }
            if (cell.Kind == CellValueKind.Number || cell.Style.WrapText || cell.Style.ShrinkToFit || cell.Runs is null && cell.DisplayText.Length == 0
                || EffectiveAlignment(cell) is not (CellHorizontalAlignment.Left or CellHorizontalAlignment.General or CellHorizontalAlignment.Center))
            {
                return;
            }
            List<Track> ext = new List<Track>();
            double x = 0;
            for (int k = firstColumn - 1; k >= c; k--)
            {
                double w = geo.ColumnWidth(k) * s;
                x -= w;
                ext.Insert(0, new Track(k, x, w));
            }
            ext.AddRange(cols);
            LayoutRect rect = new LayoutRect(gx + ext[0].Start, gy + row.Start, ext[0].Size, row.Size);
            EmitCellText(sheet, geo, cell, row.Index, c, rect, ext, gx, s, ops, sheet.Name + "!" + CellAddress.Format(row.Index, c), gx);
        }

        private void EmitCellText(GridSheet sheet, GridGeometry geo, GridCell cell, int row, int col, LayoutRect rect, List<Track> cols, double gx, double s,
            List<LayoutOp> ops, string path, double clipLeft = double.NegativeInfinity)
        {
            if (cell.DisplayText.Length == 0 && cell.Runs is null)
            {
                return;
            }
            GridCellStyle st = cell.Style;
            double pad = CellPadding(s);
            double innerWidth = Math.Max(1, rect.Width - (2 * pad));
            CellHorizontalAlignment h = EffectiveAlignment(cell);

            FlowParagraph p;
            MeasuredParagraph m;
            List<FittedLine> lines;
            LayoutRect clip = rect;
            if (st.WrapText || h is CellHorizontalAlignment.Justify or CellHorizontalAlignment.Distributed && st.WrapText)
            {
                p = CellParagraph(cell, s, innerWidth, geo);
                (m, lines) = MeasureCell(p, innerWidth);
            }
            else
            {
                p = CellParagraph(cell, s, innerWidth, geo);
                (m, lines) = MeasureCell(p, 1e7);
                double textWidth = lines.Count > 0 ? lines[0].ContentEndX - lines[0].StartX : 0;
                if (st.ShrinkToFit && textWidth > innerWidth && textWidth > 0)
                {
                    double factor = innerWidth / textWidth;
                    p = CellParagraph(cell, s * factor, innerWidth, geo);
                    (m, lines) = MeasureCell(p, 1e7);
                    textWidth = lines.Count > 0 ? lines[0].ContentEndX - lines[0].StartX : 0;
                }
                if (cell.Kind == CellValueKind.Number && textWidth > innerWidth + 0.01)
                {
                    // Excel shows #### for numbers that do not fit.
                    string hashes = FillHashes(cell, s, innerWidth, geo);
                    p = CellParagraph(new GridCell(hashes, CellValueKind.Number, st), s, innerWidth, geo);
                    (m, lines) = MeasureCell(p, 1e7);
                    textWidth = lines.Count > 0 ? lines[0].ContentEndX - lines[0].StartX : 0;
                }
                else if (h == CellHorizontalAlignment.Fill && textWidth > 0 && textWidth < innerWidth)
                {
                    int times = Math.Max(1, (int)Math.Floor(innerWidth / textWidth));
                    StringBuilder rep = new StringBuilder();
                    for (int i = 0; i < times; i++)
                    {
                        rep.Append(cell.DisplayText);
                    }
                    p = CellParagraph(new GridCell(rep.ToString(), cell.Kind, st), s, innerWidth, geo);
                    (m, lines) = MeasureCell(p, 1e7);
                    textWidth = lines.Count > 0 ? lines[0].ContentEndX - lines[0].StartX : 0;
                }
                else if (textWidth > innerWidth && cell.Kind != CellValueKind.Number)
                {
                    clip = OverflowRegion(sheet, row, col, rect, cols, gx, h, textWidth + (2 * pad));
                }
                // Re-anchor the single line to the cell (or overflow region) width.
                if (lines.Count > 0)
                {
                    double regionInner = Math.Max(1, clip.Width - (2 * pad));
                    double cellOffset = rect.X - clip.X;
                    lines = new List<FittedLine> { WithMaxX(lines[0], h, regionInner, innerWidth, cellOffset, textWidth) };
                }
            }

            double total = 0;
            foreach (FittedLine l in lines)
            {
                total += l.Height;
            }
            double onePx = GridGeometry.PixelsToPoints(1) * s;
            double top = st.VerticalAlignment switch
            {
                CellVerticalAlignmentKind.Top => rect.Y + onePx,
                CellVerticalAlignmentKind.Center or CellVerticalAlignmentKind.Justify or CellVerticalAlignmentKind.Distributed => rect.Y + ((rect.Height - total) / 2),
                _ => rect.Bottom - total - onePx,
            };
            if (clip.Right <= clipLeft)
            {
                return; // continued text that ends before this page
            }
            ops.Add(new PushClipOp(clip.X >= clipLeft ? clip : new LayoutRect(clipLeft, clip.Y, clip.Right - clipLeft, clip.Height)));
            double yy = top;
            StringBuilder ignore = new StringBuilder();
            List<LayoutBox> noBoxes = new List<LayoutBox>();
            for (int i = 0; i < lines.Count; i++)
            {
                FlowEmitter.EmitLine(new PlacedLine
                {
                    Paragraph = m,
                    Line = lines[i],
                    LineIndex = i,
                    AreaLeft = clip.X + pad,
                    AreaWidth = Math.Max(1, clip.Width - (2 * pad)),
                    Top = yy,
                    Path = path,
                }, _ctx, ops, noBoxes, ignore);
                yy += lines[i].Height;
            }
            ops.Add(PopClipOp.Instance);
        }

        private string FillHashes(GridCell cell, double s, double innerWidth, GridGeometry geo)
        {
            FlowParagraph one = CellParagraph(new GridCell("#", CellValueKind.Number, cell.Style), s, innerWidth, geo);
            (MeasuredParagraph m, List<FittedLine> lines) = MeasureCell(one, 1e7);
            double w = lines.Count > 0 ? Math.Max(0.1, lines[0].ContentEndX - lines[0].StartX) : 1;
            return new string('#', Math.Max(1, (int)Math.Floor(innerWidth / w)));
        }

        /// <summary>
        /// A single-line cell's line re-anchored to its drawing region: alignment is computed against the cell for
        /// left/right/centre (Excel keeps the text attached to its own cell while it spills).
        /// </summary>
        private static FittedLine WithMaxX(FittedLine l, CellHorizontalAlignment h, double regionInner, double cellInner, double cellOffset, double textWidth)
        {
            // Position the line start so that, within the region, the text sits where Excel puts it.
            double start = h switch
            {
                CellHorizontalAlignment.Right => cellOffset + cellInner - textWidth,
                CellHorizontalAlignment.Center or CellHorizontalAlignment.CenterContinuous => cellOffset + ((cellInner - textWidth) / 2),
                _ => cellOffset + l.StartX,
            };
            double shift = start - l.StartX;
            Dictionary<int, double> tabs = l.TabWidths;
            return new FittedLine
            {
                Start = l.Start,
                End = l.End,
                StartX = start,
                MaxX = start + textWidth,
                ContentEndX = l.ContentEndX + shift,
                TabWidths = tabs,
                EndsWithLineBreak = l.EndsWithLineBreak,
                EndsWithPageBreak = l.EndsWithPageBreak,
                IsLastLine = true,
                Hyphenated = false,
                VisibleEnd = l.VisibleEnd,
                Ascent = l.Ascent,
                Descent = l.Descent,
                Height = l.Height,
                Baseline = l.Baseline,
            };
        }

        /// <summary>The region text may spill into: empty neighbours in the direction(s) of its alignment.</summary>
        private static LayoutRect OverflowRegion(GridSheet sheet, int row, int col, LayoutRect rect, List<Track> cols, double gx, CellHorizontalAlignment h, double needed)
        {
            int pos = cols.FindIndex(t => t.Index == col);
            double left = rect.X;
            double right = rect.Right;
            bool spillRight = h is CellHorizontalAlignment.Left or CellHorizontalAlignment.General or CellHorizontalAlignment.Center or CellHorizontalAlignment.CenterContinuous or CellHorizontalAlignment.Fill;
            bool spillLeft = h is CellHorizontalAlignment.Right or CellHorizontalAlignment.Center or CellHorizontalAlignment.CenterContinuous;
            double extra = needed - rect.Width;
            double wantLeft = spillLeft && spillRight ? extra / 2 : spillLeft ? extra : 0;
            double wantRight = spillLeft && spillRight ? extra / 2 : spillRight ? extra : 0;
            for (int i = pos + 1; i < cols.Count && wantRight > 0; i++)
            {
                if (cols[i].Index != cols[i - 1].Index + 1 || !IsEmpty(sheet, row, cols[i].Index))
                {
                    break;
                }
                right = gx + cols[i].Start + cols[i].Size;
                wantRight -= cols[i].Size;
            }
            for (int i = pos - 1; i >= 0 && wantLeft > 0; i--)
            {
                if (cols[i].Index != cols[i + 1].Index - 1 || !IsEmpty(sheet, row, cols[i].Index))
                {
                    break;
                }
                left = gx + cols[i].Start;
                wantLeft -= cols[i].Size;
            }
            return new LayoutRect(left, rect.Y, right - left, rect.Height);
        }

        private static bool IsEmpty(GridSheet sheet, int row, int col)
        {
            GridCell? c = sheet.GetCell(row, col);
            return (c is null || (c.DisplayText.Length == 0 && c.Runs is null)) && !InMerge(sheet, row, col);
        }

        private static void EmitBorders(GridSheet sheet, List<Track> cols, List<Track> rows, double gx, double gy,
            List<(CellRange Range, LayoutRect Rect)> merges, List<LayoutOp> ops)
        {
            foreach (Track row in rows)
            {
                foreach (Track col in cols)
                {
                    GridCell? cell = sheet.GetCell(row.Index, col.Index);
                    if (cell is null || row.Size <= 0 || col.Size <= 0)
                    {
                        continue;
                    }
                    GridCellStyle st = cell.Style;
                    LayoutRect r = new LayoutRect(gx + col.Start, gy + row.Start, col.Size, row.Size);
                    foreach ((CellRange m, LayoutRect mr) in merges)
                    {
                        if (m.FirstRow == row.Index && m.FirstColumn == col.Index)
                        {
                            r = mr;
                        }
                    }
                    Edge(st.BorderTop, r.X, r.Y, r.Right, r.Y, ops);
                    Edge(st.BorderBottom, r.X, r.Bottom, r.Right, r.Bottom, ops);
                    Edge(st.BorderLeft, r.X, r.Y, r.X, r.Bottom, ops);
                    Edge(st.BorderRight, r.Right, r.Y, r.Right, r.Bottom, ops);
                }
            }
        }

        private static void Edge(BorderLine b, double x1, double y1, double x2, double y2, List<LayoutOp> ops)
        {
            if (!b.IsVisible)
            {
                return;
            }
            if (b.Double)
            {
                double o = b.Width;
                bool horizontal = y1 == y2;
                ops.Add(new StrokeLineOp(x1 - (horizontal ? 0 : o), y1 - (horizontal ? o : 0), x2 - (horizontal ? 0 : o), y2 - (horizontal ? o : 0), b.Width, b.Color, b.Dash));
                ops.Add(new StrokeLineOp(x1 + (horizontal ? 0 : o), y1 + (horizontal ? o : 0), x2 + (horizontal ? 0 : o), y2 + (horizontal ? o : 0), b.Width, b.Color, b.Dash));
                return;
            }
            ops.Add(new StrokeLineOp(x1, y1, x2, y2, b.Width, b.Color, b.Dash));
        }
    }
}
