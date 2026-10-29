using System;
using System.Globalization;
using System.Xml;
using Chuvadi.Docs.Word;

namespace Chuvadi.Docs.Internal;

/// <summary>
/// Writes and reads <c>w:rPr</c> (run properties) ⇄ <see cref="TextFormat"/>. One place for
/// the element order required by the schema (CT_RPr is an ordered sequence; Word tolerates
/// misordering but strict validators and some consumers do not) and for the value mappings,
/// shared by the writer, the loader and — later — style definitions.
/// </summary>
internal static class RunProperties
{
    private const string W = DocumentSerializer.W;

    /// <summary>
    /// Writes <c>w:rPr</c> for <paramref name="f"/>, optionally led by a character style
    /// reference. Writes nothing when there is nothing to say.
    /// Order (CT_RPr): rStyle, rFonts, b, bCs, i, iCs, caps, smallCaps, strike, dstrike,
    /// outline, shadow, emboss, imprint, noProof, vanish, color, spacing, w, kern, position,
    /// sz, szCs, highlight, u, shd, vertAlign, rtl, cs, lang.
    /// </summary>
    public static void Write(XmlWriter x, TextFormat f, string? characterStyleId = null)
    {
        if (f.IsDefault && characterStyleId is null) return;
        x.WriteStartElement("rPr", W);

        if (characterStyleId is not null) Val(x, "rStyle", characterStyleId);

        if (f.AsciiFont is not null || f.HighAnsiFont is not null || f.EastAsiaFont is not null
            || f.ComplexScriptFont is not null || f.FontHint is not null
            || f.AsciiThemeFont is not null || f.HighAnsiThemeFont is not null
            || f.EastAsiaThemeFont is not null || f.ComplexScriptThemeFont is not null)
        {
            x.WriteStartElement("rFonts", W);
            if (f.AsciiFont is not null) x.WriteAttributeString("ascii", W, f.AsciiFont);
            if (f.HighAnsiFont is not null) x.WriteAttributeString("hAnsi", W, f.HighAnsiFont);
            if (f.EastAsiaFont is not null) x.WriteAttributeString("eastAsia", W, f.EastAsiaFont);
            if (f.ComplexScriptFont is not null) x.WriteAttributeString("cs", W, f.ComplexScriptFont);
            if (f.FontHint is FontHint hint) x.WriteAttributeString("hint", W, HintToken(hint));
            if (f.AsciiThemeFont is ThemeFont at) x.WriteAttributeString("asciiTheme", W, ThemeFontToken(at));
            if (f.HighAnsiThemeFont is ThemeFont ht) x.WriteAttributeString("hAnsiTheme", W, ThemeFontToken(ht));
            if (f.EastAsiaThemeFont is ThemeFont et) x.WriteAttributeString("eastAsiaTheme", W, ThemeFontToken(et));
            if (f.ComplexScriptThemeFont is ThemeFont ct) x.WriteAttributeString("cstheme", W, ThemeFontToken(ct));
            x.WriteEndElement();
        }

        // With mirroring on, bold/italic/size apply to complex-script text too (Word's toolbar).
        bool mirror = f.MirrorToComplexScript;
        var off = f.ExplicitlyOff;
        Toggle(x, "b", f.Bold ? true : (off.HasFlag(RunToggles.Bold) ? false : null));
        Toggle(x, "bCs", f.BoldComplexScript ?? (mirror && f.Bold ? true : null));
        Toggle(x, "i", f.Italic ? true : (off.HasFlag(RunToggles.Italic) ? false : null));
        Toggle(x, "iCs", f.ItalicComplexScript ?? (mirror && f.Italic ? true : null));
        Toggle(x, "caps", f.Caps);
        Toggle(x, "smallCaps", f.SmallCaps);
        Toggle(x, "strike", f.Strikethrough ? true : (off.HasFlag(RunToggles.Strikethrough) ? false : null));
        Toggle(x, "dstrike", f.DoubleStrikethrough);
        Toggle(x, "outline", f.Outline);
        Toggle(x, "shadow", f.Shadow);
        Toggle(x, "emboss", f.Emboss);
        Toggle(x, "imprint", f.Imprint);
        Toggle(x, "noProof", f.NoProof);
        Toggle(x, "vanish", f.Hidden);

        if (f.ColorHex is not null || f.ThemeColor is not null)
        {
            x.WriteStartElement("color", W);
            x.WriteAttributeString("val", W, f.ColorHex?.TrimStart('#') ?? "auto");
            if (f.ThemeColor is ThemeColor tc) x.WriteAttributeString("themeColor", W, ThemeColorToken(tc));
            if (f.ThemeColorTint is byte tint) x.WriteAttributeString("themeTint", W, tint.ToString("X2", CultureInfo.InvariantCulture));
            if (f.ThemeColorShade is byte shade) x.WriteAttributeString("themeShade", W, shade.ToString("X2", CultureInfo.InvariantCulture));
            x.WriteEndElement();
        }

        if (f.CharacterSpacingPt is double sp) Val(x, "spacing", Twips(sp));
        if (f.HorizontalScalePercent is int scale) Val(x, "w", Math.Clamp(scale, 1, 600).ToString(CultureInfo.InvariantCulture));
        if (f.KerningMinSizePt is double kern) Val(x, "kern", HalfPoints(kern));
        if (f.BaselineShiftPt is double pos) Val(x, "position", HalfPoints(pos));

        if (f.SizePt > 0) Val(x, "sz", HalfPoints(f.SizePt));
        double csSize = f.SizeComplexScriptPt > 0 ? f.SizeComplexScriptPt : (mirror ? f.SizePt : 0);
        if (csSize > 0) Val(x, "szCs", HalfPoints(csSize));

        if (f.Highlight is not null) Val(x, "highlight", f.Highlight);

        if (f.EffectiveUnderline is UnderlineStyle u)
        {
            x.WriteStartElement("u", W);
            x.WriteAttributeString("val", W, UnderlineToken(u));
            if (f.UnderlineColorHex is not null) x.WriteAttributeString("color", W, f.UnderlineColorHex.TrimStart('#'));
            x.WriteEndElement();
        }

        if (f.Shading is not null) FormattingXml.WriteShading(x, f.Shading);
        else if (f.ShadingHex is not null) FormattingXml.WriteShading(x, Word.Shading.Fill(f.ShadingHex));

        if (f.VerticalPosition is VerticalTextPosition vp)
            Val(x, "vertAlign", vp switch
            {
                VerticalTextPosition.Superscript => "superscript",
                VerticalTextPosition.Subscript => "subscript",
                _ => "baseline",
            });

        Toggle(x, "rtl", f.RightToLeft);
        Toggle(x, "cs", f.ComplexScript);

        if (f.Language is not null || f.EastAsiaLanguage is not null || f.ComplexScriptLanguage is not null)
        {
            x.WriteStartElement("lang", W);
            if (f.Language is not null) x.WriteAttributeString("val", W, f.Language);
            if (f.EastAsiaLanguage is not null) x.WriteAttributeString("eastAsia", W, f.EastAsiaLanguage);
            if (f.ComplexScriptLanguage is not null) x.WriteAttributeString("bidi", W, f.ComplexScriptLanguage);
            x.WriteEndElement();
        }

        x.WriteEndElement(); // rPr
    }

