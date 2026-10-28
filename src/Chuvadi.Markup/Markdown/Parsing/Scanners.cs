using System;
using System.Collections.Generic;
using System.Text;
using Chuvadi.Markup.Internal;

namespace Chuvadi.Markup.Markdown.Parsing
{
    /// <summary>Hand-written equivalents of the cmark re2c scanners. All return an end index (exclusive) or -1.</summary>
    internal static class Scanners
    {
        private static bool IsSpaceOrTab(char c) => c == ' ' || c == '\t';

        private static bool IsWs(char c) => c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f' || c == '\v';

        private static char At(string s, int i) => i < s.Length ? s[i] : '\0';

        private static int SkipWs(string s, int i)
        {
            while (i < s.Length && IsWs(s[i])) i++;
            return i;
        }

        private static int ScanTagName(string s, int i)
        {
            if (i >= s.Length || !CharUtil.IsAsciiAlpha(s[i])) return -1;
            i++;
            while (i < s.Length && (CharUtil.IsAsciiAlnum(s[i]) || s[i] == '-')) i++;
            return i;
        }

        private static int ScanAttribute(string s, int i)
        {
            // requires at least one whitespace before
            int j = i;
            if (j >= s.Length || !IsWs(s[j])) return -1;
            j = SkipWs(s, j);
            if (j >= s.Length) return -1;
            char c = s[j];
            if (!(CharUtil.IsAsciiAlpha(c) || c == '_' || c == ':')) return -1;
            j++;
            while (j < s.Length && (CharUtil.IsAsciiAlnum(s[j]) || s[j] == '_' || s[j] == '.' || s[j] == ':' || s[j] == '-')) j++;
            // optional value spec
            int k = SkipWs(s, j);
            if (k < s.Length && s[k] == '=')
            {
                k = SkipWs(s, k + 1);
                if (k >= s.Length) return -1;
                char q = s[k];
                if (q == '"' || q == '\'')
                {
                    int e = s.IndexOf(q, k + 1);
                    if (e < 0) return -1;
                    return e + 1;
                }
                int st = k;
                while (k < s.Length)
                {
                    char v = s[k];
                    if (IsWs(v) || v == '"' || v == '\'' || v == '=' || v == '<' || v == '>' || v == '`') break;
                    k++;
                }
                if (k == st) return -1;
                return k;
            }
            return j;
        }

        /// <summary>Open tag starting at '&lt;'.</summary>
        public static int OpenTag(string s, int i)
        {
            if (At(s, i) != '<') return -1;
            int j = ScanTagName(s, i + 1);
            if (j < 0) return -1;
            while (true)
            {
                int a = ScanAttribute(s, j);
                if (a < 0) break;
                j = a;
            }
            j = SkipWs(s, j);
            if (At(s, j) == '/') j++;
            if (At(s, j) != '>') return -1;
            return j + 1;
        }

        /// <summary>Closing tag starting at '&lt;'.</summary>
        public static int ClosingTag(string s, int i)
        {
            if (At(s, i) != '<' || At(s, i + 1) != '/') return -1;
            int j = ScanTagName(s, i + 2);
            if (j < 0) return -1;
            j = SkipWs(s, j);
            if (At(s, j) != '>') return -1;
            return j + 1;
        }

        public static string? TagNameAt(string s, int i)
        {
            int st = i;
            int e = ScanTagName(s, i);
            return e < 0 ? null : s.Substring(st, e - st);
        }

        // ---- block-level scanners (s is a line without its line ending) ----

        public static int AtxHeadingStart(string s, int i, out int level)
        {
            level = 0;
            int j = i;
            while (j < s.Length && s[j] == '#' && level < 7) { j++; level++; }
            if (level == 0 || level > 6) return -1;
            if (j == s.Length) return j;
            if (!IsSpaceOrTab(s[j])) return -1;
            while (j < s.Length && IsSpaceOrTab(s[j])) j++;
            return j;
        }

