using System;
using System.Collections.Generic;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Fonts;

namespace Chuvadi.Layout.Model
{
    /// <summary>Underline styles (OOXML ST_Underline, grouped by how they are drawn).</summary>
    public enum UnderlineStyle
    {
        None,
        Single,
        Words,
        Double,
        Thick,
        Dotted,
        DottedHeavy,
        Dash,
        DashHeavy,
        DashLong,
        DashLongHeavy,
        DotDash,
        DotDashHeavy,
        DotDotDash,
        DotDotDashHeavy,
        Wave,
        WavyHeavy,
        WavyDouble,
    }

    /// <summary>Vertical alignment of a run relative to the baseline.</summary>
    public enum VerticalTextAlignment
    {
        Baseline,
        Superscript,
        Subscript,
    }

    /// <summary>Letter-case transformation applied before shaping.</summary>
    public enum TextCapitals
    {
        None,
        AllCaps,
        SmallCaps,
    }

    /// <summary>
    /// Fully resolved (effective) character formatting. Importers resolve styles and inheritance; the engine
    /// never looks anything up. Records compare by value, so identical formats share measurement work.
    /// </summary>
    public sealed record CharacterFormat
    {
        /// <summary>Font for ASCII characters (OOXML w:rFonts/@w:ascii).</summary>
        public string FontFamily { get; init; } = "Calibri";

        /// <summary>Font for other non-East-Asian, non-complex characters (w:hAnsi). Null = <see cref="FontFamily"/>.</summary>
        public string? HighAnsiFontFamily { get; init; }

        /// <summary>Font for East Asian characters (w:eastAsia). Null = <see cref="FontFamily"/>.</summary>
        public string? EastAsianFontFamily { get; init; }

        /// <summary>Font for complex-script characters such as Indic and Arabic (w:cs). Null = <see cref="FontFamily"/>.</summary>
        public string? ComplexScriptFontFamily { get; init; }

        /// <summary>Generic class used when no font of the family or its substitutes exists.</summary>
        public GenericFontFamily GenericFamily { get; init; } = GenericFontFamily.Unknown;

        /// <summary>Size in points (w:sz / 2).</summary>
        public double FontSize { get; init; } = 11;

        /// <summary>Size for complex-script characters (w:szCs / 2). Null = <see cref="FontSize"/>.</summary>
        public double? ComplexScriptFontSize { get; init; }

        public bool Bold { get; init; }

        public bool Italic { get; init; }

        /// <summary>Bold for complex-script characters (w:bCs). Null = <see cref="Bold"/>.</summary>
        public bool? ComplexScriptBold { get; init; }

        /// <summary>Italic for complex-script characters (w:iCs). Null = <see cref="Italic"/>.</summary>
        public bool? ComplexScriptItalic { get; init; }

        public UnderlineStyle Underline { get; init; }

        /// <summary>Underline colour; null = the text colour.</summary>
        public LayoutColor? UnderlineColor { get; init; }

        public bool Strikethrough { get; init; }

        public bool DoubleStrikethrough { get; init; }

        public LayoutColor Color { get; init; } = LayoutColor.Black;

        /// <summary>Highlight (w:highlight) or run shading (w:shd fill); drawn behind the text.</summary>
        public LayoutColor? Background { get; init; }

        public TextCapitals Capitals { get; init; }

        public VerticalTextAlignment VerticalAlignment { get; init; }

        /// <summary>Extra space after each character, in points (w:spacing, may be negative).</summary>
        public double CharacterSpacing { get; init; }

        /// <summary>Horizontal scale in percent (w:w), 1..600; 100 is normal.</summary>
        public double HorizontalScalePercent { get; init; } = 100;

        /// <summary>Kern pairs when the size is at least this many points (w:kern / 2); 0 = no kerning.</summary>
        public double KerningMinimumSize { get; init; }

        /// <summary>Baseline raise (positive) or lower (negative) in points (w:position / 2).</summary>
        public double BaselineShift { get; init; }

        /// <summary>Hidden text (w:vanish) is not laid out.</summary>
        public bool Hidden { get; init; }

