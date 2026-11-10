using Chuvadi.Markup.Internal;

namespace Chuvadi.Markup.Markdown.Parsing
{
    internal sealed partial class InlineParser
    {
        private char Before(int i) => i == 0 ? '\n' : _t[i - 1];

        /// <summary>
        /// Whether an extended autolink may start at <paramref name="i"/>: www. after the start of a line,
        /// whitespace, '*', '_', '~' or '('; a scheme after anything but an ASCII letter or digit. Email addresses
        /// are found later, on the finished text nodes (EmailAutolinks).
        /// </summary>
        private bool IsAutolinkTrigger(int i)
        {
            char c = _t[i];
            if (c != 'w' && c != 'h' && c != 'f' && c != 'H' && c != 'F') return false;
            char before = Before(i);
            // TRACE R-MD-0245
            // GFM 0.29 whitespace characters: space, tab, line feed, U+000B, U+000C, carriage return (D-157)
            if (c == 'w')
                return before == ' ' || before == '\t' || before == '\n' || before == '\v' || before == '\f' || before == '\r'
                    || before == '*' || before == '_' || before == '~' || before == '(';
            // TRACE R-MD-0201
            return !CharUtil.IsAsciiAlnum(before);
        }

        /// <summary>Tries an extended autolink at <paramref name="i"/>; on success adds it and moves past it.</summary>
        private bool TryExtendedAutolink(ref int i)
        {
            if (_brackets is not null) return false;
            char c = _t[i];
            int start = i, end = -1;
            string? destination = null;
            if (c == 'w')
            {
                if (!ExtendedAutolinks.Starts(_t, i, "www.")) return false;
                end = ExtendedAutolinks.WwwLink(_t, i);
                if (end > 0) destination = "http://" + _t.Substring(i, end - i);
            }
            else
            {
                int scheme = ExtendedAutolinks.SchemeLength(_t, i);
                if (scheme == 0) return false;
                end = ExtendedAutolinks.UrlLink(_t, i, scheme);
                if (end > 0) destination = _t.Substring(i, end - i);
            }
            if (destination is null || end <= start) return false;

            // TRACE R-MD-0203
            string text = _t.Substring(start, end - start);
            var link = new Link { Destination = destination, Style = LinkStyle.ExtendedAutolink, Span = SpanOf(start, end) };
            link.AppendChildUnchecked(new TextInline(text) { Span = SpanOf(start, end) });
            AddNode(link);
            i = end;
            return true;
        }
    }
}
