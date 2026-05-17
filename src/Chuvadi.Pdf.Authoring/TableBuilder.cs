// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.3 — Authoring module

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Authoring;

/// <summary>
/// Fluent table builder. Configures columns, header/cell styling, and rows;
/// <see cref="Render"/> commits the table to the page (and may overflow to a
/// continuation rendered on a subsequent page).
/// </summary>
public sealed class TableBuilder
{
    private readonly PageBuilder _page;
    private readonly double _x;
    private readonly double _yStart;
    private readonly double _width;
    private readonly List<Column> _columns = new();
    private readonly List<string[]> _rows = new();

    private string _font = StandardFonts.Helvetica;
    private double _fontSize = 10;
    private Color _textColor = Colors.Black;
    private bool _headerBold = true;
    private Color _headerBackground = Colors.LightGray;
    private double _cellPadding = 4;
    private BorderStyle _border = BorderStyle.Single;
    private Color _borderColor = Colors.Gray;
    private double _borderWidth = 0.5;
    private double _rowHeight;   // 0 = auto from font size + padding

    internal TableBuilder(PageBuilder page, double x, double y, double width)
    {
        _page = page;
        _x = x;
        _yStart = y;
        _width = width;
    }

    /// <summary>Adds a column with the given header label and width fraction (0..1 of total).</summary>
    public TableBuilder AddColumn(string header, double widthFraction)
    {
        ArgumentNullException.ThrowIfNull(header);
        if (widthFraction <= 0 || widthFraction > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(widthFraction), "Must be in (0, 1].");
        }
        _columns.Add(new Column(header, widthFraction));
        return this;
    }

    /// <summary>Configures the table font and size.</summary>
    public TableBuilder Font(string font, double size, Color? textColor = null)
    {
        _font = font;
        _fontSize = size;
        if (textColor is Color c) { _textColor = c; }
        return this;
    }

    /// <summary>Configures header row style.</summary>
    public TableBuilder HeaderStyle(bool bold = true, Color? background = null)
    {
        _headerBold = bold;
        if (background is Color c) { _headerBackground = c; }
        return this;
    }

    /// <summary>Sets cell padding (points). Default 4.</summary>
    public TableBuilder CellPadding(double padding)
    {
        _cellPadding = padding;
        return this;
    }

    /// <summary>Sets explicit row height (0 = auto from font size + padding).</summary>
    public TableBuilder RowHeight(double height)
    {
        _rowHeight = height;
        return this;
    }

    /// <summary>Sets table border style.</summary>
    public TableBuilder Border(BorderStyle style, Color color, double width = 0.5)
    {
        _border = style;
        _borderColor = color;
        _borderWidth = width;
        return this;
    }

    /// <summary>Adds a row. Cell count must match the column count.</summary>
    public TableBuilder AddRow(params string[] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        if (cells.Length != _columns.Count)
        {
            throw new ArgumentException(
                $"Row has {cells.Length} cells; table has {_columns.Count} columns.", nameof(cells));
        }
        _rows.Add(cells);
        return this;
    }

    /// <summary>
    /// Renders the table onto the page. Returns the Y position immediately below
    /// the last drawn row.
    /// </summary>
    /// <remarks>
    /// If the table overflows the page, the overflow is returned via the
    /// result; the caller is responsible for adding a new page and re-rendering
    /// the remaining rows. v1 does not auto-add pages, but the API surface is
    /// designed so v1.3.1 can without breaking existing callers.
    /// </remarks>
    public TableRenderResult Render()
    {
        if (_columns.Count == 0)
        {
            throw new InvalidOperationException("Table has no columns.");
        }

        double rowH = _rowHeight > 0 ? _rowHeight : _fontSize + (_cellPadding * 2);
        double bottom = _page.Height - 20;   // 20pt margin from bottom
        double yCursor = _yStart;

        // Header
        DrawHeader(_x, yCursor, rowH);
        yCursor += rowH;

        // Rows
        int rowsDrawn = 0;
        foreach (string[] row in _rows)
        {
            if (yCursor + rowH > bottom) { break; }
            DrawRow(_x, yCursor, rowH, row);
            yCursor += rowH;
            rowsDrawn++;
        }

        // Outer border
        if (_border == BorderStyle.Single)
        {
            double totalH = rowH * (1 + rowsDrawn);
            _page.DrawRectangle(_x, _yStart, _width, totalH,
                fill: null, stroke: _borderColor, strokeWidth: _borderWidth);
        }

        bool overflow = rowsDrawn < _rows.Count;
        List<string[]> remaining = new();
        if (overflow)
        {
            for (int i = rowsDrawn; i < _rows.Count; i++) { remaining.Add(_rows[i]); }
        }

        return new TableRenderResult
        {
            HasOverflow = overflow,
            RemainingRows = remaining,
            NextYFromTop = yCursor,
        };
    }

    private void DrawHeader(double xStart, double yTop, double rowH)
    {
        // Background fill
        _page.DrawRectangle(xStart, yTop, _width, rowH,
            fill: _headerBackground, stroke: null);
        // Header text
        double xCursor = xStart;
        string headerFont = _headerBold && _font == StandardFonts.Helvetica
            ? StandardFonts.HelveticaBold
            : _font;
        foreach (Column col in _columns)
        {
            double cw = _width * col.WidthFraction;
            _page.DrawText(col.Header,
                xCursor + _cellPadding, yTop + _cellPadding,
                headerFont, _fontSize, _textColor);
            xCursor += cw;
        }
        // Column separator lines
        if (_border == BorderStyle.Single)
        {
            DrawColumnSeparators(xStart, yTop, rowH);
            // Bottom border of header row
            _page.DrawLine(xStart, yTop + rowH, xStart + _width, yTop + rowH,
                _borderColor, _borderWidth);
        }
    }

    private void DrawRow(double xStart, double yTop, double rowH, string[] cells)
    {
        double xCursor = xStart;
        for (int i = 0; i < cells.Length; i++)
        {
            double cw = _width * _columns[i].WidthFraction;
            _page.DrawText(cells[i],
                xCursor + _cellPadding, yTop + _cellPadding,
                _font, _fontSize, _textColor);
            xCursor += cw;
        }
        if (_border == BorderStyle.Single)
        {
            DrawColumnSeparators(xStart, yTop, rowH);
            // Bottom row border
            _page.DrawLine(xStart, yTop + rowH, xStart + _width, yTop + rowH,
                _borderColor, _borderWidth);
        }
    }

    private void DrawColumnSeparators(double xStart, double yTop, double rowH)
    {
        double xCursor = xStart;
        for (int i = 0; i < _columns.Count - 1; i++)
        {
            xCursor += _width * _columns[i].WidthFraction;
            _page.DrawLine(xCursor, yTop, xCursor, yTop + rowH,
                _borderColor, _borderWidth);
        }
    }

    private sealed record Column(string Header, double WidthFraction);
}

/// <summary>Outcome of rendering a table; may contain overflow rows.</summary>
public sealed class TableRenderResult
{
    /// <summary>True when not all rows fit on the page.</summary>
    public bool HasOverflow { get; init; }

    /// <summary>The rows that didn't fit. Empty when <see cref="HasOverflow"/> is false.</summary>
    public IReadOnlyList<string[]> RemainingRows { get; init; } = System.Array.Empty<string[]>();

    /// <summary>Y position immediately below the last drawn row.</summary>
    public double NextYFromTop { get; init; }
}
