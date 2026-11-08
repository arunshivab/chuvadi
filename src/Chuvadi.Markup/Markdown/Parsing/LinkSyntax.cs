using Chuvadi.Markup.Internal;

namespace Chuvadi.Markup.Markdown.Parsing
{
    /// <summary>Scanners for link labels, link destinations and link titles (CommonMark "Links").</summary>
    internal static class LinkSyntax
    {
        // TRACE R-MD-0031
        public const int MaxLabelChars = 999;

        /// <summary>
        /// Scans a link label starting at <paramref name="i"/> ('['). Returns the index after ']' or -1.
        /// The label must have a non-whitespace char, no unescaped brackets and at most 999 chars inside.
        /// </summary>
        public static int Label(string s, int i, int end)
        {
            if (i >= end || s[i] != '[') return -1;
            bool hasContent = false;
            int k = i + 1;
            while (k < end)
            {
                char c = s[k];
                // TRACE R-MD-0032
                if (c == ']')
                {
                    if (!hasContent || k - (i + 1) > MaxLabelChars) return -1;
                    return k + 1;
                }
                if (c == '[') return -1;
                if (c == '\\' && k + 1 < end && CharUtil.IsAsciiPunctuation(s[k + 1]))
                {
                    hasContent = true;
                    k += 2;
                    continue;
                }
                if (c != ' ' && c != '\t' && c != '\n' && c != '\r') hasContent = true;
                k++;
                if (k - (i + 1) > MaxLabelChars) return -1;
            }
            return -1;
        }

        /// <summary>
        /// Scans a link destination at <paramref name="i"/>. Returns the end index (or -1); the content
        /// without angle brackets is [contentStart, contentEnd).
        /// </summary>
        public static int Destination(string s, int i, int end, out int contentStart, out int contentEnd)
        {
            contentStart = contentEnd = i;
            if (i >= end) return -1;
            if (s[i] == '<')
            {
                // TRACE R-MD-0033
                int k = i + 1;
                while (k < end)
                {
                    char c = s[k];
                    if (c == '>')
                    {
                        contentStart = i + 1;
                        contentEnd = k;
                        return k + 1;
                    }
                    if (c == '<' || c == '\n' || c == '\r') return -1;
                    if (c == '\\' && k + 1 < end && CharUtil.IsAsciiPunctuation(s[k + 1])) k += 2;
                    else k++;
                }
                return -1;
            }

            // TRACE R-MD-0034
            int depth = 0;
            int j = i;
            while (j < end)
            {
                char c = s[j];
                if (c == '\\' && j + 1 < end && CharUtil.IsAsciiPunctuation(s[j + 1]))
                {
                    j += 2;
                    continue;
                }
                if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    if (depth == 0) break;
                    depth--;
                }
                else if (c <= ' ' || c == '\u007f')
                {
                    break;
                }
                j++;
            }
            if (j == i || depth != 0) return -1;
            contentStart = i;
            contentEnd = j;
            return j;
        }

        /// <summary>
        /// Scans a link title at <paramref name="i"/> ('"', '\'' or '('). Returns the end index or -1;
        /// the content is [contentStart, contentEnd). A title may not contain a blank line.
        /// </summary>
        public static int Title(string s, int i, int end, out int contentStart, out int contentEnd)
        {
            contentStart = contentEnd = i;
            if (i >= end) return -1;
            // TRACE R-MD-0035
            char open = s[i];
            char close;
            if (open == '"' || open == '\'') close = open;
            else if (open == '(') close = ')';
            else return -1;
            int k = i + 1;
            while (k < end)
            {
                char c = s[k];
                if (c == close)
                {
                    contentStart = i + 1;
                    contentEnd = k;
                    return k + 1;
                }
                if (open == '(' && c == '(') return -1;
                if (c == '\\' && k + 1 < end && CharUtil.IsAsciiPunctuation(s[k + 1]))
                {
                    k += 2;
                    continue;
                }
                // TRACE R-MD-0036
                if (c == '\n' && IsBlankLineAhead(s, k + 1, end)) return -1;
                k++;
            }
            return -1;
        }

        private static bool IsBlankLineAhead(string s, int i, int end)
        {
            while (i < end && (s[i] == ' ' || s[i] == '\t')) i++;
            return i >= end || s[i] == '\n';
        }
    }
}
