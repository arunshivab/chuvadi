using System;
using Chuvadi.Markup.Internal;

namespace Chuvadi.Markup.Markdown.Parsing
{
    /// <summary>
    /// Scanners for GFM extended autolinks (GFM 0.29 "Autolinks (extension)"): www., http://, https:// and
    /// ftp:// links, mailto: and xmpp: addresses and bare email addresses, read on the raw content text.
    /// </summary>
    internal static class ExtendedAutolinks
    {
        public static bool Starts(string s, int i, string word)
            => s.Length - i >= word.Length && string.CompareOrdinal(s, i, word, 0, word.Length) == 0;

        private static bool StartsIgnoreCase(string s, int i, string word)
            => s.Length - i >= word.Length && string.Compare(s, i, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0;

        // TRACE R-MD-0249
        /// <summary>
        /// The length of an http://, https:// or ftp:// scheme at <paramref name="i"/>, in any case of its letters
        /// (D-232, N1: "HTTP://a" links), or 0.
        /// </summary>
        public static int SchemeLength(string s, int i)
        {
            if (StartsIgnoreCase(s, i, "http://")) return 7;
            if (StartsIgnoreCase(s, i, "https://")) return 8;
            if (StartsIgnoreCase(s, i, "ftp://")) return 6;
            return 0;
        }

        // TRACE R-MD-0205
        /// <summary>A char that may be part of a domain: not whitespace or a control char, not ASCII punctuation other than - _ .</summary>
        private static bool IsDomainChar(char c)
        {
            if (c <= ' ' || c == '\u007f') return false;
            if (c < 128 && CharUtil.IsAsciiPunctuation(c)) return c == '-' || c == '_' || c == '.';
            // TRACE R-MD-0250
            // a space separator (U+00A0 and the other Zs chars) is whitespace, not part of a domain (D-232, G2);
            // U+2028 (a line separator) stays a domain char, as the frozen output keeps it
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.SpaceSeparator) return false;
            return true;
        }

        // TRACE R-MD-0252
        /// <summary>
        /// End of the run of domain chars from <paramref name="i"/>. A backslash before '-', '_' or '.' that follows a
        /// letter, digit or period counts with the char it escapes, so "www.a.b\_" has the domain "a.b\_" and its
        /// underscore is seen (D-232, G3), while "http://-\_" has the domain "-" (D-242).
        /// </summary>
        private static int DomainEnd(string s, int i)
        {
            while (i < s.Length)
            {
                if (IsDomainChar(s[i])) i++;
                else if (s[i] == '\\' && i + 1 < s.Length && (s[i + 1] == '-' || s[i + 1] == '_' || s[i + 1] == '.')
                    && i > 0 && (char.IsLetterOrDigit(s[i - 1]) || s[i - 1] == '.')) i += 2;
                else break;
            }
            return i;
        }

        /// <summary>A char that may be the first of a domain: not ASCII punctuation, except '_' and '-'.</summary>
        private static bool MayStartDomain(char c) => !(c < 128 && CharUtil.IsAsciiPunctuation(c)) || c == '_' || c == '-';

        // TRACE R-MD-0206
        /// <summary>A valid domain: non-empty, with no underscore in its last two dot-separated segments.</summary>
        private static bool ValidDomain(string s, int start, int end, bool needsPeriod)
        {
            // TRACE R-MD-0251
            // the domain must start right after www. or the scheme with a domain char; ASCII punctuation other
            // than '_' and '-' (so '.' or an escape) is not allowed first (D-232, G2; "www._xxx.yyy.zzz" links)
            if (end <= start || !MayStartDomain(s[start])) return false;
            // a domain ends for this check before a period that follows another ("_..x1c.d" is checked as "_")
            int doubled = s.IndexOf("..", start, end - start, StringComparison.Ordinal);
            if (doubled >= 0) end = doubled;
            while (end > start && s[end - 1] == '.') end--;
            if (end <= start) return false;
            int lastDot = s.LastIndexOf('.', end - 1, end - start);
            if (needsPeriod && lastDot < 0) return false;
            int secondDot = lastDot > start ? s.LastIndexOf('.', lastDot - 1, lastDot - start) : -1;
            int checkFrom = secondDot >= 0 ? secondDot + 1 : start;
            return s.IndexOf('_', checkFrom, end - checkFrom) < 0;
        }

