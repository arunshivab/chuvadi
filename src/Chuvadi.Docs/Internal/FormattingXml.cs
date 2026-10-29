using System;
using System.Globalization;
using System.Xml;
using Chuvadi.Docs.Word;

namespace Chuvadi.Docs.Internal;

/// <summary>
/// XML mapping for the formatting value types shared by runs, paragraphs and (later) table
/// cells: <see cref="Shading"/> (w:shd) and <see cref="BorderSpec"/> (w:top/w:left/… border
/// elements), plus the ST_Shd and ST_Border token tables.
/// </summary>
internal static class FormattingXml
{
    private const string W = DocumentSerializer.W;

    private static readonly string[] ShdTokens =
    {
        "nil", "clear", "solid", "horzStripe", "vertStripe", "reverseDiagStripe", "diagStripe",
        "horzCross", "diagCross", "thinHorzStripe", "thinVertStripe", "thinReverseDiagStripe",
        "thinDiagStripe", "thinHorzCross", "thinDiagCross", "pct5", "pct10", "pct12", "pct15",
        "pct20", "pct25", "pct30", "pct35", "pct37", "pct40", "pct45", "pct50", "pct55", "pct60",
        "pct62", "pct65", "pct70", "pct75", "pct80", "pct85", "pct87", "pct90", "pct95",
    };

    private static readonly string[] BorderTokens =
    {
        "nil", "none", "single", "thick", "double", "dotted", "dashed", "dotDash", "dotDotDash",
        "triple", "thinThickSmallGap", "thickThinSmallGap", "thinThickThinSmallGap",
        "thinThickMediumGap", "thickThinMediumGap", "thinThickThinMediumGap", "thinThickLargeGap",
        "thickThinLargeGap", "thinThickThinLargeGap", "wave", "doubleWave", "dashSmallGap",
        "dashDotStroked", "threeDEmboss", "threeDEngrave", "outset", "inset",
    };

    static FormattingXml()
    {
        // Guard the enum ↔ token tables against drift.
        if (ShdTokens.Length != Enum.GetValues<ShadingPattern>().Length)
            throw new InvalidOperationException("ShadingPattern token table out of sync.");
        if (BorderTokens.Length != Enum.GetValues<BorderStyle>().Length)
            throw new InvalidOperationException("BorderStyle token table out of sync.");
    }

    // ---- Shading --------------------------------------------------------------------------

    public static void WriteShading(XmlWriter x, Shading s)
    {
        if ((uint)s.Pattern >= (uint)ShdTokens.Length)
            throw new ArgumentOutOfRangeException(nameof(Shading.Pattern), s.Pattern, "Undefined ShadingPattern value.");
        x.WriteStartElement("shd", W);
        x.WriteAttributeString("val", W, ShdTokens[(int)s.Pattern]);
        x.WriteAttributeString("color", W, s.ColorHex?.TrimStart('#') ?? "auto");
        x.WriteAttributeString("fill", W, s.FillHex?.TrimStart('#') ?? "auto");
        if (s.ThemeFill is ThemeColor tf) x.WriteAttributeString("themeFill", W, RunProperties.ThemeColorToken(tf));
        x.WriteEndElement();
    }

    /// <summary>Reads a w:shd element (reader positioned on it).</summary>
    public static Shading ReadShading(XmlReader r)
    {
        var val = r.GetAttribute("val", W);
        int idx = val is null ? 1 : Array.IndexOf(ShdTokens, val);
        return new Shading
        {
            Pattern = idx < 0 ? ShadingPattern.Clear : (ShadingPattern)idx,
            ColorHex = HexOrNull(r.GetAttribute("color", W)),
            FillHex = HexOrNull(r.GetAttribute("fill", W)),
            ThemeFill = RunProperties.ParseThemeColorToken(r.GetAttribute("themeFill", W)),
        };
    }

