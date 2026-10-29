using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Chuvadi.Sheets.Excel;

// =============================================================================================
// Read-side style table (Sheets 1.2.0). Mirrors styles.xml + the workbook theme one-to-one so
// consumers can walk the full chain themselves; Cell.Style carries the pre-resolved result.
// All types here are read-only: they describe a loaded file and are not used by the writer.
// =============================================================================================

/// <summary>How a <see cref="ColorRef"/> identifies its colour.</summary>
public enum ColorKind
{
    /// <summary>Automatic / system colour (auto="1"). Renderers use their default (black text, no fill).</summary>
    Auto,
    /// <summary>Literal ARGB value (rgb="FFRRGGBB").</summary>
    Rgb,
    /// <summary>Theme colour slot (theme="n"), optionally tinted.</summary>
    Theme,
    /// <summary>Legacy indexed palette entry (indexed="n").</summary>
    Indexed,
}

/// <summary>
/// A colour exactly as stored in styles.xml (&lt;color&gt;, &lt;fgColor&gt;, &lt;bgColor&gt;, ...).
/// Use <see cref="CellStyleTable.ResolveColor"/> to turn it into a 6-digit RGB hex value.
/// </summary>
public sealed class ColorRef
{
    internal ColorRef() { }

    /// <summary>How the colour is identified.</summary>
    public ColorKind Kind { get; internal set; }

    /// <summary>ARGB or RGB hex as stored (e.g. "FF1F4E79"), when <see cref="Kind"/> is Rgb.</summary>
    public string? Rgb { get; internal set; }

    /// <summary>Theme slot index as stored (0 = lt1, 1 = dk1, 2 = lt2, 3 = dk2, 4..9 = accents, 10 = hlink, 11 = folHlink).</summary>
    public int Theme { get; internal set; } = -1;

    /// <summary>Indexed palette index as stored (0..63 palette, 64 = system foreground, 65 = system background).</summary>
    public int Indexed { get; internal set; } = -1;

    /// <summary>Tint, -1.0 (darker) .. +1.0 (lighter). 0 = none. Applies to any kind.</summary>
    public double Tint { get; internal set; }

    /// <summary>An RGB colour: "RRGGBB" or "AARRGGBB" hex (stored as given). Since 1.7.0.</summary>
    public static ColorRef FromRgb(string hex, double tint = 0)
    {
        if (string.IsNullOrEmpty(hex) || (hex.Length != 6 && hex.Length != 8) || !IsHex(hex))
            throw new ArgumentException("Expected 6 (RRGGBB) or 8 (AARRGGBB) hex digits.", nameof(hex));
        return new ColorRef { Kind = ColorKind.Rgb, Rgb = hex, Tint = CheckTint(tint) };
    }

    /// <summary>A theme colour slot (0 = lt1, 1 = dk1, 2 = lt2, 3 = dk2, 4..9 = accents, 10 = hlink, 11 = folHlink), optionally tinted. Since 1.7.0.</summary>
    public static ColorRef FromTheme(int slot, double tint = 0)
    {
        if (slot < 0) throw new ArgumentOutOfRangeException(nameof(slot));
        return new ColorRef { Kind = ColorKind.Theme, Theme = slot, Tint = CheckTint(tint) };
    }

    /// <summary>A legacy indexed palette entry (0..63, 64 = system foreground, 65 = system background). Since 1.7.0.</summary>
    public static ColorRef FromIndexed(int index, double tint = 0)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        return new ColorRef { Kind = ColorKind.Indexed, Indexed = index, Tint = CheckTint(tint) };
    }

    /// <summary>The automatic colour (auto="1"). Since 1.7.0.</summary>
    public static ColorRef Automatic() => new() { Kind = ColorKind.Auto };

    private static bool IsHex(string s)
    {
        foreach (var c in s) if (!Uri.IsHexDigit(c)) return false;
        return true;
    }

    private static double CheckTint(double tint)
        => tint is >= -1 and <= 1 ? tint : throw new ArgumentOutOfRangeException(nameof(tint), "Tint must be -1.0 .. 1.0.");

    public override string ToString() => Kind switch
    {
        ColorKind.Rgb => $"rgb:{Rgb}" + TintSuffix(),
        ColorKind.Theme => $"theme:{Theme}" + TintSuffix(),
        ColorKind.Indexed => $"indexed:{Indexed}" + TintSuffix(),
        _ => "auto",
    };

    private string TintSuffix() => Tint == 0 ? "" : $" tint:{Tint.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>Underline styles (&lt;u val&gt;).</summary>
