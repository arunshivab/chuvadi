using System;

namespace Chuvadi.Docs.Word;

/// <summary>
/// Character-level (run) formatting — the complete set of commonly used WordprocessingML run
/// properties. All members are optional; unset members inherit from the paragraph style and
/// document defaults (Calibri 11pt). Immutable — build once, reuse.
///
/// <para><b>Scripts and font slots.</b> Word picks a font per character from four slots:
/// <see cref="AsciiFont"/> (basic Latin), <see cref="HighAnsiFont"/> (other Latin/Greek/
/// Cyrillic), <see cref="EastAsiaFont"/> (CJK) and <see cref="ComplexScriptFont"/> (Indic —
/// Tamil, Devanagari, Malayalam… — plus Arabic, Hebrew, Thai). Complex-script text also takes
/// its bold, italic and size from separate properties. <see cref="Font"/> sets the two Latin
/// slots, exactly like Word's Font box; <see cref="FontForAllScripts"/> sets all four.
/// With <see cref="MirrorToComplexScript"/> (the default for formats you build),
/// <see cref="Bold"/>, <see cref="Italic"/> and <see cref="SizePt"/> also apply to complex-script
/// text — Word's toolbar behaviour — unless the complex-script property is set explicitly.
/// Formats produced by <see cref="Document.Load(string)"/> have mirroring off and carry the
/// complex-script properties exactly as the file states them, so a load → save round-trip
/// is exact.</para>
///
/// <code>
/// var tamil = new TextFormat
/// {
///     FontForAllScripts = "Nirmala UI",
///     SizePt = 12,
///     Bold = true,
///     ComplexScriptLanguage = "ta-IN",
/// };
/// doc.AddParagraph().Text("வணக்கம் — Welcome", tamil);
/// </code>
///
/// <para><b>Tri-state toggles.</b> The original four toggles (<see cref="Bold"/>,
/// <see cref="Italic"/>, <see cref="Underline"/>, <see cref="Strikethrough"/>) are plain
/// booleans where false means "inherit". Toggles added later are <c>bool?</c>: null inherits,
/// true turns the effect on, false explicitly turns it off (overriding a style).</para>
/// </summary>
public sealed class TextFormat
{
    private readonly string? _asciiFont;
    private readonly string? _highAnsiFont;
    private readonly string? _eastAsiaFont;
    private readonly string? _complexScriptFont;

    // ---- Original members (v1.0) ----------------------------------------------------

    /// <summary>Bold (all scripts unless <see cref="BoldComplexScript"/> is set). False = inherit.</summary>
    public bool Bold { get; init; }

    /// <summary>Italic (all scripts unless <see cref="ItalicComplexScript"/> is set). False = inherit.</summary>
    public bool Italic { get; init; }

    /// <summary>Single underline. For other styles use <see cref="UnderlineStyle"/>. False = inherit.</summary>
    public bool Underline { get; init; }

    /// <summary>Single strikethrough. False = inherit. See also <see cref="DoubleStrikethrough"/>.</summary>
    public bool Strikethrough { get; init; }

    /// <summary>
    /// Font family for the Latin slots (<see cref="AsciiFont"/> and <see cref="HighAnsiFont"/>),
    /// exactly like Word's Font box. Reading returns <see cref="AsciiFont"/> (or
    /// <see cref="HighAnsiFont"/> when only that is set). Null = inherit.
    /// </summary>
    public string? Font
    {
        get => _asciiFont ?? _highAnsiFont;
        init { _asciiFont = value; _highAnsiFont = value; }
    }

    /// <summary>
    /// Font size in points for every script (e.g. 11, 14) unless <see cref="SizeComplexScriptPt"/>
    /// is set. 0 = inherit.
    /// </summary>
    public double SizePt { get; init; }

    /// <summary>Text color as 6-digit hex WITHOUT '#', e.g. "C00000". Null = inherit. See also <see cref="ThemeColor"/>.</summary>
    public string? ColorHex { get; init; }

    /// <summary>Highlight color name per OOXML (yellow, green, cyan, magenta, red, blue,
    /// darkGray, lightGray...). Null = none. For an arbitrary color use <see cref="ShadingHex"/>.</summary>
    public string? Highlight { get; init; }

    // ---- Font slots -----------------------------------------------------------------

    /// <summary>Sets all four font slots (Latin, high-ANSI, East Asian, complex script) at once. Write-only convenience.</summary>
    public string? FontForAllScripts
    {
        init { _asciiFont = value; _highAnsiFont = value; _eastAsiaFont = value; _complexScriptFont = value; }
    }

