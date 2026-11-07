using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Chuvadi.Markup.Internal;

namespace Chuvadi.Markup.Markdown
{
    /// <summary>
    /// Renders a <see cref="MarkdownDocument"/> (or any subtree) to HTML. Output matches the CommonMark and
    /// GFM spec examples byte for byte. The renderer is iterative, so tree depth never threatens the stack.
    /// </summary>
    public static class MarkdownHtmlRenderer
    {
        /// <summary>Renders a node and its descendants to a string.</summary>
        public static string Render(MarkdownNode node, MarkdownHtmlOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(node);
            options ??= new MarkdownHtmlOptions();
            var sink = new OutputSink(options.Limits.MaxOutputChars);
            new HtmlWriter(sink, options, node, cancellationToken).Run();
            return sink.ToString();
        }

        /// <summary>Renders a node and its descendants to a writer.</summary>
        public static void Render(MarkdownNode node, TextWriter writer, MarkdownHtmlOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(node);
            ArgumentNullException.ThrowIfNull(writer);
            options ??= new MarkdownHtmlOptions();
            var sink = new OutputSink(writer, options.Limits.MaxOutputChars);
            new HtmlWriter(sink, options, node, cancellationToken).Run();
        }

        /// <summary>One rendering pass: a depth-first walk with an explicit stack of enter and leave steps.</summary>
        private sealed class HtmlWriter
        {
            private readonly OutputSink _out;
            private readonly MarkdownHtmlOptions _o;
            private readonly MarkdownNode _root;
            private readonly MarkdownDocument? _document;
            private CancellationPoll _poll;
            private readonly Stack<(MarkdownNode Node, bool Leaving)> _stack = new();
            private Slugs? _slugs;

            public HtmlWriter(OutputSink sink, MarkdownHtmlOptions options, MarkdownNode root, CancellationToken token)
            {
                _out = sink;
                _o = options;
                _root = root;
                _poll = new CancellationPoll(token);
                MarkdownNode top = root;
                while (top.Parent is not null) top = top.Parent;
                _document = top as MarkdownDocument;
            }

            // TRACE R-MD-0167
            public void Run()
            {
                _stack.Push((_root, false));
                while (_stack.Count > 0)
                {
                    _poll.Tick();
                    var (node, leaving) = _stack.Pop();
                    if (leaving) Leave(node);
                    else Enter(node);
                }
            }

            private void PushChildren(MarkdownNode node)
            {
                _stack.Push((node, true));
                var kids = node.Children;
                for (int i = kids.Count - 1; i >= 0; i--) _stack.Push((kids[i], false));
            }

            // ------------------------------------------------------------ output helpers

            private void Write(string s) => _out.Write(s);

            private void Write(char c) => _out.Write(c);

            // TRACE R-MD-0168
            private void Text(string s) => CharUtil.EscapeHtml(_out, s.AsSpan(), escapeQuotes: true);

            /// <summary>Starts a new line unless the output is already at the start of one.</summary>
            private void NewLine()
            {
                if (_out.LastChar != '\n') Write('\n');
            }

            private string Void => _o.XhtmlVoidElements ? " />" : ">";

            // TRACE R-MD-0169
            /// <summary>The data-sourcepos attribute of a block: one-based start and end line:column, end inclusive.</summary>
            private void SourcePos(MarkdownNode node)
            {
                if (!_o.SourcePositions || _document is null) return;
                var span = node.Span;
                if (span.Length == 0 && span.Start == 0 && node is not MarkdownDocument && _document.Source.Length == 0) return;
                // TRACE R-MD-0272
                // a span that runs past the input (R-MD-0266) is placed at the input's end: "- >o\n\t[" gives 1:1-2:3
                int limit = _document.Source.Length;
                int startOffset = span.Start < limit ? span.Start : limit;
                int endOffset = span.Length > 0 ? span.End - 1 : span.Start;
                if (endOffset > limit) endOffset = limit;
                var start = _document.GetPosition(startOffset);
                var end = _document.GetPosition(endOffset);
                Write(" data-sourcepos=\"");
                Write(start.Line.ToString(CultureInfo.InvariantCulture));
                Write(':');
                Write(start.Column.ToString(CultureInfo.InvariantCulture));
                Write('-');
                Write(end.Line.ToString(CultureInfo.InvariantCulture));
                Write(':');
                Write(end.Column.ToString(CultureInfo.InvariantCulture));
                Write('"');
            }

