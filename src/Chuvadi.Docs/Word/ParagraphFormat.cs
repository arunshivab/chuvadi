using System;
using System.Collections.Generic;

namespace Chuvadi.Docs.Word;

/// <summary>
/// Paragraph-level formatting — the complete set of commonly used WordprocessingML paragraph
/// properties (w:pPr): alignment, spacing, indentation, pagination control, borders, shading,
/// tab stops, outline level, bidi and East Asian typography, and the paragraph mark's own
/// run formatting. Every member is optional; null inherits from the paragraph style and
/// document defaults.
///
/// Each <see cref="Paragraph"/> owns one instance (<see cref="Paragraph.Format"/>). To reuse a
/// set of settings, build a template and copy it with <see cref="Paragraph.ApplyFormat"/>
/// or <see cref="Clone"/> — never share one instance between paragraphs.
///
/// <code>
/// var body = new ParagraphFormat
/// {
///     SpaceAfterPt = 6,
///     LineSpacing = LineSpacing.Multiple(1.15),
///     FirstLineIndentPt = 18,
///     Alignment = ParagraphAlignment.Justify,
/// };
/// doc.AddParagraph("Clinical summary …").ApplyFormat(body);
///
/// doc.AddParagraph("Total").Configure(f =>
/// {
///     f.KeepWithNext = true;
///     f.Borders = new ParagraphBorders { Top = BorderSpec.Single(0.75) };
///     f.TabStops.Add(new TabStop(430, TabAlignment.Right, TabLeader.Dot));
/// });
/// </code>
/// </summary>
public sealed class ParagraphFormat
{
    // ---- Alignment & pagination --------------------------------------------------------

    /// <summary>Horizontal alignment (w:jc). Null = inherit (left).</summary>
    public ParagraphAlignment? Alignment { get; set; }

    /// <summary>Keep on the same page as the next paragraph (w:keepNext).</summary>
    public bool? KeepWithNext { get; set; }

    /// <summary>Keep all lines of this paragraph on one page (w:keepLines).</summary>
    public bool? KeepLinesTogether { get; set; }

    /// <summary>
    /// Word's "Page break before" paragraph option (w:pageBreakBefore): the paragraph always
    /// starts a new page. Unlike <see cref="Paragraph.PageBreakBefore"/>, no break character
    /// is inserted.
    /// </summary>
    public bool? StartOnNewPage { get; set; }

    /// <summary>Prevent a single first/last line on its own page (w:widowControl).</summary>
    public bool? WidowControl { get; set; }

    /// <summary>Exclude from line numbering (w:suppressLineNumbers).</summary>
    public bool? SuppressLineNumbers { get; set; }

    /// <summary>Do not hyphenate this paragraph (w:suppressAutoHyphens).</summary>
    public bool? SuppressAutoHyphens { get; set; }

    // ---- Spacing --------------------------------------------------------------------

    /// <summary>Space before the paragraph in points (w:spacing/@w:before).</summary>
    public double? SpaceBeforePt { get; set; }

    /// <summary>Space after the paragraph in points (w:spacing/@w:after).</summary>
    public double? SpaceAfterPt { get; set; }

    /// <summary>Let Word choose the space before, as for HTML paragraphs (w:spacing/@w:beforeAutospacing).</summary>
    public bool? SpaceBeforeAuto { get; set; }

    /// <summary>Let Word choose the space after (w:spacing/@w:afterAutospacing).</summary>
    public bool? SpaceAfterAuto { get; set; }

    /// <summary>Line spacing: multiple, exactly, or at least (w:spacing/@w:line + @w:lineRule).</summary>
    public LineSpacing? LineSpacing { get; set; }

    /// <summary>Ignore space before/after between paragraphs of the same style (w:contextualSpacing).</summary>
    public bool? ContextualSpacing { get; set; }

    // ---- Indentation ----------------------------------------------------------------

    /// <summary>Left indent in points (w:ind/@w:left; the leading edge for RTL paragraphs). Negative outdents.</summary>
    public double? IndentLeftPt { get; set; }

    /// <summary>Right indent in points (w:ind/@w:right).</summary>
    public double? IndentRightPt { get; set; }

    /// <summary>First-line indent in points (w:ind/@w:firstLine). Ignored when <see cref="HangingIndentPt"/> is set.</summary>
    public double? FirstLineIndentPt { get; set; }

