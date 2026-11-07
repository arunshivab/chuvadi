using System;
using Chuvadi.Markup.Internal;

namespace Chuvadi.Markup.Markdown.Parsing
{
    /// <summary>The HTML tag grammar of CommonMark and the start / end conditions of HTML blocks.</summary>
    internal static class HtmlSyntax
    {
        // TRACE R-MD-0011
        private static readonly string[] s_literalContentTags = { "pre", "script", "style", "textarea" };

        // TRACE R-MD-0012
        private static readonly string[] s_blockTags =
        {
            "address", "article", "aside", "base", "basefont", "blockquote", "body", "caption", "center", "col",
            "colgroup", "dd", "details", "dialog", "dir", "div", "dl", "dt", "fieldset", "figcaption", "figure",
            "footer", "form", "frame", "frameset", "h1", "h2", "h3", "h4", "h5", "h6", "head", "header", "hr",
            "html", "iframe", "legend", "li", "link", "main", "menu", "menuitem", "nav", "noframes", "ol",
            "optgroup", "option", "p", "param", "search", "section", "summary", "table", "tbody", "td", "tfoot",
            "th", "thead", "title", "tr", "track", "ul",
        };

        private static bool StartsWithIgnoreCase(string s, int i, int end, string word)
        {
            if (end - i < word.Length) return false;
            for (int k = 0; k < word.Length; k++)
                if (CharUtil.AsciiLower(s[i + k]) != word[k]) return false;
            return true;
        }

        private static bool IsSpaceOrTab(char c) => c == ' ' || c == '\t';

        /// <summary>
        /// The HTML block type (1 to 7) whose start condition the text at <paramref name="i"/> meets, or 0.
        /// Type 7 is only considered when <paramref name="mayBeType7"/> is true (it cannot interrupt a paragraph).
        /// </summary>
        public static int BlockStart(string s, int i, int end, bool mayBeType7)
        {
            if (i >= end || s[i] != '<') return 0;
            int j = i + 1;

            // TRACE R-MD-0013
            foreach (var tag in s_literalContentTags)
            {
                if (StartsWithIgnoreCase(s, j, end, tag))
                {
                    int k = j + tag.Length;
                    if (k == end || IsSpaceOrTab(s[k]) || s[k] == '>') return 1;
                }
            }

            // TRACE R-MD-0014
            if (StartsWith(s, j, end, "!--")) return 2;
            // TRACE R-MD-0015
            if (j < end && s[j] == '?') return 3;
            // TRACE R-MD-0016
            if (j + 1 < end && s[j] == '!' && CharUtil.IsAsciiAlpha(s[j + 1])) return 4;
            // TRACE R-MD-0017
            if (StartsWith(s, j, end, "![CDATA[")) return 5;

            // TRACE R-MD-0018
            int n = j;
            if (n < end && s[n] == '/') n++;
            int nameStart = n;
            while (n < end && CharUtil.IsAsciiAlnum(s[n])) n++;
            if (n > nameStart)
            {
                var name = s.AsSpan(nameStart, n - nameStart);
                foreach (var tag in s_blockTags)
                {
                    if (name.Length == tag.Length && EqualsIgnoreCase(name, tag))
                    {
                        if (n == end || IsSpaceOrTab(s[n]) || s[n] == '>' || (s[n] == '/' && n + 1 < end && s[n + 1] == '>'))
                            return 6;
                        break;
                    }
                }
            }

            // TRACE R-MD-0019
            if (mayBeType7)
            {
                int tagEnd = OpenTag(s, i, end, allowLineEnding: false);
                if (tagEnd > 0)
                {
                    int ns = i + 1, ne = ns;
                    while (ne < end && (CharUtil.IsAsciiAlnum(s[ne]) || s[ne] == '-')) ne++;
                    var tagName = s.AsSpan(ns, ne - ns);
                    foreach (var tag in s_literalContentTags)
                        if (tagName.Length == tag.Length && EqualsIgnoreCase(tagName, tag)) return 0;
                }
                else tagEnd = ClosingTag(s, i, end, allowLineEnding: false);
                if (tagEnd > 0)
                {
                    int k = tagEnd;
                    while (k < end && IsSpaceOrTab(s[k])) k++;
                    if (k == end) return 7;
                }
            }
            return 0;
        }

        private static bool EqualsIgnoreCase(ReadOnlySpan<char> a, string lower)
        {
            for (int k = 0; k < a.Length; k++)
                if (CharUtil.AsciiLower(a[k]) != lower[k]) return false;
            return true;
        }

        private static bool StartsWith(string s, int i, int end, string word)
            => end - i >= word.Length && string.CompareOrdinal(s, i, word, 0, word.Length) == 0;