        public static int OpenCodeFence(string s, int i, out char fenceChar, out int fenceLength)
        {
            fenceChar = At(s, i);
            fenceLength = 0;
            if (fenceChar != '`' && fenceChar != '~') return -1;
            int j = i;
            while (j < s.Length && s[j] == fenceChar) j++;
            fenceLength = j - i;
            if (fenceLength < 3) return -1;
            if (fenceChar == '`' && s.IndexOf('`', j) >= 0) return -1;
            return j;
        }

        public static int CloseCodeFence(string s, int i, char fenceChar, int minLength)
        {
            int j = i;
            while (j < s.Length && s[j] == fenceChar) j++;
            if (j - i < minLength) return -1;
            int k = j;
            while (k < s.Length && IsSpaceOrTab(s[k])) k++;
            return k == s.Length ? j - i : -1;
        }

        public static int SetextHeadingLine(string s, int i)
        {
            char c = At(s, i);
            if (c != '=' && c != '-') return 0;
            int j = i;
            while (j < s.Length && s[j] == c) j++;
            while (j < s.Length && IsSpaceOrTab(s[j])) j++;
            if (j != s.Length) return 0;
            return c == '=' ? 1 : 2;
        }

        public static bool ThematicBreak(string s, int i, out char marker)
        {
            marker = At(s, i);
            if (marker != '*' && marker != '-' && marker != '_') return false;
            int count = 0;
            for (int j = i; j < s.Length; j++)
            {
                char c = s[j];
                if (c == marker) count++;
                else if (!IsSpaceOrTab(c)) return false;
            }
            return count >= 3;
        }

        private static readonly HashSet<string> s_blockTags = new(StringComparer.OrdinalIgnoreCase)
        {
            "address", "article", "aside", "base", "basefont", "blockquote", "body", "caption", "center", "col",
            "colgroup", "dd", "details", "dialog", "dir", "div", "dl", "dt", "fieldset", "figcaption", "figure",
            "footer", "form", "frame", "frameset", "h1", "h2", "h3", "h4", "h5", "h6", "head", "header", "hr",
            "html", "iframe", "legend", "li", "link", "main", "menu", "menuitem", "nav", "noframes", "ol",
            "optgroup", "option", "p", "param", "search", "section", "summary", "table", "tbody", "td", "tfoot",
            "th", "thead", "title", "tr", "track", "ul",
        };

        private static bool StartsWithIgnoreCase(string s, int i, string prefix)
            => i + prefix.Length <= s.Length && string.Compare(s, i, prefix, 0, prefix.Length, StringComparison.OrdinalIgnoreCase) == 0;

        /// <summary>HTML block start types 1 to 6 (0 if none).</summary>
        public static int HtmlBlockStart(string s, int i)
        {
            if (At(s, i) != '<') return 0;
            foreach (var t in new[] { "pre", "script", "style", "textarea" })
            {
                if (StartsWithIgnoreCase(s, i + 1, t))
                {
                    char c = At(s, i + 1 + t.Length);
                    if (c == ' ' || c == '\t' || c == '>' || i + 1 + t.Length == s.Length) return 1;
                }
            }
            if (string.CompareOrdinal(s, i, "<!--", 0, 4) == 0) return 2;
            if (At(s, i + 1) == '?') return 3;
            if (At(s, i + 1) == '!' && CharUtil.IsAsciiAlpha(At(s, i + 2))) return 4;
            if (string.CompareOrdinal(s, i, "<![CDATA[", 0, 9) == 0) return 5;
            int j = i + 1;
            if (At(s, j) == '/') j++;
            int st = j;
            while (j < s.Length && CharUtil.IsAsciiAlnum(s[j])) j++;
            if (j > st && s_blockTags.Contains(s.Substring(st, j - st)))
            {
                char c = At(s, j);
                if (j == s.Length || c == ' ' || c == '\t' || c == '>' || (c == '/' && At(s, j + 1) == '>'))
                    return 6;
            }
            return 0;
        }

