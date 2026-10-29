using System;
using System.Collections.Generic;
using System.Globalization;

namespace Chuvadi.Sheets.Internal;

/// <summary>
/// Pure-BCL text width estimation for column auto-fit. Latin text uses embedded advance-width
/// tables for Calibri, Cambria, Arial, Times New Roman and Courier New (taken from their
/// metric-compatible free equivalents); other fonts map to the closest table with a scale
/// factor. Characters outside the tables are estimated by Unicode category: combining marks
/// take no width, East Asian wide characters one em, other scripts (Tamil, Malayalam,
/// Devanagari, ...) a per-script average. Kerning and ligatures are ignored, so results are
/// estimates, typically within a few percent for Latin text.
/// </summary>
internal static partial class FontMetrics
{
    private static readonly Dictionary<int, int> IndexOf;

    // Built in the static constructor, which runs after the field initializers of BOTH partial
    // files (initializer order across partial files is undefined, so CodePoints may not exist yet).
    static FontMetrics() { IndexOf = BuildIndex(); }

    private static Dictionary<int, int> BuildIndex()
    {
        var d = new Dictionary<int, int>(CodePoints.Length);
        for (int i = 0; i < CodePoints.Length; i++) d[CodePoints[i]] = i;
        return d;
    }

    /// <summary>Font name → (table index, width scale). Case-insensitive.</summary>
    private static readonly Dictionary<string, (int Table, double Scale)> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Calibri"] = (0, 1.0), ["Calibri Light"] = (0, 0.99), ["Carlito"] = (0, 1.0),
        ["Aptos"] = (0, 1.08), ["Aptos Narrow"] = (0, 0.98), ["Aptos Display"] = (0, 1.06),
        ["Candara"] = (0, 1.02), ["Corbel"] = (0, 0.99), ["Segoe UI"] = (2, 0.98),
        ["Cambria"] = (1, 1.0), ["Caladea"] = (1, 1.0), ["Constantia"] = (1, 1.03),
        ["Arial"] = (2, 1.0), ["Liberation Sans"] = (2, 1.0), ["Helvetica"] = (2, 1.0),
        ["Arial Narrow"] = (2, 0.82), ["Tahoma"] = (2, 1.0), ["Verdana"] = (2, 1.15),
        ["Microsoft Sans Serif"] = (2, 1.0), ["Trebuchet MS"] = (2, 0.98), ["Century Gothic"] = (2, 1.1),
        ["Nirmala UI"] = (2, 0.98), ["Mangal"] = (2, 1.0), ["Latha"] = (2, 1.05), ["Kartika"] = (2, 1.05),
        ["Times New Roman"] = (3, 1.0), ["Liberation Serif"] = (3, 1.0), ["Times"] = (3, 1.0),
        ["Georgia"] = (3, 1.12), ["Garamond"] = (3, 0.95), ["Book Antiqua"] = (3, 1.06), ["Palatino Linotype"] = (3, 1.06),
        ["Courier New"] = (4, 1.0), ["Courier"] = (4, 1.0), ["Liberation Mono"] = (4, 1.0),
        ["Consolas"] = (4, 0.92), ["Lucida Console"] = (4, 1.0),
    };

    /// <summary>Resolves a font name; unknown fonts measure as Calibri (Excel's default).</summary>
    private static (int Table, double Scale) Resolve(string? fontName)
        => fontName is not null && Aliases.TryGetValue(fontName.Trim(), out var r) ? r : (0, 1.0);

    /// <summary>
    /// Width of <paramref name="text"/> in em (1.0 = the font size). Line breaks contribute
    /// nothing (an unwrapped cell shows its text on one line).
    /// </summary>
    public static double MeasureEm(string text, string? fontName, bool bold)
    {
        var (ti, scale) = Resolve(fontName);
        var table = bold ? Tables[ti].Bold : Tables[ti].Regular;
        int fallback = table[IndexOf['0']];
        double units = 0;

        for (int i = 0; i < text.Length; i++)
        {
            int cp = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                cp = char.ConvertToUtf32(text[i], text[++i]);

            if (cp < 0x10000 && IndexOf.TryGetValue(cp, out var idx))
            {
                int w = table[idx];
                units += w >= 0 ? w : fallback;
                continue;
            }
            units += EstimateOther(cp, fallback);
        }
        return units * scale / 1000.0;
    }

    /// <summary>Estimated advance (1/1000 em) for a code point outside the tables.</summary>
    private static double EstimateOther(int cp, int digitWidth)
    {
        if (cp is '\r' or '\n' or '\t') return cp == '\t' ? digitWidth * 4 : 0;
        var cat = CharUnicodeInfo.GetUnicodeCategory(cp);
        switch (cat)
        {
            case UnicodeCategory.NonSpacingMark:
            case UnicodeCategory.EnclosingMark:
            case UnicodeCategory.Format:
            case UnicodeCategory.Control:
                return 0;
            case UnicodeCategory.SpacingCombiningMark:
                return 320;   // e.g. Indic dependent vowel signs that take space
        }
        if (IsWide(cp)) return 1000;
        // Per-script averages for letters (Nirmala UI-class fonts at the same size).
        if (cp >= 0x0B80 && cp <= 0x0BFF) return 760;   // Tamil
        if (cp >= 0x0D00 && cp <= 0x0D7F) return 760;   // Malayalam
        if (cp >= 0x0C00 && cp <= 0x0CFF) return 700;   // Telugu, Kannada
        if (cp >= 0x0900 && cp <= 0x0AFF) return 620;   // Devanagari, Bengali, Gurmukhi, Gujarati
        if (cp >= 0x0B00 && cp <= 0x0B7F) return 650;   // Oriya
        if (cp >= 0x0600 && cp <= 0x06FF) return 520;   // Arabic
        if (cp >= 0x0590 && cp <= 0x05FF) return 560;   // Hebrew
        if (cp >= 0x0E00 && cp <= 0x0E7F) return 560;   // Thai
        if (cp >= 0x0400 && cp <= 0x04FF) return 560;   // Cyrillic
        if (cp >= 0x0370 && cp <= 0x03FF) return 560;   // Greek
        if (cp >= 0x1F300 && cp <= 0x1FAFF) return 1000; // emoji
        return digitWidth;
    }

    /// <summary>East Asian wide / full-width ranges.</summary>
    private static bool IsWide(int cp)
        => (cp >= 0x1100 && cp <= 0x115F) || (cp >= 0x2E80 && cp <= 0xA4CF) || (cp >= 0xAC00 && cp <= 0xD7A3)
        || (cp >= 0xF900 && cp <= 0xFAFF) || (cp >= 0xFE30 && cp <= 0xFE4F) || (cp >= 0xFF00 && cp <= 0xFF60)
        || (cp >= 0xFFE0 && cp <= 0xFFE6) || (cp >= 0x20000 && cp <= 0x3FFFD);

    /// <summary>
    /// Excel's "maximum digit width" in pixels at 96 DPI for a font: the unit that column
    /// widths are counted in (7 for Calibri 11, the workbook default).
    /// </summary>
    public static int MaxDigitWidthPx(string? fontName, double sizePt)
    {
        var (ti, scale) = Resolve(fontName);
        var t = Tables[ti].Regular;
        int max = 0;
        for (char c = '0'; c <= '9'; c++) max = Math.Max(max, t[IndexOf[c]]);
        return Math.Max(1, (int)Math.Round(max * scale / 1000.0 * sizePt * 96.0 / 72.0, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// The column width (Excel character units) that shows text of the given pixel width:
    /// Excel's formula Truncate((px + 5) / digitWidth * 256) / 256, rounded up so the text is
    /// never clipped, where 5 px is Excel's cell padding.
    /// </summary>
    public static double ColumnWidthForPixels(double px, int maxDigitWidthPx)
        => Math.Ceiling((px + 5) / maxDigitWidthPx * 256) / 256;

    /// <summary>Pixel width at 96 DPI for text at a point size.</summary>
    public static double Pixels(double em, double sizePt) => em * sizePt * 96.0 / 72.0;
}
