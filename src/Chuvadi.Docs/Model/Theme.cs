using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Xml;

namespace Chuvadi.Docs.Model;

/// <summary>
/// The fonts and colours of a document's theme (word/theme/theme1.xml, <see cref="Document.ThemeXml"/>), read-only
/// (D-296). Get it from <see cref="Document.Theme"/>; resolve a run's theme references with
/// <see cref="Document.GetThemeFontName(ThemeFont)"/> and <see cref="Document.GetThemeColorHex(ThemeColor, byte?, byte?)"/>.
/// <para>Without a theme part, or when the part is not well-formed XML, the Office 2013–2022 default theme
/// applies (Calibri Light / Calibri, no East Asian, complex-script or script fonts, the Office colours), the
/// same defaults Chuvadi.Layout uses; <see cref="IsBuiltInDefault"/> is then true.</para>
/// </summary>
public sealed class DocumentTheme
{
    internal DocumentTheme(string? name, ThemeFontScheme major, ThemeFontScheme minor, IReadOnlyDictionary<ThemeColor, string> colors, bool isDefault)
    {
        Name = name;
        MajorFonts = major;
        MinorFonts = minor;
        Colors = colors;
        IsBuiltInDefault = isDefault;
    }

    /// <summary>The theme's name (<c>a:theme/@name</c>, for example "Office Theme"); null when not stated.</summary>
    public string? Name { get; }

    /// <summary>The heading fonts (<c>a:majorFont</c>), used by the Major* <see cref="ThemeFont"/> values.</summary>
    public ThemeFontScheme MajorFonts { get; }

    /// <summary>The body fonts (<c>a:minorFont</c>), used by the Minor* <see cref="ThemeFont"/> values.</summary>
    public ThemeFontScheme MinorFonts { get; }

    /// <summary>
    /// The twelve colour-scheme colours as six-digit hex (RRGGBB), keyed by <see cref="ThemeColor.Dark1"/>,
    /// <see cref="ThemeColor.Light1"/>, <see cref="ThemeColor.Dark2"/>, <see cref="ThemeColor.Light2"/>,
    /// <see cref="ThemeColor.Accent1"/> to <see cref="ThemeColor.Accent6"/>, <see cref="ThemeColor.Hyperlink"/>
    /// and <see cref="ThemeColor.FollowedHyperlink"/>. A system colour (<c>a:sysClr</c>) gives its
    /// <c>lastClr</c>.
    /// </summary>
    public IReadOnlyDictionary<ThemeColor, string> Colors { get; }

    /// <summary>True when the document has no usable theme part and the built-in defaults apply.</summary>
    public bool IsBuiltInDefault { get; }

    // ---- Parsing ------------------------------------------------------------------------

    private static readonly string[] SchemeSlots =
        { "dk1", "lt1", "dk2", "lt2", "accent1", "accent2", "accent3", "accent4", "accent5", "accent6", "hlink", "folHlink" };

    private static readonly string[] DefaultColors =
        { "000000", "FFFFFF", "44546A", "E7E6E6", "4472C4", "ED7D31", "A5A5A5", "FFC000", "5B9BD5", "70AD47", "0563C1", "954F72" };

    internal static DocumentTheme Default() => Build(null, "Calibri Light", null, null, null, "Calibri", null, null, null, (string[])DefaultColors.Clone(), true);

