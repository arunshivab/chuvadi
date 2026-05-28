// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.7.4   — CIDFontType2 (TrueType-based CID fonts)
//                          §9.7.4.2 — CIDToGIDMap entry
//                          §9.8     — Font descriptors
//                          §9.9     — Embedded font programs
// PHASE: Phase 2.0 — SVG export
//        v2.1.2 — hoisted from TextDispatcher so the display-list-based
//                 SvgRenderer pipeline can embed font programs as well.
//                 Without this, browsers substitute the PDF's embedded
//                 subsetted fonts with system Times/Arial, whose glyph
//                 advance widths differ from the PDF's, producing visible
//                 inter-character drift (the "Developed India's First..."
//                 splayed bold-italic symptom).
//        v2.1.5 — for Type0 TrueType (FontFile2 / CIDFontType2) fonts with
//                 a ToUnicode CMap, rewrites the embedded font program's
//                 cmap table so the browser can locate each glyph by its
//                 semantic Unicode code point. Without this rewrite,
//                 symbol fonts like Wingdings (which carry glyphs at
//                 legacy Windows code points such as 0xFC for the
//                 checkmark) fall back to a generic system glyph when the
//                 SVG text element asks for U+2713 ✓. Mirrors pdf2htmlEX's
//                 font remapping strategy.

using System;
using System.Collections.Generic;
using System.Text;
using Chuvadi.Pdf.Fonts;
using Chuvadi.Pdf.Fonts.Rendering;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Svg;