    /// <summary>Hanging indent in points (w:ind/@w:hanging): first line starts left of the rest.</summary>
    public double? HangingIndentPt { get; set; }

    /// <summary>Swap left/right indents on facing pages (w:mirrorIndents).</summary>
    public bool? MirrorIndents { get; set; }

    // ---- Decoration -----------------------------------------------------------------

    /// <summary>Paragraph borders (w:pBdr). Null = none/inherit.</summary>
    public ParagraphBorders? Borders { get; set; }

    /// <summary>Paragraph background shading (w:shd). Null = none/inherit.</summary>
    public Shading? Shading { get; set; }

    /// <summary>Custom tab stops (w:tabs), in any order (written sorted by position). Empty = inherit.</summary>
    public List<TabStop> TabStops { get; } = new();

    // ---- Structure ------------------------------------------------------------------

    /// <summary>Outline level 0–8 for navigation pane / table of contents (w:outlineLvl). 9 = body text. Null = inherit.</summary>
    public int? OutlineLevel { get; set; }

    /// <summary>Right-to-left paragraph — Arabic, Hebrew, Urdu (w:bidi).</summary>
    public bool? RightToLeft { get; set; }

    /// <summary>Vertical alignment of characters on each line (w:textAlignment).</summary>
    public FontAlignment? FontAlignment { get; set; }

    /// <summary>Snap to the document grid when one is defined (w:snapToGrid).</summary>
    public bool? SnapToGrid { get; set; }

    /// <summary>Adjust the right indent when a document grid is defined (w:adjustRightInd).</summary>
    public bool? AdjustRightIndent { get; set; }

    // ---- East Asian typography ----------------------------------------------------------

    /// <summary>Apply East Asian line-breaking rules (w:kinsoku).</summary>
    public bool? Kinsoku { get; set; }

    /// <summary>Allow Latin words to break mid-word at line ends (w:wordWrap = false to allow).</summary>
    public bool? WordWrap { get; set; }

    /// <summary>Allow punctuation to extend past the text extents (w:overflowPunct).</summary>
    public bool? OverflowPunctuation { get; set; }

    /// <summary>Compress punctuation at the start of a line (w:topLinePunct).</summary>
    public bool? TopLinePunctuation { get; set; }

    /// <summary>Auto space between East Asian and Latin text (w:autoSpaceDE).</summary>
    public bool? AutoSpaceDE { get; set; }

    /// <summary>Auto space between East Asian text and numbers (w:autoSpaceDN).</summary>
    public bool? AutoSpaceDN { get; set; }

    // ---- Paragraph mark ---------------------------------------------------------------

    /// <summary>
    /// Formatting of the paragraph mark (w:pPr/w:rPr). It sets the height of an EMPTY paragraph
    /// and the size of its list number/bullet, so it matters for layout fidelity. Null = inherit.
    /// </summary>
    public TextFormat? MarkFormat { get; set; }

    // ---- Operations -------------------------------------------------------------------

    /// <summary>A deep copy (borders and tab stops are copied; immutable values are shared).</summary>
    public ParagraphFormat Clone() => new ParagraphFormat().ApplyFrom(this);