    /// <summary>True when a shading is a plain fill expressible as a bare hex (clear, auto color, no theme).</summary>
    public static bool IsPlainFill(Shading s)
        => s.Pattern == ShadingPattern.Clear && s.ColorHex is null && s.ThemeFill is null && s.FillHex is not null;

    // ---- Borders --------------------------------------------------------------------------

    /// <summary>Writes one border element named <paramref name="side"/> (top, left, bottom, right, between, bar, insideH, insideV…).</summary>
    public static void WriteBorder(XmlWriter x, string side, BorderSpec b)
    {
        if (b.RawStyleToken is { } raw && (raw.Length == 0 || !IsToken(raw)))
            throw new ArgumentException($"BorderSpec.RawStyleToken '{raw}' is not an ST_Border token (letters and digits only).", nameof(b));
        if (b.RawStyleToken is null && (uint)b.Style >= (uint)BorderTokens.Length)
            throw new ArgumentOutOfRangeException(nameof(BorderSpec.Style), b.Style, "Undefined BorderStyle value.");
        if (!double.IsFinite(b.WidthPt) || !double.IsFinite(b.SpacePt))
            throw new ArgumentOutOfRangeException(nameof(b), "Border width and spacing must be finite.");
        // Schema ranges: w:sz is 0–96 eighths of a point (Word's 2–96 for visible lines), w:space 0–31 pt.
        int sz = (int)Math.Round(Math.Clamp(b.WidthPt, 0, 12) * 8, MidpointRounding.AwayFromZero);
        int space = (int)Math.Round(Math.Clamp(b.SpacePt, 0, 31), MidpointRounding.AwayFromZero);
        x.WriteStartElement(side, W);
        x.WriteAttributeString("val", W, b.RawStyleToken ?? BorderTokens[(int)b.Style]);
        x.WriteAttributeString("sz", W, sz.ToString(CultureInfo.InvariantCulture));
        x.WriteAttributeString("space", W, space.ToString(CultureInfo.InvariantCulture));
        x.WriteAttributeString("color", W, b.ColorHex?.TrimStart('#') ?? "auto");
        if (b.ThemeColor is ThemeColor tc) x.WriteAttributeString("themeColor", W, RunProperties.ThemeColorToken(tc));
        if (b.Shadow) x.WriteAttributeString("shadow", W, "1");
        if (b.Frame) x.WriteAttributeString("frame", W, "1");
        x.WriteEndElement();
    }

    /// <summary>Reads a border element (reader positioned on it).</summary>
    public static BorderSpec ReadBorder(XmlReader r)
    {
        var val = r.GetAttribute("val", W) ?? "single";
        int idx = Array.IndexOf(BorderTokens, val);
        return new BorderSpec
        {
            Style = idx < 0 ? BorderStyle.Single : (BorderStyle)idx,
            RawStyleToken = idx < 0 ? val : null,
            // Clamped to the schema ranges so odd input cannot produce invalid output.
            WidthPt = Math.Clamp(ParseDouble(r.GetAttribute("sz", W)), 0, 96) / 8.0,
            SpacePt = Math.Clamp(ParseDouble(r.GetAttribute("space", W)), 0, 31),
            ColorHex = HexOrNull(r.GetAttribute("color", W)),
            ThemeColor = RunProperties.ParseThemeColorToken(r.GetAttribute("themeColor", W)),
            Shadow = IsOn(r.GetAttribute("shadow", W)),
            Frame = IsOn(r.GetAttribute("frame", W)),
        };
    }

    // ---- Helpers --------------------------------------------------------------------------

    internal static string? HexOrNull(string? v)
        => v is null || v.Equals("auto", StringComparison.OrdinalIgnoreCase) ? null : v;

    private static bool IsOn(string? v) => v is "1" or "true" or "on";

    private static double ParseDouble(string? v)
        => RunProperties.TryMeasure(v, out var d) ? d : 0;

    private static bool IsToken(string s)
    {
        foreach (var c in s)
            if (!char.IsAsciiLetterOrDigit(c)) return false;
        return true;
    }
}