        // TRACE R-MD-0207
        /// <summary>End of the run from <paramref name="i"/> that stops at a space, tab, line ending, form feed or '&lt;'.</summary>
        private static int PathEnd(string s, int i)
        {
            while (i < s.Length && s[i] != '<' && s[i] != ' ' && s[i] != '\t' && s[i] != '\n' && s[i] != '\r' && s[i] != '\f') i++;
            return i;
        }

        /// <summary>End of a www. autolink at <paramref name="k"/>, or -1.</summary>
        public static int WwwLink(string s, int k)
        {
            // TRACE R-MD-0251
            if (k + 4 >= s.Length || !MayStartDomain(s[k + 4])) return -1;
            int domainEnd = DomainEnd(s, k);
            int end = Trim(s, k, PathEnd(s, k));
            if (end <= k + 4) return -1;
            // TRACE R-MD-0222
            return ValidDomain(s, k, domainEnd, needsPeriod: true) ? end : -1;
        }

        /// <summary>End of a scheme autolink at <paramref name="k"/>, or -1.</summary>
        public static int UrlLink(string s, int k, int schemeLength)
        {
            int domainStart = k + schemeLength;
            int domainEnd = DomainEnd(s, domainStart);
            if (domainEnd == domainStart) return -1;
            int end = Trim(s, k, PathEnd(s, k));
            if (end <= domainStart) return -1;
            return ValidDomain(s, domainStart, domainEnd, needsPeriod: false) ? end : -1;
        }

        // TRACE R-MD-0209
        /// <summary>A char of the local part of an email address.</summary>
        public static bool IsLocalChar(char c) => CharUtil.IsAsciiAlnum(c) || c == '.' || c == '-' || c == '_' || c == '+';

        // TRACE R-MD-0253
        /// <summary>
        /// End of an email domain at <paramref name="i"/>: alphanumeric, - and _ in segments separated by periods,
        /// at least one period, not ending in - or _; a final period is left out.
        /// </summary>
        public static int EmailDomain(string s, int i) => ScanEmailDomain(s, i, out int end) ? end : -1;

        /// <summary>
        /// Scans an email domain at <paramref name="i"/>; <paramref name="end"/> is where the scan stopped (after
        /// the label chars read), whether or not the domain is valid.
        /// </summary>
        public static bool ScanEmailDomain(string s, int i, out int end)
        {
            end = i;
            // TRACE R-MD-0253
            // labels of letters, digits, - and _ joined by single periods; the domain ends before a period
            // that does not start another label ("c.dwww..com" gives "c.dwww"); it may not start with a period
            // (D-232, N6)
            if (i >= s.Length || !IsLabelChar(s[i])) return false;
            int j = i;
            while (j < s.Length)
            {
                if (IsLabelChar(s[j])) j++;
                else if (s[j] == '.' && j + 1 < s.Length && IsLabelChar(s[j + 1])) j++;
                else break;
            }
            end = j;
            if (s.IndexOf('.', i, j - i) < 0) return false;
            char last = s[j - 1];
            return last != '-' && last != '_';
        }

        private static bool IsLabelChar(char c) => CharUtil.IsAsciiAlnum(c) || c == '-' || c == '_';

        // TRACE R-MD-0211
        /// <summary>
        /// Extended autolink path validation on [start, end): stop at '&lt;'; then repeatedly drop trailing
        /// ? ! . , : * _ ~, a trailing ')' without a matching '(', and an entity-like "&amp;name;" ending.
        /// </summary>
        public static int Trim(string s, int start, int end)
        {
            int lt = s.IndexOf('<', start, end - start);
            if (lt >= 0) end = lt;
            int open = 0, close = 0;
            for (int i = start; i < end; i++)
            {
                if (s[i] == '(') open++;
                else if (s[i] == ')') close++;
            }
            while (end > start)
            {
                char last = s[end - 1];
                // TRACE R-MD-0221
                if ("?!.,:*_~\"'".IndexOf(last) >= 0)
                {
                    end--;
                    continue;
                }
                if (last == ')' && close > open)
                {
                    end--;
                    close--;
                    continue;
                }
                if (last == ';')
                {
                    int j = end - 2;
                    while (j >= start && CharUtil.IsAsciiAlnum(s[j])) j--;
                    // TRACE R-MD-0248
                    // entity-like: '&', then an ASCII letter and more letters or digits; "&0;" is not (D-232, N4)
                    if (j >= start && j < end - 2 && s[j] == '&' && CharUtil.IsAsciiAlpha(s[j + 1]))
                    {
                        end = j;
                        continue;
                    }
                    // TRACE R-MD-0212
                    end--;
                    continue;
                }
                break;
            }
            return end;
        }
    }
}