    /// <summary>
    /// Reads a <c>w:rPr</c> subtree (reader positioned on the rPr element, as given by
    /// ReadSubtree) into a <see cref="TextFormat"/>. Unknown children are ignored.
    /// Also returns the character style id (w:rStyle) when present.
    /// </summary>
    public static TextFormat Read(XmlReader sub, out string? characterStyleId)
    {
        characterStyleId = null;
        bool b = false, i = false, u = false, strike = false;
        var explicitlyOff = RunToggles.None;
        string? ascii = null, hAnsi = null, eastAsia = null, cs = null, color = null, highlight = null;
        string? underlineColor = null, shading = null, lang = null, langEa = null, langBidi = null;
        Shading? fullShading = null;
        ThemeFont? asciiTheme = null, hAnsiTheme = null, eaTheme = null, csTheme = null;
        FontHint? hint = null;
        double size = 0, sizeCs = 0;
        bool? bCs = null, iCs = null, caps = null, smallCaps = null, dstrike = null, outline = null,
            shadow = null, emboss = null, imprint = null, noProof = null, vanish = null, rtl = null, csFlag = null;
        UnderlineStyle? underlineStyle = null;
        VerticalTextPosition? vertAlign = null;
        double? spacing = null, kern = null, position = null;
        int? scale = null;
        ThemeColor? themeColor = null;
        byte? tint = null, shade = null;

        if (!sub.Read()) return TextFormat.None;
        while (sub.Read())
        {
            if (sub.NodeType != XmlNodeType.Element || sub.NamespaceURI != W) continue;
            switch (sub.LocalName)
            {
                case "rStyle": characterStyleId = Attr(sub, "val"); break;
                case "rFonts":
                    ascii = Attr(sub, "ascii");
                    hAnsi = Attr(sub, "hAnsi");
                    eastAsia = Attr(sub, "eastAsia");
                    cs = Attr(sub, "cs");
                    hint = ParseHint(Attr(sub, "hint"));
                    asciiTheme = ParseThemeFont(Attr(sub, "asciiTheme"));
                    hAnsiTheme = ParseThemeFont(Attr(sub, "hAnsiTheme"));
                    eaTheme = ParseThemeFont(Attr(sub, "eastAsiaTheme"));
                    csTheme = ParseThemeFont(Attr(sub, "cstheme"));
                    break;
                case "b":
                    b = OnOff(sub) == true;
                    if (!b) explicitlyOff |= RunToggles.Bold;
                    break;
                case "bCs": bCs = OnOff(sub); break;
                case "i":
                    i = OnOff(sub) == true;
                    if (!i) explicitlyOff |= RunToggles.Italic;
                    break;
                case "iCs": iCs = OnOff(sub); break;
                case "caps": caps = OnOff(sub); break;
                case "smallCaps": smallCaps = OnOff(sub); break;
                case "strike":
                    strike = OnOff(sub) == true;
                    if (!strike) explicitlyOff |= RunToggles.Strikethrough;
                    break;
                case "dstrike": dstrike = OnOff(sub); break;
                case "outline": outline = OnOff(sub); break;
                case "shadow": shadow = OnOff(sub); break;
                case "emboss": emboss = OnOff(sub); break;
                case "imprint": imprint = OnOff(sub); break;
                case "noProof": noProof = OnOff(sub); break;
                case "vanish": vanish = OnOff(sub); break;
                case "rtl": rtl = OnOff(sub); break;
                case "cs": csFlag = OnOff(sub); break;
                case "color":
                    var c = Attr(sub, "val");
                    color = (c is null || c.Equals("auto", StringComparison.OrdinalIgnoreCase)) ? null : c;
                    themeColor = ParseThemeColorToken(Attr(sub, "themeColor"));
                    tint = ParseHexByte(Attr(sub, "themeTint"));
                    shade = ParseHexByte(Attr(sub, "themeShade"));
                    break;
                case "spacing": spacing = FromTwips(Attr(sub, "val")); break;
                case "w": scale = ParseInt(Attr(sub, "val")); break;
                case "kern": kern = FromHalfPoints(Attr(sub, "val")); break;
                case "position": position = FromHalfPoints(Attr(sub, "val")); break;
                case "sz": size = FromHalfPoints(Attr(sub, "val")) ?? 0; break;
                case "szCs": sizeCs = FromHalfPoints(Attr(sub, "val")) ?? 0; break;
                case "highlight":
                    var h = Attr(sub, "val");
                    highlight = h; // "none" is kept: it cancels an inherited highlight
                    break;
                case "u":
                    underlineStyle = ParseUnderline(Attr(sub, "val"));
                    underlineColor = Attr(sub, "color") is { } uc && !uc.Equals("auto", StringComparison.OrdinalIgnoreCase) ? uc : null;
                    break;
                case "shd":
                    // A plain fill maps to the ShadingHex shortcut; anything richer (pattern,
                    // pattern color, theme fill, auto fill) is carried in full as Shading.
                    var shd = FormattingXml.ReadShading(sub);
                    if (FormattingXml.IsPlainFill(shd)) shading = shd.FillHex;
                    else fullShading = shd;
                    break;
                case "vertAlign":
                    vertAlign = Attr(sub, "val") switch
                    {
                        "superscript" => VerticalTextPosition.Superscript,
                        "subscript" => VerticalTextPosition.Subscript,
                        "baseline" => VerticalTextPosition.Baseline,
                        _ => null,
                    };
                    break;
                case "lang":
                    lang = Attr(sub, "val");
                    langEa = Attr(sub, "eastAsia");
                    langBidi = Attr(sub, "bidi");
                    break;
            }
        }

        // A plain single underline maps to the original bool for compatibility.
        if (underlineStyle == UnderlineStyle.Single && underlineColor is null) { u = true; underlineStyle = null; }

        // Complex-script properties are carried exactly as stated, with mirroring off, so the
        // model reflects what the file says and a load → save round-trip is byte-faithful.
        return new TextFormat
        {
            MirrorToComplexScript = false,
            ExplicitlyOff = explicitlyOff,
            Bold = b,
            Italic = i,
            Underline = u,
            Strikethrough = strike,
            AsciiFont = ascii,
            HighAnsiFont = hAnsi,
            EastAsiaFont = eastAsia,
            ComplexScriptFont = cs,
            FontHint = hint,
            AsciiThemeFont = asciiTheme,
            HighAnsiThemeFont = hAnsiTheme,
            EastAsiaThemeFont = eaTheme,
            ComplexScriptThemeFont = csTheme,
            SizePt = size,
            SizeComplexScriptPt = sizeCs,
            BoldComplexScript = bCs,
            ItalicComplexScript = iCs,
            ColorHex = color,
            ThemeColor = themeColor,
            ThemeColorTint = tint,
            ThemeColorShade = shade,
            Highlight = highlight,
            UnderlineStyle = underlineStyle,
            UnderlineColorHex = underlineColor,
            DoubleStrikethrough = dstrike,
            Caps = caps,
            SmallCaps = smallCaps,
            Outline = outline,
            Shadow = shadow,
            Emboss = emboss,
            Imprint = imprint,
            NoProof = noProof,
            Hidden = vanish,
            RightToLeft = rtl,
            ComplexScript = csFlag,
            CharacterSpacingPt = spacing,
            HorizontalScalePercent = scale,
            KerningMinSizePt = kern,
            BaselineShiftPt = position,
            ShadingHex = shading,
            Shading = fullShading,
            VerticalPosition = vertAlign,
            Language = lang,
            EastAsiaLanguage = langEa,
            ComplexScriptLanguage = langBidi,
        };
    }