/// <summary>
/// Extracts a PDF font's embedded program (TrueType or CFF/OpenType) and
/// emits it into an SVG document as a CSS <c>@font-face</c> rule. For
/// Type0 TrueType fonts with a ToUnicode CMap, the embedded program's
/// cmap table is rewritten so the browser can locate each glyph by its
/// semantic Unicode code point.
/// </summary>
/// <remarks>
/// <para>
/// PDF 32000-1 §9.9 defines three embedded-font-program streams:
/// </para>
/// <list type="bullet">
///   <item><c>/FontFile</c> — Type 1 (PostScript). Not supported by browsers
///   directly; would require conversion. We skip this for now.</item>
///   <item><c>/FontFile2</c> — TrueType. Browsers consume this directly with
///   <c>format("truetype")</c>. The v2.1.5 cmap remap path runs here.</item>
///   <item><c>/FontFile3</c> — CFF (Compact Font Format) or OpenType. The
///   stream's <c>/Subtype</c> distinguishes the two; both are consumed by
///   browsers with <c>format("opentype")</c>. CFF cmap remapping is
///   deferred to v2.1.6 (CFF requires wrapping in an OpenType envelope
///   before browsers will accept the cmap rewrite).</item>
/// </list>
/// <para>
/// For composite (Type0) fonts the FontDescriptor lives on the descendant
/// font, not the top-level font dictionary. <see cref="ResolveDescendant"/>
/// and <see cref="ResolveFontDescriptor"/> handle the indirection.
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
    /// <param name="pdfFont">
    /// Optional. When provided and the embedded program is a TrueType font
    /// belonging to a Type0 font dictionary, the program's cmap table is
    /// rewritten to map semantic Unicode code points (derived from the
    /// font's ToUnicode CMap) to glyph indices. Pass <c>null</c> to skip
    /// the cmap remap step, falling back to v2.1.4 behaviour.
    /// </param>
    internal static string? TryEmbed(
        PdfDictionary fontDict,
        string baseFont,
        SvgWriter writer,
        IPdfObjectResolver resolver,
        HashSet<string> emittedFamilies,
        PdfFont? pdfFont = null)
    {
        ArgumentNullException.ThrowIfNull(fontDict);
        ArgumentNullException.ThrowIfNull(baseFont);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(emittedFamilies);

        PdfDictionary? descendant = ResolveDescendant(fontDict, resolver);
        PdfDictionary? descriptor = ResolveFontDescriptor(fontDict, descendant, resolver);
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
        catch (Exception) { return null; }
        if (fontBytes.Length == 0) { return null; }

        // v2.1.5 — cmap remap for Type0 TrueType fonts.
        //
        // Eligibility (all must hold):
        //   * pdfFont is non-null and exposes a non-empty ToUnicodeMap
        //   * embedded program is TrueType (FontFile2, format == "truetype")
        //   * font dictionary is Type0 (descendant is non-null)
        //
        // For simple fonts (descendant is null) we don't remap: the
        // existing font cmap already maps the encoding code points the
        // browser will use, and we don't have a CIDToGIDMap layer to invert.
        // CFF font remapping requires CFF→OpenType wrapping first and is
        // tracked separately (v2.1.6).
        if (pdfFont is not null
            && format == "truetype"
            && descendant is not null
            && pdfFont.ToUnicodeMap is { Count: > 0 } toUnicode)
        {
            IReadOnlyDictionary<int, int>? cidToGid = ParseCidToGidMap(descendant, resolver);
            Dictionary<int, int> unicodeToGid = BuildUnicodeToGid(toUnicode, cidToGid);

            if (unicodeToGid.Count > 0)
            {
                try
                {
                    fontBytes = TrueTypeFontPatch.WithAugmentedCmap(fontBytes, unicodeToGid);
                }
                catch (FontRenderingException)
                {
                    // Patch failed (corrupt font program); fall through with
                    // the original bytes. Browser will render with the
                    // legacy fallback as in v2.1.4.
                }
            }
        }

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

    // ── Resolution helpers ───────────────────────────────────────────────

    /// <summary>
    /// Returns the descendant CIDFont dictionary for a Type0 font, or
    /// <c>null</c> for simple fonts and malformed dictionaries.
    /// </summary>
    private static PdfDictionary? ResolveDescendant(
        PdfDictionary fontDict, IPdfObjectResolver resolver)
    {
        if (!fontDict.TryGetValue(PdfName.Intern("Subtype"), out PdfPrimitive? sub)
            || sub is not PdfName subName
            || subName.Value != "Type0")
        {
            return null;
        }

        if (!fontDict.TryGetValue(PdfName.Intern("DescendantFonts"), out PdfPrimitive? descVal))
        {
            return null;
        }

        if (resolver.Resolve(descVal) is not PdfArray descArr || descArr.Count == 0)
        {
            return null;
        }

        return resolver.Resolve(descArr[0]) as PdfDictionary;
    }

    /// <summary>
    /// Resolves a font dictionary's FontDescriptor. For Type0 (composite)
    /// fonts the descriptor lives on the descendant font passed in
    /// <paramref name="descendant"/>; for simple fonts it is direct on
    /// <paramref name="fontDict"/>.
    /// </summary>
    private static PdfDictionary? ResolveFontDescriptor(
        PdfDictionary fontDict, PdfDictionary? descendant, IPdfObjectResolver resolver)
    {
        if (descendant is not null
            && descendant.TryGetValue(PdfName.Intern("FontDescriptor"), out PdfPrimitive? dV))
        {
            return resolver.Resolve(dV) as PdfDictionary;
        }
        if (fontDict.TryGetValue(PdfName.Intern("FontDescriptor"), out PdfPrimitive? fdVal))
        {
            return resolver.Resolve(fdVal) as PdfDictionary;
        }
        return null;
    }

    // ── CIDToGIDMap ──────────────────────────────────────────────────────

    /// <summary>
    /// Reads the descendant font's <c>/CIDToGIDMap</c> entry. Returns
    /// <c>null</c> when the map is identity (the value is the name
    /// <c>/Identity</c>, or the entry is absent), in which case callers
    /// should treat CID equal to GID. Returns a dictionary when the entry
    /// is a stream containing an explicit map.
    /// </summary>
    /// <remarks>
    /// Per PDF 32000-1 §9.7.4.2 the CIDToGIDMap stream contains two bytes
    /// per CID, big-endian, giving the GID at that CID position. CIDs not
    /// covered by the stream length default to GID 0. Stream decoding
    /// failure is treated as identity to preserve renderability.
    /// </remarks>
    private static IReadOnlyDictionary<int, int>? ParseCidToGidMap(
        PdfDictionary descendant, IPdfObjectResolver resolver)
    {
        if (!descendant.TryGetValue(PdfName.Intern("CIDToGIDMap"), out PdfPrimitive? mapEntry))
        {
            // Per spec, default is /Identity when absent.
            return null;
        }

        PdfPrimitive resolved = resolver.Resolve(mapEntry);

        if (resolved is PdfName nm && nm.Value == "Identity")
        {
            return null;
        }

        if (resolved is not PdfStream mapStream)
        {
            // Unknown shape — fall back to identity rather than throwing.
            return null;
        }

        byte[] bytes;
        try { bytes = StreamDecoder.Decode(mapStream); }
        catch (Exception) { return null; }

        if (bytes.Length < 2) { return null; }

        Dictionary<int, int> result = new(bytes.Length / 2);
        for (int cid = 0; cid * 2 + 1 < bytes.Length; cid++)
        {
            int gid = (bytes[cid * 2] << 8) | bytes[cid * 2 + 1];
            if (gid != 0)
            {
                result[cid] = gid;
            }
        }

        return result;
    }

    // ── Unicode → GID derivation ─────────────────────────────────────────

    /// <summary>
    /// Builds a Unicode-code-point-to-glyph-index map suitable for
    /// <see cref="TrueTypeFontPatch.WithAugmentedCmap"/>.
    /// </summary>
    /// <param name="toUnicode">
    /// Source-code (CID) to Unicode-string mapping from the font's ToUnicode CMap.
    /// </param>
    /// <param name="cidToGid">
    /// CID-to-GID mapping from the descendant font's CIDToGIDMap. Pass
    /// <c>null</c> for identity (CID = GID).
    /// </param>
    /// <remarks>
    /// For each <c>(cid, unicodeString)</c> entry, the GID is resolved
    /// from <paramref name="cidToGid"/> (or treated as equal to the CID
    /// when null). Then the unicode string's first code point is used as
    /// the map key. Ligature-style ToUnicode entries (multi-code-point
    /// strings) contribute their first code point only, so a ligature
    /// glyph remains reachable at the first character's code point and
    /// will render as an approximation rather than a system-font fallback.
    /// </remarks>
    private static Dictionary<int, int> BuildUnicodeToGid(
        IReadOnlyDictionary<int, string> toUnicode,
        IReadOnlyDictionary<int, int>? cidToGid)
    {
        Dictionary<int, int> result = new(toUnicode.Count);

        foreach (KeyValuePair<int, string> kv in toUnicode)
        {
            int cid = kv.Key;
            string str = kv.Value;
            if (str.Length == 0) { continue; }

            // Take the first complete code point. Handles UTF-16 surrogate
            // pairs so non-BMP destinations (very rare in PDFs but legal)
            // are addressed correctly.
            int codePoint;
            if (char.IsHighSurrogate(str[0]) && str.Length >= 2 && char.IsLowSurrogate(str[1]))
            {
                codePoint = char.ConvertToUtf32(str, 0);
            }
            else
            {
                codePoint = str[0];
            }

            // Skip control characters: the browser would not request them
            // via the cmap path anyway, and they pollute the cmap table.
            if (codePoint < 0x20) { continue; }

            int gid;
            if (cidToGid is null)
            {
                gid = cid;
            }
            else if (!cidToGid.TryGetValue(cid, out gid))
            {
                continue;
            }

            if (gid <= 0 || gid > 0xFFFF) { continue; }

            // First-writer wins. ToUnicode iteration order in a Dictionary
            // is implementation-defined, but the only way to get duplicate
            // codePoint keys is for the source font to map multiple CIDs
            // to the same Unicode (rare but possible — e.g. presentation
            // forms of the same letter). The browser only needs ONE
            // reachable glyph at the codepoint for rendering to succeed.
            result.TryAdd(codePoint, gid);
        }

        return result;
    }

    // ── Misc ─────────────────────────────────────────────────────────────

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