        /// <summary>HTML block start type 7.</summary>
        public static bool HtmlBlockStart7(string s, int i)
        {
            int e = OpenTag(s, i);
            if (e >= 0)
            {
                string? name = TagNameAt(s, i + 1);
                if (name is not null && (name.Equals("pre", StringComparison.OrdinalIgnoreCase) || name.Equals("script", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("style", StringComparison.OrdinalIgnoreCase) || name.Equals("textarea", StringComparison.OrdinalIgnoreCase)))
                    return false;
            }
            else
            {
                e = ClosingTag(s, i);
            }
            if (e < 0) return false;
            while (e < s.Length && IsSpaceOrTab(s[e])) e++;
            return e == s.Length;
        }

        public static bool HtmlBlockEnd(int type, string s, int i)
        {
            switch (type)
            {
                case 1:
                    return s.IndexOf("</pre>", i, StringComparison.OrdinalIgnoreCase) >= 0
                        || s.IndexOf("</script>", i, StringComparison.OrdinalIgnoreCase) >= 0
                        || s.IndexOf("</style>", i, StringComparison.OrdinalIgnoreCase) >= 0
                        || s.IndexOf("</textarea>", i, StringComparison.OrdinalIgnoreCase) >= 0;
                case 2: return s.IndexOf("-->", i, StringComparison.Ordinal) >= 0;
                case 3: return s.IndexOf("?>", i, StringComparison.Ordinal) >= 0;
                case 4: return s.IndexOf('>', i) >= 0;
                case 5: return s.IndexOf("]]>", i, StringComparison.Ordinal) >= 0;
                default: return false;
            }
        }

        // ---- inline scanners (s is the whole subject) ----

        /// <summary>Autolink URI after '&lt;' (i points at the scheme); returns index after '&gt;'.</summary>
        public static int AutolinkUri(string s, int i)
        {
            int j = i;
            if (j >= s.Length || !CharUtil.IsAsciiAlpha(s[j])) return -1;
            j++;
            while (j < s.Length && (CharUtil.IsAsciiAlnum(s[j]) || s[j] == '+' || s[j] == '.' || s[j] == '-')) j++;
            int schemeLen = j - i;
            if (schemeLen < 2 || schemeLen > 32 || At(s, j) != ':') return -1;
            j++;
            while (j < s.Length)
            {
                char c = s[j];
                if (c == '>') return j + 1;
                if (c == '<' || c <= ' ' || c == '\u007f') return -1;
                j++;
            }
            return -1;
        }

        /// <summary>Autolink email after '&lt;'; returns index after '&gt;'.</summary>
        public static int AutolinkEmail(string s, int i)
        {
            int j = i;
            const string localExtra = ".!#$%&'*+/=?^_`{|}~-";
            while (j < s.Length && (CharUtil.IsAsciiAlnum(s[j]) || localExtra.IndexOf(s[j]) >= 0)) j++;
            if (j == i || At(s, j) != '@') return -1;
            j++;
            while (true)
            {
                // label: [a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?
                int st = j;
                if (j >= s.Length || !CharUtil.IsAsciiAlnum(s[j])) return -1;
                j++;
                while (j < s.Length && (CharUtil.IsAsciiAlnum(s[j]) || s[j] == '-')) j++;
                if (s[j - 1] == '-' || j - st > 63) return -1;
                if (At(s, j) == '.') { j++; continue; }
                break;
            }
            return At(s, j) == '>' ? j + 1 : -1;
        }

        /// <summary>
        /// Link destination for inline links and definitions. Returns the end index or -1; sets
        /// <paramref name="raw"/> to the destination source text (without angle brackets).
        /// </summary>
        public static int LinkDestination(string s, int i, out string raw)
        {
            raw = string.Empty;
            if (At(s, i) == '<')
            {
                int j = i + 1;
                while (j < s.Length)
                {
                    char c = s[j];
                    if (c == '>') { raw = s.Substring(i + 1, j - i - 1); return j + 1; }
                    if (c == '\\' && j + 1 < s.Length && CharUtil.IsAsciiPunctuation(s[j + 1])) { j += 2; continue; }
                    if (c == '\n' || c == '\r' || c == '<') return -1;
                    j++;
                }
                return -1;
            }
            int k = i;
            int depth = 0;
            while (k < s.Length)
            {
                char c = s[k];
                if (c == '\\' && k + 1 < s.Length && CharUtil.IsAsciiPunctuation(s[k + 1])) { k += 2; continue; }
                if (c == '(')
                {
                    depth++;
                    if (depth > 32) return -1;
                }
                else if (c == ')')
                {
                    if (depth == 0) break;
                    depth--;
                }
                else if (c <= ' ' || c == '\u007f') break;
                k++;
            }
            if (k == i || depth != 0) return k == i && depth == 0 ? -2 : -1; // -2: empty raw destination
            raw = s.Substring(i, k - i);
            return k;
        }

        /// <summary>Link title (quoted or parenthesized); returns end index or -1. <paramref name="raw"/> excludes delimiters.</summary>
        public static int LinkTitle(string s, int i, out string raw)
        {
            raw = string.Empty;
            char open = At(s, i);
            char close;
            if (open == '"') close = '"';
            else if (open == '\'') close = '\'';
            else if (open == '(') close = ')';
            else return -1;
            int j = i + 1;
            while (j < s.Length)
            {
                char c = s[j];
                if (c == '\\' && j + 1 < s.Length && CharUtil.IsAsciiPunctuation(s[j + 1])) { j += 2; continue; }
                if (c == close) { raw = s.Substring(i + 1, j - i - 1); return j + 1; }
                if (open == '(' && c == '(') return -1;
                j++;
            }
            return -1;
        }

        /// <summary>Spaces, tabs and up to one line ending (spnl). Returns the new index.</summary>
        public static int SpaceNewlineSpace(string s, int i)
        {
            while (i < s.Length && IsSpaceOrTab(s[i])) i++;
            if (i < s.Length && (s[i] == '\n' || s[i] == '\r'))
            {
                if (s[i] == '\r' && At(s, i + 1) == '\n') i++;
                i++;
                while (i < s.Length && IsSpaceOrTab(s[i])) i++;
            }
            return i;
        }

        /// <summary>Resolves backslash escapes and entity references (for destinations, titles, info strings).</summary>
        public static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0 && s.IndexOf('&') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length && CharUtil.IsAsciiPunctuation(s[i + 1]))
                {
                    sb.Append(s[i + 1]);
                    i++;
                }
                else if (c == '&')
                {
                    int e = EntityReference(s, i, sb);
                    if (e < 0) sb.Append('&');
                    else i = e - 1;
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Parses an entity or numeric character reference at '&amp;' (CommonMark rules: semicolon
        /// required). Appends the decoded text and returns the end index, or -1.
        /// </summary>
        public static int EntityReference(string s, int i, StringBuilder sb)
        {
            int j = i + 1;
            if (At(s, j) == '#')
            {
                j++;
                bool hex = At(s, j) == 'x' || At(s, j) == 'X';
                if (hex) j++;
                int st = j;
                int max = hex ? 6 : 7;
                int cp = 0;
                while (j < s.Length && j - st < max && (hex ? CharUtil.IsAsciiHex(s[j]) : CharUtil.IsAsciiDigit(s[j])))
                {
                    cp = cp * (hex ? 16 : 10) + HexVal(s[j]);
                    j++;
                }
                if (j == st || At(s, j) != ';') return -1;
                CharUtil.AppendCodePoint(sb, cp);
                return j + 1;
            }
            int ns = j;
            while (j < s.Length && j - ns <= HtmlEntities.MaxNameLength && CharUtil.IsAsciiAlnum(s[j])) j++;
            if (j == ns || At(s, j) != ';') return -1;
            if (!HtmlEntities.TryGet(s.AsSpan(ns, j - ns + 1), out var value)) return -1;
            sb.Append(value);
            return j + 1;
        }

        private static int HexVal(char c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;
    }
}
