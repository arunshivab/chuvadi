// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.10.3 — ToUnicode CMaps
// PHASE: Phase 1 — Chuvadi.Pdf.Fonts
// Parses ToUnicode CMap streams to build code→Unicode mappings.

using System;
using System.Collections.Generic;
using System.Text;

namespace Chuvadi.Pdf.Fonts;

/// <summary>
/// Parses a PDF ToUnicode CMap stream and builds a character code to
/// Unicode string mapping.
/// </summary>
/// <remarks>
/// A ToUnicode CMap is a PostScript-like text stream that maps character
/// codes (1 or 2 bytes) to Unicode codepoints or sequences.
///
/// The two key sections are:
/// <list type="bullet">
///   <item>
///     <c>beginbfchar / endbfchar</c> — individual code→Unicode mappings.
///   </item>
///   <item>
///     <c>beginbfrange / endbfrange</c> — range mappings where a contiguous
///     block of codes maps to a contiguous block of Unicode values.
///   </item>
/// </list>
///
/// PDF 32000-1:2008 §9.10.3 — ToUnicode CMaps.
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
    /// <returns>
    /// A dictionary where keys are character codes (0-65535 for 2-byte CMaps,
    /// 0-255 for 1-byte CMaps) and values are the corresponding Unicode strings.
    /// </returns>
    public Dictionary<int, string> Parse()
    {
        Dictionary<int, string> result = new Dictionary<int, string>();
        ParseBfChar(result);
        ParseBfRange(result);
        return result;
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