    /// <summary>Font for basic Latin characters (w:rFonts/@w:ascii). Null = inherit.</summary>
    public string? AsciiFont { get => _asciiFont; init => _asciiFont = value; }

    /// <summary>Font for other Latin, Greek, Cyrillic… characters (w:rFonts/@w:hAnsi). Null = inherit.</summary>
    public string? HighAnsiFont { get => _highAnsiFont; init => _highAnsiFont = value; }

    /// <summary>Font for East Asian characters (w:rFonts/@w:eastAsia). Null = inherit.</summary>
    public string? EastAsiaFont { get => _eastAsiaFont; init => _eastAsiaFont = value; }

    /// <summary>
    /// Font for complex-script characters — Indic scripts (Tamil, Devanagari, Malayalam,
    /// Telugu, Kannada, Bengali, Gujarati…), Arabic, Hebrew, Thai (w:rFonts/@w:cs). Null = inherit.
    /// </summary>
    public string? ComplexScriptFont { get => _complexScriptFont; init => _complexScriptFont = value; }

    /// <summary>Theme font for the ASCII slot (overrides <see cref="AsciiFont"/> in Word). Null = none.</summary>
    public ThemeFont? AsciiThemeFont { get; init; }

    /// <summary>Theme font for the high-ANSI slot. Null = none.</summary>
    public ThemeFont? HighAnsiThemeFont { get; init; }

    /// <summary>Theme font for the East Asian slot. Null = none.</summary>
    public ThemeFont? EastAsiaThemeFont { get; init; }

    /// <summary>Theme font for the complex-script slot. Null = none.</summary>
    public ThemeFont? ComplexScriptThemeFont { get; init; }

    /// <summary>Which slot Word should use for characters that belong to more than one (w:rFonts/@w:hint). Null = default.</summary>
    public FontHint? FontHint { get; init; }

    // ---- Complex-script properties --------------------------------------------------

    /// <summary>
    /// When true (default), <see cref="Bold"/>, <see cref="Italic"/> and <see cref="SizePt"/> are
    /// also written for complex-script text wherever <see cref="BoldComplexScript"/>,
    /// <see cref="ItalicComplexScript"/> or <see cref="SizeComplexScriptPt"/> is unset — as Word's
    /// toolbar does. Loaded formats have this off so they round-trip exactly.
    /// </summary>
    public bool MirrorToComplexScript { get; init; } = true;

    /// <summary>Bold for complex-script text (w:bCs). Null = unset (mirrors <see cref="Bold"/> when <see cref="MirrorToComplexScript"/>).</summary>
    public bool? BoldComplexScript { get; init; }

    /// <summary>Italic for complex-script text (w:iCs). Null = unset (mirrors <see cref="Italic"/> when <see cref="MirrorToComplexScript"/>).</summary>
    public bool? ItalicComplexScript { get; init; }

    /// <summary>Size in points for complex-script text (w:szCs). 0 = unset (mirrors <see cref="SizePt"/> when <see cref="MirrorToComplexScript"/>).</summary>
    public double SizeComplexScriptPt { get; init; }

    /// <summary>Force the run to be treated as complex script (w:cs). Null = inherit.</summary>
    public bool? ComplexScript { get; init; }

    /// <summary>Right-to-left run (w:rtl), for Arabic/Hebrew text. Null = inherit.</summary>
    public bool? RightToLeft { get; init; }

    // ---- Language --------------------------------------------------------------------

    /// <summary>Language for Latin text, BCP-47 (e.g. "en-IN"); drives spell-check and hyphenation. Null = inherit.</summary>
    public string? Language { get; init; }

    /// <summary>Language for East Asian text (e.g. "ja-JP"). Null = inherit.</summary>
    public string? EastAsiaLanguage { get; init; }

    /// <summary>Language for complex-script text (e.g. "ta-IN", "hi-IN", "ml-IN"). Null = inherit.</summary>
    public string? ComplexScriptLanguage { get; init; }

    // ---- Effects --------------------------------------------------------------------

    /// <summary>Underline style; takes precedence over <see cref="Underline"/>. <see cref="Word.UnderlineStyle.None"/> removes an inherited underline. Null = per <see cref="Underline"/>.</summary>
    public UnderlineStyle? UnderlineStyle { get; init; }

    /// <summary>Underline color as 6-digit hex without '#'. Null = text color.</summary>
    public string? UnderlineColorHex { get; init; }

