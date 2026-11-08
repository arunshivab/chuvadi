using System;
using System.Text;
using Chuvadi.Markup.Internal;

namespace Chuvadi.Markup.Markdown.Parsing
{
    /// <summary>Backslash escapes and entity / numeric character references.</summary>
    internal static class CharacterReferences
    {
        /// <summary>
        /// Length of the character reference starting at <paramref name="i"/> (which holds '&amp;'), or 0 if
        /// there is none; the decoded text goes to <paramref name="value"/>.
        /// </summary>
        public static int Match(string s, int i, int end, out string value)
        {
            value = string.Empty;
            int j = i + 1;
            if (j >= end) return 0;
            if (s[j] == '#')
            {
                j++;
                int code = 0;
                int digits = 0;
                if (j < end && (s[j] == 'x' || s[j] == 'X'))
                {
                    // TRACE R-MD-0005
                    j++;
                    while (j < end && CharUtil.IsAsciiHex(s[j]) && digits < 7)
                    {
                        char h = s[j];
                        int d = h <= '9' ? h - '0' : (h | 0x20) - 'a' + 10;
                        code = code * 16 + d;
                        digits++;
                        j++;
                    }
                    if (digits == 0 || digits > 6 || j >= end || s[j] != ';') return 0;
                }
                else
                {
                    // TRACE R-MD-0006
                    while (j < end && CharUtil.IsAsciiDigit(s[j]) && digits < 8)
                    {
                        code = code * 10 + (s[j] - '0');
                        digits++;
                        j++;
                    }
                    if (digits == 0 || digits > 7 || j >= end || s[j] != ';') return 0;
                }
                // TRACE R-MD-0007
                if (code == 0 || code > 0x10FFFF || (code >= 0xD800 && code <= 0xDFFF)) value = "�";
                else value = char.ConvertFromUtf32(code);
                return j + 1 - i;
            }

            // TRACE R-MD-0008
            int k = j;
            while (k < end && k - j < HtmlEntities.MaxNameLength && CharUtil.IsAsciiAlnum(s[k])) k++;
            if (k == j || k >= end || s[k] != ';') return 0;
            if (!HtmlEntities.TryGet(s.AsSpan(j, k + 1 - j), out var named)) return 0;
            value = named;
            return k + 1 - i;
        }

        // TRACE R-MD-0247
        /// <summary>
        /// Copies <paramref name="s"/> resolving character references only (no backslash escapes), as inside a URI
        /// autolink (D-232, G1): references are decoded in its text and its destination.
        /// </summary>
        public static string DecodeReferences(string s)
        {
            int first = s.IndexOf('&');
            if (first < 0) return s;
            var sb = new StringBuilder(s.Length);
            sb.Append(s, 0, first);
            for (int i = first; i < s.Length; i++)
            {
                if (s[i] == '&')
                {
                    int len = Match(s, i, s.Length, out string value);
                    if (len > 0)
                    {
                        sb.Append(value);
                        i += len - 1;
                        continue;
                    }
                }
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        // TRACE R-MD-0009
        /// <summary>Copies [start, end) resolving backslash escapes of ASCII punctuation and character references.</summary>
        public static string Resolve(string s, int start, int end)
        {
            int first = start;
            while (first < end && s[first] != '\\' && s[first] != '&' && s[first] != '\0') first++;
            if (first == end) return s.Substring(start, end - start);
            var sb = new StringBuilder(end - start);
            sb.Append(s, start, first - start);
            for (int i = first; i < end; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < end && CharUtil.IsAsciiPunctuation(s[i + 1]))
                {
                    sb.Append(s[i + 1]);
                    i++;
                }
                else if (c == '&')
                {
                    int len = Match(s, i, end, out var value);
                    if (len > 0)
                    {
                        sb.Append(value);
                        i += len - 1;
                    }
                    else sb.Append('&');
                }
                else sb.Append(SafeChar(c));
            }
            return sb.ToString();
        }

        // TRACE R-MD-0010
        /// <summary>U+0000 is replaced by U+FFFD.</summary>
        public static char SafeChar(char c) => c == '\0' ? '�' : c;

        /// <summary>Copies a string replacing U+0000 by U+FFFD.</summary>
        public static string Safe(string s) => s.IndexOf('\0') < 0 ? s : s.Replace('\0', '�');
    }
}
