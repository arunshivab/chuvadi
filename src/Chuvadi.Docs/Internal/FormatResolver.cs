using System;
using System.Collections.Generic;
using System.Linq;
using Chuvadi.Docs.Word;

namespace Chuvadi.Docs.Internal;

/// <summary>
/// Resolves EFFECTIVE (fully inherited) formatting through the style hierarchy, per
/// ECMA-376 Part 1 §17.7.2 (style hierarchy) and §17.7.3 (toggle properties):
/// <list type="number">
/// <item>document defaults (w:docDefaults);</item>
/// <item>the paragraph style and its basedOn chain (root first, each level overriding);</item>
/// <item>the character style and its basedOn chain;</item>
/// <item>direct formatting, which always wins.</item>
/// </list>
/// Toggle properties (bold, italic, caps, small caps, strike, double strike, outline, shadow,
/// emboss, imprint, hidden, and the complex-script bold/italic) set by BOTH the paragraph
/// style and the character style combine by exclusive-or; set by one of them, that value
/// applies; direct formatting overrides absolutely.
/// <para>Design choice: the combined style-level value REPLACES a toggle set in the document
/// defaults (the spec does not pin docDefaults' interaction with toggles down; Word documents
/// do not set toggles in docDefaults in practice).</para>
/// </summary>
internal static class FormatResolver
{
    private const int MaxChainDepth = 32;

    public static TextFormat ResolveRun(Document doc, Paragraph p, Run r)
    {
        var bag = RunBag.From(doc.Defaults.RunFormat);
        var paragraphStyle = ChainRunBag(doc, ParagraphStyleId(doc, p), StyleType.Paragraph);
        var characterStyleId = r.CharacterStyleId ?? (r.HyperlinkUrl is not null ? "Hyperlink" : null);
        var characterStyle = ChainRunBag(doc, characterStyleId, StyleType.Character);
        bag.Overlay(RunBag.CombineStyleLevels(paragraphStyle, characterStyle));
        bag.Overlay(RunBag.From(r.Format));
        return bag.ToFormat();
    }

    public static ParagraphFormat ResolveParagraph(Document doc, Paragraph p)
    {
        var levels = new List<ParagraphFormat> { doc.Defaults.ParagraphFormat };
        foreach (var s in Chain(doc, ParagraphStyleId(doc, p), StyleType.Paragraph)) levels.Add(s.ParagraphFormat);
        levels.Add(p.Format);

        var result = new ParagraphFormat();
        var tabs = new SortedDictionary<double, TabStop>();
        ParagraphBorders? borders = null;
        foreach (var level in levels)
        {
            var keepBorders = borders;
            // Mutually exclusive pairs: a level that sets one side replaces an inherited other side.
            if (level.FirstLineIndentPt is not null && level.HangingIndentPt is null) result.HangingIndentPt = null;
            if (level.HangingIndentPt is not null && level.FirstLineIndentPt is null) result.FirstLineIndentPt = null;
            if (level.SpaceBeforePt is not null && level.SpaceBeforeAuto is null) result.SpaceBeforeAuto = null;
            if (level.SpaceAfterPt is not null && level.SpaceAfterAuto is null) result.SpaceAfterAuto = null;
            result.ApplyFrom(level);

            // Tab stops accumulate down the hierarchy; a Clear stop removes the inherited one.
            foreach (var t in level.TabStops)
            {
                if (t.Alignment == TabAlignment.Clear) tabs.Remove(t.PositionPt);
                else tabs[t.PositionPt] = t;
            }
            // Borders merge per side.
            if (level.Borders is { } lb)
            {
                borders = keepBorders?.Clone() ?? new ParagraphBorders();
                borders.Top = lb.Top ?? borders.Top;
                borders.Left = lb.Left ?? borders.Left;
                borders.Bottom = lb.Bottom ?? borders.Bottom;
                borders.Right = lb.Right ?? borders.Right;
                borders.Between = lb.Between ?? borders.Between;
                borders.Bar = lb.Bar ?? borders.Bar;
            }
        }
        result.TabStops.Clear();
        result.TabStops.AddRange(tabs.Values);
        result.Borders = borders;
        return result;
    }

    /// <summary>The paragraph's style id, falling back to the default paragraph style when it does not exist.</summary>
    private static string? ParagraphStyleId(Document doc, Paragraph p)
    {
        var id = p.StyleId;
        if (doc.Styles.TryGet(id, out var s) && s.Type == StyleType.Paragraph) return id;
        return doc.Styles.GetDefault(StyleType.Paragraph)?.StyleId;
    }

    /// <summary>The style and its basedOn ancestors, ROOT FIRST. Stops at missing styles, cycles and a depth cap.</summary>
    internal static List<StyleDefinition> Chain(Document doc, string? styleId, StyleType type)
    {
        var chain = new List<StyleDefinition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var id = styleId;
        while (id is not null && chain.Count < MaxChainDepth && seen.Add(id) && doc.Styles.TryGet(id, out var s))
        {
            if (s.Type != type) break;
            chain.Add(s);
            id = s.BasedOn;
        }
        chain.Reverse();
        return chain;
    }