            private static bool InTightList(MarkdownNode paragraph)
                => paragraph.Parent is ListItem item && item.Parent is ListBlock list && list.IsTight;

            // ------------------------------------------------------------ nodes

            private void Enter(MarkdownNode node)
            {
                switch (node)
                {
                    case MarkdownDocument:
                        PushChildren(node);
                        break;

                    case Paragraph p:
                        // TRACE R-MD-0170
                        if (InTightList(p))
                        {
                            TaskMarker(p);
                            PushChildren(node);
                            break;
                        }
                        NewLine();
                        Write("<p");
                        SourcePos(node);
                        Write('>');
                        PushChildren(node);
                        break;

                    case Heading h:
                        NewLine();
                        Write("<h");
                        Write((char)('0' + h.Level));
                        HeadingId(h);
                        SourcePos(node);
                        Write('>');
                        PushChildren(node);
                        break;

                    case ThematicBreak:
                        NewLine();
                        Write("<hr");
                        SourcePos(node);
                        Write(Void);
                        Write('\n');
                        break;

                    case BlockQuote:
                        NewLine();
                        Write("<blockquote");
                        SourcePos(node);
                        Write(">\n");
                        PushChildren(node);
                        break;

                    case ListBlock list:
                        // TRACE R-MD-0171
                        NewLine();
                        if (list.ListKind == ListKind.Bullet) Write("<ul");
                        else
                        {
                            Write("<ol");
                            if (list.Start != 1)
                            {
                                Write(" start=\"");
                                Write(list.Start.ToString(CultureInfo.InvariantCulture));
                                Write('"');
                            }
                        }
                        SourcePos(node);
                        Write(">\n");
                        PushChildren(node);
                        break;

                    case ListItem item:
                        // TRACE R-MD-0172
                        NewLine();
                        Write("<li");
                        SourcePos(node);
                        Write('>');
                        // TRACE R-MD-0257
                        // the marker of a task item with no content, or of one in a loose list, comes right after
                        // <li>, before the line ending and the <p> (D-232, G6)
                        bool tightFirst = item.Children.Count > 0 && item.Children[0] is Paragraph && InTightList(item.Children[0]);
                        if (item.TaskState != TaskState.None && !tightFirst && (item.Children.Count == 0 || item.Children[0] is Paragraph))
                            TaskMarkerFor(item);
                        if (item.Children.Count > 0 && !tightFirst)
                            Write('\n');
                        PushChildren(node);
                        break;

                    case CodeBlock code:
                        CodeBlock(code);
                        break;

                    case HtmlBlock html:
                        // TRACE R-MD-0173
                        NewLine();
                        if (_o.RawHtml == RawHtmlMode.Escape)
                        {
                            // TRACE R-MD-0235
                            Write("<p");
                            SourcePos(node);
                            Write('>');
                            Text(html.Html.TrimEnd('\n'));
                            Write("</p>\n");
                            break;
                        }
                        RawHtml(html.Html, block: true);
                        break;

                    case Table:
                    case TableRow:
                    case TableCell:
                        TableEnter(node);
                        break;

                    case TextInline t:
                        Text(t.Text);
                        break;

                    case SoftBreak:
                        // TRACE R-MD-0174
                        switch (_o.SoftBreak)
                        {
                            case SoftBreakStyle.Space: Write(' '); break;
                            case SoftBreakStyle.LineBreak: Write("<br"); Write(Void); Write('\n'); break;
                            default: Write('\n'); break;
                        }
                        break;

                    case HardBreak:
                        // TRACE R-MD-0175
                        Write("<br");
                        Write(Void);
                        Write('\n');
                        break;

                    case CodeSpan cs:
                        // TRACE R-MD-0176
                        Write("<code>");
                        Text(cs.Code);
                        Write("</code>");
                        break;

                    case Emphasis:
                        // TRACE R-MD-0177
                        Write("<em>");
                        PushChildren(node);
                        break;

                    case Strong:
                        Write("<strong>");
                        PushChildren(node);
                        break;

                    case Strikethrough:
                        Write("<del>");
                        PushChildren(node);
                        break;

                    case Link link:
                        // TRACE R-MD-0178
                        Write("<a href=\"");
                        Url(link.Destination);
                        Write('"');
                        if (link.Title is not null)
                        {
                            Write(" title=\"");
                            Text(link.Title);
                            Write('"');
                        }
                        Write('>');
                        PushChildren(node);
                        break;

                    case Image image:
                        ImageTag(image);
                        break;

                    case HtmlInline hi:
                        RawHtml(hi.Html, block: false);
                        break;

                    default:
                        PushChildren(node);
                        break;
                }
            }