    internal static DocumentTheme Parse(byte[]? themeXml)
    {
        if (themeXml is null || themeXml.Length == 0) return Default();
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            MaxCharactersInDocument = 64L * 1024 * 1024,
        };
        string? name = null;
        string majorLatin = "Calibri Light", minorLatin = "Calibri";
        string? majorEa = null, minorEa = null, majorCs = null, minorCs = null;
        Dictionary<string, string>? majorScripts = null, minorScripts = null;
        var colors = (string[])DefaultColors.Clone();
        try
        {
            using var r = XmlReader.Create(new MemoryStream(themeXml, writable: false), settings);
            string? scheme = null; // "clr", "major", "minor"
            int slot = -1;
            while (r.Read())
            {
                if (r.NodeType == XmlNodeType.EndElement)
                {
                    // Only the scheme elements themselves count: a:latin and similar elsewhere in the
                    // theme (object defaults, for example) are not theme fonts.
                    if (r.LocalName is "clrScheme" or "majorFont" or "minorFont") { scheme = null; slot = -1; }
                    continue;
                }
                if (r.NodeType != XmlNodeType.Element) continue;
                switch (r.LocalName)
                {
                    case "theme" when r.Depth == 0:
                        name = r.GetAttribute("name");
                        break;
                    case "clrScheme": scheme = r.IsEmptyElement ? null : "clr"; break;
                    case "majorFont": scheme = r.IsEmptyElement ? null : "major"; break;
                    case "minorFont": scheme = r.IsEmptyElement ? null : "minor"; break;
                    case "srgbClr" when scheme == "clr" && slot >= 0:
                        if (Hex(r.GetAttribute("val")) is string c) colors[slot] = c;
                        slot = -1;
                        break;
                    case "sysClr" when scheme == "clr" && slot >= 0:
                        if (Hex(r.GetAttribute("lastClr")) is string sc) colors[slot] = sc;
                        slot = -1;
                        break;
                    case "latin" when scheme is "major" or "minor":
                        if (r.GetAttribute("typeface") is { Length: > 0 } latin)
                        {
                            if (scheme == "major") majorLatin = latin; else minorLatin = latin;
                        }
                        break;
                    case "ea" when scheme is "major" or "minor":
                        if (r.GetAttribute("typeface") is { Length: > 0 } ea)
                        {
                            if (scheme == "major") majorEa = ea; else minorEa = ea;
                        }
                        break;
                    case "cs" when scheme is "major" or "minor":
                        if (r.GetAttribute("typeface") is { Length: > 0 } cs)
                        {
                            if (scheme == "major") majorCs = cs; else minorCs = cs;
                        }
                        break;
                    case "font" when scheme is "major" or "minor":
                        if (r.GetAttribute("script") is { Length: > 0 } script && r.GetAttribute("typeface") is { Length: > 0 } face)
                        {
                            var map = scheme == "major" ? (majorScripts ??= new(StringComparer.Ordinal)) : (minorScripts ??= new(StringComparer.Ordinal));
                            map.TryAdd(script, face);
                        }
                        break;
                    default:
                        if (scheme == "clr") slot = Array.IndexOf(SchemeSlots, r.LocalName) is int i && i >= 0 ? i : slot;
                        break;
                }
            }
        }
        catch (XmlException)
        {
            return Default();
        }
        return Build(name, majorLatin, majorEa, majorCs, majorScripts, minorLatin, minorEa, minorCs, minorScripts, colors, false);
    }

    private static DocumentTheme Build(string? name, string majorLatin, string? majorEa, string? majorCs, Dictionary<string, string>? majorScripts,
        string minorLatin, string? minorEa, string? minorCs, Dictionary<string, string>? minorScripts, string[] colors, bool isDefault)
    {
        var map = new Dictionary<ThemeColor, string>(12);
        for (int i = 0; i < 12; i++) map[(ThemeColor)i] = colors[i];
        return new DocumentTheme(name,
            new ThemeFontScheme(majorLatin, majorEa, majorCs, majorScripts),
            new ThemeFontScheme(minorLatin, minorEa, minorCs, minorScripts),
            new ReadOnlyDictionary<ThemeColor, string>(map), isDefault);
    }

    private static string? Hex(string? v)
    {
        if (v is null || v.Length != 6 || !uint.TryParse(v, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)) return null;
        return v.ToUpperInvariant();
    }

    // ---- Resolution ---------------------------------------------------------------------

    internal string? FontName(ThemeFont font, ThemeFontLanguages languages)
    {
        bool major = font is ThemeFont.MajorAscii or ThemeFont.MajorHAnsi or ThemeFont.MajorEastAsia or ThemeFont.MajorBidi;
        var scheme = major ? MajorFonts : MinorFonts;
        string? language;
        string? fallback;
        switch (font)
        {
            case ThemeFont.MajorEastAsia:
            case ThemeFont.MinorEastAsia:
                language = languages.EastAsia;
                fallback = scheme.EastAsia;
                break;
            case ThemeFont.MajorBidi:
            case ThemeFont.MinorBidi:
                language = languages.ComplexScript;
                fallback = scheme.ComplexScript;
                break;
            default:
                language = languages.Latin;
                fallback = scheme.Latin;
                break;
        }
        if (ScriptOf(language) is string script && scheme.ScriptFonts.TryGetValue(script, out var face)) return face;
        return fallback;
    }

    internal string ColorHex(ThemeColor color, byte? tint, byte? shade, ColorSchemeMapping mapping)
    {
        int index = color switch
        {
            ThemeColor.Dark1 => 0,
            ThemeColor.Light1 => 1,
            ThemeColor.Dark2 => 2,
            ThemeColor.Light2 => 3,
            ThemeColor.Background1 => (int)(mapping.Background1 ?? ColorSchemeIndex.Light1),
            ThemeColor.Text1 => (int)(mapping.Text1 ?? ColorSchemeIndex.Dark1),
            ThemeColor.Background2 => (int)(mapping.Background2 ?? ColorSchemeIndex.Light2),
            ThemeColor.Text2 => (int)(mapping.Text2 ?? ColorSchemeIndex.Dark2),
            ThemeColor.Accent1 => (int)(mapping.Accent1 ?? ColorSchemeIndex.Accent1),
            ThemeColor.Accent2 => (int)(mapping.Accent2 ?? ColorSchemeIndex.Accent2),
            ThemeColor.Accent3 => (int)(mapping.Accent3 ?? ColorSchemeIndex.Accent3),
            ThemeColor.Accent4 => (int)(mapping.Accent4 ?? ColorSchemeIndex.Accent4),
            ThemeColor.Accent5 => (int)(mapping.Accent5 ?? ColorSchemeIndex.Accent5),
            ThemeColor.Accent6 => (int)(mapping.Accent6 ?? ColorSchemeIndex.Accent6),
            ThemeColor.Hyperlink => (int)(mapping.Hyperlink ?? ColorSchemeIndex.Hyperlink),
            ThemeColor.FollowedHyperlink => (int)(mapping.FollowedHyperlink ?? ColorSchemeIndex.FollowedHyperlink),
            _ => throw new ArgumentOutOfRangeException(nameof(color), color, "Not a defined ThemeColor value."),
        };
        // ColorSchemeIndex and the first twelve ThemeColor values share their order (dk1 … folHlink).
        string hex = Colors[(ThemeColor)index];
        if ((tint is null || tint == 255) && (shade is null || shade == 255)) return hex;
        uint v = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var (h, s, l) = ToHsl((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        if (tint is byte t && t != 255) l = (l * t / 255.0) + (1 - (t / 255.0));
        if (shade is byte sh && sh != 255) l *= sh / 255.0;
        var (r, g, b) = FromHsl(h, s, Math.Clamp(l, 0, 1));
        return string.Create(CultureInfo.InvariantCulture, $"{r:X2}{g:X2}{b:X2}");
    }

    // Word's tint and shade act on the HSL lightness (the same maths as Chuvadi.Layout).
    private static (double H, double S, double L) ToHsl(byte rb, byte gb, byte bb)
    {
        double r = rb / 255.0, g = gb / 255.0, b = bb / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2;
        if (max == min) return (0, 0, l);
        double d = max - min;
        double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == r ? ((g - b) / d) + (g < b ? 6 : 0) : max == g ? ((b - r) / d) + 2 : ((r - g) / d) + 4;
        return (h / 6, s, l);
    }

    private static (byte R, byte G, byte B) FromHsl(double h, double s, double l)
    {
        if (s == 0)
        {
            byte v = (byte)Math.Round(l * 255);
            return (v, v, v);
        }
        double q = l < 0.5 ? l * (1 + s) : l + s - (l * s);
        double p = (2 * l) - q;
        return (Channel(p, q, h + (1.0 / 3)), Channel(p, q, h), Channel(p, q, h - (1.0 / 3)));
    }

    private static byte Channel(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        double v = t < 1.0 / 6 ? p + ((q - p) * 6 * t) : t < 0.5 ? q : t < 2.0 / 3 ? p + ((q - p) * ((2.0 / 3) - t) * 6) : p;
        return (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
    }

    // ---- Language → script (the script codes DrawingML themes use) ------------------------

    private static readonly Dictionary<string, string> LanguageScripts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ja"] = "Jpan", ["ko"] = "Hang",
        ["ar"] = "Arab", ["fa"] = "Arab", ["ur"] = "Arab", ["ps"] = "Arab", ["sd"] = "Arab", ["prs"] = "Arab", ["ks"] = "Arab",
        ["he"] = "Hebr", ["yi"] = "Hebr",
        ["th"] = "Thai", ["am"] = "Ethi", ["ti"] = "Ethi",
        ["bn"] = "Beng", ["as"] = "Beng", ["gu"] = "Gujr", ["km"] = "Khmr", ["kn"] = "Knda", ["pa"] = "Guru",
        ["iu"] = "Cans", ["chr"] = "Cher", ["ii"] = "Yiii", ["bo"] = "Tibt", ["dz"] = "Tibt", ["dv"] = "Thaa",
        ["hi"] = "Deva", ["mr"] = "Deva", ["ne"] = "Deva", ["sa"] = "Deva", ["kok"] = "Deva",
        ["te"] = "Telu", ["ta"] = "Taml", ["syr"] = "Syrc", ["or"] = "Orya", ["ml"] = "Mlym", ["lo"] = "Laoo", ["si"] = "Sinh",
        ["vi"] = "Viet", ["ug"] = "Uigh", ["ka"] = "Geor", ["hy"] = "Armn", ["my"] = "Mymr",
    };

    /// <summary>
    /// The theme script code for a language tag: an explicit four-letter script subtag ("pa-Arab", "mn-Mong")
    /// wins; Chinese goes to Hant for TW, HK and MO and to Hans otherwise; then the table above. Null when the
    /// language has no script entry (Latin, Cyrillic, Greek and other scripts DrawingML themes do not list).
    /// </summary>
    internal static string? ScriptOf(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return null;
        var parts = language.Trim().Split('-', '_');
        string primary = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            if (parts[i].Length == 4 && IsLetters(parts[i]))
            {
                string s = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1).ToLowerInvariant();
                if (s is "Latn" or "Cyrl") return null;
                return s == "Arab" && primary.Equals("ug", StringComparison.OrdinalIgnoreCase) ? "Uigh" : s;
            }
        }
        if (primary.Equals("zh", StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 1; i < parts.Length; i++)
                if (parts[i].Equals("TW", StringComparison.OrdinalIgnoreCase) || parts[i].Equals("HK", StringComparison.OrdinalIgnoreCase) || parts[i].Equals("MO", StringComparison.OrdinalIgnoreCase))
                    return "Hant";
            return "Hans";
        }
        return LanguageScripts.TryGetValue(primary, out var script) ? script : null;
    }

    private static bool IsLetters(string s)
    {
        foreach (char c in s) if (!char.IsAsciiLetter(c)) return false;
        return true;
    }
}

