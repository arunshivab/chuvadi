using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Chuvadi.Markup.Internal
{
    internal static class CharUtil
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAsciiAlpha(char c) => (uint)((c | 0x20) - 'a') <= 'z' - 'a';

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAsciiDigit(char c) => (uint)(c - '0') <= 9;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAsciiAlnum(char c) => IsAsciiAlpha(c) || IsAsciiDigit(c);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAsciiHex(char c) => IsAsciiDigit(c) || (uint)((c | 0x20) - 'a') <= 'f' - 'a';

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static char AsciiLower(char c) => (uint)(c - 'A') <= 'Z' - 'A' ? (char)(c | 0x20) : c;

        /// <summary>CommonMark ASCII punctuation.</summary>
        public static bool IsAsciiPunctuation(char c)
            => (c >= '!' && c <= '/') || (c >= ':' && c <= '@') || (c >= '[' && c <= '`') || (c >= '{' && c <= '~');

        /// <summary>CommonMark 0.31 Unicode punctuation: general category P or S.</summary>
        public static bool IsUnicodePunctuation(int codePoint)
        {
            if (codePoint < 128) return IsAsciiPunctuation((char)codePoint);
            switch (CharUnicodeInfo.GetUnicodeCategory(codePoint))
            {
                case UnicodeCategory.ConnectorPunctuation:
                case UnicodeCategory.DashPunctuation:
                case UnicodeCategory.OpenPunctuation:
                case UnicodeCategory.ClosePunctuation:
                case UnicodeCategory.InitialQuotePunctuation:
                case UnicodeCategory.FinalQuotePunctuation:
                case UnicodeCategory.OtherPunctuation:
                case UnicodeCategory.MathSymbol:
                case UnicodeCategory.CurrencySymbol:
                case UnicodeCategory.ModifierSymbol:
                case UnicodeCategory.OtherSymbol:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>CommonMark Unicode whitespace: Zs, tab, LF, FF, CR.</summary>
        public static bool IsUnicodeWhitespace(int codePoint)
        {
            if (codePoint == ' ' || codePoint == '\t' || codePoint == '\n' || codePoint == '\f' || codePoint == '\r') return true;
            if (codePoint < 128) return false;
            return CharUnicodeInfo.GetUnicodeCategory(codePoint) == UnicodeCategory.SpaceSeparator;
        }

        /// <summary>HTML ASCII whitespace: tab, LF, FF, CR, space.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsHtmlWhitespace(char c) => c == ' ' || c == '\t' || c == '\n' || c == '\f' || c == '\r';

        /// <summary>Reads the code point ending at index-1 (handles surrogate pairs); -1 maps to newline.</summary>
        public static int CodePointBefore(string s, int index)
        {
            if (index <= 0) return '\n';
            char c = s[index - 1];
            if (char.IsLowSurrogate(c) && index >= 2 && char.IsHighSurrogate(s[index - 2]))
                return char.ConvertToUtf32(s[index - 2], c);
            return c;
        }

        /// <summary>Reads the code point starting at index; past the end maps to newline.</summary>
        public static int CodePointAt(string s, int index)
        {
            if (index >= s.Length) return '\n';
            char c = s[index];
            if (char.IsHighSurrogate(c) && index + 1 < s.Length && char.IsLowSurrogate(s[index + 1]))
                return char.ConvertToUtf32(c, s[index + 1]);
            return c;
        }

        /// <summary>Appends a code point, mapping invalid values and 0 to U+FFFD.</summary>
        public static void AppendCodePoint(StringBuilder sb, int cp)
        {
            if (cp == 0 || cp > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF))
                sb.Append('�');
            else if (cp < 0x10000)
                sb.Append((char)cp);
            else
                sb.Append(char.ConvertFromUtf32(cp));
        }

        /// <summary>Escapes text for an HTML text node (&amp; &lt; &gt; and optionally quotes).</summary>
        public static void EscapeHtml(OutputSink sink, ReadOnlySpan<char> text, bool escapeQuotes)
        {
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                string? rep = text[i] switch
                {
                    '&' => "&amp;",
                    '<' => "&lt;",
                    '>' => "&gt;",
                    '"' when escapeQuotes => "&quot;",
                    _ => null,
                };
                if (rep is null) continue;
                if (i > start) sink.Write(text.Slice(start, i - start));
                sink.Write(rep);
                start = i + 1;
            }
            if (start < text.Length) sink.Write(text.Slice(start));
        }
    }
}
