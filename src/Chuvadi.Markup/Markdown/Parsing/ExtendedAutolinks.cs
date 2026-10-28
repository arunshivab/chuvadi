using System;
using System.Globalization;
using Chuvadi.Markup.Internal;

namespace Chuvadi.Markup.Markdown.Parsing
{
    /// <summary>GFM extended autolinks (behaviour of cmark-gfm's autolink extension).</summary>
    internal static class ExtendedAutolinks
    {
        public static bool IsCSpace(char c) => c == ' ' || c == '\t' || c == '\n' || c == '\f' || c == '\r'; // cmark_isspace (no VT)

        private static bool IsAsciiPunct(char c) => c < 128 && CharUtil.IsAsciiPunctuation(c);

        private static bool IsValidHostChar(string s, int i)
        {
            int cp = CharUtil.CodePointAt(s, i);
            if (CharUtil.IsUnicodeWhitespace(cp)) return false;
            if (cp < 128) return !CharUtil.IsAsciiPunctuation((char)cp);
            switch (CharUnicodeInfo.GetUnicodeCategory(cp))
            {
                case UnicodeCategory.ConnectorPunctuation:
                case UnicodeCategory.DashPunctuation:
                case UnicodeCategory.OpenPunctuation:
                case UnicodeCategory.ClosePunctuation:
                case UnicodeCategory.InitialQuotePunctuation:
                case UnicodeCategory.FinalQuotePunctuation:
                case UnicodeCategory.OtherPunctuation:
                    return false;
                default:
                    return true;
            }
        }

        public static bool IsSafeScheme(string s, int start, int len)
        {
            foreach (var scheme in new[] { "http://", "https://", "ftp://" })
            {
                if (len > scheme.Length && string.Compare(s, start, scheme, 0, scheme.Length, StringComparison.OrdinalIgnoreCase) == 0
                    && IsValidHostChar(s, start + scheme.Length))
                    return true;
            }
            return false;
        }

        /// <summary>Returns the domain length (0 if invalid). data = s[start..start+size).</summary>
        public static int CheckDomain(string s, int start, int size, bool allowShort)
        {
            int i, np = 0, uscore1 = 0, uscore2 = 0;
            for (i = 1; i < size - 1; i++)
            {
                if (s[start + i] == '\\' && i < size - 2) i++;
                char c = s[start + i];
                if (c == '_') uscore2++;
                else if (c == '.')
                {
                    uscore1 = uscore2;
                    uscore2 = 0;
                    np++;
                }
                else if (!IsValidHostChar(s, start + i) && c != '-') break;
            }
            if ((uscore1 > 0 || uscore2 > 0) && np <= 10) return 0;
            if (allowShort) return i;
            return np > 0 ? i : 0;
        }

        /// <summary>Trims trailing punctuation from a candidate link of length linkEnd starting at start.</summary>
        public static int AutolinkDelim(string s, int start, int linkEnd)
        {
            int closing = 0, opening = 0;
            for (int i = 0; i < linkEnd; i++)
            {
                char c = s[start + i];
                if (c == '<') { linkEnd = i; break; }
                if (c == '(') opening++;
                else if (c == ')') closing++;
            }
            while (linkEnd > 0)
            {
                char c = s[start + linkEnd - 1];
                switch (c)
                {
                    case ')':
                        if (closing <= opening) return linkEnd;
                        closing--;
                        linkEnd--;
                        break;
                    case '?':
                    case '!':
                    case '.':
                    case ',':
                    case ':':
                    case '*':
                    case '_':
                    case '~':
                    case '\'':
                    case '"':
                        linkEnd--;
                        break;
                    case ';':
                    {
                        int newEnd = linkEnd - 2;
                        while (newEnd > 0 && CharUtil.IsAsciiAlpha(s[start + newEnd])) newEnd--;
                        if (newEnd < linkEnd - 2 && newEnd >= 0 && s[start + newEnd] == '&') linkEnd = newEnd;
                        else linkEnd--;
                        break;
                    }
                    default:
                        return linkEnd;
                }
            }
            return linkEnd;
        }

        private static bool ValidateProtocol(string protocol, string data, int at, int rewind, int maxRewind)
        {
            int len = protocol.Length;
            if (len > maxRewind - rewind) return false;
            int pstart = at - rewind - len;
            if (string.CompareOrdinal(data, pstart, protocol, 0, len) != 0) return false;
            if (len == maxRewind - rewind) return true;
            char prev = data[pstart - 1];
            return !CharUtil.IsAsciiAlnum(prev);
        }

        /// <summary>Links bare email addresses in text nodes that are not inside links.</summary>
        public static void ProcessEmails(InlineNode root, NodeBudget budget)
        {
            Consolidate(root);
            Walk(root, budget);
        }