    private static RunBag ChainRunBag(Document doc, string? styleId, StyleType type)
    {
        var bag = new RunBag();
        foreach (var s in Chain(doc, styleId, type)) bag.Overlay(RunBag.From(s.RunFormat));
        return bag;
    }

    /// <summary>Every run property as "unset (null) or value", so levels can be overlaid.</summary>
    private sealed class RunBag
    {
        // Toggles (§17.7.3).
        public bool? Bold, BoldCs, Italic, ItalicCs, Caps, SmallCaps, Strike, DStrike, Outline, Shadow, Emboss, Imprint, Hidden;
        // Everything else overrides.
        public string? Ascii, HAnsi, EastAsia, Cs, Color, Highlight, UnderlineColor, Lang, LangEa, LangBidi;
        public ThemeFont? AsciiTheme, HAnsiTheme, EaTheme, CsTheme;
        public FontHint? Hint;
        public double? Size, SizeCs, Spacing, Kern, Position;
        public bool? CsFlag, Rtl, NoProof;
        public UnderlineStyle? Underline;
        public VerticalTextPosition? VertAlign;
        public int? Scale;
        public ThemeColor? ThemeColor;
        public byte? Tint, Shade;
        public Shading? Shading;

        /// <summary>A level's values, with the writer's own complex-script mirroring applied so what is resolved is what Word reads.</summary>
        public static RunBag From(TextFormat? f)
        {
            var b = new RunBag();
            if (f is null) return b;
            bool mirror = f.MirrorToComplexScript;
            var off = f.ExplicitlyOff;
            b.Bold = f.Bold ? true : off.HasFlag(RunToggles.Bold) ? false : null;
            b.BoldCs = f.BoldComplexScript ?? (mirror && f.Bold ? true : null);
            b.Italic = f.Italic ? true : off.HasFlag(RunToggles.Italic) ? false : null;
            b.ItalicCs = f.ItalicComplexScript ?? (mirror && f.Italic ? true : null);
            b.Strike = f.Strikethrough ? true : off.HasFlag(RunToggles.Strikethrough) ? false : null;
            b.Caps = f.Caps; b.SmallCaps = f.SmallCaps; b.DStrike = f.DoubleStrikethrough;
            b.Outline = f.Outline; b.Shadow = f.Shadow; b.Emboss = f.Emboss; b.Imprint = f.Imprint; b.Hidden = f.Hidden;
            b.Ascii = f.AsciiFont; b.HAnsi = f.HighAnsiFont; b.EastAsia = f.EastAsiaFont; b.Cs = f.ComplexScriptFont;
            b.AsciiTheme = f.AsciiThemeFont; b.HAnsiTheme = f.HighAnsiThemeFont; b.EaTheme = f.EastAsiaThemeFont; b.CsTheme = f.ComplexScriptThemeFont;
            b.Hint = f.FontHint;
            b.Size = f.SizePt > 0 ? f.SizePt : null;
            b.SizeCs = f.SizeComplexScriptPt > 0 ? f.SizeComplexScriptPt : (mirror && f.SizePt > 0 ? f.SizePt : null);
            b.Color = f.ColorHex; b.ThemeColor = f.ThemeColor; b.Tint = f.ThemeColorTint; b.Shade = f.ThemeColorShade;
            b.Highlight = f.Highlight;
            b.Underline = f.EffectiveUnderline; b.UnderlineColor = f.UnderlineColorHex;
            b.Shading = f.Shading ?? (f.ShadingHex is { } hex ? Word.Shading.Fill(hex) : null);
            b.VertAlign = f.VerticalPosition; b.Position = f.BaselineShiftPt; b.Spacing = f.CharacterSpacingPt;
            b.Scale = f.HorizontalScalePercent; b.Kern = f.KerningMinSizePt;
            b.CsFlag = f.ComplexScript; b.Rtl = f.RightToLeft; b.NoProof = f.NoProof;
            b.Lang = f.Language; b.LangEa = f.EastAsiaLanguage; b.LangBidi = f.ComplexScriptLanguage;
            return b;
        }

        /// <summary>Overlays every SET value of <paramref name="top"/> onto this bag.</summary>
        public void Overlay(RunBag top)
        {
            Bold = top.Bold ?? Bold; BoldCs = top.BoldCs ?? BoldCs; Italic = top.Italic ?? Italic; ItalicCs = top.ItalicCs ?? ItalicCs;
            Caps = top.Caps ?? Caps; SmallCaps = top.SmallCaps ?? SmallCaps; Strike = top.Strike ?? Strike; DStrike = top.DStrike ?? DStrike;
            Outline = top.Outline ?? Outline; Shadow = top.Shadow ?? Shadow; Emboss = top.Emboss ?? Emboss; Imprint = top.Imprint ?? Imprint;
            Hidden = top.Hidden ?? Hidden;
            OverlayNonToggles(top);
        }

