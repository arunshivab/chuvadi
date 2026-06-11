using System;

namespace Chuvadi.Sheets.Excel;

/// <summary>
/// Ergonomic fluent builder for <see cref="CellStyle"/>. Chain method calls to set properties,
/// then call <see cref="Build"/> to obtain the immutable result.
///
/// <code>
/// var headerStyle = new CellStyleBuilder()
///     .Font("Calibri", 11)
///     .Bold()
///     .Foreground("#FFFFFF")
///     .Background("#1A237E")
///     .HAlign(HorizontalAlign.Center)
///     .Build();
/// </code>
///
/// The builder is mutable; the result of <see cref="Build"/> is not. Each call to Build
/// returns an independent value, so the builder may be reused.
/// </summary>
public sealed class CellStyleBuilder
{
    private string? _fontName;
    private double? _fontSize;
    private bool _bold;
    private bool _italic;
    private bool _underline;
    private string? _fontColor;
    private string? _fillColor;
    private Border _borderTop;
    private Border _borderBottom;
    private Border _borderLeft;
    private Border _borderRight;
    private string? _numberFormat;
    private HorizontalAlign _hAlign;
    private VerticalAlign _vAlign;
    private bool _wrapText;

    /// <summary>Sets font name and size in a single call.</summary>
    public CellStyleBuilder Font(string name, double size)
    {
        _fontName = name;
        _fontSize = size;
        return this;
    }

    public CellStyleBuilder FontName(string name) { _fontName = name; return this; }
    public CellStyleBuilder FontSize(double size) { _fontSize = size; return this; }
    public CellStyleBuilder Bold(bool value = true) { _bold = value; return this; }
    public CellStyleBuilder Italic(bool value = true) { _italic = value; return this; }
    public CellStyleBuilder Underline(bool value = true) { _underline = value; return this; }

    /// <summary>Sets the font (text) color. Accepts "#RRGGBB" or "RRGGBB".</summary>
    public CellStyleBuilder Foreground(string hex) { _fontColor = NormalizeHex(hex); return this; }

    /// <summary>Sets the cell fill (background) color. Accepts "#RRGGBB" or "RRGGBB".</summary>
    public CellStyleBuilder Background(string hex) { _fillColor = NormalizeHex(hex); return this; }

    /// <summary>Sets a number format string (e.g. "0.00", "#,##0", "yyyy-mm-dd", "0.00%").</summary>
    public CellStyleBuilder Format(string formatCode) { _numberFormat = formatCode; return this; }

    /// <summary>Applies the same border to all four edges.</summary>
    public CellStyleBuilder BorderAll(BorderStyle style, string color)
    {
        var hex = NormalizeHex(color);
        var b = new Border(style, hex);
        _borderTop = b;
        _borderBottom = b;
        _borderLeft = b;
        _borderRight = b;
        return this;
    }

    public CellStyleBuilder BorderTop(BorderStyle style, string color)
        { _borderTop = new Border(style, NormalizeHex(color)); return this; }
    public CellStyleBuilder BorderBottom(BorderStyle style, string color)
        { _borderBottom = new Border(style, NormalizeHex(color)); return this; }
    public CellStyleBuilder BorderLeft(BorderStyle style, string color)
        { _borderLeft = new Border(style, NormalizeHex(color)); return this; }
    public CellStyleBuilder BorderRight(BorderStyle style, string color)
        { _borderRight = new Border(style, NormalizeHex(color)); return this; }

    public CellStyleBuilder HAlign(HorizontalAlign align) { _hAlign = align; return this; }
    public CellStyleBuilder VAlign(VerticalAlign align) { _vAlign = align; return this; }
    public CellStyleBuilder WrapText(bool value = true) { _wrapText = value; return this; }

    /// <summary>Materializes the immutable CellStyle.</summary>
    public CellStyle Build() => new(
        FontName: _fontName,
        FontSize: _fontSize,
        Bold: _bold,
        Italic: _italic,
        Underline: _underline,
        FontColor: _fontColor,
        FillColor: _fillColor,
        BorderTop: _borderTop,
        BorderBottom: _borderBottom,
        BorderLeft: _borderLeft,
        BorderRight: _borderRight,
        NumberFormat: _numberFormat,
        HAlign: _hAlign,
        VAlign: _vAlign,
        WrapText: _wrapText);

    // ---- Helpers -------------------------------------------------------------------

    /// <summary>
    /// Normalizes a color string to the no-hash uppercase form Excel writes ("FF0000").
    /// Accepts "#RRGGBB", "RRGGBB", "#rrggbb", "rrggbb". Returns null for null input.
    /// Throws on malformed input — Excel is strict and we'd rather surface this at build time.
    /// </summary>
    internal static string? NormalizeHex(string? color)
    {
        if (color is null) return null;
        var s = color.StartsWith('#') ? color.Substring(1) : color;
        if (s.Length != 6)
            throw new ArgumentException(
                $"Color must be a 6-digit hex RGB value like '#FF0000' or 'FF0000'. Got: '{color}'.",
                nameof(color));
        for (int i = 0; i < 6; i++)
        {
            var c = s[i];
            var isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!isHex)
                throw new ArgumentException(
                    $"Color contains non-hex character '{c}' in '{color}'.", nameof(color));
        }
        return s.ToUpperInvariant();
    }
}
