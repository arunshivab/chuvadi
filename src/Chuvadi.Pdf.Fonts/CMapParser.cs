// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.10.3 — ToUnicode CMaps
// PHASE: Phase 1 — Chuvadi.Pdf.Fonts
//        v2.1.4 — codespacerange parsing + multi-byte mapping support.
//                 The legacy Parse() returns just the bf-char/bf-range mapping
//                 (1- or 2-byte codes packed into int keys, unchanged from
//                 v2.1.3); the new ParseFull() additionally returns the
//                 codespacerange declarations so the decoder can size byte
//                 windows correctly for fonts whose ToUnicode CMap uses
//                 3+ byte source codes (e.g. Word's UTF-8-encoded
//                 Wingdings glyph codes).
// Parses ToUnicode CMap streams to build code→Unicode mappings.

using System;
using System.Collections.Generic;
using System.Text;

namespace Chuvadi.Pdf.Fonts;

/// <summary>
/// A declared codespace range from a CMap's
/// <c>begincodespacerange ... endcodespacerange</c> block.
/// </summary>
/// <param name="Lo">Inclusive low code point (bytes packed big-endian into an integer).</param>
/// <param name="Hi">Inclusive high code point (bytes packed big-endian into an integer).</param>
/// <param name="ByteCount">Number of source bytes consumed by codes in this range.</param>
/// <remarks>
/// PDF 32000-1:2008 §9.7.6.2 — codespace ranges declare how the input byte
/// stream is partitioned into character codes. A CMap can mix 1-byte and
/// multi-byte ranges; the decoder uses the longest declared byte count as
/// the upper bound when matching codes.
/// </remarks>
public readonly record struct CodespaceRange(int Lo, int Hi, int ByteCount);

/// <summary>
/// The full result of parsing a ToUnicode CMap: the bf-char/bf-range mappings
/// and the declared codespace ranges.
/// </summary>
public sealed class CMapParseResult
{
    /// <summary>Code → Unicode string mapping from bfchar/bfrange sections.</summary>
    public required IReadOnlyDictionary<int, string> Mapping { get; init; }

    /// <summary>Declared codespace ranges from begincodespacerange sections.</summary>
    public required IReadOnlyList<CodespaceRange> CodespaceRanges { get; init; }
}

/// <summary>
/// Parses a PDF ToUnicode CMap stream and builds a character code to
/// Unicode string mapping.
/// </summary>
/// <remarks>
/// A ToUnicode CMap is a PostScript-like text stream that maps character
/// codes (1 to 4 bytes) to Unicode codepoints or sequences.
///
/// The three key sections are:
/// <list type="bullet">
///   <item>
///     <c>begincodespacerange / endcodespacerange</c> — declares valid
///     source code-byte windows.
///   </item>
///   <item>
///     <c>beginbfchar / endbfchar</c> — individual code→Unicode mappings.
///   </item>
///   <item>
///     <c>beginbfrange / endbfrange</c> — range mappings where a contiguous
///     block of codes maps to a contiguous block of Unicode values.
///   </item>
/// </list>
///
/// PDF 32000-1:2008 §9.10.3 — ToUnicode CMaps. The bf-char and bf-range
/// sections accept hex source codes of any byte width (1, 2, 3, 4 bytes);
/// the codespacerange block tells the decoder how to slice the byte stream
/// into codes of that width.
/// </remarks>
public sealed class CMapParser
{
    private readonly string _content;

