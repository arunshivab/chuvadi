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
/// Visual style of a border line. Covers all 14 SpreadsheetML border styles (ST_BorderStyle).
/// Members after <see cref="Hair"/> were added in 1.2.0; existing values are unchanged.
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
    MediumDashed,
    DashDot,
    MediumDashDot,
    DashDotDot,
    MediumDashDotDot,
    SlantDashDot,
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

    // -----------------------------------------------------------------------------------------
    // Extended properties (1.2.0). Init-only, outside the positional list, so existing
    // constructor calls keep compiling and binding. The read side populates them from a loaded
    // file. The WRITER DOES NOT YET EMIT THEM (except the new BorderStyle values): a load → save
    // round-trip keeps only the positional properties above. See CHANGELOG 1.2.0.
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Explicit vertical alignment. When set, it overrides <see cref="VAlign"/> and is always
    /// written. It exists because <see cref="VerticalAlign.Top"/> is the enum's default value,
    /// which the positional <see cref="VAlign"/> cannot tell apart from "not set": a style
    /// with <c>VAlign == Top</c> and this property null writes no vertical alignment (Excel
    /// then shows Bottom), exactly as in earlier versions. <see cref="CellStyleBuilder.VAlign"/>
    /// and loaded styles set it, so top alignment is written. Null = use <see cref="VAlign"/>.
    /// </summary>
    public VerticalAlign? VerticalAlignment { get; init; }

    /// <summary>
    /// The vertical alignment the writer emits, or null for none (Excel's default, Bottom):
    /// <see cref="VerticalAlignment"/> if set, else <see cref="VAlign"/> unless it is Top.
    /// </summary>
    internal VerticalAlign? EffectiveWriteVAlign
        => VerticalAlignment ?? (VAlign != VerticalAlign.Top ? VAlign : null);

    /// <summary>Kind of underline. <see cref="Underline"/> is true whenever this is not None.</summary>
    public UnderlineStyle UnderlineKind { get; init; }

    /// <summary>Strikethrough text.</summary>
    public bool Strikethrough { get; init; }

    /// <summary>Superscript / subscript position.</summary>
    public FontVerticalAlign FontVerticalPosition { get; init; }

    /// <summary>Font family class (1 Roman, 2 Swiss, 3 Modern, 4 Script, 5 Decorative), or null.</summary>
    public int? FontFamily { get; init; }

    /// <summary>Font character set, or null.</summary>
    public int? FontCharset { get; init; }

    /// <summary>Theme font scheme the face follows (major/minor), or None.</summary>
    public FontScheme FontScheme { get; init; }

    /// <summary>
    /// Fill pattern, or null when unspecified. <see cref="FillColor"/> is set only for
    /// <see cref="FillPattern.Solid"/>; other patterns are described by
    /// <see cref="FillForegroundColor"/> (pattern lines) over <see cref="FillBackgroundColor"/>.
    /// </summary>
    public FillPattern? FillPattern { get; init; }

    /// <summary>Pattern foreground colour (6-digit RGB hex), or null.</summary>
    public string? FillForegroundColor { get; init; }

    /// <summary>Pattern background colour (6-digit RGB hex), or null.</summary>
    public string? FillBackgroundColor { get; init; }

    /// <summary>
    /// Gradient fill as stored, or null. Stop colours are raw <see cref="ColorRef"/> values;
    /// resolve them with <see cref="CellStyleTable.ResolveColor"/>.
    /// </summary>
    public GradientFill? FillGradient { get; init; }

    /// <summary>Diagonal border line; drawn per <see cref="DiagonalUp"/> / <see cref="DiagonalDown"/>.</summary>
    public Border BorderDiagonal { get; init; }

    /// <summary>Diagonal runs bottom-left to top-right.</summary>
    public bool DiagonalUp { get; init; }

    /// <summary>Diagonal runs top-left to bottom-right.</summary>
    public bool DiagonalDown { get; init; }

    /// <summary>Indent level (each level is about three spaces of the default font).</summary>
    public int Indent { get; init; }

    /// <summary>Text rotation as stored: 0..90 counter-clockwise, 91..180 = 1..90 clockwise, 255 = vertical stacked.</summary>
    public int TextRotation { get; init; }

    /// <summary>Shrink text to fit the cell width.</summary>
    public bool ShrinkToFit { get; init; }

    /// <summary>Text reading order.</summary>
    public ReadingOrder ReadingOrder { get; init; }

    /// <summary>Justify the last line of distributed text.</summary>
    public bool JustifyLastLine { get; init; }

    /// <summary>
    /// Cell lock state when the sheet is protected, or null when unspecified (Excel's default
    /// is locked). Populated as true/false for loaded styles.
    /// </summary>
    public bool? Locked { get; init; }

    /// <summary>Hide the formula when the sheet is protected.</summary>
    public bool FormulaHidden { get; init; }

    /// <summary>The value was entered with a leading apostrophe and is shown as text.</summary>
    public bool QuotePrefix { get; init; }

    /// <summary>
    /// The numFmtId behind <see cref="NumberFormat"/>, or null for a style built in code. For
    /// built-in ids whose code depends on Excel's UI language (e.g. 5-8, 41-44),
    /// <see cref="NumberFormat"/> is null and this id is the only information.
    /// </summary>
    public int? NumberFormatId { get; init; }

    /// <summary>Name of the named cell style this format derives from (e.g. "Normal", "Heading 1"), or null.</summary>
    public string? StyleName { get; init; }
}