public enum UnderlineStyle { None = 0, Single, Double, SingleAccounting, DoubleAccounting }

/// <summary>Font vertical position (&lt;vertAlign val&gt;).</summary>
public enum FontVerticalAlign { Baseline = 0, Superscript, Subscript }

/// <summary>Font scheme membership (&lt;scheme val&gt;): whether the face follows the theme's major/minor font.</summary>
public enum FontScheme { None = 0, Major, Minor }

/// <summary>One &lt;font&gt; entry from styles.xml.</summary>
public sealed class FontEntry
{
    internal FontEntry() { }

    /// <summary>Face name (&lt;name val&gt;), e.g. "Calibri". Null if absent.</summary>
    public string? Name { get; internal set; }
    /// <summary>Size in points (&lt;sz val&gt;). Null if absent.</summary>
    public double? Size { get; internal set; }
    public bool Bold { get; internal set; }
    public bool Italic { get; internal set; }
    public UnderlineStyle Underline { get; internal set; }
    public bool Strikethrough { get; internal set; }
    public FontVerticalAlign VerticalAlign { get; internal set; }
    /// <summary>Text colour, or null if the font specifies none.</summary>
    public ColorRef? Color { get; internal set; }
    /// <summary>Font family class (&lt;family val&gt;: 1 Roman, 2 Swiss, 3 Modern, 4 Script, 5 Decorative). Null if absent.</summary>
    public int? Family { get; internal set; }
    /// <summary>Character set (&lt;charset val&gt;). Null if absent.</summary>
    public int? Charset { get; internal set; }
    /// <summary>Theme font scheme this face follows.</summary>
    public FontScheme Scheme { get; internal set; }
    public bool Outline { get; internal set; }
    public bool Shadow { get; internal set; }
    public bool Condense { get; internal set; }
    public bool Extend { get; internal set; }
}

/// <summary>Pattern fill types (&lt;patternFill patternType&gt;).</summary>
public enum FillPattern
{
    None = 0, Solid, MediumGray, DarkGray, LightGray,
    DarkHorizontal, DarkVertical, DarkDown, DarkUp, DarkGrid, DarkTrellis,
    LightHorizontal, LightVertical, LightDown, LightUp, LightGrid, LightTrellis,
    Gray125, Gray0625,
}

/// <summary>Gradient fill types (&lt;gradientFill type&gt;).</summary>
public enum GradientType { Linear = 0, Path }

/// <summary>One colour stop in a gradient fill.</summary>
public sealed class GradientStop
{
    internal GradientStop(double position, ColorRef color) { Position = position; Color = color; }
    /// <summary>Position along the gradient, 0..1.</summary>
    public double Position { get; }
    public ColorRef Color { get; }
}

/// <summary>Gradient fill details (&lt;gradientFill&gt;).</summary>
public sealed class GradientFill
{
    internal GradientFill() { }
    public GradientType Type { get; internal set; }
    /// <summary>Angle in degrees for linear gradients.</summary>
    public double Degree { get; internal set; }
    /// <summary>Path-gradient focus rectangle edges, 0..1.</summary>
    public double Left { get; internal set; }
    public double Right { get; internal set; }
    public double Top { get; internal set; }
    public double Bottom { get; internal set; }
    public IReadOnlyList<GradientStop> Stops { get; internal set; } = Array.Empty<GradientStop>();
}

/// <summary>One &lt;fill&gt; entry from styles.xml: either a pattern fill or a gradient fill.</summary>
public sealed class FillEntry
{
    internal FillEntry() { }

    /// <summary>
    /// Pattern type as stored, or null when &lt;patternFill&gt; has no patternType (or there is no
    /// patternFill). In a cell fill an absent type means none. In a differential format (dxf)
    /// Excel treats an absent type with a <see cref="BackgroundColor"/> as a solid fill in that
    /// colour. For a cell's solid fill, the visible colour is <see cref="ForegroundColor"/>.
    /// </summary>
    public FillPattern? Pattern { get; internal set; }
    /// <summary>Pattern foreground colour (&lt;fgColor&gt;), or null.</summary>
    public ColorRef? ForegroundColor { get; internal set; }
    /// <summary>Pattern background colour (&lt;bgColor&gt;), or null.</summary>
    public ColorRef? BackgroundColor { get; internal set; }
    /// <summary>Gradient details when this is a gradient fill; otherwise null.</summary>
    public GradientFill? Gradient { get; internal set; }
}

