using System;
using System.Collections.Generic;
using System.Threading;
using Chuvadi.Layout.Engine.Flow;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.Engine.Grid
{
    /// <summary>One printed page of a sheet: a band of rows and columns at a scale.</summary>
    internal sealed record GridPageRecord(
        int Sheet,
        int RowFirst,
        int RowLast,
        int ColumnFirst,
        int ColumnLast,
        double Scale,
        bool TitleRows,
        bool TitleColumns,
        int PageNumber,
        int SheetPageIndex);

    /// <summary>
    /// Paginates a <see cref="GridWorkbook"/> the way Excel prints it. Page bands depend only on column widths,
    /// row heights, breaks and scale, so every page is known at open (the page count is final immediately);
    /// cells are measured and drawn only for the page being built.
    /// </summary>
    internal sealed partial class GridEngine : IPageSource
    {
        private const double Epsilon = 0.01;

        private readonly GridWorkbook _book;
        private readonly LayoutContext _ctx;
        private readonly List<GridPageRecord> _pages = new List<GridPageRecord>();
        private readonly Dictionary<int, GridGeometry> _geometry = new Dictionary<int, GridGeometry>();
        private readonly Dictionary<int, Dictionary<int, List<int>>> _rowIndex = new Dictionary<int, Dictionary<int, List<int>>>();
        private readonly FieldStub _fields = new FieldStub();

        public GridEngine(GridWorkbook book, LayoutContext ctx, LayoutSourceInfo source, CancellationToken ct)
        {
            _book = book;
            _ctx = ctx;
            Source = source;
            int pageNumber = 1;
            for (int s = 0; s < book.Sheets.Count; s++)
            {
                ct.ThrowIfCancellationRequested();
                GridSheet sheet = book.Sheets[s];
                if (sheet.Hidden)
                {
                    continue;
                }
                LayoutLimits.Check(nameof(LayoutLimits.MaxCellsPerSheet), ctx.Limits.MaxCellsPerSheet, sheet.CellCount);
                GridGeometry geo = new GridGeometry(sheet, ctx);
                _geometry[s] = geo;
                Dictionary<int, List<int>> rows = new Dictionary<int, List<int>>();
                foreach ((int r, int c, GridCell _) in sheet.Cells())
                {
                    if (!rows.TryGetValue(r, out List<int>? cols))
                    {
                        cols = new List<int>();
                        rows[r] = cols;
                    }
                    cols.Add(c);
                }
                foreach (List<int> cols in rows.Values)
                {
                    cols.Sort();
                }
                _rowIndex[s] = rows;
                AutoFitWrappedRows(s, sheet, geo, rows, ct);

                if (sheet.PageSetup.FirstPageNumber is int first)
                {
                    pageNumber = first;
                }
                int sheetPageIndex = 0;
                List<CellRange> areas = new List<CellRange>(sheet.PrintAreas);
                if (areas.Count == 0 && PrintedRange(sheet, geo) is CellRange used0 && WithOverflow(sheet, geo, used0) is CellRange used)
                {
                    areas.Add(used);
                }
                foreach (CellRange area in areas)
                {
                    foreach (GridPageRecord rec in PaginateArea(s, sheet, geo, area, ref pageNumber, ref sheetPageIndex, ct))
                    {
                        LayoutLimits.Check(nameof(LayoutLimits.MaxPages), ctx.Limits.MaxPages, _pages.Count + 1);
                        _pages.Add(rec);
                    }
                }
                foreach ((int r, int c, GridCell cell) in sheet.Cells())
                {
                    if (cell.Style.TextRotation != 0)
                    {
                        ctx.Warn(LayoutWarningCodes.UnsupportedFeature, LayoutWarningSeverity.Fidelity,
                            "Rotated cell text is drawn horizontally.", sheet.Name + "!" + CellAddress.Format(r, c));
                        break;
                    }
                }
            }
        }

        /// <summary>The used range, widened to the drawings (Excel prints pictures and shapes beyond the last cell).</summary>
        private static CellRange? PrintedRange(GridSheet sheet, GridGeometry geo)
        {
            CellRange? used = sheet.UsedRange();
            foreach (GridDrawing d in sheet.Drawings)
            {
                if (LastCell(d, geo) is not (int lr, int lc))
                {
                    continue;
                }
                int fr = d.From?.Row ?? 0;
                int fc = d.From?.Column ?? 0;
                used = used is CellRange u
                    ? new CellRange(Math.Min(u.FirstRow, fr), Math.Min(u.FirstColumn, fc), Math.Max(u.LastRow, lr), Math.Max(u.LastColumn, lc))
                    : new CellRange(fr, fc, Math.Max(fr, lr), Math.Max(fc, lc));
            }
            return used;
        }

        /// <summary>
        /// F11 (standard silent; follows Excel): text that overflows to the right past the last used column, through empty
        /// cells, is printed in full: the printed range is widened to the column where the text ends.
        /// </summary>
        private CellRange WithOverflow(GridSheet sheet, GridGeometry geo, CellRange used)
        {
            Dictionary<int, int> lastInRow = new Dictionary<int, int>();
            foreach ((int r, int c, GridCell cell) in sheet.Cells())
            {
                if (cell.DisplayText.Length > 0 || cell.Runs is not null)
                {
                    lastInRow[r] = lastInRow.TryGetValue(r, out int lc) ? Math.Max(lc, c) : c;
                }
            }
            int last = used.LastColumn;
            foreach ((int r, int c) in lastInRow)
            {
                GridCell cell = sheet.GetCell(r, c)!;
                CellHorizontalAlignment h = EffectiveAlignment(cell);
                if (cell.Kind == CellValueKind.Number || cell.Style.WrapText || cell.Style.ShrinkToFit
                    || h is not (CellHorizontalAlignment.Left or CellHorizontalAlignment.General or CellHorizontalAlignment.Center))
                {
                    continue;
                }
                double pad = CellPadding(1);
                double colW = geo.ColumnWidth(c);
                FlowParagraph p = CellParagraph(cell, 1, Math.Max(1, colW - (2 * pad)), geo);
                (_, List<FittedLine> lines) = MeasureCell(p, 1e7);
                double needed = (lines.Count > 0 ? lines[0].ContentEndX - lines[0].StartX : 0) + (2 * pad);
                double right = h == CellHorizontalAlignment.Center ? colW + ((needed - colW) / 2) : needed;
                int col = c;
                double acc = colW;
                while (acc < right - 0.01 && col < 16383)
                {
                    col++;
                    acc += geo.ColumnWidth(col);
                }
                last = Math.Max(last, col);
            }
            return last == used.LastColumn ? used : new CellRange(used.FirstRow, used.FirstColumn, used.LastRow, last);
        }

        public LayoutSourceInfo Source { get; }

        public int EstimatedPageCount => _pages.Count;

        public bool IsPageCountFinal => true;

        public int KnownPageCount => _pages.Count;

        public void EnsurePages(int count, CancellationToken ct)
        {
        }

        internal IReadOnlyList<GridPageRecord> Pages => _pages;

        private static (double Width, double Height) Printable(GridPageSetup ps)
            => (Math.Max(1, ps.PageWidth - ps.MarginLeft - ps.MarginRight), Math.Max(1, ps.PageHeight - ps.MarginTop - ps.MarginBottom));

        private IEnumerable<GridPageRecord> PaginateArea(int s, GridSheet sheet, GridGeometry geo, CellRange area, ref int pageNumber, ref int sheetPageIndex, CancellationToken ct)
        {
            GridPageSetup ps = sheet.PageSetup;
            (double pw, double ph) = Printable(ps);
            double headingW = ps.PrintHeadings ? HeadingColumnWidth(geo, area) : 0;
            double headingH = ps.PrintHeadings ? sheet.DefaultRowHeight : 0;

            double scale;
            bool honourBreaks;
            if (ps.FitToPage)
            {
                honourBreaks = false; // Excel ignores manual breaks when fitting to pages.
                scale = FitScale(sheet, geo, area, pw, ph, headingW, headingH, ps, ct);
            }
            else
            {
                honourBreaks = true;
                scale = Math.Clamp(ps.Scale, 10, 400) / 100.0;
            }

            List<(int, int)> colBands = Bands(area.FirstColumn, area.LastColumn, c => geo.ColumnWidth(c) * scale,
                pw - (headingW * scale), TitleExtent(sheet.PrintTitleColumns, geo.ColumnWidth) * scale, sheet.PrintTitleColumns?.Last ?? -1,
                honourBreaks ? sheet.ColumnBreaks : null, ct);
            List<(int, int)> rowBands = Bands(area.FirstRow, area.LastRow, r => geo.RowHeight(r) * scale,
                ph - (headingH * scale), TitleExtent(sheet.PrintTitleRows, geo.RowHeight) * scale, sheet.PrintTitleRows?.Last ?? -1,
                honourBreaks ? sheet.RowBreaks : null, ct);

            List<GridPageRecord> result = new List<GridPageRecord>();
            if (ps.PageOrder == GridPageOrder.DownThenOver)
            {
                foreach ((int c0, int c1) in colBands)
                {
                    foreach ((int r0, int r1) in rowBands)
                    {
                        result.Add(Record(s, sheet, r0, r1, c0, c1, scale, pageNumber++, sheetPageIndex++));
                    }
                }
            }
            else
            {
                foreach ((int r0, int r1) in rowBands)
                {
                    foreach ((int c0, int c1) in colBands)
                    {
                        result.Add(Record(s, sheet, r0, r1, c0, c1, scale, pageNumber++, sheetPageIndex++));
                    }
                }
            }
            return result;
        }

        private static GridPageRecord Record(int s, GridSheet sheet, int r0, int r1, int c0, int c1, double scale, int number, int index)
        {
            bool titleRows = sheet.PrintTitleRows is (int _, int tl) && r0 > tl;
            bool titleCols = sheet.PrintTitleColumns is (int _, int cl) && c0 > cl;
            return new GridPageRecord(s, r0, r1, c0, c1, scale, titleRows, titleCols, number, index);
        }

        private static double TitleExtent((int First, int Last)? titles, Func<int, double> size)
        {
            if (titles is not (int f, int l))
            {
                return 0;
            }
            double sum = 0;
            for (int i = f; i <= l; i++)
            {
                sum += size(i);
            }
            return sum;
        }

        /// <summary>
        /// Splits [first, last] into bands that fit <paramref name="available"/>. Bands that start after the
        /// title range lose <paramref name="titleExtent"/>. A band always holds at least one item; a manual break
        /// before index b starts a new band at b.
        /// </summary>
        internal static List<(int First, int Last)> Bands(int first, int last, Func<int, double> size, double available,
            double titleExtent, int titleLast, ICollection<int>? breaks, CancellationToken ct)
        {
            List<(int, int)> bands = new List<(int, int)>();
            int start = first;
            while (start <= last)
            {
                double room = start > titleLast && titleExtent > 0 ? available - titleExtent : available;
                double used = 0;
                int i = start;
                while (i <= last)
                {
                    if (((i - start) & 1023) == 0)
                    {
                        ct.ThrowIfCancellationRequested();
                    }
                    if (i > start && breaks is not null && breaks.Contains(i))
                    {
                        break;
                    }
                    double w = size(i);
                    if (i > start && used + w > room + Epsilon)
                    {
                        break;
                    }
                    used += w;
                    i++;
                }
                bands.Add((start, i - 1));
                start = i;
            }
            return bands;
        }

        /// <summary>
        /// Excel fit-to-page: the largest whole-percent scale (10..100) at which the area needs at most
        /// FitToWidth pages across and FitToHeight pages down (0 = unconstrained).
        /// </summary>
        private static double FitScale(GridSheet sheet, GridGeometry geo, CellRange area, double pw, double ph, double headingW, double headingH, GridPageSetup ps, CancellationToken ct)
        {
            double totalW = 0;
            for (int c = area.FirstColumn; c <= area.LastColumn; c++)
            {
                totalW += geo.ColumnWidth(c);
            }
            double totalH = 0;
            for (int r = area.FirstRow; r <= area.LastRow; r++)
            {
                totalH += geo.RowHeight(r);
            }
            double guess = 1.0;
            if (ps.FitToWidth > 0 && totalW + headingW > 0)
            {
                guess = Math.Min(guess, ps.FitToWidth * pw / (totalW + headingW));
            }
            if (ps.FitToHeight > 0 && totalH + headingH > 0)
            {
                guess = Math.Min(guess, ps.FitToHeight * ph / (totalH + headingH));
            }
            int percent = Math.Clamp((int)Math.Floor((guess * 100) + 1e-9), 10, 100);
            for (; percent > 10; percent--)
            {
                double s = percent / 100.0;
                int across = Bands(area.FirstColumn, area.LastColumn, c => geo.ColumnWidth(c) * s, pw - (headingW * s),
                    TitleExtent(sheet.PrintTitleColumns, geo.ColumnWidth) * s, sheet.PrintTitleColumns?.Last ?? -1, null, ct).Count;
                int down = Bands(area.FirstRow, area.LastRow, r => geo.RowHeight(r) * s, ph - (headingH * s),
                    TitleExtent(sheet.PrintTitleRows, geo.RowHeight) * s, sheet.PrintTitleRows?.Last ?? -1, null, ct).Count;
                if ((ps.FitToWidth <= 0 || across <= ps.FitToWidth) && (ps.FitToHeight <= 0 || down <= ps.FitToHeight))
                {
                    break;
                }
            }
            return percent / 100.0;
        }

        private static double HeadingColumnWidth(GridGeometry geo, CellRange area)
        {
            int digits = Math.Max(1, (area.LastRow + 1).ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
            return GridGeometry.PixelsToPoints((digits * geo.MaxDigitWidth) + 8);
        }

        /// <summary>Rows without a stored height that contain wrapped text grow to fit it (as Excel auto-fits on entry).</summary>
        private void AutoFitWrappedRows(int s, GridSheet sheet, GridGeometry geo, Dictionary<int, List<int>> rows, CancellationToken ct)
        {
            foreach (KeyValuePair<int, List<int>> row in rows)
            {
                if (geo.HasStoredHeight(row.Key))
                {
                    continue;
                }
                double needed = 0;
                foreach (int c in row.Value)
                {
                    GridCell cell = sheet.GetCell(row.Key, c)!;
                    if (!cell.Style.WrapText || cell.DisplayText.Length == 0 || InMerge(sheet, row.Key, c))
                    {
                        continue;
                    }
                    ct.ThrowIfCancellationRequested();
                    double width = geo.ColumnWidth(c) - (2 * CellPadding(1));
                    FlowParagraph p = CellParagraph(cell, 1, width, geo);
                    (MeasuredParagraph m, List<FittedLine> lines) = MeasureCell(p, Math.Max(1, width));
                    double h = 0;
                    foreach (FittedLine l in lines)
                    {
                        h += l.Height;
                    }
                    needed = Math.Max(needed, h + GridGeometry.PixelsToPoints(2));
                }
                if (needed > 0)
                {
                    geo.SetAutoRowHeight(row.Key, needed);
                }
            }
        }

        internal static bool InMerge(GridSheet sheet, int r, int c)
        {
            foreach (CellRange m in sheet.MergedRanges)
            {
                if (m.Contains(r, c))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Horizontal text padding inside a cell: 2 px at 96 dpi, scaled.</summary>
        internal static double CellPadding(double scale) => GridGeometry.PixelsToPoints(2) * scale;

        internal (MeasuredParagraph Measured, List<FittedLine> Lines) MeasureCell(FlowParagraph p, double width)
        {
            MeasuredParagraph m = ParagraphMeasurer.Measure(p, _ctx, _fields);
            return (m, LineFitter.Fit(m, width, 36));
        }

        private sealed class FieldStub : IFieldValues
        {
            public string Evaluate(FieldInline field) => field.CachedResult ?? string.Empty;
        }
    }
}