        private static void Walk(InlineNode node, NodeBudget budget)
        {
            // iterative pre-order walk; skips link subtrees
            var stack = new System.Collections.Generic.Stack<InlineNode>();
            for (var c = node.Last; c is not null; c = c.Prev) stack.Push(c);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                if (n.Type == InlineType.Link) continue;
                if (n.Type == InlineType.Text)
                {
                    ProcessText(n, budget);
                    continue;
                }
                for (var c = n.Last; c is not null; c = c.Prev) stack.Push(c);
            }
        }

        private static void Consolidate(InlineNode node)
        {
            var stack = new System.Collections.Generic.Stack<InlineNode>();
            stack.Push(node);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                for (var c = n.First; c is not null; c = c.Next)
                {
                    if (c.Type == InlineType.Text && c.Next is not null && c.Next.Type == InlineType.Text)
                    {
                        var sb = new System.Text.StringBuilder(c.Literal);
                        while (c.Next is not null && c.Next.Type == InlineType.Text)
                        {
                            var nx = c.Next;
                            sb.Append(nx.Literal);
                            c.End = nx.End;
                            nx.Unlink();
                        }
                        c.Literal = sb.ToString();
                    }
                    else if (c.First is not null)
                    {
                        stack.Push(c);
                    }
                }
            }
        }

        private static void ProcessText(InlineNode text, NodeBudget budget)
        {
            string data = text.Literal;
            int baseStart = text.Start;
            int start = 0, offset = 0, remaining = data.Length;
            while (true)
            {
                if (offset >= remaining) break;
                int at = data.IndexOf('@', start + offset, remaining - offset);
                if (at < 0) break;
                int maxRewind = at - (start + offset);
                bool autoMailto = true, isXmpp = false;
                int rewind;
                found_at:
                for (rewind = 0; rewind < maxRewind; ++rewind)
                {
                    char c = data[start + offset + maxRewind - rewind - 1];
                    if (CharUtil.IsAsciiAlnum(c)) continue;
                    if (".+-_".IndexOf(c) >= 0) continue;
                    if (c == ':')
                    {
                        if (ValidateProtocol("mailto:", data, start + offset + maxRewind, rewind, maxRewind)) { autoMailto = false; continue; }
                        if (ValidateProtocol("xmpp:", data, start + offset + maxRewind, rewind, maxRewind)) { autoMailto = false; isXmpp = true; continue; }
                    }
                    break;
                }
                if (rewind == 0)
                {
                    offset += maxRewind + 1;
                    continue;
                }
                int linkEnd, np = 0;
                bool restart = false;
                for (linkEnd = 1; linkEnd < remaining - offset - maxRewind; ++linkEnd)
                {
                    char c = data[start + offset + maxRewind + linkEnd];
                    if (CharUtil.IsAsciiAlnum(c)) continue;
                    if (c == '@')
                    {
                        offset += maxRewind + 1;
                        maxRewind = linkEnd - 1;
                        restart = true;
                        break;
                    }
                    if (c == '.' && linkEnd < remaining - offset - maxRewind - 1 && CharUtil.IsAsciiAlnum(data[start + offset + maxRewind + linkEnd + 1]))
                        np++;
                    else if (c == '/' && isXmpp)
                        continue;
                    else if (c != '-' && c != '_')
                        break;
                }
                if (restart) goto found_at;
                char last = data[start + offset + maxRewind + linkEnd - 1];
                if (linkEnd < 2 || np == 0 || (!CharUtil.IsAsciiAlpha(last) && last != '.'))
                {
                    offset += maxRewind + linkEnd;
                    continue;
                }
                linkEnd = AutolinkDelim(data, start + offset + maxRewind, linkEnd);
                if (linkEnd == 0)
                {
                    offset += maxRewind + 1;
                    continue;
                }
                int emailStart = start + offset + maxRewind - rewind;
                int emailLen = linkEnd + rewind;
                string email = data.Substring(emailStart, emailLen);
                budget.Take();
                budget.Take();
                budget.Take();
                var link = new InlineNode(InlineType.Link)
                {
                    Destination = (autoMailto ? "mailto:" : string.Empty) + email,
                    Style = LinkStyle.ExtendedAutolink,
                    IsEmail = true,
                    Start = baseStart + emailStart,
                    End = baseStart + emailStart + emailLen,
                };
                link.Append(new InlineNode(InlineType.Text) { Literal = email, Start = link.Start, End = link.End });
                int postStart = emailStart + emailLen;
                var post = new InlineNode(InlineType.Text)
                {
                    Literal = data.Substring(postStart),
                    Start = baseStart + postStart,
                    End = text.End,
                };
                text.InsertAfter(link);
                link.InsertAfter(post);
                text.Literal = data.Substring(start, emailStart - start);
                text.End = baseStart + emailStart;
                text = post;
                int consumed = offset + maxRewind + linkEnd;
                start += consumed;
                remaining -= consumed;
                offset = 0;
            }
        }
    }
}