            private void Leave(MarkdownNode node)
            {
                switch (node)
                {
                    case Paragraph p:
                        if (!InTightList(p)) Write("</p>\n");
                        break;
                    case Heading h:
                        Write("</h");
                        Write((char)('0' + h.Level));
                        Write(">\n");
                        break;
                    case BlockQuote:
                        NewLine();
                        Write("</blockquote>\n");
                        break;
                    case ListBlock list:
                        NewLine();
                        Write(list.ListKind == ListKind.Bullet ? "</ul>\n" : "</ol>\n");
                        break;
                    case ListItem:
                        Write("</li>\n");
                        break;
                    case Table:
                    case TableRow:
                    case TableCell:
                        TableLeave(node);
                        break;
                    case Emphasis:
                        Write("</em>");
                        break;
                    case Strong:
                        Write("</strong>");
                        break;
                    case Strikethrough:
                        Write("</del>");
                        break;
                    case Link:
                        Write("</a>");
                        break;
                }
            }

            // TRACE R-MD-0179
            private void CodeBlock(CodeBlock code)
            {
                NewLine();
                Write("<pre");
                SourcePos(code);
                Write("><code");
                string language = code.IsFenced ? code.Language : string.Empty;
                if (language.Length > 0 && !string.IsNullOrEmpty(_o.CodeLanguageClassPrefix))
                {
                    Write(" class=\"");
                    Text(_o.CodeLanguageClassPrefix);
                    Text(language);
                    Write('"');
                }
                Write('>');
                Text(code.Code);
                Write("</code></pre>\n");
            }

            // TRACE R-MD-0180
            private void ImageTag(Image image)
            {
                Write("<img src=\"");
                Url(image.Destination);
                Write("\" alt=\"");
                Text(PlainText(image));
                Write('"');
                if (image.Title is not null)
                {
                    Write(" title=\"");
                    Text(image.Title);
                    Write('"');
                }
                Write(Void);
            }

            /// <summary>The text of an image description, without markup, for the alt attribute.</summary>
            // for slugs: no raw inline HTML, and a hard line break gives nothing (D-237: "a  \nd" gives "ad"); a soft
            // break still counts as a space ("baz\na" gives "baz-a")
            internal static string PlainTextOf(MarkdownNode node) => PlainText(node, withHtml: false, breaks: false);