/// <summary>One edge of a border entry.</summary>
public sealed class BorderEdge
{
    internal BorderEdge(BorderStyle style, ColorRef? color) { Style = style; Color = color; }
    public BorderStyle Style { get; }
    /// <summary>Edge colour, or null (automatic).</summary>
    public ColorRef? Color { get; }
    internal static readonly BorderEdge None = new(BorderStyle.None, null);
}

/// <summary>One &lt;border&gt; entry from styles.xml.</summary>
public sealed class BorderEntry
{
    internal BorderEntry() { }

    /// <summary>Left edge (&lt;left&gt; or the bidi-aware &lt;start&gt;).</summary>
    public BorderEdge Left { get; internal set; } = BorderEdge.None;
    /// <summary>Right edge (&lt;right&gt; or the bidi-aware &lt;end&gt;).</summary>
    public BorderEdge Right { get; internal set; } = BorderEdge.None;
    public BorderEdge Top { get; internal set; } = BorderEdge.None;
    public BorderEdge Bottom { get; internal set; } = BorderEdge.None;
    /// <summary>Diagonal line; drawn in the directions given by <see cref="DiagonalUp"/>/<see cref="DiagonalDown"/>.</summary>
    public BorderEdge Diagonal { get; internal set; } = BorderEdge.None;
    /// <summary>Inner vertical edges (used by table/differential formats).</summary>
    public BorderEdge Vertical { get; internal set; } = BorderEdge.None;
    /// <summary>Inner horizontal edges (used by table/differential formats).</summary>
    public BorderEdge Horizontal { get; internal set; } = BorderEdge.None;
    public bool DiagonalUp { get; internal set; }
    public bool DiagonalDown { get; internal set; }
    /// <summary>Whether left/right/top/bottom apply to the outline of a range (default true).</summary>
    public bool Outline { get; internal set; } = true;
}

/// <summary>Text reading order (&lt;alignment readingOrder&gt;).</summary>
public enum ReadingOrder { Context = 0, LeftToRight = 1, RightToLeft = 2 }

/// <summary>Cell alignment (&lt;alignment&gt;).</summary>
public sealed class AlignmentInfo
{
    internal AlignmentInfo() { }
    public HorizontalAlign Horizontal { get; internal set; }
    /// <summary>Vertical alignment. Excel's default when unspecified is Bottom.</summary>
    public VerticalAlign Vertical { get; internal set; } = VerticalAlign.Bottom;
    public bool WrapText { get; internal set; }
    public bool ShrinkToFit { get; internal set; }
    /// <summary>Indent level (each level is about three spaces of the default font).</summary>
    public int Indent { get; internal set; }
    public int RelativeIndent { get; internal set; }
    /// <summary>Text rotation as stored: 0..90 counter-clockwise, 91..180 = 1..90 clockwise, 255 = vertical stacked.</summary>
    public int TextRotation { get; internal set; }
    public ReadingOrder ReadingOrder { get; internal set; }
    public bool JustifyLastLine { get; internal set; }
}

/// <summary>Cell protection flags (&lt;protection&gt;). Effective only when the sheet is protected.</summary>
public sealed class ProtectionInfo
{
    internal ProtectionInfo() { }
    /// <summary>Default true in Excel.</summary>
    public bool Locked { get; internal set; } = true;
    public bool Hidden { get; internal set; }
}

/// <summary>
/// One &lt;xf&gt; entry: an entry of cellXfs (a cell format, referenced by a cell's s=) or of
/// cellStyleXfs (a named-style format). Holds indexes into the font, fill, border and
/// number-format tables plus inline alignment and protection.
/// </summary>
public sealed class CellFormatEntry
{
    internal CellFormatEntry() { }

    public int NumberFormatId { get; internal set; }
    public int FontId { get; internal set; }
    public int FillId { get; internal set; }
    public int BorderId { get; internal set; }

