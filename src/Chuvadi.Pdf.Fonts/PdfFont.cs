// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.6 — Simple fonts
//        PDF 32000-1:2008 §9.7 — Composite fonts (Type0)
//        PDF 32000-1:2008 §9.10 — Extraction of text content
// PHASE: Phase 1 — Chuvadi.Pdf.Fonts
// Maps character codes from PDF text strings to Unicode codepoints.

using System;
using System.Collections.Generic;
using System.Text;
using Chuvadi.Pdf.Filters;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Fonts;

/// <summary>
/// Represents a PDF font and provides character code to Unicode mapping
/// for text extraction purposes.
/// </summary>
/// <remarks>
/// Phase 1 supports text extraction only — no glyph rendering or metrics.
///
/// The mapping strategy, in priority order:
/// <list type="number">
///   <item>ToUnicode CMap — present in most modern PDFs, most accurate.</item>
///   <item>Encoding + glyph name lookup — for simple fonts without ToUnicode.</item>
///   <item>Direct code-as-Unicode — last resort for unrecognised configurations.</item>
/// </list>
///
/// PDF 32000-1:2008 §9.10.2 — Mapping character codes to Unicode values.
/// </remarks>
public sealed class PdfFont
{
    private readonly Dictionary<int, string>? _toUnicodeMap;
    private readonly PdfFontEncoding? _encoding;
    private readonly bool _isComposite;

    private PdfFont(
        Dictionary<int, string>? toUnicodeMap,
        PdfFontEncoding? encoding,
        bool isComposite)
    {
        _toUnicodeMap = toUnicodeMap;
        _encoding = encoding;
        _isComposite = isComposite;
    }

    // ── Factory ───────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="PdfFont"/> from a font dictionary.
    /// </summary>
    /// <param name="fontDict">The font dictionary from the page Resources.</param>
    /// <param name="resolver">Used to resolve indirect objects (e.g. ToUnicode stream).</param>
    public static PdfFont FromDictionary(PdfDictionary fontDict, IPdfObjectResolver resolver)
    {
        if (fontDict is null)
        {
            throw new ArgumentNullException(nameof(fontDict));
        }

        if (resolver is null)
        {
            throw new ArgumentNullException(nameof(resolver));
        }

        PdfName? subtype = fontDict.Subtype;
        bool isComposite = subtype is not null &&
            subtype.Value.Equals("Type0", StringComparison.Ordinal);

        // Try ToUnicode first — most reliable for all font types.
        Dictionary<int, string>? toUnicodeMap = ParseToUnicode(fontDict, resolver);

        // For simple fonts, also parse the encoding as fallback.
        PdfFontEncoding? encoding = null;

        if (!isComposite)
        {
            PdfPrimitive? encodingEntry =
                fontDict.TryGetValue(PdfName.Intern("Encoding"), out PdfPrimitive? enc)
                    ? enc
                    : null;

            if (encodingEntry is PdfReference encRef)
            {
                encodingEntry = resolver.Resolve(encRef);
            }

            encoding = PdfFontEncoding.Build(encodingEntry);
        }

        return new PdfFont(toUnicodeMap, encoding, isComposite);
    }

    /// <summary>
    /// Returns a default font that maps codes directly to their Latin-1 equivalents.
    /// Used when no font dictionary is available.
    /// </summary>
    public static PdfFont Default()
    {
        return new PdfFont(null, PdfFontEncoding.FromNamedEncoding("WinAnsiEncoding"), false);
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a sequence of bytes from a PDF text string operator to Unicode text.
    /// </summary>
    /// <param name="bytes">The raw bytes from a Tj, TJ, or similar operator.</param>
    /// <returns>The decoded Unicode string.</returns>
    public string Decode(byte[] bytes)
    {
        if (bytes is null)
        {
            throw new ArgumentNullException(nameof(bytes));
        }

        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        StringBuilder sb = new StringBuilder(bytes.Length);

        if (_isComposite)
        {
            DecodeComposite(bytes, sb);
        }
        else
        {
            DecodeSimple(bytes, sb);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Converts a single character code to a Unicode string.
    /// Returns an empty string when the code cannot be mapped.
    /// </summary>
    public string DecodeCode(int code)
    {
        // ToUnicode CMap takes priority.
        if (_toUnicodeMap is not null && _toUnicodeMap.TryGetValue(code, out string? unicode))
        {
            return unicode;
        }

        // Encoding fallback for simple fonts.
        if (_encoding is not null && code >= 0 && code <= 255)
        {
            char c = _encoding.GetCharacter((byte)code);

            if (c != '\0')
            {
                return c.ToString();
            }
        }

        // Last resort: direct code-point interpretation.
        if (code >= 0x20 && code <= 0x7E)
        {
            return ((char)code).ToString();
        }

        return string.Empty;
    }

    // ── Private decoding ──────────────────────────────────────────────────

    private void DecodeSimple(byte[] bytes, StringBuilder sb)
    {
        foreach (byte b in bytes)
        {
            sb.Append(DecodeCode(b));
        }
    }

    private void DecodeComposite(byte[] bytes, StringBuilder sb)
    {
        // Composite fonts use 2-byte codes (CIDFont / Type0).
        // Try 2-byte codes first; fall back to 1-byte.
        int i = 0;

        while (i < bytes.Length)
        {
            if (i + 1 < bytes.Length)
            {
                int twoByteCode = (bytes[i] << 8) | bytes[i + 1];
                string decoded = DecodeCode(twoByteCode);

                if (decoded.Length > 0)
                {
                    sb.Append(decoded);
                    i += 2;
                    continue;
                }
            }

            // Fall back to 1-byte.
            sb.Append(DecodeCode(bytes[i]));
            i++;
        }
    }

    // ── ToUnicode parsing ─────────────────────────────────────────────────

    private static Dictionary<int, string>? ParseToUnicode(
        PdfDictionary fontDict,
        IPdfObjectResolver resolver)
    {
        if (!fontDict.TryGetValue(PdfName.Intern("ToUnicode"), out PdfPrimitive? toUnicodeRef))
        {
            return null;
        }

        PdfPrimitive resolved = resolver.Resolve(toUnicodeRef);

        if (resolved is not PdfStream stream)
        {
            return null;
        }

        // Decode the stream (usually FlateDecode compressed).
        byte[] rawBytes = stream.RawBytes;

        // If the stream has a filter, apply it.
        if (stream.Filter is not null)
        {
            rawBytes = FilterRegistry
                .CreateDefaultPipeline()
                .Decode(
                    FilterRegistry.ResolveAlias(
                        stream.Filter is PdfName fn ? fn.Value : "FlateDecode"),
                    rawBytes);
        }

        CMapParser parser = new CMapParser(rawBytes);
        return parser.Parse();
    }
}