        /// <summary>True when the line [start, end) meets the end condition of an HTML block of type 1 to 5.</summary>
        public static bool BlockEnds(int type, string s, int start, int end)
        {
            switch (type)
            {
                case 1:
                    // TRACE R-MD-0020
                    for (int i = start; i + 2 < end; i++)
                    {
                        if (s[i] != '<' || s[i + 1] != '/') continue;
                        foreach (var tag in s_literalContentTags)
                            if (StartsWithIgnoreCase(s, i + 2, end, tag) && i + 2 + tag.Length < end && s[i + 2 + tag.Length] == '>')
                                return true;
                    }
                    return false;
                case 2:
                    // TRACE R-MD-0021
                    return s.AsSpan(start, end - start).IndexOf("-->", StringComparison.Ordinal) >= 0;
                case 3:
                    // TRACE R-MD-0022
                    return s.AsSpan(start, end - start).IndexOf("?>", StringComparison.Ordinal) >= 0;
                case 4:
                    // TRACE R-MD-0023
                    return s.AsSpan(start, end - start).IndexOf('>') >= 0;
                case 5:
                    // TRACE R-MD-0024
                    return s.AsSpan(start, end - start).IndexOf("]]>", StringComparison.Ordinal) >= 0;
                default:
                    return false;
            }
        }

        // TRACE R-MD-0025
        /// <summary>Skips spaces and tabs and at most one line ending; returns the new index.</summary>
        private static int SkipTagWhitespace(string s, int i, int end, bool allowLineEnding)
        {
            bool sawLineEnding = false;
            while (i < end)
            {
                char c = s[i];
                if (c == ' ' || c == '\t') i++;
                else if (allowLineEnding && !sawLineEnding && (c == '\n' || c == '\r'))
                {
                    sawLineEnding = true;
                    i += c == '\r' && i + 1 < end && s[i + 1] == '\n' ? 2 : 1;
                }
                else break;
            }
            return i;
        }

        // TRACE R-MD-0026
        private static int TagName(string s, int i, int end)
        {
            if (i >= end || !CharUtil.IsAsciiAlpha(s[i])) return -1;
            i++;
            while (i < end && (CharUtil.IsAsciiAlnum(s[i]) || s[i] == '-')) i++;
            return i;
        }

        /// <summary>End index of an open tag starting at <paramref name="i"/> ('&lt;'), or -1.</summary>
        public static int OpenTag(string s, int i, int end, bool allowLineEnding, QuoteFinder? quotes = null)
        {
            int k = TagName(s, i + 1, end);
            if (k < 0) return -1;
            while (true)
            {
                int afterSpace = SkipTagWhitespace(s, k, end, allowLineEnding);
                if (afterSpace < end && (s[afterSpace] == '>' || s[afterSpace] == '/'))
                {
                    // TRACE R-MD-0027
                    if (s[afterSpace] == '>') return afterSpace + 1;
                    return afterSpace + 1 < end && s[afterSpace + 1] == '>' ? afterSpace + 2 : -1;
                }
                if (afterSpace == k) return -1;
                // TRACE R-MD-0028
                int a = afterSpace;
                if (a >= end || !(CharUtil.IsAsciiAlpha(s[a]) || s[a] == '_' || s[a] == ':')) return -1;
                a++;
                while (a < end && (CharUtil.IsAsciiAlnum(s[a]) || s[a] == '_' || s[a] == '.' || s[a] == ':' || s[a] == '-')) a++;
                k = a;
                // TRACE R-MD-0029
                int eq = SkipTagWhitespace(s, a, end, allowLineEnding);
                if (eq < end && s[eq] == '=')
                {
                    int v = SkipTagWhitespace(s, eq + 1, end, allowLineEnding);
                    if (v >= end) return -1;
                    char q = s[v];
                    if (q == '"' || q == '\'')
                    {
                        int close = quotes is not null ? quotes.Find(s, q, v + 1, end) : s.IndexOf(q, v + 1, end - v - 1);
                        if (close < 0) return -1;
                        k = close + 1;
                    }
                    else
                    {
                        int u = v;
                        while (u < end && " \t\n\r\"'=<>`".IndexOf(s[u]) < 0) u++;
                        if (u == v) return -1;
                        k = u;
                    }
                }
            }
        }

        // TRACE R-MD-0030
        /// <summary>End index of a closing tag starting at <paramref name="i"/> ('&lt;'), or -1.</summary>
        public static int ClosingTag(string s, int i, int end, bool allowLineEnding)
        {
            if (i + 1 >= end || s[i + 1] != '/') return -1;
            int k = TagName(s, i + 2, end);
            if (k < 0) return -1;
            k = SkipTagWhitespace(s, k, end, allowLineEnding);
            return k < end && s[k] == '>' ? k + 1 : -1;
        }
    }
}