    /// <summary>For cellXfs: index of the parent named-style format in cellStyleXfs (xfId). -1 for cellStyleXfs entries or when absent.</summary>
    public int ParentFormatIndex { get; internal set; } = -1;

    /// <summary>apply* flags exactly as stored: null when the attribute is absent.</summary>
    public bool? ApplyNumberFormat { get; internal set; }
    public bool? ApplyFont { get; internal set; }
    public bool? ApplyFill { get; internal set; }
    public bool? ApplyBorder { get; internal set; }
    public bool? ApplyAlignment { get; internal set; }
    public bool? ApplyProtection { get; internal set; }

    /// <summary>True if the cell text was entered with a leading apostrophe (shown as text).</summary>
    public bool QuotePrefix { get; internal set; }
    public bool PivotButton { get; internal set; }

    /// <summary>Inline alignment, or null if the entry has none.</summary>
    public AlignmentInfo? Alignment { get; internal set; }
    /// <summary>Inline protection, or null if the entry has none.</summary>
    public ProtectionInfo? Protection { get; internal set; }
}

/// <summary>A named cell style (&lt;cellStyle&gt;), e.g. "Normal", "Heading 1", "Good".</summary>
public sealed class NamedCellStyle
{
    internal NamedCellStyle() { }
    public string Name { get; internal set; } = string.Empty;
    /// <summary>Index into <see cref="CellStyleTable.CellStyleFormats"/> (xfId).</summary>
    public int FormatIndex { get; internal set; }
    /// <summary>Built-in style id (0 = Normal, ...), or null for a custom style.</summary>
    public int? BuiltinId { get; internal set; }
    /// <summary>Outline level for RowLevel_n / ColLevel_n built-ins, or null.</summary>
    public int? OutlineLevel { get; internal set; }
    public bool Hidden { get; internal set; }
    public bool CustomBuiltin { get; internal set; }
}

/// <summary>
/// A differential format (&lt;dxf&gt;), used by conditional formatting and table styles. Every
/// part is optional: only what is present overrides the cell's own formatting.
/// </summary>
public sealed class DifferentialFormat
{
    internal DifferentialFormat() { }
    public FontEntry? Font { get; internal set; }
    public FillEntry? Fill { get; internal set; }
    public BorderEntry? Border { get; internal set; }
    /// <summary>Number format id, or null if the dxf has none.</summary>
    public int? NumberFormatId { get; internal set; }
    /// <summary>Number format code carried inline by the dxf, or null.</summary>
    public string? NumberFormatCode { get; internal set; }
    public AlignmentInfo? Alignment { get; internal set; }
    public ProtectionInfo? Protection { get; internal set; }
}

/// <summary>Major or minor font of the theme, per script.</summary>
public sealed class ThemeFontScheme
{
    internal ThemeFontScheme() { }
    /// <summary>Latin typeface (&lt;a:latin typeface&gt;), e.g. "Calibri".</summary>
    public string? Latin { get; internal set; }
    /// <summary>East Asian typeface (&lt;a:ea&gt;); empty string means "use the system default".</summary>
    public string? EastAsian { get; internal set; }
    /// <summary>Complex-script typeface (&lt;a:cs&gt;).</summary>
    public string? ComplexScript { get; internal set; }
    /// <summary>Per-script typefaces (&lt;a:font script="Taml" typeface="Latha"/&gt;), keyed by ISO 15924 script code.</summary>
    public IReadOnlyDictionary<string, string> Scripts { get; internal set; } = EmptyMap;

    internal static readonly IReadOnlyDictionary<string, string> EmptyMap =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
}

/// <summary>The workbook theme's colour scheme and font scheme (xl/theme/theme1.xml).</summary>
public sealed class ThemeInfo
{
    internal ThemeInfo() { }

    /// <summary>Theme name (&lt;a:theme name&gt;), or null.</summary>
    public string? Name { get; internal set; }
    /// <summary>Colour scheme name (&lt;a:clrScheme name&gt;), or null.</summary>
    public string? ColorSchemeName { get; internal set; }