    /// <summary>Makes every value equal to <paramref name="other"/>'s (unset values included).</summary>
    internal void CopyAllFrom(ParagraphFormat other)
    {
        foreach (var prop in typeof(ParagraphFormat).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            if (prop.CanWrite && prop.GetIndexParameters().Length == 0) prop.SetValue(this, prop.GetValue(other));
        Borders = other.Borders?.Clone(); // mutable: never shared
        TabStops.Clear();
        TabStops.AddRange(other.TabStops);
    }

    /// <summary>
    /// Copies every value that is SET (non-null; non-empty for tab stops) on
    /// <paramref name="other"/> onto this format, leaving the rest untouched. Tab stops are
    /// replaced when <paramref name="other"/> has any.
    /// </summary>
    public ParagraphFormat ApplyFrom(ParagraphFormat other)
    {
        if (other is null) throw new ArgumentNullException(nameof(other));
        if (ReferenceEquals(other, this)) return this;
        Alignment = other.Alignment ?? Alignment;
        KeepWithNext = other.KeepWithNext ?? KeepWithNext;
        KeepLinesTogether = other.KeepLinesTogether ?? KeepLinesTogether;
        StartOnNewPage = other.StartOnNewPage ?? StartOnNewPage;
        WidowControl = other.WidowControl ?? WidowControl;
        SuppressLineNumbers = other.SuppressLineNumbers ?? SuppressLineNumbers;
        SuppressAutoHyphens = other.SuppressAutoHyphens ?? SuppressAutoHyphens;
        SpaceBeforePt = other.SpaceBeforePt ?? SpaceBeforePt;
        SpaceAfterPt = other.SpaceAfterPt ?? SpaceAfterPt;
        SpaceBeforeAuto = other.SpaceBeforeAuto ?? SpaceBeforeAuto;
        SpaceAfterAuto = other.SpaceAfterAuto ?? SpaceAfterAuto;
        LineSpacing = other.LineSpacing ?? LineSpacing;
        ContextualSpacing = other.ContextualSpacing ?? ContextualSpacing;
        IndentLeftPt = other.IndentLeftPt ?? IndentLeftPt;
        IndentRightPt = other.IndentRightPt ?? IndentRightPt;
        FirstLineIndentPt = other.FirstLineIndentPt ?? FirstLineIndentPt;
        HangingIndentPt = other.HangingIndentPt ?? HangingIndentPt;
        MirrorIndents = other.MirrorIndents ?? MirrorIndents;
        Borders = other.Borders?.Clone() ?? Borders;
        Shading = other.Shading ?? Shading;
        if (other.TabStops.Count > 0)
        {
            TabStops.Clear();
            TabStops.AddRange(other.TabStops);
        }
        OutlineLevel = other.OutlineLevel ?? OutlineLevel;
        RightToLeft = other.RightToLeft ?? RightToLeft;
        FontAlignment = other.FontAlignment ?? FontAlignment;
        SnapToGrid = other.SnapToGrid ?? SnapToGrid;
        AdjustRightIndent = other.AdjustRightIndent ?? AdjustRightIndent;
        Kinsoku = other.Kinsoku ?? Kinsoku;
        WordWrap = other.WordWrap ?? WordWrap;
        OverflowPunctuation = other.OverflowPunctuation ?? OverflowPunctuation;
        TopLinePunctuation = other.TopLinePunctuation ?? TopLinePunctuation;
        AutoSpaceDE = other.AutoSpaceDE ?? AutoSpaceDE;
        AutoSpaceDN = other.AutoSpaceDN ?? AutoSpaceDN;
        MarkFormat = other.MarkFormat ?? MarkFormat;
        return this;
    }

    /// <summary>True when nothing is set (no w:pPr content needed beyond style/list).</summary>
    internal bool IsDefault =>
        Alignment is null && KeepWithNext is null && KeepLinesTogether is null && StartOnNewPage is null
        && WidowControl is null && SuppressLineNumbers is null && SuppressAutoHyphens is null
        && SpaceBeforePt is null && SpaceAfterPt is null && SpaceBeforeAuto is null && SpaceAfterAuto is null
        && LineSpacing is null && ContextualSpacing is null
        && IndentLeftPt is null && IndentRightPt is null && FirstLineIndentPt is null && HangingIndentPt is null
        && MirrorIndents is null && (Borders is null || Borders.IsEmpty) && Shading is null && TabStops.Count == 0
        && OutlineLevel is null && RightToLeft is null && FontAlignment is null && SnapToGrid is null
        && AdjustRightIndent is null && Kinsoku is null && WordWrap is null && OverflowPunctuation is null
        && TopLinePunctuation is null && AutoSpaceDE is null && AutoSpaceDN is null
        && (MarkFormat is null || MarkFormat.IsDefault);
}

/// <summary>Line spacing (w:spacing/@w:line with @w:lineRule). Immutable.</summary>
public sealed class LineSpacing
{
    /// <summary>How <see cref="Value"/> is interpreted.</summary>
    public LineSpacingRule Rule { get; }

    /// <summary>A multiple of single spacing (Rule = Multiple), or points (Exactly / AtLeast).</summary>
    public double Value { get; }

    private LineSpacing(LineSpacingRule rule, double value)
    {
        if (value <= 0 || double.IsNaN(value) || double.IsInfinity(value))
            throw new ArgumentOutOfRangeException(nameof(value), "Line spacing must be positive.");
        Rule = rule;
        Value = value;
    }