    // ---- Token maps -------------------------------------------------------------------

    internal static string UnderlineToken(UnderlineStyle u) => u switch
    {
        UnderlineStyle.None => "none",
        UnderlineStyle.Single => "single",
        UnderlineStyle.Words => "words",
        UnderlineStyle.Double => "double",
        UnderlineStyle.Thick => "thick",
        UnderlineStyle.Dotted => "dotted",
        UnderlineStyle.DottedHeavy => "dottedHeavy",
        UnderlineStyle.Dash => "dash",
        UnderlineStyle.DashedHeavy => "dashedHeavy",
        UnderlineStyle.DashLong => "dashLong",
        UnderlineStyle.DashLongHeavy => "dashLongHeavy",
        UnderlineStyle.DotDash => "dotDash",
        UnderlineStyle.DashDotHeavy => "dashDotHeavy",
        UnderlineStyle.DotDotDash => "dotDotDash",
        UnderlineStyle.DashDotDotHeavy => "dashDotDotHeavy",
        UnderlineStyle.Wave => "wave",
        UnderlineStyle.WavyHeavy => "wavyHeavy",
        UnderlineStyle.WavyDouble => "wavyDouble",
        _ => "single",
    };

    private static UnderlineStyle? ParseUnderline(string? v)
    {
        if (v is null) return UnderlineStyle.Single; // <w:u/> with no val means single
        foreach (UnderlineStyle u in Enum.GetValues<UnderlineStyle>())
            if (UnderlineToken(u) == v) return u;
        return UnderlineStyle.Single;
    }

