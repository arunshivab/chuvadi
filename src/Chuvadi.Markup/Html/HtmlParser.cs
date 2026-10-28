using System;
using System.IO;
using System.Text;
using System.Threading;
using Chuvadi.Markup.Internal;

namespace Chuvadi.Markup.Html
{
    /// <summary>
    /// Parses HTML into a DOM with the WHATWG HTML parsing algorithm (the same tree a browser builds,
    /// scripting never executed). Tolerant of any input; safe for untrusted input under
    /// <see cref="MarkupLimits"/> and a <see cref="CancellationToken"/>.
    /// </summary>
    public static class HtmlParser
    {
        /// <summary>Parses a complete document from a string.</summary>
        /// <exception cref="MarkupLimitExceededException">A hard limit was exceeded.</exception>
        /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
        public static HtmlDocument Parse(string html, HtmlParserOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(html);
            options ??= new HtmlParserOptions();
            options.Limits.Validate();
            var tk = new HtmlTokenizer(html, options.Limits, cancellationToken);
            var doc = new HtmlDocument();
            var tb = new TreeBuilder(tk, doc, options, cancellationToken);
            tb.Run();
            CopyErrors(tk, doc.ErrorList, options);
            doc.ParseErrorCount = options.CollectErrors ? tk.ErrorCount : 0;
            return doc;
        }

        /// <summary>
        /// Parses a document from bytes: BOM, then <see cref="HtmlParserOptions.Encoding"/>, then a
        /// <c>&lt;meta&gt;</c> prescan, then <see cref="HtmlParserOptions.FallbackEncoding"/> (windows-1252).
        /// </summary>
        public static HtmlDocument Parse(byte[] bytes, HtmlParserOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            return Parse(new ReadOnlySpan<byte>(bytes), options, cancellationToken);
        }

        /// <inheritdoc cref="Parse(byte[], HtmlParserOptions?, CancellationToken)"/>
        public static HtmlDocument Parse(ReadOnlySpan<byte> bytes, HtmlParserOptions? options = null, CancellationToken cancellationToken = default)
        {
            options ??= new HtmlParserOptions();
            options.Limits.Validate();
            if (bytes.Length > options.Limits.MaxInputChars)
                throw new MarkupLimitExceededException(nameof(MarkupLimits.MaxInputChars), options.Limits.MaxInputChars, bytes.Length);
            var (encoding, source, skip) = HtmlEncoding.Determine(bytes, options);
            string text = encoding.GetString(bytes.Slice(skip));
            var doc = Parse(text, options, cancellationToken);
            doc.Encoding = encoding;
            doc.EncodingSource = source;
            return doc;
        }

        /// <summary>Reads a stream (up to <see cref="MarkupLimits.MaxInputChars"/> bytes) and parses it as bytes.</summary>
        public static HtmlDocument Parse(Stream stream, HtmlParserOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stream);
            options ??= new HtmlParserOptions();
            int max = options.Limits.MaxInputChars;
            using var ms = new MemoryStream();
            var buf = new byte[81920];
            int n;
            while ((n = stream.Read(buf, 0, buf.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ms.Length + n > max)
                    throw new MarkupLimitExceededException(nameof(MarkupLimits.MaxInputChars), max, ms.Length + n);
                ms.Write(buf, 0, n);
            }
            return Parse(new ReadOnlySpan<byte>(ms.GetBuffer(), 0, (int)ms.Length), options, cancellationToken);
        }

        /// <summary>
        /// Parses an HTML fragment as if it were the inner HTML of an element named <paramref name="contextLocalName"/>
        /// (default <c>body</c>), using the WHATWG fragment parsing algorithm.
        /// </summary>
        public static HtmlDocumentFragment ParseFragment(string html, string contextLocalName = "body", HtmlNamespace contextNamespace = HtmlNamespace.Html, HtmlParserOptions? options = null, CancellationToken cancellationToken = default)
            => ParseFragment(html, new HtmlElement(contextLocalName, contextNamespace), options, cancellationToken);

        /// <summary>Parses an HTML fragment in the context of an element (its attributes and ancestors are consulted as the algorithm requires).</summary>
        public static HtmlDocumentFragment ParseFragment(string html, HtmlElement context, HtmlParserOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(html);
            ArgumentNullException.ThrowIfNull(context);
            options ??= new HtmlParserOptions();
            options.Limits.Validate();
            var tk = new HtmlTokenizer(html, options.Limits, cancellationToken);
            var doc = new HtmlDocument();
            for (HtmlNode? n = context; n is not null; n = n.Parent)
                if (n is HtmlDocument cd) doc.QuirksMode = cd.QuirksMode;
            var tb = new TreeBuilder(tk, doc, options, cancellationToken);
            tb.InitializeFragment(context);
            tb.Run();
            var fragment = new HtmlDocumentFragment();
            var root = doc.DocumentElement!;
            while (root.FirstChild is { } c)
            {
                root.RemoveChild(c);
                fragment.InsertBeforeCore(c, null);
            }
            var errors = new System.Collections.Generic.List<HtmlParseError>();
            CopyErrors(tk, errors, options);
            fragment.ParseErrors = errors;
            return fragment;
        }

        private static void CopyErrors(HtmlTokenizer tk, System.Collections.Generic.List<HtmlParseError> target, HtmlParserOptions options)
        {
            if (!options.CollectErrors) return;
            int[]? lineStarts = null;
            foreach (var e in tk.Errors)
            {
                lineStarts ??= LineStarts(tk.Input);
                int idx = Array.BinarySearch(lineStarts, Math.Max(0, e.Offset));
                if (idx < 0) idx = ~idx - 1;
                e.Line = idx + 1;
                e.Column = Math.Max(0, e.Offset) - lineStarts[idx] + 1;
                target.Add(e);
            }
        }

        private static int[] LineStarts(string s)
        {
            var list = new System.Collections.Generic.List<int> { 0 };
            for (int i = 0; i < s.Length; i++)
                if (s[i] == '\n') list.Add(i + 1);
            return list.ToArray();
        }
    }
}