    /// <summary>A multiple of single spacing: 1 = single, 1.15, 1.5, 2 = double…</summary>
    public static LineSpacing Multiple(double factor) => new(LineSpacingRule.Multiple, factor);

    /// <summary>Exactly this many points per line (text taller than this is clipped).</summary>
    public static LineSpacing Exactly(double points) => new(LineSpacingRule.Exactly, points);

    /// <summary>At least this many points per line (grows for taller text).</summary>
    public static LineSpacing AtLeast(double points) => new(LineSpacingRule.AtLeast, points);

    public static readonly LineSpacing Single = Multiple(1);
    public static readonly LineSpacing OneAndHalf = Multiple(1.5);
    public static readonly LineSpacing Double = Multiple(2);

    public override bool Equals(object? obj) => obj is LineSpacing o && o.Rule == Rule && o.Value == Value;
    public override int GetHashCode() => HashCode.Combine(Rule, Value);
    public override string ToString() => Rule == LineSpacingRule.Multiple ? $"{Value}×" : $"{Rule} {Value}pt";
}

/// <summary>Interpretation of <see cref="LineSpacing.Value"/> (ST_LineSpacingRule).</summary>
public enum LineSpacingRule
{
    /// <summary>A multiple of single line spacing (lineRule="auto").</summary>
    Multiple,
    /// <summary>Exact height in points (lineRule="exact").</summary>
    Exactly,
    /// <summary>Minimum height in points (lineRule="atLeast").</summary>
    AtLeast,
}

/// <summary>
/// Background shading (w:shd) for runs, paragraphs and table cells: a pattern drawn in
/// <see cref="ColorHex"/> over a <see cref="FillHex"/> background. Immutable.
/// </summary>
public sealed record Shading
{
    /// <summary>The pattern (ST_Shd). <see cref="ShadingPattern.Clear"/> = plain fill.</summary>
    public ShadingPattern Pattern { get; init; } = ShadingPattern.Clear;

    /// <summary>Pattern (foreground) color as 6-digit hex without '#'; null = auto.</summary>
    public string? ColorHex { get; init; }

    /// <summary>Background fill as 6-digit hex without '#'; null = auto (none).</summary>
    public string? FillHex { get; init; }

    /// <summary>Theme color for the fill (w:shd/@w:themeFill). Null = none.</summary>
    public ThemeColor? ThemeFill { get; init; }

    /// <summary>A plain background fill.</summary>
    public static Shading Fill(string hexNoHash) => new() { FillHex = hexNoHash };

    /// <summary>A percentage / stripe pattern in <paramref name="patternHex"/> over <paramref name="fillHex"/>.</summary>
    public static Shading WithPattern(ShadingPattern pattern, string? patternHex, string? fillHex = null)
        => new() { Pattern = pattern, ColorHex = patternHex, FillHex = fillHex };
}

/// <summary>Shading patterns (ST_Shd).</summary>
public enum ShadingPattern
{
    Nil, Clear, Solid, HorizontalStripe, VerticalStripe, ReverseDiagonalStripe, DiagonalStripe,
    HorizontalCross, DiagonalCross, ThinHorizontalStripe, ThinVerticalStripe,
    ThinReverseDiagonalStripe, ThinDiagonalStripe, ThinHorizontalCross, ThinDiagonalCross,
    Percent5, Percent10, Percent12, Percent15, Percent20, Percent25, Percent30, Percent35,
    Percent37, Percent40, Percent45, Percent50, Percent55, Percent60, Percent62, Percent65,
    Percent70, Percent75, Percent80, Percent85, Percent87, Percent90, Percent95,
}

/// <summary>
/// One border line (w:top, w:bottom, … inside pBdr, tcBorders, tblBorders). Immutable.
/// </summary>
public sealed record BorderSpec
{
    /// <summary>Line style. <see cref="BorderStyle.None"/> removes an inherited border.</summary>
    public BorderStyle Style { get; init; } = BorderStyle.Single;

    /// <summary>Line width in points, 0.25–12 (written in eighths of a point, w:sz).</summary>
    public double WidthPt { get; init; } = 0.5;

    /// <summary>Line color as 6-digit hex without '#'; null = auto.</summary>
    public string? ColorHex { get; init; }

    /// <summary>Theme color for the line (w:themeColor). Null = none.</summary>
    public ThemeColor? ThemeColor { get; init; }