    internal static string ThemeColorToken(ThemeColor c) => c switch
    {
        ThemeColor.Dark1 => "dark1",
        ThemeColor.Light1 => "light1",
        ThemeColor.Dark2 => "dark2",
        ThemeColor.Light2 => "light2",
        ThemeColor.Accent1 => "accent1",
        ThemeColor.Accent2 => "accent2",
        ThemeColor.Accent3 => "accent3",
        ThemeColor.Accent4 => "accent4",
        ThemeColor.Accent5 => "accent5",
        ThemeColor.Accent6 => "accent6",
        ThemeColor.Hyperlink => "hyperlink",
        ThemeColor.FollowedHyperlink => "followedHyperlink",
        ThemeColor.Background1 => "background1",
        ThemeColor.Text1 => "text1",
        ThemeColor.Background2 => "background2",
        ThemeColor.Text2 => "text2",
        _ => "text1",
    };

    internal static ThemeColor? ParseThemeColorToken(string? v)
    {
        if (v is null) return null;
        foreach (ThemeColor c in Enum.GetValues<ThemeColor>())
            if (ThemeColorToken(c) == v) return c;
        return null;
    }

    internal static string ThemeFontToken(ThemeFont f) => f switch
    {
        ThemeFont.MajorEastAsia => "majorEastAsia",
        ThemeFont.MajorBidi => "majorBidi",
        ThemeFont.MajorAscii => "majorAscii",
        ThemeFont.MajorHAnsi => "majorHAnsi",
        ThemeFont.MinorEastAsia => "minorEastAsia",
        ThemeFont.MinorBidi => "minorBidi",
        ThemeFont.MinorAscii => "minorAscii",
        ThemeFont.MinorHAnsi => "minorHAnsi",
        _ => "minorHAnsi",
    };

