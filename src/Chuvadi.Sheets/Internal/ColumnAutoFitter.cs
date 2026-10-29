using System;
using System.Collections.Generic;
using System.Globalization;
using Chuvadi.Sheets.Excel;

namespace Chuvadi.Sheets.Internal;

/// <summary>
/// Accumulates displayed-text widths per column while a sheet is written, then produces
/// column widths. Keeps the widest few cells per column (with their row numbers) so that
/// cells inside merges or filter header rows declared after the data can still be handled
/// at the end.
/// </summary>
internal sealed class ColumnAutoFitter
{
    private const int KeepPerColumn = 16;
    private const double FilterButtonPx = 16;     // Excel's drop-down button is ~16 px at 100%
    private const string DefaultFont = "Calibri";
    private const double DefaultSize = 11;

    private readonly AutoFitOptions _o;
    private readonly HashSet<int>? _only;
    private readonly NumberFormatOptions _fmt;
    private readonly int _mdw;   // workbook default font's max digit width, px
    private readonly Dictionary<int, List<(int Row, double Px)>> _widest = new();
    private readonly List<(int R1, int C1, int R2, int C2)> _merges = new();
    private int _rowsSeen;

    public ColumnAutoFitter(AutoFitOptions? options)
    {
        _o = options ?? AutoFitOptions.Default;
        _only = _o.Columns is null ? null : new HashSet<int>(_o.Columns);
        _fmt = _o.Format ?? NumberFormatOptions.Default;
        _mdw = FontMetrics.MaxDigitWidthPx(DefaultFont, DefaultSize);
    }

    public AutoFitOptions Options => _o;

    /// <summary>True when this row should be measured (honours MaxRowsToMeasure).</summary>
    public bool BeginRow()
    {
        _rowsSeen++;
        return _o.MaxRowsToMeasure is not int max || _rowsSeen <= max;
    }

    public bool Applies(int column) => _only is null || _only.Contains(column);

    /// <summary>Registers a merged range; cells inside it are not counted.</summary>
    public void AddMerge(string range)
    {
        if (TryParseRange(range, out var r)) _merges.Add(r);
    }

    /// <summary>Measures one cell. A null style means the workbook default (Calibri 11, General).</summary>
    public void Observe(int row, int column, object? value, CellStyle? style)
    {
        if (value is null || !Applies(column)) return;
        var s = style ?? default;
        if (s.WrapText) return;                                    // wraps instead of widening
        if (s.TextRotation != 0) return;                           // rotated / stacked text
        if (InMerge(row, column)) return;

        string text = value switch
        {
            string str => NumberFormatter.Format(str, s.NumberFormat, _fmt),
            _ => NumberFormatter.Format(value, s.NumberFormat, _fmt),
        };
        if (text.Length == 0) return;

        double size = s.FontSize ?? DefaultSize;
        double em = FontMetrics.MeasureEm(text, s.FontName ?? DefaultFont, s.Bold);
        double px = FontMetrics.Pixels(em, size);
        if (s.Indent > 0) px += s.Indent * 9.0 * size / DefaultSize;   // one indent level ≈ 9 px at 11 pt
        if (s.Italic) px += size * 0.15;                           // slant overhang on the last glyph

        if (!_widest.TryGetValue(column, out var list)) _widest[column] = list = new List<(int, double)>(4);
        if (list.Count < KeepPerColumn) { list.Add((row, px)); return; }
        int minIdx = 0;
        for (int i = 1; i < list.Count; i++) if (list[i].Px < list[minIdx].Px) minIdx = i;
        if (px > list[minIdx].Px) list[minIdx] = (row, px);
    }

    /// <summary>
    /// The fitted width per measured column, excluding cells in merges, adding room for
    /// filter buttons on the given header cells, clamped to [MinWidth, MaxWidth].
    /// </summary>
    public Dictionary<int, double> ComputeWidths(IEnumerable<(int Row, int C1, int C2)>? filterHeaders)
    {
        var headerCells = new HashSet<(int, int)>();
        if (filterHeaders is not null && _o.AllowForFilterButton)
            foreach (var (row, c1, c2) in filterHeaders)
                for (int c = c1; c <= c2; c++) headerCells.Add((row, c));

        var result = new Dictionary<int, double>();
        foreach (var (col, list) in _widest)
        {
            double best = -1;
            foreach (var (row, px0) in list)
            {
                if (InMerge(row, col)) continue;
                double px = headerCells.Contains((row, col)) ? px0 + FilterButtonPx : px0;
                if (px > best) best = px;
            }
            if (best < 0) continue;
            // Measured widths land on Excel's 1/256-character grid; the caps stay exact.
            double w = Math.Ceiling((FontMetrics.ColumnWidthForPixels(best, _mdw) + _o.ExtraWidth) * 256) / 256;
            result[col] = Math.Min(_o.MaxWidth, Math.Max(_o.MinWidth, w));
        }
        return result;
    }

    private bool InMerge(int row, int col)
    {
        foreach (var (r1, c1, r2, c2) in _merges)
            if (row >= r1 && row <= r2 && col >= c1 && col <= c2 && !(r1 == r2 && c1 == c2)) return true;
        return false;
    }

    /// <summary>Parses "A1:C3" (or "B2") into 1-based bounds; sheet-qualified or $-anchored refs allowed.</summary>
    internal static bool TryParseRange(string range, out (int R1, int C1, int R2, int C2) r)
    {
        r = default;
        if (string.IsNullOrWhiteSpace(range)) return false;
        var s = range.Replace("$", "");
        int bang = s.LastIndexOf('!');
        if (bang >= 0) s = s.Substring(bang + 1);
        var parts = s.Split(':');
        if (!TryCell(parts[0], out var a)) return false;
        var b = a;
        if (parts.Length > 1 && !TryCell(parts[1], out b)) return false;
        r = (Math.Min(a.Row, b.Row), Math.Min(a.Col, b.Col), Math.Max(a.Row, b.Row), Math.Max(a.Col, b.Col));
        return true;
    }

    private static bool TryCell(string s, out (int Row, int Col) cell)
    {
        cell = default;
        int i = 0, col = 0, row = 0;
        while (i < s.Length && char.IsLetter(s[i])) { col = col * 26 + (char.ToUpperInvariant(s[i]) - 'A' + 1); i++; }
        if (col == 0 || i == s.Length) return false;
        if (!int.TryParse(s.AsSpan(i), NumberStyles.None, CultureInfo.InvariantCulture, out row) || row < 1) return false;
        cell = (row, col);
        return true;
    }
}
