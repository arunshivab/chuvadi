using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Chuvadi.Markup.Internal;

namespace Chuvadi.Markup.Html
{
    /// <summary>Options for <see cref="HtmlSerializer"/>.</summary>
    public sealed class HtmlSerializerOptions
    {
        /// <summary>
        /// Escape <c>&lt;</c> and <c>&gt;</c> inside attribute values (current WHATWG serialization; closes the
        /// attribute-based mutation XSS class). Default true.
        /// </summary>
        public bool EscapeLessThanInAttributes { get; set; } = true;

        /// <summary>Serialize <c>noscript</c> text literally (as a browser with scripting enabled does). Default true.</summary>
        public bool Scripting { get; set; } = true;

        /// <summary>Emit a newline after <c>&lt;pre&gt;</c>, <c>&lt;textarea&gt;</c> and <c>&lt;listing&gt;</c> when the content starts with one, so re-parsing keeps it. Default true.</summary>
        public bool PreserveLeadingNewline { get; set; } = true;

        /// <summary>Output limits. Default <c>new MarkupLimits()</c>.</summary>
        public MarkupLimits Limits { get; set; } = new MarkupLimits();
    }

    /// <summary>
    /// The WHATWG HTML fragment serialization algorithm (iterative). Text is escaped (&amp;, U+00A0,
    /// &lt;, &gt;) except inside raw-text elements; attribute values additionally escape quotes.
    /// </summary>
    public static class HtmlSerializer
    {
        private static readonly HashSet<string> s_void = new HashSet<string>(StringComparer.Ordinal)
        {
            "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr",
            "basefont", "bgsound", "frame", "keygen", "param",
        };

        private static readonly HashSet<string> s_rawText = new HashSet<string>(StringComparer.Ordinal)
        {
            "style", "script", "xmp", "iframe", "noembed", "noframes", "plaintext",
        };

        /// <summary>True if the HTML element serializes as void (no end tag, no children).</summary>
        public static bool IsVoidElement(string localName) => s_void.Contains(localName);

        /// <summary>Serializes a node itself and its subtree (outerHTML; a document or fragment serializes its children).</summary>
        public static string Serialize(HtmlNode node, HtmlSerializerOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(node);
            options ??= new HtmlSerializerOptions();
            var sink = new OutputSink(options.Limits.MaxOutputChars, 1024);
            if (node is HtmlDocument or HtmlDocumentFragment) WriteChildren(sink, node, options, cancellationToken);
            else WriteNode(sink, node, options, cancellationToken);
            return sink.ToString();
        }

        /// <summary>Serializes a node's children (innerHTML).</summary>
        public static string SerializeChildren(HtmlNode node, HtmlSerializerOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(node);
            options ??= new HtmlSerializerOptions();
            var sink = new OutputSink(options.Limits.MaxOutputChars, 1024);
            WriteChildren(sink, node, options, cancellationToken);
            return sink.ToString();
        }

        /// <summary>Serializes a node (outerHTML) to a writer.</summary>
        public static void Serialize(HtmlNode node, TextWriter writer, HtmlSerializerOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(node);
            ArgumentNullException.ThrowIfNull(writer);
            options ??= new HtmlSerializerOptions();
            var sink = new OutputSink(writer, options.Limits.MaxOutputChars);
            if (node is HtmlDocument or HtmlDocumentFragment) WriteChildren(sink, node, options, cancellationToken);
            else WriteNode(sink, node, options, cancellationToken);
        }

        private static void WriteNode(OutputSink o, HtmlNode node, HtmlSerializerOptions opt, CancellationToken ct)
        {
            // serialize node via a temporary fragment-like walk: emit open, children, close
            var poll = new CancellationPoll(ct);
            var stack = new Stack<(HtmlNode node, bool close)>();
            stack.Push((node, false));
            Walk(o, stack, opt, ref poll);
        }

        private static void WriteChildren(OutputSink o, HtmlNode node, HtmlSerializerOptions opt, CancellationToken ct)
        {
            var poll = new CancellationPoll(ct);
            if (node is HtmlElement el && el.Namespace == HtmlNamespace.Html && s_void.Contains(el.LocalName)) return;
            var parent = node is HtmlElement { Content: not null } tpl ? tpl.Content : node;
            var stack = new Stack<(HtmlNode node, bool close)>();
            if (node is HtmlElement pe && pe.Namespace == HtmlNamespace.Html && opt.PreserveLeadingNewline
                && pe.LocalName is "pre" or "textarea" or "listing" && parent.FirstChild is HtmlText ft && ft.Data.Length > 0 && ft.Data[0] == '\n')
                o.Write('\n');
            for (var c = parent.LastChild; c is not null; c = c.PreviousSibling) stack.Push((c, false));
            Walk(o, stack, opt, ref poll);
        }

