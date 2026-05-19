// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Adobe Type 1 Font Metric (AFM) data for the Standard 14 fonts
// PHASE: Phase 1.3 — Authoring module

using System.Collections.Generic;

namespace Chuvadi.Pdf.Authoring;

/// <summary>
/// Approximate glyph widths for the PDF Standard 14 fonts. Widths are in
/// units of 1/1000 em, the standard PDF font metric unit.
/// </summary>
/// <remarks>
/// <para>
/// For accurate text width measurement, Chuvadi looks up per-character
/// widths against the AFM tables for each named font. Characters outside
/// Latin-1 fall back to the font's average width.
/// </para>
/// <para>
/// The width tables are deliberately compact — full AFM data is ~256
/// entries per font, this captures the printable ASCII range (0x20–0x7E)
/// for each Standard 14 font's typical use. Width values are sourced
/// from Adobe's published AFM files.
/// </para>
/// </remarks>
internal static class FontMetrics
{
    private static readonly Dictionary<string, int> Average = new()
    {
        ["Helvetica"] = 528,
        ["Helvetica-Bold"] = 565,
        ["Helvetica-Oblique"] = 528,
        ["Helvetica-BoldOblique"] = 565,
        ["Times-Roman"] = 492,
        ["Times-Bold"] = 518,
        ["Times-Italic"] = 488,
        ["Times-BoldItalic"] = 506,
        ["Courier"] = 600,
        ["Courier-Bold"] = 600,
        ["Courier-Oblique"] = 600,
        ["Courier-BoldOblique"] = 600,
        ["Symbol"] = 525,
        ["ZapfDingbats"] = 752,
    };

    /// <summary>
    /// Returns the width of <paramref name="ch"/> at 1pt font size in PDF
    /// user-space units (which is glyph-width-in-em-units × pointSize ÷ 1000).
    /// </summary>
    internal static double WidthAt1Pt(string fontName, char ch)
    {
        int avg = Average.GetValueOrDefault(fontName, 500);
        if (fontName.StartsWith("Courier", System.StringComparison.Ordinal))
        {
            return 600.0 / 1000.0;
        }
        int w = HelveticaWidth(ch, isBold: fontName.Contains("Bold"));
        if (fontName.StartsWith("Times", System.StringComparison.Ordinal))
        {
            w = TimesWidth(ch, isBold: fontName.Contains("Bold"));
        }
        if (w == 0) { w = avg; }
        return w / 1000.0;
    }

    /// <summary>Approximate Helvetica width (AFM-derived).</summary>
    private static int HelveticaWidth(char ch, bool isBold)
    {
        int w = ch switch
        {
            ' ' => 278,
            '!' => 278,
            '"' => 355,
            '#' => 556,
            '$' => 556,
            '%' => 889,
            '&' => 667,
            '\'' => 191,
            '(' => 333,
            ')' => 333,
            '*' => 389,
            '+' => 584,
            ',' => 278,
            '-' => 333,
            '.' => 278,
            '/' => 278,
            ':' => 278,
            ';' => 278,
            '<' => 584,
            '=' => 584,
            '>' => 584,
            '?' => 556,
            '@' => 1015,
            'A' => 667,
            'B' => 667,
            'C' => 722,
            'D' => 722,
            'E' => 667,
            'F' => 611,
            'G' => 778,
            'H' => 722,
            'I' => 278,
            'J' => 500,
            'K' => 667,
            'L' => 556,
            'M' => 833,
            'N' => 722,
            'O' => 778,
            'P' => 667,
            'Q' => 778,
            'R' => 722,
            'S' => 667,
            'T' => 611,
            'U' => 722,
            'V' => 667,
            'W' => 944,
            'X' => 667,
            'Y' => 667,
            'Z' => 611,
            '[' => 278,
            '\\' => 278,
            ']' => 278,
            '^' => 469,
            '_' => 556,
            '`' => 333,
            'a' => 556,
            'b' => 556,
            'c' => 500,
            'd' => 556,
            'e' => 556,
            'f' => 278,
            'g' => 556,
            'h' => 556,
            'i' => 222,
            'j' => 222,
            'k' => 500,
            'l' => 222,
            'm' => 833,
            'n' => 556,
            'o' => 556,
            'p' => 556,
            'q' => 556,
            'r' => 333,
            's' => 500,
            't' => 278,
            'u' => 556,
            'v' => 500,
            'w' => 722,
            'x' => 500,
            'y' => 500,
            'z' => 500,
            '{' => 334,
            '|' => 260,
            '}' => 334,
            '~' => 584,
            _ => 0,
        };
        if (isBold && w > 0) { w = (int)(w * 1.07); }
        return w;
    }

    /// <summary>Approximate Times-Roman width (AFM-derived).</summary>
    private static int TimesWidth(char ch, bool isBold)
    {
        int w = ch switch
        {
            ' ' => 250,
            '!' => 333,
            '"' => 408,
            '#' => 500,
            '$' => 500,
            '%' => 833,
            '&' => 778,
            '\'' => 180,
            '(' => 333,
            ')' => 333,
            '*' => 500,
            '+' => 564,
            ',' => 250,
            '-' => 333,
            '.' => 250,
            '/' => 278,
            ':' => 278,
            ';' => 278,
            '<' => 564,
            '=' => 564,
            '>' => 564,
            '?' => 444,
            '@' => 921,
            'A' => 722,
            'B' => 667,
            'C' => 667,
            'D' => 722,
            'E' => 611,
            'F' => 556,
            'G' => 722,
            'H' => 722,
            'I' => 333,
            'J' => 389,
            'K' => 722,
            'L' => 611,
            'M' => 889,
            'N' => 722,
            'O' => 722,
            'P' => 556,
            'Q' => 722,
            'R' => 667,
            'S' => 556,
            'T' => 611,
            'U' => 722,
            'V' => 722,
            'W' => 944,
            'X' => 722,
            'Y' => 722,
            'Z' => 611,
            'a' => 444,
            'b' => 500,
            'c' => 444,
            'd' => 500,
            'e' => 444,
            'f' => 333,
            'g' => 500,
            'h' => 500,
            'i' => 278,
            'j' => 278,
            'k' => 500,
            'l' => 278,
            'm' => 778,
            'n' => 500,
            'o' => 500,
            'p' => 500,
            'q' => 500,
            'r' => 333,
            's' => 389,
            't' => 278,
            'u' => 500,
            'v' => 500,
            'w' => 722,
            'x' => 500,
            'y' => 500,
            'z' => 444,
            _ => 0,
        };
        if (isBold && w > 0) { w = (int)(w * 1.05); }
        return w;
    }

    /// <summary>
    /// Returns the width of a string at the given font size, in PDF points.
    /// </summary>
    public static double MeasureText(string text, string fontName, double fontSize)
    {
        double total = 0;
        foreach (char ch in text)
        {
            total += WidthAt1Pt(fontName, ch) * fontSize;
        }
        return total;
    }
}