    /// <summary>
    /// The 12 scheme colours as 6-digit RGB hex in file order: dk1, lt1, dk2, lt2, accent1..6,
    /// hlink, folHlink. System colours (sysClr) use their lastClr value. A missing slot is null.
    /// Note: a cell's theme="n" index swaps the first two pairs (0 = lt1, 1 = dk1, 2 = lt2,
    /// 3 = dk2); <see cref="CellStyleTable.ResolveColor"/> applies that mapping.
    /// </summary>
    public IReadOnlyList<string?> SchemeColors { get; internal set; } = new string?[12];

    public ThemeFontScheme MajorFont { get; internal set; } = new();
    public ThemeFontScheme MinorFont { get; internal set; } = new();

    /// <summary>True if the workbook had a theme part.</summary>
    public bool IsPresent { get; internal set; }
}

/// <summary>
/// The workbook's complete style table: every list from styles.xml plus the theme, as stored.
/// Exposed as <see cref="Workbook.StyleTable"/> and <see cref="XlsxReader.StyleTable"/>.
/// A cell's s= attribute is an index into <see cref="CellFormats"/>.
/// </summary>
public sealed class CellStyleTable
{
    internal CellStyleTable() { }

    /// <summary>An empty table: no styles.xml and no theme. Used for workbooks built in memory.</summary>
    public static CellStyleTable Empty { get; } = new();

    /// <summary>Custom number formats (&lt;numFmts&gt;) keyed by numFmtId.</summary>
    public IReadOnlyDictionary<int, string> CustomNumberFormats { get; internal set; } = EmptyFormats;
    public IReadOnlyList<FontEntry> Fonts { get; internal set; } = Array.Empty<FontEntry>();
    public IReadOnlyList<FillEntry> Fills { get; internal set; } = Array.Empty<FillEntry>();
    public IReadOnlyList<BorderEntry> Borders { get; internal set; } = Array.Empty<BorderEntry>();
    /// <summary>Named-style formats (&lt;cellStyleXfs&gt;).</summary>
    public IReadOnlyList<CellFormatEntry> CellStyleFormats { get; internal set; } = Array.Empty<CellFormatEntry>();
    /// <summary>Cell formats (&lt;cellXfs&gt;); a cell's s= indexes this list.</summary>
    public IReadOnlyList<CellFormatEntry> CellFormats { get; internal set; } = Array.Empty<CellFormatEntry>();
    /// <summary>Named cell styles (&lt;cellStyles&gt;).</summary>
    public IReadOnlyList<NamedCellStyle> NamedStyles { get; internal set; } = Array.Empty<NamedCellStyle>();
    /// <summary>Differential formats (&lt;dxfs&gt;) used by conditional formatting and table styles.</summary>
    public IReadOnlyList<DifferentialFormat> DifferentialFormats { get; internal set; } = Array.Empty<DifferentialFormat>();

    /// <summary>
    /// The effective indexed-colour palette (64 entries, 6-digit RGB hex): the file's custom
    /// &lt;indexedColors&gt; if present, otherwise Excel's default palette.
    /// </summary>
    public IReadOnlyList<string> IndexedColors { get; internal set; } = DefaultIndexedColors;
    /// <summary>True if the file overrides the indexed palette.</summary>
    public bool HasCustomIndexedColors { get; internal set; }
    /// <summary>Recently used colours (&lt;mruColors&gt;).</summary>
    public IReadOnlyList<ColorRef> RecentColors { get; internal set; } = Array.Empty<ColorRef>();

    /// <summary>The workbook theme. <see cref="ThemeInfo.IsPresent"/> is false when the file has none.</summary>
    public ThemeInfo Theme { get; internal set; } = new();

    // ---- Number formats ----------------------------------------------------------------

    /// <summary>
    /// The format code for a numFmtId: a custom format from the file, else a built-in from the
    /// ECMA-376 "All Languages" table. Returns null for ids the standard leaves to the
    /// application's UI language (e.g. 5-8 currency, 41-44 accounting); see
    /// <see cref="IsLocaleDependentNumberFormat"/>.
    /// </summary>
    public string? GetNumberFormatCode(int numberFormatId)
    {
        if (CustomNumberFormats.TryGetValue(numberFormatId, out var code)) return code;
        return BuiltInNumberFormats.TryGetValue(numberFormatId, out var b) ? b : null;
    }

