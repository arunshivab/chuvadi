// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.6.2.2 — Standard Type 1 Fonts
//        Adobe Core 14 — original AFM advance-width tables
// PHASE: v2.0.0 R1 D3b — Standard 14 width fallback

using System;

namespace Chuvadi.Pdf.Fonts.Rendering;

/// <summary>
/// Provides advance-width metrics for the PDF Standard 14 fonts in
/// 1/1000-em font design units.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="RenderableFont"/> as the width source for Standard 14
/// fonts. Works even when <see cref="Standard14Outlines.BundleAvailable"/>
/// is false — in that case glyphs cannot be drawn, but layout and selection
/// still produce stable positions for the reader-app text layer.
/// </para>
/// <para>
/// Width fidelity in v2.0.0 R1 D3b:
/// </para>
/// <list type="bullet">
///   <item><b>Exact</b> for all four Courier fonts (monospace, every glyph is
///   600/1000 em) and the space character in every family (well-known
///   per-family AFM constants).</item>
///   <item><b>Approximate</b> for variable-width fonts: a single per-font
///   average is returned for any non-space code. Close enough that paragraph
///   layout reads correctly; column alignment in monospaced-mimicking content
///   may drift compared to an Adobe-exact reference renderer.</item>
/// </list>
/// <para>
/// A follow-up will populate the full Adobe AFM tables via a codegen tool
/// reading Liberation Fonts <c>hmtx</c> metrics, replacing the approximate
/// path.
/// </para>
/// <para>
/// Units: 1/1000 of an em square. To convert the returned value to PDF
/// user-space points for a given <c>pointSize</c>, multiply by
/// <c>pointSize / 1000.0</c>.
/// </para>
/// </remarks>
public static class Standard14Widths
{
    /// <summary>The units-per-em value used by all Standard 14 widths (1000).</summary>
    public const int UnitsPerEm = 1000;

    /// <summary>
    /// Returns the advance width of <paramref name="charCode"/> in
    /// <paramref name="fontName"/> in 1/1000 em units.
    /// </summary>
    /// <param name="fontName">A Standard 14 PostScript font name (e.g. "Helvetica").</param>
    /// <param name="charCode">A WinAnsi character code (typically 0–255).</param>
    /// <returns>
    /// The advance width in 1/1000 em. For non-Standard 14 fonts returns
    /// the em-half default of 500.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="fontName"/> is null.
    /// </exception>
    public static int GetWidth(string fontName, int charCode)
    {
        ArgumentNullException.ThrowIfNull(fontName);

        // Space has a well-known per-family width.
        if (charCode == 0x20)
        {
            return GetSpaceWidth(fontName);
        }

        // Courier family is monospace: every glyph (including space) is 600 em.
        if (IsCourierFamily(fontName))
        {
            return 600;
        }

        return GetAverageWidth(fontName);
    }

    /// <summary>
    /// Returns true when <paramref name="fontName"/> matches one of the
    /// 14 Standard PostScript font names.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="fontName"/> is null.
    /// </exception>
    public static bool IsStandard14(string fontName)
    {
        ArgumentNullException.ThrowIfNull(fontName);
        return fontName switch
        {
            "Helvetica" or "Helvetica-Bold" or "Helvetica-Oblique" or "Helvetica-BoldOblique" => true,
            "Times-Roman" or "Times-Bold" or "Times-Italic" or "Times-BoldItalic" => true,
            "Courier" or "Courier-Bold" or "Courier-Oblique" or "Courier-BoldOblique" => true,
            "Symbol" or "ZapfDingbats" => true,
            _ => false,
        };
    }

    private static int GetSpaceWidth(string fontName) => fontName switch
    {
        "Helvetica" or "Helvetica-Oblique" => 278,
        "Helvetica-Bold" or "Helvetica-BoldOblique" => 278,
        "Times-Roman" or "Times-Italic" => 250,
        "Times-Bold" or "Times-BoldItalic" => 250,
        "Courier" or "Courier-Bold" or "Courier-Oblique" or "Courier-BoldOblique" => 600,
        "Symbol" => 250,
        "ZapfDingbats" => 278,
        _ => 250,
    };

    private static int GetAverageWidth(string fontName) => fontName switch
    {
        "Helvetica" or "Helvetica-Oblique" => 556,
        "Helvetica-Bold" or "Helvetica-BoldOblique" => 600,
        "Times-Roman" or "Times-Italic" => 500,
        "Times-Bold" or "Times-BoldItalic" => 556,
        "Symbol" => 500,
        "ZapfDingbats" => 750,
        _ => 500,
    };

    private static bool IsCourierFamily(string fontName) =>
        fontName.StartsWith("Courier", StringComparison.Ordinal);
}