    /// <summary>Distance between the border and the text, in points (w:space).</summary>
    public double SpacePt { get; init; }

    /// <summary>Draw a shadow (w:shadow).</summary>
    public bool Shadow { get; init; }

    /// <summary>Draw with a 3-D frame effect (w:frame).</summary>
    public bool Frame { get; init; }

    /// <summary>
    /// The raw ST_Border token when the file used a style outside <see cref="BorderStyle"/>
    /// (Word's ~160 decorative art borders). Written back verbatim so such borders round-trip.
    /// </summary>
    public string? RawStyleToken { get; init; }

    /// <summary>A single line of <paramref name="widthPt"/> points.</summary>
    public static BorderSpec Single(double widthPt = 0.5, string? colorHex = null)
        => new() { Style = BorderStyle.Single, WidthPt = widthPt, ColorHex = colorHex };

    /// <summary>Explicitly no border (cancels an inherited one).</summary>
    public static readonly BorderSpec None = new() { Style = BorderStyle.None, WidthPt = 0 };
}

/// <summary>Border line styles (ST_Border, line styles; decorative art borders round-trip via <see cref="BorderSpec.RawStyleToken"/>).</summary>
public enum BorderStyle
{
    Nil, None, Single, Thick, Double, Dotted, Dashed, DotDash, DotDotDash, Triple,
    ThinThickSmallGap, ThickThinSmallGap, ThinThickThinSmallGap,
    ThinThickMediumGap, ThickThinMediumGap, ThinThickThinMediumGap,
    ThinThickLargeGap, ThickThinLargeGap, ThinThickThinLargeGap,
    Wave, DoubleWave, DashSmallGap, DashDotStroked, ThreeDEmboss, ThreeDEngrave, Outset, Inset,
}

/// <summary>Borders around a paragraph (w:pBdr). Consecutive paragraphs with identical borders share one box; <see cref="Between"/> draws between them.</summary>
public sealed class ParagraphBorders
{
    public BorderSpec? Top { get; set; }
    public BorderSpec? Left { get; set; }
    public BorderSpec? Bottom { get; set; }
    public BorderSpec? Right { get; set; }
    /// <summary>Line between consecutive paragraphs that share these borders.</summary>
    public BorderSpec? Between { get; set; }
    /// <summary>Vertical bar in the margin beside the paragraph.</summary>
    public BorderSpec? Bar { get; set; }

    /// <summary>Same border on all four sides.</summary>
    public static ParagraphBorders Box(BorderSpec border)
        => new() { Top = border, Left = border, Bottom = border, Right = border };

    public ParagraphBorders Clone() => (ParagraphBorders)MemberwiseClone();

    internal bool IsEmpty => Top is null && Left is null && Bottom is null && Right is null && Between is null && Bar is null;
}

/// <summary>A custom tab stop (w:tab inside w:tabs).</summary>
/// <param name="PositionPt">Position from the paragraph's leading indent edge, in points.</param>
/// <param name="Alignment">How text aligns at the stop.</param>
/// <param name="Leader">Fill character drawn up to the stop.</param>
public readonly record struct TabStop(double PositionPt, TabAlignment Alignment = TabAlignment.Left, TabLeader Leader = TabLeader.None);

/// <summary>Tab stop alignment (ST_TabJc).</summary>
public enum TabAlignment
{
    Left,
    Center,
    Right,
    /// <summary>Align on the decimal point.</summary>
    Decimal,
    /// <summary>Draws a vertical bar at the position (not a real stop).</summary>
    Bar,
    /// <summary>Removes an inherited tab stop at this position.</summary>
    Clear,
    /// <summary>List-number tab.</summary>
    Number,
}

/// <summary>Tab leader characters (ST_TabTlc).</summary>
public enum TabLeader
{
    None,
    /// <summary>Dots ……</summary>
    Dot,
    /// <summary>Hyphens ------</summary>
    Hyphen,
    /// <summary>Underscores ______</summary>
    Underscore,
    /// <summary>Heavy line.</summary>
    Heavy,
    /// <summary>Middle dots ·····</summary>
    MiddleDot,
}

/// <summary>Vertical alignment of characters within a line (ST_TextAlignment).</summary>
public enum FontAlignment
{
    Auto,
    Top,
    Center,
    Baseline,
    Bottom,
}