    /// <summary>Double strikethrough (w:dstrike). Null = inherit.</summary>
    public bool? DoubleStrikethrough { get; init; }

    /// <summary>ALL CAPS display (w:caps); the stored text is unchanged. Null = inherit.</summary>
    public bool? Caps { get; init; }

    /// <summary>Small caps display (w:smallCaps). Null = inherit.</summary>
    public bool? SmallCaps { get; init; }

    /// <summary>Hidden text (w:vanish). Null = inherit.</summary>
    public bool? Hidden { get; init; }

    /// <summary>Outline effect (w:outline). Null = inherit.</summary>
    public bool? Outline { get; init; }

    /// <summary>Shadow effect (w:shadow). Null = inherit.</summary>
    public bool? Shadow { get; init; }

    /// <summary>Emboss effect (w:emboss). Null = inherit.</summary>
    public bool? Emboss { get; init; }

    /// <summary>Engrave effect (w:imprint). Null = inherit.</summary>
    public bool? Imprint { get; init; }

    /// <summary>Exclude from spelling and grammar checking (w:noProof). Null = inherit.</summary>
    public bool? NoProof { get; init; }

    /// <summary>Superscript / subscript (w:vertAlign). Null = inherit.</summary>
    public VerticalTextPosition? VerticalPosition { get; init; }

    /// <summary>Raise (positive) or lower (negative) the text from the baseline, in points (w:position). Null = inherit.</summary>
    public double? BaselineShiftPt { get; init; }

    /// <summary>Extra space between characters in points; negative condenses (w:spacing). Null = inherit.</summary>
    public double? CharacterSpacingPt { get; init; }

    /// <summary>Horizontal scaling of the characters in percent, 1–600 (w:w). Null = inherit (100%).</summary>
    public int? HorizontalScalePercent { get; init; }

    /// <summary>Kern font pairs at and above this size in points (w:kern). Null = inherit.</summary>
    public double? KerningMinSizePt { get; init; }

    // ---- Color ----------------------------------------------------------------------

    /// <summary>Theme color for the text (w:color/@w:themeColor). Word uses it in preference to <see cref="ColorHex"/>. Null = none.</summary>
    public ThemeColor? ThemeColor { get; init; }

    /// <summary>Tint applied to <see cref="ThemeColor"/>, 0–255 (255 = no tint). Null = none.</summary>
    public byte? ThemeColorTint { get; init; }

    /// <summary>Shade applied to <see cref="ThemeColor"/>, 0–255 (255 = no shade). Null = none.</summary>
    public byte? ThemeColorShade { get; init; }

    /// <summary>Background shading behind the run as 6-digit hex without '#' (w:shd). Any color, unlike <see cref="Highlight"/>. Null = none.</summary>
    public string? ShadingHex { get; init; }

    /// <summary>
    /// Original toggles written as EXPLICITLY OFF (<c>w:val="0"</c>), cancelling bold, italic or
    /// strikethrough inherited from a style. Applies only where the matching bool is false.
    /// Document.Load sets these from the file, so explicit-off runs round-trip.
    /// (For underline use <see cref="UnderlineStyle"/> = <see cref="Word.UnderlineStyle.None"/>.)
    /// </summary>
    public RunToggles ExplicitlyOff { get; init; }

    // ---- Helpers --------------------------------------------------------------------

    /// <summary>True when every member is at its default (nothing to emit).</summary>
    internal bool IsDefault =>
        !Bold && !Italic && !Underline && !Strikethrough && ExplicitlyOff == RunToggles.None
        && _asciiFont is null && _highAnsiFont is null && _eastAsiaFont is null && _complexScriptFont is null
        && AsciiThemeFont is null && HighAnsiThemeFont is null && EastAsiaThemeFont is null && ComplexScriptThemeFont is null
        && FontHint is null
        && SizePt == 0 && ColorHex is null && Highlight is null
        && BoldComplexScript is null && ItalicComplexScript is null && SizeComplexScriptPt == 0
        && ComplexScript is null && RightToLeft is null
        && Language is null && EastAsiaLanguage is null && ComplexScriptLanguage is null
        && UnderlineStyle is null && UnderlineColorHex is null
        && DoubleStrikethrough is null && Caps is null && SmallCaps is null && Hidden is null
        && Outline is null && Shadow is null && Emboss is null && Imprint is null && NoProof is null
        && VerticalPosition is null && BaselineShiftPt is null && CharacterSpacingPt is null
        && HorizontalScalePercent is null && KerningMinSizePt is null
        && ThemeColor is null && ThemeColorTint is null && ThemeColorShade is null && ShadingHex is null;