            private static string PlainText(MarkdownNode node, bool withHtml = true, bool breaks = true)
            {
                var sb = new StringBuilder();
                var stack = new Stack<MarkdownNode>();
                for (int i = node.Children.Count - 1; i >= 0; i--) stack.Push(node.Children[i]);
                while (stack.Count > 0)
                {
                    var n = stack.Pop();
                    switch (n)
                    {
                        case TextInline t: sb.Append(t.Text); break;
                        case CodeSpan c: sb.Append(c.Code); break;
                        // TRACE R-MD-0183
                        case SoftBreak: sb.Append(' '); break;
                        case HardBreak: if (breaks) sb.Append(' '); break;
                        case HtmlInline h:
                            if (withHtml) sb.Append(h.Html);
                            break;
                        default:
                            for (int i = n.Children.Count - 1; i >= 0; i--) stack.Push(n.Children[i]);
                            break;
                    }
                }
                return sb.ToString();
            }

            // TRACE R-MD-0181
            /// <summary>
            /// Writes a URL: chars outside the set kept by URLs (ASCII letters and digits, the reserved and
            /// unreserved marks, and well-formed %XX escapes) are percent-encoded as UTF-8, then the result is
            /// escaped for an attribute.
            /// </summary>
            private void Url(string url)
            {
                if (_o.UnsafeUrls == UnsafeUrlPolicy.Remove && UrlSafety.IsDangerous(url)) url = string.Empty;
                var sb = new StringBuilder(url.Length + 8);
                Span<byte> bytes = stackalloc byte[4];
                for (int i = 0; i < url.Length; i++)
                {
                    char c = url[i];
                    if (c < 128 && KeptInUrl(c))
                    {
                        sb.Append(c);
                        continue;
                    }
                    // TRACE R-MD-0255
                    // '%' is always kept, followed by two hex digits or not (D-232, N5: "%" stays "%", not "%25")
                    if (c == '%')
                    {
                        sb.Append('%');
                        continue;
                    }
                    int cp = c;
                    if (char.IsHighSurrogate(c) && i + 1 < url.Length && char.IsLowSurrogate(url[i + 1]))
                    {
                        cp = char.ConvertToUtf32(c, url[i + 1]);
                        i++;
                    }
                    else if (char.IsSurrogate(c)) cp = 0xFFFD;
                    int n = new Rune(cp).EncodeToUtf8(bytes);
                    for (int k = 0; k < n; k++)
                    {
                        sb.Append('%');
                        sb.Append("0123456789ABCDEF"[bytes[k] >> 4]);
                        sb.Append("0123456789ABCDEF"[bytes[k] & 15]);
                    }
                }
                // TRACE R-MD-0228
                // in the attribute, & is &amp; and an apostrophe is &#x27; (the other specials are already encoded)
                foreach (char ch in sb.ToString())
                {
                    if (ch == '&') Write("&amp;");
                    else if (ch == '\'') Write("&#x27;");
                    else Write(ch);
                }
            }

            private static bool KeptInUrl(char c)
                => CharUtil.IsAsciiAlnum(c) || "-_.!~*'();/?:@&=+$,#".IndexOf(c) >= 0;

            // TRACE R-MD-0182
            private void RawHtml(string html, bool block)
            {
                switch (_o.RawHtml)
                {
                    case RawHtmlMode.Strip:
                        return;
                    case RawHtmlMode.Escape:
                        Text(html);
                        return;
                    default:
                        if (_o.TagFilter) TagFilter.Write(_out, html);
                        else Write(html);
                        return;
                }
            }

            private void HeadingId(Heading h)
            {
                if (!_o.HeadingIds) return;
                Write(" id=\"");
                _slugs ??= new Slugs();
                Text(_o.HeadingIdPrefix + _slugs.Make(h));
                Write('"');
            }

            // TRACE R-MD-0257
            /// <summary>The task list marker, written at the start of a task item's first paragraph.</summary>
            private void TaskMarker(Paragraph p)
            {
                if (p.Parent is not ListItem item || item.TaskState == TaskState.None || item.Children[0] != p) return;
                // TRACE R-MD-0240
                // the marker belongs to the item: a paragraph rendered on its own gets none
                if (ReferenceEquals(p, _root)) return;
                TaskMarkerFor(item);
            }