        /// <summary>BCP 47 language of the run (w:lang), for shaping and number formatting.</summary>
        public string? Language { get; init; }

        public static CharacterFormat Default { get; } = new CharacterFormat();
    }

    /// <summary>Paragraph alignment.</summary>
    public enum ParagraphAlignment
    {
        Left,
        Center,
        Right,

        /// <summary>Justified; the last line and lines ending in a line break stay left-aligned.</summary>
        Justify,

        /// <summary>Distributed: every line, including the last, fills the width by spacing characters.</summary>
        Distribute,
    }

    /// <summary>How <see cref="ParagraphFormat.LineSpacing"/> is interpreted.</summary>
    public enum LineSpacingRule
    {
        /// <summary>A multiple of single spacing (w:lineRule="auto"; 1.0 single, 1.15, 1.5, 2.0 double).</summary>
        Multiple,

        /// <summary>Exactly this many points.</summary>
        Exact,

        /// <summary>At least this many points.</summary>
        AtLeast,
    }

    /// <summary>Tab stop alignment.</summary>
    public enum TabAlignment
    {
        Left,
        Center,
        Right,
        Decimal,

        /// <summary>A vertical bar drawn at the position; does not stop text.</summary>
        Bar,
    }

    /// <summary>Tab leader characters.</summary>
    public enum TabLeader
    {
        None,
        Dot,
        Hyphen,
        Underscore,
        Heavy,
        MiddleDot,
    }

    /// <summary>A tab stop. Position is measured from the paragraph's left indent origin (the text area's left edge).</summary>
    public readonly record struct TabStop(double Position, TabAlignment Alignment = TabAlignment.Left, TabLeader Leader = TabLeader.None);

    /// <summary>One side of a border.</summary>
    public readonly record struct BorderLine(double Width, LayoutColor Color, LineDash Dash = LineDash.Solid, double Space = 0, bool Double = false)
    {
        public static BorderLine None { get; } = new BorderLine(0, LayoutColor.Transparent);

        public bool IsVisible => Width > 0 && !Color.IsTransparent;
    }

    /// <summary>Fully resolved paragraph formatting. Lengths in points.</summary>
    public sealed record ParagraphFormat
    {
        public ParagraphAlignment Alignment { get; init; } = ParagraphAlignment.Left;

        /// <summary>Left indent (w:ind/@w:start or @w:left).</summary>
        public double LeftIndent { get; init; }

        /// <summary>Right indent.</summary>
        public double RightIndent { get; init; }

        /// <summary>First-line indent relative to <see cref="LeftIndent"/>; negative for a hanging indent.</summary>
        public double FirstLineIndent { get; init; }

        public double SpaceBefore { get; init; }

        public double SpaceAfter { get; init; }

        /// <summary>Ignore space before/after between paragraphs of the same <see cref="StyleId"/> (w:contextualSpacing).</summary>
        public bool ContextualSpacing { get; init; }

        public LineSpacingRule LineSpacingRule { get; init; } = LineSpacingRule.Multiple;

        /// <summary>Multiple (for <see cref="LineSpacingRule.Multiple"/>) or points.</summary>
        public double LineSpacing { get; init; } = 1.0;

        public bool KeepWithNext { get; init; }

        public bool KeepLinesTogether { get; init; }

        public bool PageBreakBefore { get; init; }

        /// <summary>Widow and orphan control (Word default on).</summary>
        public bool WidowControl { get; init; } = true;

        /// <summary>Custom tab stops, any order.</summary>
        public IReadOnlyList<TabStop> TabStops { get; init; } = Array.Empty<TabStop>();

        /// <summary>Paragraph shading behind the text area.</summary>
        public LayoutColor? Background { get; init; }

        public BorderLine BorderTop { get; init; } = BorderLine.None;
        public BorderLine BorderBottom { get; init; } = BorderLine.None;
        public BorderLine BorderLeft { get; init; } = BorderLine.None;
        public BorderLine BorderRight { get; init; } = BorderLine.None;

        /// <summary>Style identifier, used only for contextual spacing.</summary>
        public string? StyleId { get; init; }

        public static ParagraphFormat Default { get; } = new ParagraphFormat();
    }
}
