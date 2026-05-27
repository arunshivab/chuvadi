// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.8 — Font descriptors
//        §9.9 — Embedded font programs
// PHASE: Phase 2.0 — SVG export
//        v2.1.2 — hoisted from TextDispatcher so the display-list-based
//                 SvgRenderer pipeline can embed font programs as well.
//                 Without this, browsers substitute the PDF's embedded
//                 subsetted fonts with system Times/Arial, whose glyph
//                 advance widths differ from the PDF's, producing visible
//                 inter-character drift (the "Developed India's First..."
//                 splayed bold-italic symptom).

using System;
using System.Collections.Generic;
using System.Text;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Svg;

/// <summary>
/// Extracts a PDF font's embedded program (TrueType or CFF/OpenType) and
/// emits it into an SVG document as a CSS <c>@font-face</c> rule.
/// </summary>
/// <remarks>
/// <para>
/// PDF 32000-1 §9.9 defines three embedded-font-program streams:
/// </para>
/// <list type="bullet">
///   <item><c>/FontFile</c> — Type 1 (PostScript). Not supported by browsers
///   directly; would require conversion. We skip this for now.</item>
///   <item><c>/FontFile2</c> — TrueType. Browsers consume this directly with
///   <c>format("truetype")</c>.</item>
///   <item><c>/FontFile3</c> — CFF (Compact Font Format) or OpenType. The
///   stream's <c>/Subtype</c> distinguishes the two; both are consumed by
///   browsers with <c>format("opentype")</c>.</item>
/// </list>
/// <para>
/// For composite (Type0) fonts the FontDescriptor lives on the descendant
/// font, not the top-level font dictionary. <see cref="ResolveFontDescriptor"/>
/// handles the indirection.
/// </para>
/// </remarks>
internal static class FontEmbedder
{
    /// <summary>
    /// Attempts to extract <paramref name="fontDict"/>'s embedded font program
    /// and register it as a CSS <c>@font-face</c> rule on
    /// <paramref name="writer"/>. Returns the assigned unique CSS family name
    /// on success, or <c>null</c> when the font is not embeddable (no
    /// FontFile2/FontFile3 stream, or stream decode failed).
    /// </summary>
    /// <param name="fontDict">The PDF font dictionary to embed.</param>
    /// <param name="baseFont">
    /// The font's BaseFont name with any subset prefix already stripped
    /// (e.g. <c>"TimesNewRomanPS-BoldMT"</c>, not <c>"ABCDEF+TimesNewRomanPS-BoldMT"</c>).
    /// </param>
    /// <param name="writer">The SVG writer to register the @font-face on.</param>
    /// <param name="resolver">Resolver for indirect object references.</param>
    /// <param name="emittedFamilies">
    /// A cache of family names already registered on this writer. Used to
    /// avoid emitting duplicate <c>@font-face</c> rules when the same font
    /// program is referenced multiple times.
    /// </param>
    internal static string? TryEmbed(
        PdfDictionary fontDict,
        string baseFont,
        SvgWriter writer,
        IPdfObjectResolver resolver,
        HashSet<string> emittedFamilies)
    {
        ArgumentNullException.ThrowIfNull(fontDict);
        ArgumentNullException.ThrowIfNull(baseFont);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(emittedFamilies);

        PdfDictionary? descriptor = ResolveFontDescriptor(fontDict, resolver);
        if (descriptor is null) { return null; }

        // Prefer FontFile2 (TrueType) over FontFile3 (CFF/OpenType).
        // Type 1 (FontFile) is not handled — browsers cannot consume it
        // directly and conversion would be a substantial extra dependency.
        PdfStream? fontProgram = null;
        string? format = null;
        if (descriptor.TryGetValue(PdfName.Intern("FontFile2"), out PdfPrimitive? ff2)
            && resolver.Resolve(ff2) is PdfStream ff2Stream)
        {
            fontProgram = ff2Stream;
            format = "truetype";
        }
        else if (descriptor.TryGetValue(PdfName.Intern("FontFile3"), out PdfPrimitive? ff3)
            && resolver.Resolve(ff3) is PdfStream ff3Stream)
        {
            fontProgram = ff3Stream;
            // PDF 32000-1 §9.9 Table 126: /FontFile3 Subtype is one of
            // /Type1C, /CIDFontType0C, /OpenType. The first two are CFF
            // programs; the third is a complete OpenType font. Browsers
            // accept all three under format("opentype").
            if (ff3Stream.Dictionary.TryGetValue(PdfName.Intern("Subtype"), out PdfPrimitive? sub)
                && sub is PdfName subName)
            {
                format = subName.Value == "OpenType" ? "opentype" : "opentype";
            }
            else { format = "opentype"; }
        }
        if (fontProgram is null || format is null) { return null; }

        byte[] fontBytes;
        try { fontBytes = StreamDecoder.Decode(fontProgram); }
        catch { return null; }
        if (fontBytes.Length == 0) { return null; }

        // The family-name format encodes the sanitized BaseFont and the byte
        // length so that two same-named subsets with different contents
        // produce different family names. We use length rather than a hash
        // for speed; collisions on (name, length) are vanishingly rare in
        // practice and harmless when they do occur (the second font silently
        // shares the first font's @font-face).
        string family = $"chuvadi_{SanitizeFontName(baseFont)}_{fontBytes.Length:X}";
        if (emittedFamilies.Add(family))
        {
            string dataUrl = $"data:font/{format};base64,{Convert.ToBase64String(fontBytes)}";
            writer.AddFontFace(family, dataUrl, format);
        }
        return family;
    }

    /// <summary>
    /// Resolves a font dictionary's FontDescriptor. For Type0 (composite)
    /// fonts the descriptor lives on the descendant font, not the top-level
    /// dictionary; for simple fonts it is direct.
    /// </summary>
    private static PdfDictionary? ResolveFontDescriptor(
        PdfDictionary fontDict, IPdfObjectResolver resolver)
    {
        if (fontDict.TryGetValue(PdfName.Intern("Subtype"), out PdfPrimitive? sub)
            && sub is PdfName subName && subName.Value == "Type0"
            && fontDict.TryGetValue(PdfName.Intern("DescendantFonts"), out PdfPrimitive? descVal))
        {
            if (resolver.Resolve(descVal) is PdfArray descArr && descArr.Count > 0
                && resolver.Resolve(descArr[0]) is PdfDictionary desc
                && desc.TryGetValue(PdfName.Intern("FontDescriptor"), out PdfPrimitive? dV))
            {
                return resolver.Resolve(dV) as PdfDictionary;
            }
        }
        if (fontDict.TryGetValue(PdfName.Intern("FontDescriptor"), out PdfPrimitive? fdVal))
        {
            return resolver.Resolve(fdVal) as PdfDictionary;
        }
        return null;
    }

    /// <summary>
    /// Replaces every character of <paramref name="name"/> that is not a
    /// letter or digit with an underscore, so the result is safe to embed
    /// in a CSS identifier.
    /// </summary>
    private static string SanitizeFontName(string name)
    {
        StringBuilder sb = new(name.Length);
        foreach (char ch in name)
        {
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        }
        return sb.ToString();
    }
}