    /// <summary>
    /// Initialises a new <see cref="CMapParser"/> over the given CMap text.
    /// </summary>
    /// <param name="content">The CMap stream content as a Latin-1 string.</param>
    public CMapParser(string content)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
    }

    /// <summary>
    /// Initialises a new <see cref="CMapParser"/> over raw CMap bytes.
    /// </summary>
    public CMapParser(byte[] bytes)
    {
        _content = bytes is null
            ? throw new ArgumentNullException(nameof(bytes))
            : Encoding.Latin1.GetString(bytes);
    }

    /// <summary>
    /// Parses the CMap and returns a dictionary mapping character codes
    /// (as integers) to Unicode strings.
    /// </summary>
    /// <remarks>
    /// Equivalent to <see cref="ParseFull"/>.<see cref="CMapParseResult.Mapping"/>.
    /// Retained for callers that only need the code→Unicode mapping and
    /// don't care about codespace declarations.
    /// </remarks>
    /// <returns>
    /// A dictionary where keys are character codes (1- to 4-byte codes
    /// packed big-endian into int) and values are the corresponding
    /// Unicode strings.
    /// </returns>
    public Dictionary<int, string> Parse()
    {
        Dictionary<int, string> result = new Dictionary<int, string>();
        ParseBfChar(result);
        ParseBfRange(result);
        return result;
    }

    /// <summary>
    /// Parses the CMap and returns the full result, including the
    /// code→Unicode mapping and the declared codespace ranges.
    /// </summary>
    /// <returns>A <see cref="CMapParseResult"/> with both pieces.</returns>
    public CMapParseResult ParseFull()
    {
        Dictionary<int, string> mapping = new Dictionary<int, string>();
        ParseBfChar(mapping);
        ParseBfRange(mapping);

        List<CodespaceRange> ranges = new List<CodespaceRange>();
        ParseCodespaceRanges(ranges);

        return new CMapParseResult
        {
            Mapping = mapping,
            CodespaceRanges = ranges,
        };
    }

    // ── codespacerange ────────────────────────────────────────────────────

    private void ParseCodespaceRanges(List<CodespaceRange> ranges)
    {
        // N begincodespacerange
        // <srcLo> <srcHi>
        // ...
        // endcodespacerange
        int searchFrom = 0;

        while (true)
        {
            int beginIdx = _content.IndexOf(
                "begincodespacerange", searchFrom, StringComparison.Ordinal);

            if (beginIdx < 0)
            {
                break;
            }

            int endIdx = _content.IndexOf(
                "endcodespacerange", beginIdx, StringComparison.Ordinal);

            if (endIdx < 0)
            {
                break;
            }

            string section = _content[beginIdx..endIdx];
            ParseCodespaceRangeSection(section, ranges);
            searchFrom = endIdx + 17; // 17 = "endcodespacerange".Length
        }
    }

    private static void ParseCodespaceRangeSection(
        string section, List<CodespaceRange> ranges)
    {
        int pos = 0;

        while (pos < section.Length)
        {
            int loStart = section.IndexOf('<', pos);

            if (loStart < 0)
            {
                break;
            }

            int loEnd = section.IndexOf('>', loStart);

            if (loEnd < 0)
            {
                break;
            }

            string loHex = section[(loStart + 1)..loEnd].Trim();
            int loCode = ParseHexCode(loHex);
            int loBytes = (loHex.Length + 1) / 2;
            pos = loEnd + 1;

            int hiStart = section.IndexOf('<', pos);

            if (hiStart < 0)
            {
                break;
            }

            int hiEnd = section.IndexOf('>', hiStart);

            if (hiEnd < 0)
            {
                break;
            }

            string hiHex = section[(hiStart + 1)..hiEnd].Trim();
            int hiCode = ParseHexCode(hiHex);
            int hiBytes = (hiHex.Length + 1) / 2;
            pos = hiEnd + 1;

            if (loCode >= 0 && hiCode >= 0 && loBytes == hiBytes && loBytes >= 1)
            {
                ranges.Add(new CodespaceRange(loCode, hiCode, loBytes));
            }
        }
    }

    // ── bfchar ────────────────────────────────────────────────────────────

    private void ParseBfChar(Dictionary<int, string> result)
    {
        // beginbfchar
        // <srcCode> <dstString>
        // ...
        // endbfchar
        int searchFrom = 0;

        while (true)
        {
            int beginIdx = _content.IndexOf("beginbfchar", searchFrom, StringComparison.Ordinal);

            if (beginIdx < 0)
            {
                break;
            }

            int endIdx = _content.IndexOf("endbfchar", beginIdx, StringComparison.Ordinal);

            if (endIdx < 0)
            {
                break;
            }

            string section = _content[beginIdx..endIdx];
            ParseBfCharSection(section, result);
            searchFrom = endIdx + 9; // 9 = "endbfchar".Length
        }
    }

    private static void ParseBfCharSection(string section, Dictionary<int, string> result)
    {
        int pos = 0;

        while (pos < section.Length)
        {
            int srcStart = section.IndexOf('<', pos);

            if (srcStart < 0)
            {
                break;
            }

            int srcEnd = section.IndexOf('>', srcStart);

            if (srcEnd < 0)
            {
                break;
            }

            string srcHex = section[(srcStart + 1)..srcEnd];
            int srcCode = ParseHexCode(srcHex);
            pos = srcEnd + 1;

            int dstStart = section.IndexOf('<', pos);

            if (dstStart < 0)
            {
                break;
            }

            int dstEnd = section.IndexOf('>', dstStart);

            if (dstEnd < 0)
            {
                break;
            }

            string dstHex = section[(dstStart + 1)..dstEnd];
            string unicode = HexToUnicode(dstHex);
            pos = dstEnd + 1;

            if (srcCode >= 0 && unicode.Length > 0)
            {
                result[srcCode] = unicode;
            }
        }
    }

    // ── bfrange ───────────────────────────────────────────────────────────

    private void ParseBfRange(Dictionary<int, string> result)
    {
        int searchFrom = 0;

        while (true)
        {
            int beginIdx = _content.IndexOf("beginbfrange", searchFrom, StringComparison.Ordinal);

            if (beginIdx < 0)
            {
                break;
            }

            int endIdx = _content.IndexOf("endbfrange", beginIdx, StringComparison.Ordinal);

            if (endIdx < 0)
            {
                break;
            }

            string section = _content[beginIdx..endIdx];
            ParseBfRangeSection(section, result);
            searchFrom = endIdx + 10; // 10 = "endbfrange".Length
        }
    }

    private static void ParseBfRangeSection(string section, Dictionary<int, string> result)
    {
        int pos = 0;

        while (pos < section.Length)
        {
            int startHexPos = section.IndexOf('<', pos);

            if (startHexPos < 0)
            {
                break;
            }

            int startHexEnd = section.IndexOf('>', startHexPos);

            if (startHexEnd < 0)
            {
                break;
            }

            int startCode = ParseHexCode(section[(startHexPos + 1)..startHexEnd]);
            pos = startHexEnd + 1;

            int endHexPos = section.IndexOf('<', pos);

            if (endHexPos < 0)
            {
                break;
            }

            int endHexEnd = section.IndexOf('>', endHexPos);

            if (endHexEnd < 0)
            {
                break;
            }

            int endCode = ParseHexCode(section[(endHexPos + 1)..endHexEnd]);
            pos = endHexEnd + 1;

            // Skip whitespace to find the destination — either <hex> or [array]
            while (pos < section.Length && IsWhitespace(section[pos]))
            {
                pos++;
            }

            if (pos >= section.Length)
            {
                break;
            }

            if (section[pos] == '<')
            {
                // Single destination: increment Unicode for each code in range.
                int dstEnd = section.IndexOf('>', pos);

                if (dstEnd < 0)
                {
                    break;
                }

                string dstHex = section[(pos + 1)..dstEnd];
                int baseUnicode = ParseHexCode(dstHex);
                pos = dstEnd + 1;

                for (int code = startCode; code <= endCode; code++)
                {
                    int cp = baseUnicode + (code - startCode);

                    if (cp <= 0xFFFF)
                    {
                        result[code] = ((char)cp).ToString();
                    }
                }
            }
            else if (section[pos] == '[')
            {
                // Array destination: each entry maps to the corresponding code.
                int arrayEnd = section.IndexOf(']', pos);

                if (arrayEnd < 0)
                {
                    break;
                }

                string arrayContent = section[(pos + 1)..arrayEnd];
                pos = arrayEnd + 1;

                List<string> entries = ParseHexArray(arrayContent);
                int code = startCode;

                foreach (string entry in entries)
                {
                    if (code <= endCode)
                    {
                        result[code] = entry;
                        code++;
                    }
                }
            }
            else
            {
                // Unexpected — skip to next line.
                int nl = section.IndexOf('\n', pos);
                pos = nl >= 0 ? nl + 1 : section.Length;
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static int ParseHexCode(string hex)
    {
        if (string.IsNullOrEmpty(hex))
        {
            return -1;
        }

        if (int.TryParse(hex.Trim(),
            System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture,
            out int value))
        {
            return value;
        }

        return -1;
    }

    private static string HexToUnicode(string hex)
    {
        if (string.IsNullOrEmpty(hex))
        {
            return string.Empty;
        }

        hex = hex.Trim();

        // Each pair of hex digits is one UTF-16 code unit.
        StringBuilder sb = new StringBuilder(hex.Length / 4);

        for (int i = 0; i + 3 < hex.Length; i += 4)
        {
            string chunk = hex.Substring(i, 4);

            if (int.TryParse(chunk,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out int cp))
            {
                sb.Append((char)cp);
            }
        }

        // Handle 2-digit hex (single byte → BMP character).
        if (hex.Length == 2)
        {
            if (int.TryParse(hex,
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out int cp))
            {
                return ((char)cp).ToString();
            }
        }

        return sb.Length > 0 ? sb.ToString() : string.Empty;
    }

    private static List<string> ParseHexArray(string content)
    {
        List<string> entries = new List<string>();
        int pos = 0;

        while (pos < content.Length)
        {
            int start = content.IndexOf('<', pos);

            if (start < 0)
            {
                break;
            }

            int end = content.IndexOf('>', start);

            if (end < 0)
            {
                break;
            }

            entries.Add(HexToUnicode(content[(start + 1)..end]));
            pos = end + 1;
        }

        return entries;
    }

    private static bool IsWhitespace(char c)
    {
        return c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\f';
    }
}
