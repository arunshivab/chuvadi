using System.Collections.Generic;
using Chuvadi.Markup.Internal;

namespace Chuvadi.Markup.Markdown.Parsing
{
    /// <summary>
    /// GFM extended email autolinks (GFM 0.29 "Autolinks (extension)": an email address is recognised within any
    /// text node), plus mailto: and xmpp: addresses, found on the finished text nodes of a block outside links.
    /// </summary>
    internal static class EmailAutolinks
    {
        // TRACE R-MD-0202
        /// <summary>Splits every text node outside a link around the email autolinks it holds.</summary>
        public static void Apply(MarkdownBlock block, WorkBudget work)
        {
            var stack = new Stack<MarkdownNode>();
            stack.Push(block);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                var kids = node.MutableChildren;
                List<MarkdownNode>? rebuilt = null;
                for (int k = 0; k < kids.Count; k++)
                {
                    var child = kids[k];
                    if (child is TextInline text && text.Text.IndexOf('@') >= 0)
                    {
                        var pieces = Split(text, work);
                        if (pieces is not null)
                        {
                            rebuilt ??= new List<MarkdownNode>(kids.GetRange(0, k));
                            rebuilt.AddRange(pieces);
                            continue;
                        }
                    }
                    else if (child is not Link && child.CanHaveChildren) stack.Push(child);
                    rebuilt?.Add(child);
                }
                if (rebuilt is not null) node.SetChildren(rebuilt);
            }
        }

        private static List<MarkdownNode>? Split(TextInline node, WorkBudget work)
        {
            string s = node.Text;
            int baseOffset = node.Span.Start;
            List<MarkdownNode>? result = null;
            int emitted = 0;
            int floor = 0;
            int k = 0;
            while (k < s.Length)
            {
                work.Step();
                if (s[k] != '@')
                {
                    k++;
                    continue;
                }
                // TRACE R-MD-0254
                // the local part, a prefix and the chars taken in before a prefix all stay at or after the floor:
                // the end of the last link, or of the domain scan of the last started address whose domain was
                // invalid (D-232, N6; see below)
                int localStart = k;
                while (localStart > floor && ExtendedAutolinks.IsLocalChar(s[localStart - 1])) localStart--;
                bool domainValid = ExtendedAutolinks.ScanEmailDomain(s, k + 1, out int end);
                bool domainOk = domainValid;
                // TRACE R-MD-0219
                if (domainOk && end < s.Length && s[end] == '@') domainOk = false;
                // TRACE R-MD-0220
                if (domainOk && char.IsDigit(s[end - 1])) domainOk = false;

                // TRACE R-MD-0254 (prefixes)
                // prefixes may repeat ("xmpp:+mailto:x@y.z"), each taking in the local-part chars before it; after
                // a prefix the local part may be empty ("mailto:@a.b")
                int start = localStart;
                bool prefixed = false, xmpp = false;
                while (true)
                {
                    int p = -1;
                    if (start - 7 >= floor && string.CompareOrdinal(s, start - 7, "mailto:", 0, 7) == 0 && PrefixMayStart(s, start - 7)) p = 7;
                    else if (start - 5 >= floor && string.CompareOrdinal(s, start - 5, "xmpp:", 0, 5) == 0 && PrefixMayStart(s, start - 5)) p = 5;
                    if (p < 0) break;
                    xmpp = p == 5;
                    prefixed = true;
                    start = ExtendBack(s, start - p, floor);
                }
                if (!domainOk || (localStart == k && !prefixed))
                {
                    // only an attempt that started (a local part or a prefix) and whose domain is itself invalid
                    // moves the floor past what its domain scan read, and not when that scan stopped at another '@'
                    if (!domainValid && end > k + 1 && (localStart < k || prefixed) && !(end < s.Length && s[end] == '@')) floor = end;
                    k++;
                    continue;
                }
                string destination;
                if (prefixed)
                {
                    if (xmpp && end < s.Length && s[end] == '/')
                    {
                        int resourceEnd = ExtendedAutolinks.Trim(s, start, ResourceEnd(s, end));
                        if (resourceEnd > end + 1) end = resourceEnd;
                    }
                    destination = s.Substring(start, end - start);
                }
                // TRACE R-MD-0230
                else if (FollowsPrefixedAddress(s, start, floor)) destination = s.Substring(start, end - start);
                else destination = "mailto:" + s.Substring(start, end - start);

                result ??= new List<MarkdownNode>();
                // TRACE R-MD-0217
                if (start > emitted)
                    result.Add(new TextInline(s.Substring(emitted, start - emitted)) { Span = new SourceSpan(baseOffset + emitted, start - emitted) });
                var link = new Link
                {
                    Destination = destination,
                    Style = LinkStyle.ExtendedAutolink,
                    IsEmail = true,
                    Span = new SourceSpan(baseOffset + start, end - start),
                };
                link.AppendChildUnchecked(new TextInline(s.Substring(start, end - start)) { Span = new SourceSpan(baseOffset + start, end - start) });
                result.Add(link);
                emitted = end;
                floor = end;
                k = end;
            }
            if (result is null) return null;
            if (emitted < s.Length)
            {
                // TRACE R-MD-0223
                int from = baseOffset + emitted;
                int to = node.Span.End > from ? node.Span.End : from;
                result.Add(new TextInline(s.Substring(emitted)) { Span = new SourceSpan(from, to - from) });
            }
            return result;
        }

        // TRACE R-MD-0218
        /// <summary>The start of a prefixed address also takes in local-part chars written before the prefix.</summary>
        private static int ExtendBack(string s, int i, int floor)
        {
            while (i > floor && ExtendedAutolinks.IsLocalChar(s[i - 1])) i--;
            return i;
        }

        /// <summary>
        /// An address written straight after an '@' that ends a rejected mailto: or xmpp: address
        /// ("mailto:a@b.ca@b.c") keeps its text as the destination, without a mailto: scheme added.
        /// </summary>
        private static bool FollowsPrefixedAddress(string s, int start, int floor)
        {
            int i = start;
            if (i <= floor || s[i - 1] != '@') return false;
            while (i > floor && (ExtendedAutolinks.IsLocalChar(s[i - 1]) || s[i - 1] == '@')) i--;
            return (i - 7 >= floor && string.CompareOrdinal(s, i - 7, "mailto:", 0, 7) == 0 && PrefixMayStart(s, i - 7))
                || (i - 5 >= floor && string.CompareOrdinal(s, i - 5, "xmpp:", 0, 5) == 0 && PrefixMayStart(s, i - 5));
        }

        /// <summary>A mailto: or xmpp: prefix may not follow an ASCII letter or digit.</summary>
        private static bool PrefixMayStart(string s, int i) => i == 0 || !CharUtil.IsAsciiAlnum(s[i - 1]);

        private static int ResourceEnd(string s, int i)
        {
            while (i < s.Length && s[i] != '<' && s[i] != ' ' && s[i] != '\t' && s[i] != '\n') i++;
            return i;
        }
    }
}