/// <summary>One font scheme of a theme (<c>a:majorFont</c> or <c>a:minorFont</c>).</summary>
public sealed class ThemeFontScheme
{
    internal ThemeFontScheme(string latin, string? eastAsia, string? complexScript, Dictionary<string, string>? scriptFonts)
    {
        Latin = latin;
        EastAsia = eastAsia;
        ComplexScript = complexScript;
        ScriptFonts = new ReadOnlyDictionary<string, string>(scriptFonts ?? new Dictionary<string, string>(StringComparer.Ordinal));
    }

    /// <summary>The Latin typeface (<c>a:latin</c>).</summary>
    public string Latin { get; }

    /// <summary>The East Asian typeface (<c>a:ea</c>); null when the theme leaves it empty.</summary>
    public string? EastAsia { get; }

    /// <summary>The complex-script typeface (<c>a:cs</c>); null when the theme leaves it empty.</summary>
    public string? ComplexScript { get; }

    /// <summary>The script-specific typefaces (<c>a:font</c>), keyed by the theme's script code (for example "Jpan", "Hans", "Arab", "Taml").</summary>
    public IReadOnlyDictionary<string, string> ScriptFonts { get; }
}

public sealed partial class Document
{
    private DocumentTheme? _theme;

    /// <summary>
    /// The theme's fonts and colours, parsed from <see cref="ThemeXml"/> when first read and again after
    /// <see cref="ThemeXml"/> is set (D-296). Without a theme part, the Office 2013–2022 defaults.
    /// </summary>
    public DocumentTheme Theme => _theme ??= DocumentTheme.Parse(_themeXml);