    /// <summary>The underline that will actually be written: <see cref="UnderlineStyle"/> if set, else Single when <see cref="Underline"/>.</summary>
    public UnderlineStyle? EffectiveUnderline => UnderlineStyle ?? (Underline ? Word.UnderlineStyle.Single : null);

    public static readonly TextFormat None = new();
    public static readonly TextFormat BoldText = new() { Bold = true };
    public static readonly TextFormat ItalicText = new() { Italic = true };
    public static readonly TextFormat SuperscriptText = new() { VerticalPosition = VerticalTextPosition.Superscript };
    public static readonly TextFormat SubscriptText = new() { VerticalPosition = VerticalTextPosition.Subscript };
}

/// <summary>The original boolean toggles, as flags for <see cref="TextFormat.ExplicitlyOff"/>.</summary>
[Flags]
public enum RunToggles
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Strikethrough = 4,
}

/// <summary>Underline styles (ST_Underline).</summary>
public enum UnderlineStyle
{
    /// <summary>Explicitly no underline (removes an inherited one).</summary>
    None,
    Single,
    /// <summary>Underline words only, not the spaces between them.</summary>
    Words,
    Double,
    Thick,
    Dotted,
    DottedHeavy,
    Dash,
    DashedHeavy,
    DashLong,
    DashLongHeavy,
    DotDash,
    DashDotHeavy,
    DotDotDash,
    DashDotDotHeavy,
    Wave,
    WavyHeavy,
    WavyDouble,
}

/// <summary>Superscript / subscript position (ST_VerticalAlignRun).</summary>
public enum VerticalTextPosition
{
    /// <summary>Normal baseline (removes an inherited super/subscript).</summary>
    Baseline,
    Superscript,
    Subscript,
}

/// <summary>Theme colors (ST_ThemeColor).</summary>
public enum ThemeColor
{
    Dark1,
    Light1,
    Dark2,
    Light2,
    Accent1,
    Accent2,
    Accent3,
    Accent4,
    Accent5,
    Accent6,
    Hyperlink,
    FollowedHyperlink,
    Background1,
    Text1,
    Background2,
    Text2,
}

/// <summary>Theme font references (ST_Theme): the major (headings) or minor (body) theme font per script.</summary>
public enum ThemeFont
{
    MajorEastAsia,
    MajorBidi,
    MajorAscii,
    MajorHAnsi,
    MinorEastAsia,
    MinorBidi,
    MinorAscii,
    MinorHAnsi,
}

/// <summary>Font slot hint for ambiguous characters (ST_Hint).</summary>
public enum FontHint
{
    Default,
    EastAsia,
    ComplexScript,
}

/// <summary>Break kinds for <see cref="Paragraph.Break(BreakType, TextFormat?)"/> (w:br type/clear).</summary>
public enum BreakType
{
    /// <summary>Line break within the paragraph (Shift+Enter).</summary>
    Line,
    /// <summary>Page break.</summary>
    Page,
    /// <summary>Column break (next text column, or next page with one column).</summary>
    Column,
    /// <summary>Line break that also clears floating objects on the left.</summary>
    TextWrappingClearLeft,
    /// <summary>Line break that also clears floating objects on the right.</summary>
    TextWrappingClearRight,
    /// <summary>Line break that clears floating objects on both sides.</summary>
    TextWrappingClearAll,
}

/// <summary>Built-in paragraph styles emitted into styles.xml. Word renders these with
/// sensible defaults and lists them in its style gallery.</summary>
public enum ParagraphStyle
{
    Normal,
    Title,
    Heading1,
    Heading2,
    Heading3,
    Quote,
    /// <summary>Used automatically for bulleted/numbered list items.</summary>
    ListParagraph,
}

/// <summary>Paragraph alignment.</summary>
public enum ParagraphAlignment
{
    Left,
    Center,
    Right,
    Justify,
}

/// <summary>List kind for <see cref="Paragraph"/> items that are part of a list.</summary>
public enum ListKind
{
    None,
    Bullet,
    Number,
}

/// <summary>Page sizes (portrait dimensions; flipped automatically for landscape).</summary>
public enum PageSize
{
    A4,
    Letter,
    Legal,
}

/// <summary>Page orientation.</summary>
public enum PageOrientation
{
    Portrait,
    Landscape,
}
