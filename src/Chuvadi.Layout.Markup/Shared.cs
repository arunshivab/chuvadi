using System;
using System.Collections.Generic;
using Chuvadi.Layout.Model;

namespace Chuvadi.Layout.Markup
{
    /// <summary>Pieces shared by the Markdown and HTML importers.</summary>
    internal static class Shared
    {
        /// <summary>A footer paragraph: <paramref name="text"/> with "{page}" and "{pages}" replaced by page-number fields.</summary>
        public static FlowParagraph PageFooter(string? text, CharacterFormat f)
        {
            FlowParagraph p = new FlowParagraph { Format = new ParagraphFormat { Alignment = ParagraphAlignment.Center }, MarkFormat = f };
            text ??= string.Empty;
            int i = 0;
            while (i < text.Length)
            {
                int page = text.IndexOf("{page}", i, StringComparison.Ordinal);
                int pages = text.IndexOf("{pages}", i, StringComparison.Ordinal);
                int next = page < 0 ? pages : pages < 0 ? page : Math.Min(page, pages);
                if (next < 0)
                {
                    p.Inlines.Add(new TextRun(text.Substring(i), f));
                    break;
                }
                if (next > i)
                {
                    p.Inlines.Add(new TextRun(text.Substring(i, next - i), f));
                }
                bool isTotal = next == pages;
                p.Inlines.Add(new FieldInline(isTotal ? FieldKind.NumPages : FieldKind.Page, f));
                i = next + (isTotal ? 7 : 6);
            }
            return p;
        }

        /// <summary>The bytes of a base64 <c>data:</c> URI, or null when it is not one (or does not decode).</summary>
        /// <exception cref="LayoutLimitExceededException">The data is larger than <see cref="LayoutLimits.MaxResourceBytes"/>.</exception>
        public static byte[]? DataUri(string uri, LayoutLimits limits)
        {
            if (!uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            int comma = uri.IndexOf(',', StringComparison.Ordinal);
            if (comma < 0 || !uri.AsSpan(0, comma).EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            int length = uri.Length - comma - 1;
            Limits.Check(nameof(LayoutLimits.MaxResourceBytes), limits.MaxResourceBytes, length * 3L / 4);
            // Base64 in HTML attributes may carry whitespace; it is not part of the data.
            string data = uri.Substring(comma + 1);
            if (data.AsSpan().IndexOfAny(" \t\r\n\f") >= 0)
            {
                data = string.Concat(data.Split(new[] { ' ', '\t', '\r', '\n', '\f' }, StringSplitOptions.RemoveEmptyEntries));
            }
            byte[] buffer = new byte[(data.Length * 3 / 4) + 3];
            return Convert.TryFromBase64String(data, buffer, out int written) ? buffer.AsSpan(0, written).ToArray() : null;
        }

        /// <summary>The default link filter: absolute http, https and mailto destinations are kept.</summary>
        public static string? DefaultLink(string destination) =>
            Uri.TryCreate(destination.Trim(), UriKind.Absolute, out Uri? u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeMailto)
                ? u.AbsoluteUri
                : null;

        /// <summary>Warns once per distinct code and message.</summary>
        public sealed class Warnings
        {
            private readonly Action<LayoutWarning> _warn;
            private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);

            public Warnings(Action<LayoutWarning> warn) => _warn = warn;

            public void Add(string code, LayoutWarningSeverity severity, string message)
            {
                if (_seen.Add(code + "\u0000" + message))
                {
                    _warn(new LayoutWarning(code, severity, message));
                }
            }
        }
    }
}