    /// <summary>
    /// True for built-in ids below 164 that have no fixed code in ECMA-376 and that the file did
    /// not define: Excel renders them per its UI locale (currency, accounting, East Asian dates).
    /// </summary>
    public bool IsLocaleDependentNumberFormat(int numberFormatId)
        => numberFormatId >= 0 && numberFormatId < 164
           && !CustomNumberFormats.ContainsKey(numberFormatId)
           && !BuiltInNumberFormats.ContainsKey(numberFormatId);

    /// <summary>The ECMA-376 Part 1 §18.8.30 "All Languages" built-in number formats.</summary>
    public static IReadOnlyDictionary<int, string> BuiltInNumberFormats { get; } =
        new ReadOnlyDictionary<int, string>(new Dictionary<int, string>
        {
            [0] = "General", [1] = "0", [2] = "0.00", [3] = "#,##0", [4] = "#,##0.00",
            [9] = "0%", [10] = "0.00%", [11] = "0.00E+00", [12] = "# ?/?", [13] = "# ??/??",
            [14] = "mm-dd-yy", [15] = "d-mmm-yy", [16] = "d-mmm", [17] = "mmm-yy",
            [18] = "h:mm AM/PM", [19] = "h:mm:ss AM/PM", [20] = "h:mm", [21] = "h:mm:ss",
            [22] = "m/d/yy h:mm",
            [37] = "#,##0 ;(#,##0)", [38] = "#,##0 ;[Red](#,##0)",
            [39] = "#,##0.00;(#,##0.00)", [40] = "#,##0.00;[Red](#,##0.00)",
            [45] = "mm:ss", [46] = "[h]:mm:ss", [47] = "mmss.0", [48] = "##0.0E+0", [49] = "@",
        });

    /// <summary>Excel's default 64-entry indexed colour palette (6-digit RGB hex).</summary>
    public static IReadOnlyList<string> DefaultIndexedColors { get; } = Array.AsReadOnly(new[]
    {
        "000000", "FFFFFF", "FF0000", "00FF00", "0000FF", "FFFF00", "FF00FF", "00FFFF",
        "000000", "FFFFFF", "FF0000", "00FF00", "0000FF", "FFFF00", "FF00FF", "00FFFF",
        "800000", "008000", "000080", "808000", "800080", "008080", "C0C0C0", "808080",
        "9999FF", "993366", "FFFFCC", "CCFFFF", "660066", "FF8080", "0066CC", "CCCCFF",
        "000080", "FF00FF", "FFFF00", "00FFFF", "800080", "800000", "008080", "0000FF",
        "00CCFF", "CCFFFF", "CCFFCC", "FFFF99", "99CCFF", "FF99CC", "CC99FF", "FFCC99",
        "3366FF", "33CCCC", "99CC00", "FFCC00", "FF9900", "FF6600", "666699", "969696",
        "003366", "339966", "003300", "333300", "993300", "993366", "333399", "333333",
    });

    private static readonly IReadOnlyDictionary<int, string> EmptyFormats =
        new ReadOnlyDictionary<int, string>(new Dictionary<int, string>());

    // ---- Resolution ---------------------------------------------------------------------

    private CellStyle?[]? _resolved;

    /// <summary>
    /// The effective style of cellXfs entry 0 — what cells with no style (or s="0") render as.
    /// Null when the file has no cellXfs.
    /// </summary>
    public CellStyle? DefaultCellStyle => ResolveCellFormat(0);

    /// <summary>
    /// Resolves a cellXfs entry (a cell's s= index, or a row/column style index) to a flat,
    /// ready-to-render <see cref="CellStyle"/>: font, fill, border, number format, alignment and
    /// protection looked up through their tables, theme/indexed/tinted colours turned into RGB
    /// hex, and the named-style parent applied. Returns null for an index outside cellXfs.
    ///
    /// Inheritance rule: each component (number format, font, fill, border, alignment,
    /// protection) comes from the cellXf itself, unless its apply* flag is explicitly "0" and a
    /// parent named-style format exists, in which case the parent's component is used. An
    /// alignment or protection element missing from the cellXf (with no apply flag) also falls
    /// back to the parent's. Excel writes fully expanded cellXfs, so Excel files resolve the
    /// same under either reading of the flags. Results are cached per index.
    /// </summary>
    public CellStyle? ResolveCellFormat(int index)
    {
        var xfs = CellFormats;
        if (index < 0 || index >= xfs.Count) return null;
        _resolved ??= new CellStyle?[xfs.Count];
        return _resolved[index] ??= Resolve(xfs[index]);
    }

