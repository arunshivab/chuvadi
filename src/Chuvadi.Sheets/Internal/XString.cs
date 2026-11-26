using System;
using System.Text;

namespace Chuvadi.Sheets.Internal;

/// <summary>
/// 2.2.1 (P8, S-13): OOXML's escape for characters XML cannot hold (ECMA-376 Part 1, ST_Xstring). A character such as
/// U+000B (a line break pasted from Word) or U+0001 is written as <c>_x000B_</c>, <c>_x0001_</c>, as Excel writes it;
/// until 2.2.0 such a text made the save throw at the very end, with no hint of where. An underscore that would
/// otherwise start such an escape (a literal <c>_x0041_</c> in a cell) is written as <c>_x005F_</c>, so the reader's
/// <see cref="Decode"/> gives back exactly the original text: the value round-trips unchanged.
/// <para>
/// It is used, on both sides, for the free-text fields: cell text (shared strings, inline strings, rich-text runs and
/// cached formula strings), comment text and authors, headers and footers, data-validation titles and messages,
/// hyperlink tooltips and display text, and table column names (written only). Text without such characters or
/// patterns is written byte for byte as before.
/// </para>
/// </summary>
internal static class XString
{
    /// <summary>The text with every character XML 1.0 cannot hold, and every underscore that starts an escape, escaped.</summary>
    public static string Encode(string? s)
    {
        if (string.IsNullOrEmpty(s)) return s ?? string.Empty;
        int i = 0;
        for (; i < s.Length; i++)
            if (NeedsEscape(s, i)) break;
        if (i == s.Length) return s;   // the usual case: unchanged, the same string

        var sb = new StringBuilder(s.Length + 16);
        sb.Append(s, 0, i);
        for (; i < s.Length; i++)
        {
            char c = s[i];
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                sb.Append(c).Append(s[++i]);   // a valid pair
                continue;
            }
            if (NeedsEscape(s, i)) Hex(sb, c);
            else sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>The text with every <c>_xHHHH_</c> escape turned back into its character.</summary>
    public static string Decode(string? s)
    {
        if (string.IsNullOrEmpty(s)) return s ?? string.Empty;
        int i = s.IndexOf("_x", StringComparison.Ordinal);
        if (i < 0) return s;
        StringBuilder? sb = null;
        int copied = 0;
        while (i >= 0)
        {
            if (IsEscape(s, i, out char ch))
            {
                sb ??= new StringBuilder(s.Length);
                sb.Append(s, copied, i - copied).Append(ch);
                copied = i + 7;
                i = s.IndexOf("_x", copied, StringComparison.Ordinal);
            }
            else i = s.IndexOf("_x", i + 1, StringComparison.Ordinal);
        }
        if (sb is null) return s;
        sb.Append(s, copied, s.Length - copied);
        return sb.ToString();
    }

    private static bool NeedsEscape(string s, int i)
    {
        char c = s[i];
        if (c == '_') return IsEscape(s, i, out _);
        if (c < 0x20) return c is not ('\t' or '\n' or '\r');
        if (c >= 0xD800 && c <= 0xDFFF)
        {
            // Only an unpaired surrogate is invalid.
            if (char.IsHighSurrogate(c)) return !(i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]));
            return !(i > 0 && char.IsHighSurrogate(s[i - 1]));
        }
        return c is '￾' or '￿';
    }

    private static bool IsEscape(string s, int i, out char ch)
    {
        ch = '\0';
        if (i + 6 >= s.Length || s[i] != '_' || s[i + 1] != 'x' || s[i + 6] != '_') return false;
        int v = 0;
        for (int k = 2; k < 6; k++)
        {
            int d = HexValue(s[i + k]);
            if (d < 0) return false;
            v = v * 16 + d;
        }
        ch = (char)v;
        return true;
    }

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'A' and <= 'F' => c - 'A' + 10,
        >= 'a' and <= 'f' => c - 'a' + 10,
        _ => -1,
    };

    private static void Hex(StringBuilder sb, char c)
        => sb.Append("_x").Append(((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture)).Append('_');
}