            private void TaskMarkerFor(ListItem item)
            {
                bool done = item.TaskState == TaskState.Checked;
                switch (_o.TaskLists)
                {
                    case TaskListStyle.Glyph:
                        Write(done ? "<span class=\"task-checked\">\u2611</span> " : "<span class=\"task-unchecked\">\u2610</span> ");
                        break;
                    case TaskListStyle.Text:
                        Write(done ? "[x] " : "[ ] ");
                        break;
                    default:
                        Write(done ? "<input type=\"checkbox\" checked=\"\" disabled=\"\"" : "<input type=\"checkbox\" disabled=\"\"");
                        Write(Void);
                        Write(' ');
                        break;
                }
            }

            // TRACE R-MD-0226
            /// <summary>
            /// Tables: &lt;table&gt;, the header row in &lt;thead&gt; with &lt;th&gt; cells, the other rows in
            /// &lt;tbody&gt; (left out when there are none) with &lt;td&gt; cells, alignment as an attribute or a style.
            /// </summary>
            private void TableEnter(MarkdownNode node)
            {
                switch (node)
                {
                    case Table:
                        NewLine();
                        Write("<table");
                        SourcePos(node);
                        Write(">\n");
                        break;
                    case TableRow row:
                        if (row.IsHeader) Write("<thead>\n");
                        else if (IsFirstBodyRow(row)) Write("<tbody>\n");
                        Write("<tr");
                        SourcePos(node);
                        Write(">\n");
                        break;
                    case TableCell cell:
                        Write(IsHeaderCell(cell) ? "<th" : "<td");
                        if (cell.Alignment != TableAlignment.None)
                        {
                            string a = cell.Alignment switch
                            {
                                TableAlignment.Left => "left",
                                TableAlignment.Center => "center",
                                _ => "right",
                            };
                            if (_o.TableAlignment == TableAlignmentStyle.StyleAttribute)
                            {
                                Write(" style=\"text-align: ");
                                Write(a);
                                Write('"');
                            }
                            else
                            {
                                Write(" align=\"");
                                Write(a);
                                Write('"');
                            }
                        }
                        SourcePos(node);
                        Write('>');
                        break;
                }
                PushChildren(node);
            }

            private void TableLeave(MarkdownNode node)
            {
                switch (node)
                {
                    case Table table:
                        if (HasBody(table)) Write("</tbody>\n");
                        Write("</table>\n");
                        break;
                    case TableRow row:
                        // TRACE R-MD-0241
                        // the line ending after a row or a cell is written by its parent, so a row or a cell
                        // rendered on its own ends at its closing tag
                        Write("</tr>");
                        if (row.IsHeader) Write("\n</thead>");
                        if (!ReferenceEquals(row, _root)) Write('\n');
                        break;
                    case TableCell cell:
                        Write(IsHeaderCell(cell) ? "</th>" : "</td>");
                        if (!ReferenceEquals(cell, _root)) Write('\n');
                        break;
                }
            }

            // a cell is a header cell only when written as part of its header row
            private bool IsHeaderCell(TableCell cell) => !ReferenceEquals(cell, _root) && cell.Parent is TableRow row && row.IsHeader;

            private static bool IsFirstBodyRow(TableRow row)
            {
                if (row.Parent is null) return true;
                foreach (var r in row.Parent.Children)
                {
                    if (r is TableRow tr && !tr.IsHeader) return ReferenceEquals(tr, row);
                }
                return false;
            }

            private static bool HasBody(Table table)
            {
                foreach (var r in table.Children)
                    if (r is TableRow tr && !tr.IsHeader) return true;
                return false;
            }
        }