    private CellStyle Resolve(CellFormatEntry xf)
    {
        var parent = xf.ParentFormatIndex >= 0 && xf.ParentFormatIndex < CellStyleFormats.Count
            ? CellStyleFormats[xf.ParentFormatIndex]
            : null;

        bool Inherit(bool? apply) => parent is not null && apply == false;

        int numFmtId = Inherit(xf.ApplyNumberFormat) ? parent!.NumberFormatId : xf.NumberFormatId;
        int fontId = Inherit(xf.ApplyFont) ? parent!.FontId : xf.FontId;
        int fillId = Inherit(xf.ApplyFill) ? parent!.FillId : xf.FillId;
        int borderId = Inherit(xf.ApplyBorder) ? parent!.BorderId : xf.BorderId;
        var alignment = Inherit(xf.ApplyAlignment)
            ? parent!.Alignment
            : xf.Alignment ?? (xf.ApplyAlignment is null ? parent?.Alignment : null);
        var protection = Inherit(xf.ApplyProtection)
            ? parent!.Protection
            : xf.Protection ?? (xf.ApplyProtection is null ? parent?.Protection : null);

        var font = fontId >= 0 && fontId < Fonts.Count ? Fonts[fontId] : null;
        var fill = fillId >= 0 && fillId < Fills.Count ? Fills[fillId] : null;
        var border = borderId >= 0 && borderId < Borders.Count ? Borders[borderId] : null;

        string? fontName = font?.Name;
        if (fontName is null && font is not null && font.Scheme != FontScheme.None)
            fontName = font.Scheme == FontScheme.Major ? Theme.MajorFont.Latin : Theme.MinorFont.Latin;

        var pattern = fill?.Pattern;
        var fg = ResolveColor(fill?.ForegroundColor);
        var bg = ResolveColor(fill?.BackgroundColor);

        string? styleName = null;
        if (xf.ParentFormatIndex >= 0)
        {
            foreach (var ns in NamedStyles)
                if (ns.FormatIndex == xf.ParentFormatIndex) { styleName = ns.Name; break; }
        }

        return new CellStyle(
            FontName: fontName,
            FontSize: font?.Size,
            Bold: font?.Bold ?? false,
            Italic: font?.Italic ?? false,
            Underline: (font?.Underline ?? UnderlineStyle.None) != UnderlineStyle.None,
            FontColor: ResolveColor(font?.Color),
            FillColor: pattern == FillPattern.Solid ? fg : null,
            BorderTop: ToBorder(border?.Top),
            BorderBottom: ToBorder(border?.Bottom),
            BorderLeft: ToBorder(border?.Left),
            BorderRight: ToBorder(border?.Right),
            NumberFormat: numFmtId == 0 ? null : GetNumberFormatCode(numFmtId),
            HAlign: alignment?.Horizontal ?? HorizontalAlign.General,
            VAlign: alignment?.Vertical ?? VerticalAlign.Bottom,
            WrapText: alignment?.WrapText ?? false)
        {
            UnderlineKind = font?.Underline ?? UnderlineStyle.None,
            Strikethrough = font?.Strikethrough ?? false,
            FontVerticalPosition = font?.VerticalAlign ?? FontVerticalAlign.Baseline,
            FontFamily = font?.Family,
            FontCharset = font?.Charset,
            FontScheme = font?.Scheme ?? FontScheme.None,
            FillPattern = pattern,
            FillForegroundColor = fg,
            FillBackgroundColor = bg,
            FillGradient = fill?.Gradient,
            BorderDiagonal = ToBorder(border?.Diagonal),
            DiagonalUp = border?.DiagonalUp ?? false,
            DiagonalDown = border?.DiagonalDown ?? false,
            Indent = alignment?.Indent ?? 0,
            TextRotation = alignment?.TextRotation ?? 0,
            ShrinkToFit = alignment?.ShrinkToFit ?? false,
            ReadingOrder = alignment?.ReadingOrder ?? ReadingOrder.Context,
            JustifyLastLine = alignment?.JustifyLastLine ?? false,
            Locked = protection?.Locked ?? true,
            FormulaHidden = protection?.Hidden ?? false,
            QuotePrefix = xf.QuotePrefix,
            VerticalAlignment = alignment?.Vertical ?? VerticalAlign.Bottom,
            NumberFormatId = numFmtId,
            StyleName = styleName,
        };
    }

