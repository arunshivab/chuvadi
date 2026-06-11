using System;

namespace Chuvadi.Sheets.Excel;

/// <summary>
/// Horizontal alignment for cell content. Matches OOXML's "horizontalAlignment" attribute values.
/// </summary>
public enum HorizontalAlign
{
    /// <summary>Default — Excel renders text left, numbers right, booleans centered.</summary>
    General = 0,
    Left,
    Center,
    Right,
    Fill,
    Justify,
    CenterContinuous,
    Distributed,
}

/// <summary>
/// Vertical alignment for cell content. Matches OOXML's "verticalAlignment" attribute values.
/// </summary>
public enum VerticalAlign
{
    Top = 0,
    Center,
    Bottom,
    Justify,
    Distributed,
}

/// <summary>
/// Visual style of a border line.
/// </summary>
public enum BorderStyle
{
    None = 0,
    Thin,
    Medium,
    Dashed,
    Dotted,
    Thick,
    Double,
    Hair,
}

/// <summary>
/// Describes one edge of a cell border (top, bottom, left, or right).
/// A border with <see cref="BorderStyle.None"/> is treated as "no border" and is
/// not written into styles.xml.
/// </summary>
public readonly record struct Border(BorderStyle Style, string? Color)
{
    /// <summary>Convenience: a "no border" value, equivalent to default(Border).</summary>
    public static readonly Border None = new(BorderStyle.None, null);

    /// <summary>True if this edge has no border (style is None).</summary>
    public bool IsNone => Style == BorderStyle.None;
}

/// <summary>
/// An immutable cell style. Use <see cref="CellStyleBuilder"/> to construct one ergonomically;
/// the resulting CellStyle is suitable for use as a dictionary key (value equality is built in).
///
/// Colors are 6-digit hex RGB strings *without* the leading '#', e.g. "FF0000" for red. The
/// builder accepts "#FF0000" or "FF0000"; both are normalized to the no-hash uppercase form.
///
/// A CellStyle with all default values (the result of <c>default(CellStyle)</c>) represents
/// the workbook default style and maps to cellXf index 0 in styles.xml.
/// </summary>
public readonly record struct CellStyle(
    // Font properties
    string? FontName,
    double? FontSize,
    bool Bold,
    bool Italic,
    bool Underline,
    string? FontColor,

    // Fill properties
    string? FillColor,

    // Border (four edges)
    Border BorderTop,
    Border BorderBottom,
    Border BorderLeft,
    Border BorderRight,

    // Number format
    string? NumberFormat,

    // Alignment
    HorizontalAlign HAlign,
    VerticalAlign VAlign,
    bool WrapText)
{
    /// <summary>
    /// The default style — corresponds to cellXf index 0 in styles.xml, which Excel always
    /// includes as the "Normal" style entry. Cells that don't specify a style use this.
    /// </summary>
    public static readonly CellStyle Default = default;

    /// <summary>True if every field is at its default value — i.e. no styling applied.</summary>
    public bool IsDefault => this == Default;
}
