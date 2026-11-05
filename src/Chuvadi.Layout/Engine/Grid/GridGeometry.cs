using System;
using System.Collections.Generic;
using Chuvadi.Layout.Fonts;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.Engine.Grid
{
    /// <summary>
    /// Unscaled sheet geometry in points: Excel column widths (from character units via the max digit width)
    /// and row heights (stored, default, or auto-fitted for wrapped text).
    /// </summary>
    internal sealed class GridGeometry
    {
        private readonly GridSheet _sheet;
        private readonly Dictionary<int, double> _autoRowHeights = new Dictionary<int, double>();
        private readonly double _defaultColumnWidthPt;

        public GridGeometry(GridSheet sheet, LayoutContext ctx)
        {
            _sheet = sheet;
            FontResolution normal = ctx.Fonts.Resolve(new FontRequest(sheet.NormalFont.FontFamily, sheet.NormalFont.Bold ? 700 : 400, sheet.NormalFont.Italic, sheet.NormalFont.GenericFamily));
            MaxDigitWidth = normal.Kind != FontMatchKind.Exact && SubstitutedDigitWidths.TryGetValue(sheet.NormalFont.FontFamily, out double em)
                ? Math.Max(1, (int)Math.Floor(em * (int)Math.Round(sheet.NormalFont.FontSize * 96 / 72.0, MidpointRounding.AwayFromZero)))
                : normal.Face.MaxDigitWidthPixels(sheet.NormalFont.FontSize);
            if (sheet.DefaultColumnWidth is double explicitDefault)
            {
                _defaultColumnWidthPt = PixelsToPoints(WidthToPixels(explicitDefault, MaxDigitWidth));
            }
            else
            {
                // From baseColWidth (default 8), as ECMA-376 Part 1 18.3.1.81 gives it: baseColWidth x MDW + 5 px
                // (Calibri 11: 8 x 7 + 5 = 61 px). Excel rounds this up to a multiple of 8 (64 px); Layout does not (W1).
                _defaultColumnWidthPt = PixelsToPoints(WidthToPixels(DefaultWidthFromBase(sheet.BaseColumnWidth, MaxDigitWidth), MaxDigitWidth));
            }
        }

        /// <summary>
        /// F7c: the widest digit of fonts Layout substitutes with different widths, in em, so that a workbook whose Normal font
        /// is missing still gets the column widths the standard gives with the real font. Cambria: 554/1000 em, from the
        /// glyph widths Word embeds in its PDF (calib-excel-cambria11, 3 Nov 2026).
        /// </summary>
        private static readonly Dictionary<string, double> SubstitutedDigitWidths = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["Cambria"] = 0.554,
        };

        /// <summary>Excel max digit width of the Normal font, in pixels at 96 dpi.</summary>
        public int MaxDigitWidth { get; }

        public double DefaultColumnWidthPt => _defaultColumnWidthPt;

        /// <summary>ECMA-376 18.3.1.13: pixels = Truncate(((256 × width + Truncate(128 / MDW)) / 256) × MDW).</summary>
        public static int WidthToPixels(double width, int mdw)
            => (int)Math.Truncate(((256 * width) + Math.Truncate(128.0 / mdw)) / 256 * mdw);

        /// <summary>Default column width from baseColWidth: Truncate((base × MDW + 5) / MDW × 256) / 256 characters.</summary>
        public static double DefaultWidthFromBase(int baseColWidth, int mdw)
            => Math.Truncate(((baseColWidth * mdw) + 5.0) / mdw * 256) / 256;

        public static double PixelsToPoints(int px) => px * 0.75;

        public double ColumnWidth(int column)
        {
            if (_sheet.Columns.TryGetValue(column, out GridColumn c))
            {
                return c.Hidden ? 0 : PixelsToPoints(WidthToPixels(c.Width, MaxDigitWidth));
            }
            return _defaultColumnWidthPt;
        }

        public double RowHeight(int row)
        {
            if (_sheet.Rows.TryGetValue(row, out GridRow r))
            {
                if (r.Hidden)
                {
                    return 0;
                }
                if (r.Height is double h)
                {
                    return h;
                }
            }
            else if (_sheet.RowsHiddenByDefault)
            {
                return 0;
            }
            return _autoRowHeights.TryGetValue(row, out double auto) ? auto : _sheet.DefaultRowHeight;
        }

        /// <summary>Records an auto-fitted height for a row without a stored height.</summary>
        public void SetAutoRowHeight(int row, double height)
        {
            if (!_sheet.Rows.TryGetValue(row, out GridRow r) || r.Height is null)
            {
                _autoRowHeights[row] = Math.Max(_sheet.DefaultRowHeight, height);
            }
        }

        private readonly List<double> _colStarts = new List<double> { 0 };
        private readonly List<double> _rowStarts = new List<double> { 0 };

        /// <summary>Distance in points from the sheet's left edge to the left edge of <paramref name="column"/> (unscaled).</summary>
        public double ColumnStart(int column)
        {
            column = Math.Clamp(column, 0, 16384);
            while (_colStarts.Count <= column)
            {
                _colStarts.Add(_colStarts[^1] + ColumnWidth(_colStarts.Count - 1));
            }
            return _colStarts[column];
        }

        /// <summary>Distance in points from the sheet's top edge to the top of <paramref name="row"/> (unscaled).</summary>
        public double RowStart(int row)
        {
            row = Math.Clamp(row, 0, 1048576);
            while (_rowStarts.Count <= row)
            {
                _rowStarts.Add(_rowStarts[^1] + RowHeight(_rowStarts.Count - 1));
            }
            return _rowStarts[row];
        }

        public bool HasStoredHeight(int row) => _sheet.Rows.TryGetValue(row, out GridRow r) && (r.Height is not null || r.Hidden);
    }
}