    private static ThemeFont? ParseThemeFont(string? v)
    {
        if (v is null) return null;
        foreach (ThemeFont f in Enum.GetValues<ThemeFont>())
            if (ThemeFontToken(f) == v) return f;
        return null;
    }

    private static string HintToken(FontHint h) => h switch
    {
        FontHint.EastAsia => "eastAsia",
        FontHint.ComplexScript => "cs",
        _ => "default",
    };

    private static FontHint? ParseHint(string? v) => v switch
    {
        "default" => FontHint.Default,
        "eastAsia" => FontHint.EastAsia,
        "cs" => FontHint.ComplexScript,
        _ => null,
    };

    // ---- Primitive helpers --------------------------------------------------------------

    private static void Empty(XmlWriter x, string name)
    {
        x.WriteStartElement(name, W);
        x.WriteEndElement();
    }

    private static void Val(XmlWriter x, string name, string value)
    {
        x.WriteStartElement(name, W);
        x.WriteAttributeString("val", W, value);
        x.WriteEndElement();
    }

    /// <summary>Writes an ST_OnOff toggle: true → bare element; false → val="0"; null → nothing.</summary>
    private static void Toggle(XmlWriter x, string name, bool? value)
    {
        if (value is null) return;
        x.WriteStartElement(name, W);
        if (value == false) x.WriteAttributeString("val", W, "0");
        x.WriteEndElement();
    }

    private static string? Attr(XmlReader r, string name) => r.GetAttribute(name, W);

    /// <summary>ST_OnOff: absent val, "1", "true", "on" → true; "0", "false", "off" → false.</summary>
    internal static bool? OnOff(XmlReader r) => Attr(r, "val") switch
    {
        null or "1" or "true" or "on" => true,
        "0" or "false" or "off" => false,
        _ => true,
    };

    internal static string HalfPoints(double pt) => Scaled(pt, 2);

    internal static string Twips(double pt) => Scaled(pt, 20);

    /// <summary>Points → integer units; a non-finite or out-of-range value is a caller error, reported clearly at save time.</summary>
    private static string Scaled(double pt, int unitsPerPoint)
    {
        double units = Math.Round(pt * unitsPerPoint, MidpointRounding.AwayFromZero);
        if (!double.IsFinite(units) || Math.Abs(units) > MaxMeasure)
            throw new ArgumentOutOfRangeException(nameof(pt), pt, "Measurement must be a finite number of points within the document's range.");
        return ((int)units).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Largest magnitude accepted for a measurement read from a file (in its own unit); beyond it the value is ignored.</summary>
    private const double MaxMeasure = 1_000_000;

    /// <summary>Half-points → points; null for missing, non-numeric, non-finite or absurd values (hostile input).</summary>
    internal static double? FromHalfPoints(string? v)
        => TryMeasure(v, out var h) ? h / 2.0 : null;

    /// <summary>Twips → points; null for missing, non-numeric, non-finite or absurd values (hostile input).</summary>
    internal static double? FromTwips(string? v)
        => TryMeasure(v, out var t) ? t / 20.0 : null;

    internal static bool TryMeasure(string? v, out double value)
    {
        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value) && Math.Abs(value) <= MaxMeasure)
            return true;
        value = 0;
        return false;
    }

    private static int? ParseInt(string? v)
        => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static byte? ParseHexByte(string? v)
        => byte.TryParse(v, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b) ? b : null;
}
