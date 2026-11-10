using Chuvadi.Markup.Internal;

namespace Chuvadi.Markup.Markdown.Parsing
{
    /// <summary>Two syntax checks that code outside the parser also uses.</summary>
    internal static class MarkdownSyntax
    {
        // TRACE R-MD-0231
        /// <summary>
        /// True when "&lt;" + <paramref name="body"/> + "&gt;" is a URI autolink (CommonMark "Autolinks":
        /// an absolute URI). Used by MarkdownWriter to decide whether a link can be written as an autolink.
        /// The caller has already rejected bodies containing '&lt;', '&gt;', space, CR, LF or tab.
        /// </summary>
        internal static bool IsUriAutolinkBody(string body)
        {
            if (body.Length == 0 || !CharUtil.IsAsciiAlpha(body[0])) return false;
            int k = 1;
            while (k < body.Length && (CharUtil.IsAsciiAlnum(body[k]) || body[k] == '+' || body[k] == '.' || body[k] == '-')) k++;
            if (k < 2 || k > 32 || k >= body.Length || body[k] != ':') return false;
            for (int i = k + 1; i < body.Length; i++)
            {
                char c = body[i];
                if (c <= ' ' || c == '\u007f' || c == '<' || c == '>') return false;
            }
            return true;
        }

        // TRACE R-MD-0232
        /// <summary>
        /// GFM "Autolinks (extension)": the length of the extended autolink that starts at
        /// <paramref name="start"/>, given a candidate of <paramref name="length"/> characters that
        /// already stops at whitespace, '&lt;', '&gt;', '"' and U+FFFD. Applies the extended autolink path
        /// validation (trailing punctuation, unbalanced closing parentheses, an entity-like suffix ending
        /// in ';') and stops at a '&lt;'. Used by PlainText for links in plain text.
        /// </summary>
        internal static int TrimExtendedAutolink(string text, int start, int length)
            => ExtendedAutolinks.Trim(text, start, start + length) - start;
    }
}
