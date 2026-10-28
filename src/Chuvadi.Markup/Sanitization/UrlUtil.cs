using System;
using System.Text;

namespace Chuvadi.Markup.Sanitization
{
    internal static class UrlUtil
    {
        /// <summary>WHATWG URL preprocessing: trim leading/trailing C0 controls and spaces, remove tabs and newlines.</summary>
        public static string Normalize(string url)
        {
            int s = 0, e = url.Length;
            while (s < e && url[s] <= ' ') s++;
            while (e > s && url[e - 1] <= ' ') e--;
            var span = url.AsSpan(s, e - s);
            if (span.IndexOfAny('\t', '\n', '\r') < 0) return span.ToString();
            var sb = new StringBuilder(span.Length);
            foreach (char c in span)
                if (c != '\t' && c != '\n' && c != '\r') sb.Append(c);
            return sb.ToString();
        }

        /// <summary>
        /// The scheme a URL would have if every control (C0 and C1), whitespace and format character
        /// (zero-width space, BOM, soft hyphen ...) were removed. Browsers treat such URLs as relative, but
        /// other consumers of the output might not, so a relative URL that hides a scheme is rejected.
        /// </summary>
        public static string? LenientScheme(string normalized) => Scheme(LenientPrefix(normalized));

        /// <summary>
        /// True if the URL starts with two slashes or backslashes in any mix once control, whitespace and
        /// format characters are removed (a network-path reference in some consumer).
        /// </summary>
        public static bool LenientNetworkPath(string normalized)
        {
            string p = LenientPrefix(normalized);
            return p.Length >= 2 && (p[0] is '/' or '\\') && (p[1] is '/' or '\\');
        }

        private static string LenientPrefix(string normalized)
        {
            var sb = new StringBuilder(Math.Min(normalized.Length, 64));
            foreach (char c in normalized)
            {
                if (char.IsControl(c) || char.IsWhiteSpace(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format) continue;
                sb.Append(c);
                if (c == ':' || sb.Length >= 64) break;
            }
            return sb.ToString();
        }

        /// <summary>Lowercase scheme of an absolute URL, or null for a relative URL.</summary>
        public static string? Scheme(string normalized)
        {
            if (normalized.Length == 0 || !char.IsAsciiLetter(normalized[0])) return null;
            for (int i = 1; i < normalized.Length; i++)
            {
                char c = normalized[i];
                if (c == ':') return normalized.Substring(0, i).ToLowerInvariant();
                if (!(char.IsAsciiLetterOrDigit(c) || c == '+' || c == '-' || c == '.')) return null;
            }
            return null;
        }

        /// <summary>Parses a data: URL's MIME type (lowercase, parameters removed) and its decoded length estimate.</summary>
        public static bool TryParseData(string normalized, out string mime, out long decodedLength)
        {
            mime = string.Empty;
            decodedLength = 0;
            if (!normalized.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return false;
            int comma = normalized.IndexOf(',', 5);
            if (comma < 0) return false;
            string header = normalized.Substring(5, comma - 5);
            int semi = header.IndexOf(';');
            mime = (semi < 0 ? header : header.Substring(0, semi)).Trim().ToLowerInvariant();
            if (mime.Length == 0) mime = "text/plain";
            bool base64 = header.EndsWith(";base64", StringComparison.OrdinalIgnoreCase) || header.Contains(";base64;", StringComparison.OrdinalIgnoreCase);
            long dataLen = normalized.Length - comma - 1;
            decodedLength = base64 ? dataLen * 3 / 4 : dataLen;
            return true;
        }
    }
}
