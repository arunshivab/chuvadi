using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Chuvadi.Layout.Drawing;
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
    /// The pages of one print area (D-291): its column and row bands, scale and page order. Page <c>i</c> of the
    /// area is computed from the bands, so no per-page record is kept; a sheet with millions of pages opens at the
    /// cost of its bands.
    /// </summary>
    internal sealed class GridAreaPages
    {
        public GridAreaPages(int sheet, CellRange area, IReadOnlyList<(int First, int Last)> columnBands, IReadOnlyList<(int First, int Last)> rowBands,
            double scale, bool downThenOver, int firstPageNumber, int firstSheetPageIndex, long firstIndex)
        {
            Sheet = sheet;
            Area = area;
            ColumnBands = columnBands;
            RowBands = rowBands;
            Scale = scale;
            DownThenOver = downThenOver;
            FirstPageNumber = firstPageNumber;
            FirstSheetPageIndex = firstSheetPageIndex;
            FirstIndex = firstIndex;
        }

        public int Sheet { get; }

        public CellRange Area { get; }

        public IReadOnlyList<(int First, int Last)> ColumnBands { get; }

        public IReadOnlyList<(int First, int Last)> RowBands { get; }

        public double Scale { get; }

        public bool DownThenOver { get; }

        public int FirstPageNumber { get; }

        public int FirstSheetPageIndex { get; }

        /// <summary>Document index of this area's first page.</summary>
        public long FirstIndex { get; }

        public long Count => (long)ColumnBands.Count * RowBands.Count;

        /// <summary>The bands of the area's page <paramref name="offset"/> (0-based within the area).</summary>
        public ((int First, int Last) Rows, (int First, int Last) Columns) Bands(long offset)
        {
            int rowCount = RowBands.Count;
            int colCount = ColumnBands.Count;
            return DownThenOver
                ? (RowBands[(int)(offset % rowCount)], ColumnBands[(int)(offset / rowCount)])
                : (RowBands[(int)(offset / colCount)], ColumnBands[(int)(offset % colCount)]);
        }
    }

    /// <summary>
    /// Paginates a <see cref="GridWorkbook"/> the way Excel prints it. Page bands depend only on column widths,
    /// row heights, breaks and scale, so every page is known at open (the page count is final immediately). A page
    /// record is computed from its area's bands when the page is asked for (D-291), and cells are measured and drawn
    /// only for the page being built.
    /// </summary>
    internal sealed partial class GridEngine : IPageSource
    {
        private const double Epsilon = 0.01;

        private readonly GridWorkbook _book;
        private readonly LayoutContext _ctx;
        private readonly List<GridAreaPages> _areas = new List<GridAreaPages>();
        private readonly int _pageCount;
        private readonly Dictionary<int, GridGeometry> _geometry = new Dictionary<int, GridGeometry>();
        private readonly Dictionary<int, Dictionary<int, List<int>>> _rowIndex = new Dictionary<int, Dictionary<int, List<int>>>();
        private readonly FieldStub _fields = new FieldStub();

        public GridEngine(GridWorkbook book, LayoutContext ctx, LayoutSourceInfo source, CancellationToken ct)
        {
            _book = book;
            _ctx = ctx;
            Source = source;
            int pageNumber = 1;
            long total = 0;
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
                AutoFitRows(s, sheet, geo, rows, ct);

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
                    GridAreaPages pages = PaginateArea(s, sheet, geo, area, pageNumber, sheetPageIndex, total, ct);
                    _areas.Add(pages);
                    total += pages.Count;
                    if (total > int.MaxValue)
                    {
                        // More pages than a document can index: refused at open, whatever MaxPages is.
                        throw PageLimit(int.MaxValue, total, pages);
                    }
                    pageNumber = (int)Math.Min(int.MaxValue, pageNumber + pages.Count);
                    sheetPageIndex += (int)pages.Count;
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
            _pageCount = (int)total;
        }

        /// <summary>
        /// The range Excel prints when the sheet has no print area (D-291, measured on 25 sheets with Excel 2021):
        /// from A1 to the last row and the last column that hold something printed, that is a value, a data bar or
        /// icon, a visible fill, a visible border (diagonals included), a merge or a drawing. Formats with nothing to see (font, colour,
        /// number format, alignment, wrap, indent, protection) on an empty cell do not extend it. Null when the sheet
        /// prints nothing.
        /// </summary>
        private static CellRange? PrintedRange(GridSheet sheet, GridGeometry geo)
        {
            int lastRow = -1;
            int lastColumn = -1;
            foreach ((int r, int c, GridCell cell) in sheet.Cells())
            {
                if (Prints(cell))
                {
                    lastRow = Math.Max(lastRow, r);
                    lastColumn = Math.Max(lastColumn, c);
                }
            }
            foreach (CellRange m in sheet.MergedRanges)
            {
                lastRow = Math.Max(lastRow, m.LastRow);
                lastColumn = Math.Max(lastColumn, m.LastColumn);
            }
            foreach (GridDrawing d in sheet.Drawings)
            {
                if (LastCell(d, geo) is not (int lr, int lc))
                {
                    continue;
                }
                lastRow = Math.Max(lastRow, Math.Max(d.From?.Row ?? 0, lr));
                lastColumn = Math.Max(lastColumn, Math.Max(d.From?.Column ?? 0, lc));
            }
            return lastRow < 0 ? null : new CellRange(0, 0, lastRow, lastColumn);
        }

        /// <summary>
        /// True when the cell puts something on the page or holds a value: a value of any kind (even one its number
        /// format hides), rich text, a data bar or icon, a visible fill or a visible border.
        /// </summary>
        internal static bool Prints(GridCell cell)
        {
            if (cell.Kind != CellValueKind.Empty || cell.DisplayText.Length > 0 || cell.Runs is not null || cell.DataBar is not null || cell.Icon is not null)
            {
                return true;
            }
            GridCellStyle st = cell.Style;
            return (st.Fill is LayoutColor fill && !fill.IsTransparent)
                || st.BorderTop.IsVisible || st.BorderBottom.IsVisible || st.BorderLeft.IsVisible || st.BorderRight.IsVisible
                || st.BorderDiagonalUp.IsVisible || st.BorderDiagonalDown.IsVisible;
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

        public int EstimatedPageCount => _pageCount;

        public bool IsPageCountFinal => true;

        public int KnownPageCount => _pageCount;

        public void EnsurePages(int count, CancellationToken ct)
        {
        }

        /// <summary>The print areas, in page order.</summary>
        internal IReadOnlyList<GridAreaPages> Areas => _areas;

        /// <summary>
        /// The record of page <paramref name="index"/>, computed from its area's bands. Building a page at or past
        /// <see cref="LayoutLimits.MaxPages"/> throws (D-291): the limit applies to pages built, exported or printed,
        /// not to opening.
        /// </summary>
        internal GridPageRecord Record(int index)
        {
            GridAreaPages area = AreaOf(index);
            if (index >= _ctx.Limits.MaxPages)
            {
                throw PageLimit(_ctx.Limits.MaxPages, _pageCount, area);
            }
            return RecordIn(area, index);
        }

        /// <summary>The record of page <paramref name="index"/> without the MaxPages check (tests and diagnostics).</summary>
        internal GridPageRecord RecordUnchecked(int index) => RecordIn(AreaOf(index), index);

        private GridAreaPages AreaOf(int index)
        {
            if (index < 0 || index >= _pageCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index), $"The document has {_pageCount} page(s).");
            }
            int lo = 0;
            int hi = _areas.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (_areas[mid].FirstIndex <= index)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid - 1;
                }
            }
            return _areas[lo];
        }

        private GridPageRecord RecordIn(GridAreaPages area, int index)
        {
            long offset = index - area.FirstIndex;
            ((int r0, int r1), (int c0, int c1)) = area.Bands(offset);
            GridSheet sheet = _book.Sheets[area.Sheet];
            return Record(area.Sheet, sheet, r0, r1, c0, c1, area.Scale,
                (int)Math.Min(int.MaxValue, area.FirstPageNumber + offset), (int)(area.FirstSheetPageIndex + offset));
        }

        private LayoutLimitExceededException PageLimit(long limit, long total, GridAreaPages area)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            string location = _book.Sheets[area.Sheet].Name + "!" + area.Area.ToString();
            string message = string.Format(inv,
                "Layout limit 'MaxPages' exceeded: the workbook prints as {0:N0} pages and the limit is {1:N0}. {2} prints as {3:N0} × {4:N0} pages ({5:N0}).",
                total, limit, location, area.ColumnBands.Count, area.RowBands.Count, area.Count);
            return new LayoutLimitExceededException(nameof(LayoutLimits.MaxPages), limit, total, message, location);
        }

        private static (double Width, double Height) Printable(GridPageSetup ps)
            => (Math.Max(1, ps.PageWidth - ps.MarginLeft - ps.MarginRight), Math.Max(1, ps.PageHeight - ps.MarginTop - ps.MarginBottom));

        private static GridAreaPages PaginateArea(int s, GridSheet sheet, GridGeometry geo, CellRange area, int pageNumber, int sheetPageIndex, long firstIndex, CancellationToken ct)
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

            return new GridAreaPages(s, area, colBands, rowBands, scale, ps.PageOrder == GridPageOrder.DownThenOver,
                pageNumber, sheetPageIndex, firstIndex);
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

        /// <summary>
        /// Rows without a stored height grow to fit their text (as Excel fits such rows when it opens a file):
        /// wrapped text by its lines, other text by the height of its line in the font it is drawn with. A line that
        /// fits within the default row height leaves the row at the default; otherwise the row is the line height
        /// plus 2 px, to the nearest whole pixel at 96 dpi. Cells in a merge that spans more than one row are skipped;
        /// a merge within one row counts as one line. Rows never go below the default height.
        /// </summary>
        private void AutoFitRows(int s, GridSheet sheet, GridGeometry geo, Dictionary<int, List<int>> rows, CancellationToken ct)
        {
            double padding = GridGeometry.PixelsToPoints(2);
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
                    if (cell.DisplayText.Length == 0)
                    {
                        continue;
                    }
                    bool merged = InMerge(sheet, row.Key, c);
                    if (merged && !InSingleRowMerge(sheet, row.Key, c))
                    {
                        continue;
                    }
                    ct.ThrowIfCancellationRequested();
                    bool wrapped = cell.Style.WrapText && !merged;
                    double width = wrapped ? geo.ColumnWidth(c) - (2 * CellPadding(1)) : OneLineWidth;
                    FlowParagraph p = CellParagraph(cell, 1, width, geo);
                    (MeasuredParagraph m, List<FittedLine> lines) = MeasureCell(p, Math.Max(1, width));
                    if (wrapped)
                    {
                        double h = 0;
                        foreach (FittedLine l in lines)
                        {
                            h += l.Height;
                        }
                        needed = Math.Max(needed, h + padding);
                        continue;
                    }
                    double line = 0;
                    foreach (FittedLine l in lines)
                    {
                        line = Math.Max(line, l.Height);
                    }
                    if (line <= sheet.DefaultRowHeight)
                    {
                        continue; // the line fits the default row: the row stays at the default
                    }
                    int px = (int)Math.Round((line + padding) / 0.75, MidpointRounding.AwayFromZero);
                    needed = Math.Max(needed, GridGeometry.PixelsToPoints(px));
                }
                if (needed > 0)
                {
                    geo.SetAutoRowHeight(row.Key, needed);
                }
            }
        }

        /// <summary>The width a cell that does not wrap is measured at: wide enough that its text stays on one line.</summary>
        private const double OneLineWidth = 1e6;

        /// <summary>True when the cell is in a merged range that spans only its own row.</summary>
        internal static bool InSingleRowMerge(GridSheet sheet, int r, int c)
        {
            foreach (CellRange m in sheet.MergedRanges)
            {
                if (m.Contains(r, c))
                {
                    return m.FirstRow == m.LastRow;
                }
            }
            return false;
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