    private Border ToBorder(BorderEdge? edge)
        => edge is null || edge.Style == BorderStyle.None
            ? Border.None
            : new Border(edge.Style, ResolveColor(edge.Color));

    // ---- Colours -----------------------------------------------------------------------

    /// <summary>
    /// Resolves a stored colour to 6-digit uppercase RGB hex (no '#', alpha dropped), applying
    /// the theme, the indexed palette and tint. Returns null for automatic colours, a missing
    /// theme slot, or an out-of-range index — the renderer's default applies.
    /// Indexed 64 (system foreground) resolves to "000000" and 65 (system background) to "FFFFFF".
    /// </summary>
    public string? ResolveColor(ColorRef? color)
    {
        if (color is null) return null;
        string? baseHex = color.Kind switch
        {
            ColorKind.Rgb => NormalizeHex(color.Rgb),
            ColorKind.Theme => ThemeSlot(color.Theme),
            ColorKind.Indexed => IndexedSlot(color.Indexed),
            _ => null,
        };
        if (baseHex is null) return null;
        return color.Tint == 0 ? baseHex : ApplyTint(baseHex, color.Tint);
    }

    private string? ThemeSlot(int theme)
    {
        // SpreadsheetML theme indexes swap the light/dark pairs relative to clrScheme order.
        int slot = theme switch { 0 => 1, 1 => 0, 2 => 3, 3 => 2, _ => theme };
        var colors = Theme.SchemeColors;
        return slot >= 0 && slot < colors.Count ? colors[slot] : null;
    }

    private string? IndexedSlot(int index)
    {
        if (index == 64) return "000000";
        if (index == 65) return "FFFFFF";
        return index >= 0 && index < IndexedColors.Count ? IndexedColors[index] : null;
    }

    /// <summary>"FFAABBCC" or "AABBCC" (any case) → "AABBCC"; anything else → null.</summary>
    internal static string? NormalizeHex(string? s)
    {
        if (s is null) return null;
        if (s.Length == 8) s = s.Substring(2);
        if (s.Length != 6) return null;
        foreach (var ch in s)
            if (!Uri.IsHexDigit(ch)) return null;
        return s.ToUpperInvariant();
    }

    /// <summary>
    /// Applies an OOXML tint to an RGB colour in HLS space (ECMA-376 §18.8.19): a negative tint
    /// scales luminance down, a positive tint moves it toward white. Computed in continuous
    /// HLS; results can differ from Excel's own swatches by up to about 2 units per channel
    /// because Excel rounds internally.
    /// </summary>
    internal static string ApplyTint(string rgbHex, double tint)
    {
        int r = Convert.ToInt32(rgbHex.Substring(0, 2), 16);
        int g = Convert.ToInt32(rgbHex.Substring(2, 2), 16);
        int b = Convert.ToInt32(rgbHex.Substring(4, 2), 16);
        RgbToHls(r / 255.0, g / 255.0, b / 255.0, out var h, out var l, out var s);
        l = tint < 0 ? l * (1.0 + tint) : l * (1.0 - tint) + tint;
        HlsToRgb(h, l, s, out var rr, out var gg, out var bb);
        return ToByte(rr).ToString("X2") + ToByte(gg).ToString("X2") + ToByte(bb).ToString("X2");
    }

    private static int ToByte(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255.0, MidpointRounding.AwayFromZero);

    private static void RgbToHls(double r, double g, double b, out double h, out double l, out double s)
    {
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        l = (max + min) / 2.0;
        if (max == min) { h = 0; s = 0; return; }
        double d = max - min;
        s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
        if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
        else if (max == g) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        h /= 6.0;
    }

    private static void HlsToRgb(double h, double l, double s, out double r, out double g, out double b)
    {
        if (s == 0) { r = g = b = l; return; }
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        double p = 2 * l - q;
        r = Hue(p, q, h + 1.0 / 3);
        g = Hue(p, q, h);
        b = Hue(p, q, h - 1.0 / 3);
    }

    private static double Hue(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }
}