    /// <summary>
    /// The font name a theme font reference stands for, resolved as ECMA-376 §17.15.1.88 describes (D-296):
    /// <list type="number">
    /// <item>the language for the slot comes from <see cref="ThemeFontLanguages"/>: <c>Latin</c> for the Ascii and
    /// HAnsi slots, <c>EastAsia</c> for the EastAsia slots, <c>ComplexScript</c> for the Bidi slots;</item>
    /// <item>if that language's script has a script font in the theme (<see cref="ThemeFontScheme.ScriptFonts"/>), that font;</item>
    /// <item>otherwise the scheme's <see cref="ThemeFontScheme.Latin"/>, <see cref="ThemeFontScheme.EastAsia"/> or
    /// <see cref="ThemeFontScheme.ComplexScript"/>.</item>
    /// </list>
    /// Null when the theme leaves the East Asian or complex-script typeface empty and no script font applies.
    /// <para>The language → script table is Chuvadi's own (Word's is not published) and covers the scripts that
    /// Office themes list. When the file states no theme-font languages, Word guesses from the Office install
    /// language and the keyboards ([MS-OI29500] §2.1.423); this follows the standard and uses the scheme's fonts.</para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="ThemeFont"/>.</exception>
    public string? GetThemeFontName(ThemeFont font)
    {
        if (!Enum.IsDefined(font)) throw new ArgumentOutOfRangeException(nameof(font), font, "Not a defined ThemeFont value.");
        return Theme.FontName(font, ThemeFontLanguages);
    }

    /// <summary>
    /// The colour a theme colour reference stands for, as six-digit hex (RRGGBB), with Word's tint and shade
    /// (0–255; 255 or null = unchanged) applied to its HSL lightness, the same maths as Chuvadi.Layout (D-296).
    /// <see cref="ThemeColor.Background1"/>, <see cref="ThemeColor.Text1"/>, <see cref="ThemeColor.Background2"/>,
    /// <see cref="ThemeColor.Text2"/> and the accent and hyperlink colours go through <see cref="ColorSchemeMapping"/>
    /// (Word's default mapping when not stated); <see cref="ThemeColor.Dark1"/> to <see cref="ThemeColor.Light2"/>
    /// name scheme slots directly.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="ThemeColor"/>.</exception>
    public string GetThemeColorHex(ThemeColor color, byte? tint = null, byte? shade = null) =>
        Theme.ColorHex(color, tint, shade, ColorSchemeMapping);
}