        /// <summary>Dangerous URL schemes (MarkdownHtmlOptions.UnsafeUrls).</summary>
        private static class UrlSafety
        {
            // TRACE R-MD-0234
            /// <summary>
            /// javascript:, vbscript:, file: and data: URLs (scheme in any case) are dangerous, except data: URLs
            /// of the image types png, gif, jpeg and webp.
            /// </summary>
            public static bool IsDangerous(string url)
            {
                if (HasScheme(url, "javascript:") || HasScheme(url, "vbscript:") || HasScheme(url, "file:")) return true;
                if (!HasScheme(url, "data:")) return false;
                return !(HasScheme(url, "data:image/png") || HasScheme(url, "data:image/gif")
                    || HasScheme(url, "data:image/jpeg") || HasScheme(url, "data:image/webp"));
            }

            private static bool HasScheme(string url, string prefix)
            {
                if (url.Length < prefix.Length) return false;
                for (int i = 0; i < prefix.Length; i++)
                    if (CharUtil.AsciiLower(url[i]) != prefix[i]) return false;
                return true;
            }
        }

        // TRACE R-MD-0227
        /// <summary>
        /// GFM tag filter: the '&lt;' of a title, textarea, style, xmp, iframe, noembed, noframes, script or
        /// plaintext open or closing tag (any case) is written as &amp;lt;.
        /// </summary>
        private static class TagFilter
        {
            private static readonly string[] s_names = { "title", "textarea", "style", "xmp", "iframe", "noembed", "noframes", "script", "plaintext" };

            public static void Write(OutputSink sink, string html)
            {
                int from = 0;
                for (int i = 0; i < html.Length; i++)
                {
                    if (html[i] != '<' || !Filtered(html, i)) continue;
                    if (i > from) sink.Write(html.AsSpan(from, i - from));
                    sink.Write("&lt;");
                    from = i + 1;
                }
                if (from < html.Length) sink.Write(html.AsSpan(from));
            }

            private static bool Filtered(string s, int i)
            {
                int k = i + 1;
                if (k < s.Length && s[k] == '/') k++;
                foreach (var name in s_names)
                {
                    if (s.Length - k < name.Length) continue;
                    bool same = true;
                    for (int j = 0; j < name.Length; j++)
                    {
                        if (CharUtil.AsciiLower(s[k + j]) != name[j])
                        {
                            same = false;
                            break;
                        }
                    }
                    if (!same) continue;
                    int e = k + name.Length;
                    if (e == s.Length) return true;
                    char c = s[e];
                    return c == '>' || c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f' || (c == '/' && e + 1 < s.Length && s[e + 1] == '>');
                }
                return false;
            }
        }

        // TRACE R-MD-0271
        /// <summary>
        /// Heading id slugs: the heading's plain text (raw inline HTML left out), trimmed of whitespace, in lower case, letters, digits, '-' and '_' kept, each space
        /// written as '-', everything else dropped. A slug already used in this rendering gets "-1", "-2", ...
        /// </summary>
        private sealed class Slugs
        {
            private readonly Dictionary<string, int> _used = new(StringComparer.Ordinal);

            public string Make(Heading h)
            {
                string text = HtmlWriter.PlainTextOf(h);
                var sb = new StringBuilder(text.Length);
                foreach (char c in text.Trim().ToLowerInvariant())
                {
                    // TRACE R-MD-0271
                    // letters, digits, combining marks (vowel signs, viramas and other marks), '-' and '_' stay; a
                    // space becomes '-'; everything else goes, tabs, U+00A0 and line breaks included (D-237)
                    var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
                    if (char.IsLetterOrDigit(c) || c == '-' || c == '_'
                        || cat == System.Globalization.UnicodeCategory.NonSpacingMark
                        || cat == System.Globalization.UnicodeCategory.SpacingCombiningMark
                        || cat == System.Globalization.UnicodeCategory.EnclosingMark) sb.Append(c);
                    else if (c == ' ') sb.Append('-');
                }
                string slug = sb.ToString();
                // TRACE R-MD-0237
                if (_used.TryGetValue(slug, out int n))
                {
                    _used[slug] = n + 1;
                    return slug + "-" + n.ToString(CultureInfo.InvariantCulture);
                }
                _used[slug] = 1;
                return slug;
            }
        }
    }
}