        private static void Walk(OutputSink o, Stack<(HtmlNode node, bool close)> stack, HtmlSerializerOptions opt, ref CancellationPoll poll)
        {
            while (stack.Count > 0)
            {
                poll.Tick();
                var (n, close) = stack.Pop();
                if (close)
                {
                    var e = (HtmlElement)n;
                    o.Write("</");
                    o.Write(e.LocalName);
                    o.Write('>');
                    continue;
                }
                switch (n)
                {
                    case HtmlElement e:
                    {
                        o.Write('<');
                        o.Write(e.LocalName);
                        foreach (var a in e.Attributes)
                        {
                            o.Write(' ');
                            o.Write(a.Name);
                            o.Write("=\"");
                            EscapeAttribute(o, a.Value, opt.EscapeLessThanInAttributes);
                            o.Write('"');
                        }
                        o.Write('>');
                        if (e.Namespace == HtmlNamespace.Html && s_void.Contains(e.LocalName)) break;
                        stack.Push((e, true));
                        HtmlNode container = e.Content ?? (HtmlNode)e;
                        if (e.Namespace == HtmlNamespace.Html && opt.PreserveLeadingNewline && e.LocalName is "pre" or "textarea" or "listing"
                            && container.FirstChild is HtmlText ft && ft.Data.Length > 0 && ft.Data[0] == '\n')
                            o.Write('\n');
                        for (var c = container.LastChild; c is not null; c = c.PreviousSibling) stack.Push((c, false));
                        break;
                    }
                    case HtmlText t:
                    {
                        if (t.Parent is HtmlElement p && p.Namespace == HtmlNamespace.Html
                            && (s_rawText.Contains(p.LocalName) || (p.LocalName == "noscript" && opt.Scripting)))
                            o.Write(t.Data);
                        else
                            EscapeText(o, t.Data);
                        break;
                    }
                    case HtmlComment c:
                        o.Write("<!--");
                        o.Write(c.Data);
                        o.Write("-->");
                        break;
                    case HtmlDocumentType d:
                        o.Write("<!DOCTYPE ");
                        o.Write(d.Name);
                        o.Write('>');
                        break;
                    case HtmlDocument or HtmlDocumentFragment:
                        for (var c = n.LastChild; c is not null; c = c.PreviousSibling) stack.Push((c, false));
                        break;
                }
            }
        }

        /// <summary>Escapes text content (&amp;, U+00A0, &lt;, &gt;).</summary>
        public static string EscapeText(string text)
        {
            var o = new OutputSink(long.MaxValue, text.Length + 16);
            EscapeText(o, text);
            return o.ToString();
        }

        /// <summary>Escapes an attribute value (&amp;, U+00A0, ", &lt;, &gt;).</summary>
        public static string EscapeAttribute(string value)
        {
            var o = new OutputSink(long.MaxValue, value.Length + 16);
            EscapeAttribute(o, value, true);
            return o.ToString();
        }

        internal static void EscapeText(OutputSink o, string s)
        {
            int start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                string? rep = s[i] switch
                {
                    '&' => "&amp;",
                    ' ' => "&nbsp;",
                    '<' => "&lt;",
                    '>' => "&gt;",
                    _ => null,
                };
                if (rep is null) continue;
                if (i > start) o.Write(s.AsSpan(start, i - start));
                o.Write(rep);
                start = i + 1;
            }
            if (start < s.Length) o.Write(s.AsSpan(start));
        }

        internal static void EscapeAttribute(OutputSink o, string s, bool ltgt)
        {
            int start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                string? rep = s[i] switch
                {
                    '&' => "&amp;",
                    ' ' => "&nbsp;",
                    '"' => "&quot;",
                    '<' when ltgt => "&lt;",
                    '>' when ltgt => "&gt;",
                    _ => null,
                };
                if (rep is null) continue;
                if (i > start) o.Write(s.AsSpan(start, i - start));
                o.Write(rep);
                start = i + 1;
            }
            if (start < s.Length) o.Write(s.AsSpan(start));
        }
    }
}