        private void OverlayNonToggles(RunBag top)
        {
            Ascii = top.Ascii ?? Ascii; HAnsi = top.HAnsi ?? HAnsi; EastAsia = top.EastAsia ?? EastAsia; Cs = top.Cs ?? Cs;
            AsciiTheme = top.AsciiTheme ?? AsciiTheme; HAnsiTheme = top.HAnsiTheme ?? HAnsiTheme;
            EaTheme = top.EaTheme ?? EaTheme; CsTheme = top.CsTheme ?? CsTheme; Hint = top.Hint ?? Hint;
            Size = top.Size ?? Size; SizeCs = top.SizeCs ?? SizeCs;
            Color = top.Color ?? Color; ThemeColor = top.ThemeColor ?? ThemeColor; Tint = top.Tint ?? Tint; Shade = top.Shade ?? Shade;
            Highlight = top.Highlight ?? Highlight; Underline = top.Underline ?? Underline; UnderlineColor = top.UnderlineColor ?? UnderlineColor;
            Shading = top.Shading ?? Shading; VertAlign = top.VertAlign ?? VertAlign; Position = top.Position ?? Position;
            Spacing = top.Spacing ?? Spacing; Scale = top.Scale ?? Scale; Kern = top.Kern ?? Kern;
            CsFlag = top.CsFlag ?? CsFlag; Rtl = top.Rtl ?? Rtl; NoProof = top.NoProof ?? NoProof;
            Lang = top.Lang ?? Lang; LangEa = top.LangEa ?? LangEa; LangBidi = top.LangBidi ?? LangBidi;
        }

        /// <summary>Paragraph-style level ⊕ character-style level: toggles XOR when both are set; other properties: character style wins.</summary>
        public static RunBag CombineStyleLevels(RunBag paragraphStyle, RunBag characterStyle)
        {
            static bool? X(bool? a, bool? b) => a is null ? b : b is null ? a : a.Value ^ b.Value;
            var r = new RunBag
            {
                Bold = X(paragraphStyle.Bold, characterStyle.Bold),
                BoldCs = X(paragraphStyle.BoldCs, characterStyle.BoldCs),
                Italic = X(paragraphStyle.Italic, characterStyle.Italic),
                ItalicCs = X(paragraphStyle.ItalicCs, characterStyle.ItalicCs),
                Caps = X(paragraphStyle.Caps, characterStyle.Caps),
                SmallCaps = X(paragraphStyle.SmallCaps, characterStyle.SmallCaps),
                Strike = X(paragraphStyle.Strike, characterStyle.Strike),
                DStrike = X(paragraphStyle.DStrike, characterStyle.DStrike),
                Outline = X(paragraphStyle.Outline, characterStyle.Outline),
                Shadow = X(paragraphStyle.Shadow, characterStyle.Shadow),
                Emboss = X(paragraphStyle.Emboss, characterStyle.Emboss),
                Imprint = X(paragraphStyle.Imprint, characterStyle.Imprint),
                Hidden = X(paragraphStyle.Hidden, characterStyle.Hidden),
            };
            r.OverlayNonToggles(paragraphStyle);
            r.OverlayNonToggles(characterStyle);
            return r;
        }

        public TextFormat ToFormat()
        {
            var off = RunToggles.None;
            if (Bold == false) off |= RunToggles.Bold;
            if (Italic == false) off |= RunToggles.Italic;
            if (Strike == false) off |= RunToggles.Strikethrough;
            bool plainFill = Shading is { } sh && FormattingXml.IsPlainFill(sh);
            return new TextFormat
            {
                MirrorToComplexScript = false,
                Bold = Bold == true, Italic = Italic == true, Strikethrough = Strike == true,
                Underline = Underline == UnderlineStyle.Single && UnderlineColor is null,
                UnderlineStyle = Underline == UnderlineStyle.Single && UnderlineColor is null ? null : Underline,
                UnderlineColorHex = UnderlineColor,
                ExplicitlyOff = off,
                BoldComplexScript = BoldCs, ItalicComplexScript = ItalicCs,
                Caps = Caps, SmallCaps = SmallCaps, DoubleStrikethrough = DStrike,
                Outline = Outline, Shadow = Shadow, Emboss = Emboss, Imprint = Imprint, Hidden = Hidden,
                AsciiFont = Ascii, HighAnsiFont = HAnsi, EastAsiaFont = EastAsia, ComplexScriptFont = Cs,
                AsciiThemeFont = AsciiTheme, HighAnsiThemeFont = HAnsiTheme, EastAsiaThemeFont = EaTheme, ComplexScriptThemeFont = CsTheme,
                FontHint = Hint,
                SizePt = Size ?? 0, SizeComplexScriptPt = SizeCs ?? 0,
                ColorHex = Color, ThemeColor = ThemeColor, ThemeColorTint = Tint, ThemeColorShade = Shade,
                Highlight = Highlight,
                ShadingHex = plainFill ? Shading!.FillHex : null,
                Shading = plainFill ? null : Shading,
                VerticalPosition = VertAlign, BaselineShiftPt = Position, CharacterSpacingPt = Spacing,
                HorizontalScalePercent = Scale, KerningMinSizePt = Kern,
                ComplexScript = CsFlag, RightToLeft = Rtl, NoProof = NoProof,
                Language = Lang, EastAsiaLanguage = LangEa, ComplexScriptLanguage = LangBidi,
            };
        }
    }
}
