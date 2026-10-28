using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Chuvadi.Markup.Internal;

namespace Chuvadi.Markup.Markdown
{
    /// <summary>
    /// Renders a <see cref="MarkdownDocument"/> (or any subtree) to HTML. Output matches the reference
    /// implementations (cmark / cmark-gfm) byte for byte for the CommonMark and GFM spec examples.
    /// The renderer is iterative, so tree depth never threatens the stack.
    /// </summary>
    public static class MarkdownHtmlRenderer
    {
        /// <summary>Renders a node and its descendants to a string.</summary>
        public static string Render(MarkdownNode node, MarkdownHtmlOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(node);
            options ??= new MarkdownHtmlOptions();
            var sink = new OutputSink(options.Limits.MaxOutputChars, 256);
            new Writer(sink, options, node, cancellationToken).Run();
            return sink.ToString();
        }

        /// <summary>Renders a node and its descendants to a writer.</summary>
        public static void Render(MarkdownNode node, TextWriter writer, MarkdownHtmlOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(node);
            ArgumentNullException.ThrowIfNull(writer);
            options ??= new MarkdownHtmlOptions();
            var sink = new OutputSink(writer, options.Limits.MaxOutputChars);
            new Writer(sink, options, node, cancellationToken).Run();
        }

        private sealed class Writer
        {
            private readonly OutputSink _o;
            private readonly MarkdownHtmlOptions _opt;
            private readonly MarkdownNode _root;
            private CancellationPoll _poll;
            private readonly MarkdownDocument? _doc;
            private readonly Dictionary<string, int> _slugs = new(StringComparer.Ordinal);
            private bool _inTableHeader;
            private bool _needClosingTbody;

            public Writer(OutputSink o, MarkdownHtmlOptions opt, MarkdownNode root, CancellationToken ct)
            {
                _o = o;
                _opt = opt;
                _root = root;
                _poll = new CancellationPoll(ct);
                for (MarkdownNode? p = root; p is not null; p = p.Parent)
                    if (p is MarkdownDocument d) _doc = d;
            }

            private string Void => _opt.XhtmlVoidElements ? " />" : ">";

            private void Cr()
            {
                if (_o.Written > 0 && _o.LastChar != '\n') _o.Write('\n');
            }

            private void Esc(string s) => CharUtil.EscapeHtml(_o, s, true);

            public void Run()
            {
                var stack = new Stack<(MarkdownNode node, bool entering)>();
                stack.Push((_root, true));
                while (stack.Count > 0)
                {
                    _poll.Tick();
                    var (node, entering) = stack.Pop();
                    if (entering)
                    {
                        bool descend = Enter(node);
                        if (descend)
                        {
                            stack.Push((node, false));
                            var kids = node.Children;
                            for (int i = kids.Count - 1; i >= 0; i--) stack.Push((kids[i], true));
                        }
                    }
                    else
                    {
                        Exit(node);
                    }
                }
            }

            private void SourcePos(MarkdownNode node)
            {
                if (!_opt.SourcePositions || _doc is null || node.Span.Length == 0 && node.Span.Start == 0 && _doc.Source.Length == 0) return;
                var s = _doc.GetPosition(Math.Min(node.Span.Start, _doc.Source.Length));
                var e = _doc.GetPosition(Math.Min(Math.Max(node.Span.Start, node.Span.End - 1), _doc.Source.Length));
                _o.Write(" data-sourcepos=\"");
                _o.Write(s.Line + ":" + s.Column + "-" + e.Line + ":" + e.Column);
                _o.Write('"');
            }

            private static bool InTightList(MarkdownNode node)
                => node.Parent is ListItem item && item.Parent is ListBlock list && list.IsTight;

            /// <summary>Returns true if children should be visited (and Exit called).</summary>
            private bool Enter(MarkdownNode node)
            {
                switch (node)
                {
                    case MarkdownDocument:
                        return true;
                    case BlockQuote bq:
                        Cr();
                        _o.Write("<blockquote");
                        SourcePos(bq);
                        _o.Write(">\n");
                        return true;
                    case ListBlock list:
                        Cr();
                        if (list.ListKind == ListKind.Bullet)
                        {
                            _o.Write("<ul");
                        }
                        else
                        {
                            _o.Write("<ol");
                            if (list.Start != 1) _o.Write(" start=\"" + list.Start.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"");
                        }
                        SourcePos(list);
                        _o.Write(">\n");
                        return true;
                    case ListItem item:
                        Cr();
                        _o.Write("<li");
                        SourcePos(item);
                        _o.Write('>');
                        if (item.TaskState != TaskState.None) WriteTaskMarker(item.TaskState == TaskState.Checked);
                        return true;
                    case Heading h:
                        Cr();
                        _o.Write("<h" + h.Level);
                        if (_opt.HeadingIds)
                        {
                            _o.Write(" id=\"");
                            Esc(_opt.HeadingIdPrefix + Slug(h.ToPlainText()));
                            _o.Write('"');
                        }
                        SourcePos(h);
                        _o.Write('>');
                        return true;
                    case CodeBlock code:
                        Cr();
                        _o.Write("<pre");
                        SourcePos(code);
                        _o.Write("><code");
                        string lang = code.Language;
                        if (lang.Length > 0 && !string.IsNullOrEmpty(_opt.CodeLanguageClassPrefix))
                        {
                            _o.Write(" class=\"");
                            Esc(_opt.CodeLanguageClassPrefix + lang);
                            _o.Write('"');
                        }
                        _o.Write('>');
                        Esc(code.Code);
                        _o.Write("</code></pre>\n");
                        return false;
                    case HtmlBlock html:
                        Cr();
                        WriteRawHtml(html.Html, true);
                        Cr();
                        return false;
                    case ThematicBreak tb:
                        Cr();
                        _o.Write("<hr");
                        SourcePos(tb);
                        _o.Write(Void);
                        _o.Write('\n');
                        return false;
                    case Paragraph p:
                        if (!InTightList(p))
                        {
                            Cr();
                            _o.Write("<p");
                            SourcePos(p);
                            _o.Write('>');
                        }
                        return true;
                    case Table table:
                        Cr();
                        _o.Write("<table");
                        SourcePos(table);
                        _o.Write('>');
                        _needClosingTbody = false;
                        return true;
                    case TableRow row:
                        Cr();
                        if (row.IsHeader)
                        {
                            _inTableHeader = true;
                            _o.Write("<thead>");
                            Cr();
                        }
                        else if (!_needClosingTbody)
                        {
                            _o.Write("<tbody>");
                            Cr();
                            _needClosingTbody = true;
                        }
                        _o.Write("<tr");
                        SourcePos(row);
                        _o.Write('>');
                        return true;
                    case TableCell cell:
                        Cr();
                        _o.Write(_inTableHeader ? "<th" : "<td");
                        if (cell.Alignment != TableAlignment.None)
                        {
                            string a = cell.Alignment == TableAlignment.Left ? "left" : cell.Alignment == TableAlignment.Center ? "center" : "right";
                            _o.Write(_opt.TableAlignment == TableAlignmentStyle.AlignAttribute ? " align=\"" + a + "\"" : " style=\"text-align: " + a + "\"");
                        }
                        SourcePos(cell);
                        _o.Write('>');
                        return true;
                    case TextInline t:
                        Esc(t.Text);
                        return false;
                    case CodeSpan cs:
                        _o.Write("<code>");
                        Esc(cs.Code);
                        _o.Write("</code>");
                        return false;
                    case SoftBreak:
                        switch (_opt.SoftBreak)
                        {
                            case SoftBreakStyle.Space: _o.Write(' '); break;
                            case SoftBreakStyle.LineBreak: _o.Write("<br" + Void + "\n"); break;
                            default: _o.Write('\n'); break;
                        }
                        return false;
                    case HardBreak:
                        _o.Write("<br" + Void + "\n");
                        return false;
                    case HtmlInline hi:
                        WriteRawHtml(hi.Html, false);
                        return false;
                    case Emphasis:
                        _o.Write("<em>");
                        return true;
                    case Strong:
                        _o.Write("<strong>");
                        return true;
                    case Strikethrough:
                        _o.Write("<del>");
                        return true;
                    case Link link:
                        _o.Write("<a href=\"");
                        WriteUrl(link.Destination);
                        _o.Write('"');
                        if (!string.IsNullOrEmpty(link.Title))
                        {
                            _o.Write(" title=\"");
                            Esc(link.Title);
                            _o.Write('"');
                        }
                        _o.Write('>');
                        return true;
                    case Image img:
                        _o.Write("<img src=\"");
                        WriteUrl(img.Destination);
                        _o.Write("\" alt=\"");
                        WriteAltText(img);
                        _o.Write('"');
                        if (!string.IsNullOrEmpty(img.Title))
                        {
                            _o.Write(" title=\"");
                            Esc(img.Title);
                            _o.Write('"');
                        }
                        _o.Write(Void);
                        return false;
                    default:
                        return true;
                }
            }

            private void Exit(MarkdownNode node)
            {
                switch (node)
                {
                    case BlockQuote:
                        Cr();
                        _o.Write("</blockquote>\n");
                        break;
                    case ListBlock list:
                        Cr();
                        _o.Write(list.ListKind == ListKind.Bullet ? "</ul>\n" : "</ol>\n");
                        break;
                    case ListItem:
                        _o.Write("</li>\n");
                        break;
                    case Heading h:
                        _o.Write("</h" + h.Level + ">\n");
                        break;
                    case Paragraph p:
                        if (!InTightList(p)) _o.Write("</p>\n");
                        break;
                    case Table:
                        if (_needClosingTbody)
                        {
                            Cr();
                            _o.Write("</tbody>");
                            Cr();
                        }
                        _needClosingTbody = false;
                        Cr();
                        _o.Write("</table>");
                        Cr();
                        break;
                    case TableRow row:
                        Cr();
                        _o.Write("</tr>");
                        if (row.IsHeader)
                        {
                            Cr();
                            _o.Write("</thead>");
                            _inTableHeader = false;
                        }
                        break;
                    case TableCell:
                        _o.Write(_inTableHeader ? "</th>" : "</td>");
                        break;
                    case Emphasis:
                        _o.Write("</em>");
                        break;
                    case Strong:
                        _o.Write("</strong>");
                        break;
                    case Strikethrough:
                        _o.Write("</del>");
                        break;
                    case Link:
                        _o.Write("</a>");
                        break;
                }
            }

            private void WriteTaskMarker(bool isChecked)
            {
                switch (_opt.TaskLists)
                {
                    case TaskListStyle.DisabledCheckbox:
                        _o.Write(isChecked ? "<input type=\"checkbox\" checked=\"\" disabled=\"\"" : "<input type=\"checkbox\" disabled=\"\"");
                        _o.Write(Void);
                        _o.Write(' ');
                        break;
                    case TaskListStyle.Glyph:
                        _o.Write(isChecked ? "<span class=\"task-checked\">☑</span> " : "<span class=\"task-unchecked\">☐</span> ");
                        break;
                    default:
                        _o.Write(isChecked ? "[x] " : "[ ] ");
                        break;
                }
            }

            private void WriteRawHtml(string html, bool block)
            {
                switch (_opt.RawHtml)
                {
                    case RawHtmlMode.Passthrough:
                        if (_opt.TagFilter) WriteTagFiltered(html);
                        else _o.Write(html);
                        break;
                    case RawHtmlMode.Escape:
                        if (block)
                        {
                            _o.Write("<p>");
                            Esc(html.TrimEnd('\n'));
                            _o.Write("</p>");
                        }
                        else
                        {
                            Esc(html);
                        }
                        break;
                    default:
                        break;
                }
            }

            private static readonly string[] s_filtered = { "title", "textarea", "style", "xmp", "iframe", "noembed", "noframes", "script", "plaintext" };

            private void WriteTagFiltered(string html)
            {
                int start = 0;
                for (int i = 0; i < html.Length; i++)
                {
                    if (html[i] != '<') continue;
                    if (!IsFilteredTag(html, i)) continue;
                    _o.Write(html.AsSpan(start, i - start));
                    _o.Write("&lt;");
                    start = i + 1;
                }
                _o.Write(html.AsSpan(start));
            }

            private static bool IsFilteredTag(string s, int i)
            {
                int j = i + 1;
                if (j < s.Length && s[j] == '/') j++;
                foreach (var name in s_filtered)
                {
                    if (j + name.Length > s.Length) continue;
                    if (string.Compare(s, j, name, 0, name.Length, StringComparison.OrdinalIgnoreCase) != 0) continue;
                    int k = j + name.Length;
                    if (k >= s.Length) return false;
                    char c = s[k];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f' || c == '\v' || c == '>') return true;
                    if (c == '/' && k + 1 < s.Length && s[k + 1] == '>') return true;
                }
                return false;
            }

            private void WriteUrl(string url)
            {
                if (_opt.UnsafeUrls == UnsafeUrlPolicy.Remove && IsDangerousUrl(url)) return;
                EscapeHref(_o, url);
            }

            private void WriteAltText(MarkdownNode img)
            {
                foreach (var d in img.Descendants())
                {
                    switch (d)
                    {
                        case TextInline t: Esc(t.Text); break;
                        case CodeSpan c: Esc(c.Code); break;
                        case HtmlInline h: Esc(h.Html); break;
                        case SoftBreak:
                        case HardBreak: _o.Write(' '); break;
                    }
                }
            }

            private string Slug(string text)
            {
                var sb = new StringBuilder(text.Length);
                foreach (char ch in text.Trim().ToLowerInvariant())
                {
                    if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_') sb.Append(ch);
                    else if (ch == ' ') sb.Append('-');
                }
                string slug = sb.ToString();
                if (_slugs.TryGetValue(slug, out int n))
                {
                    _slugs[slug] = n + 1;
                    slug = slug + "-" + (n + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                else
                {
                    _slugs[slug] = 0;
                }
                return slug;
            }
        }

        /// <summary>The cmark "safe" URL rule: true for javascript:, vbscript:, file: and non-image data: URLs.</summary>
        internal static bool IsDangerousUrl(string url)
        {
            string u = url.TrimStart();
            static bool Starts(string s, string p) => s.StartsWith(p, StringComparison.OrdinalIgnoreCase);
            if (Starts(u, "data:"))
            {
                return !(Starts(u, "data:image/png") || Starts(u, "data:image/gif") || Starts(u, "data:image/jpeg") || Starts(u, "data:image/webp"));
            }
            return Starts(u, "javascript:") || Starts(u, "vbscript:") || Starts(u, "file:");
        }

        private const string HrefSafe = "-_.+!*'(),%#@?=;:/,+&$~";

        /// <summary>cmark's href escaping: percent-encode unsafe chars (UTF-8), keep existing %XX, entity-escape &amp; and '.</summary>
        internal static void EscapeHref(OutputSink o, string url)
        {
            Span<byte> buf = stackalloc byte[4];
            for (int i = 0; i < url.Length; i++)
            {
                char c = url[i];
                if (c < 128 && (CharUtil.IsAsciiAlnum(c) || HrefSafe.IndexOf(c) >= 0))
                {
                    if (c == '&') o.Write("&amp;");
                    else if (c == '\'') o.Write("&#x27;");
                    else o.Write(c);
                    continue;
                }
                int cp = c;
                if (char.IsHighSurrogate(c) && i + 1 < url.Length && char.IsLowSurrogate(url[i + 1]))
                {
                    cp = char.ConvertToUtf32(c, url[i + 1]);
                    i++;
                }
                else if (char.IsSurrogate(c))
                {
                    cp = 0xFFFD;
                }
                int n = new Rune(cp).EncodeToUtf8(buf);
                for (int k = 0; k < n; k++)
                {
                    o.Write('%');
                    o.Write("0123456789ABCDEF"[buf[k] >> 4]);
                    o.Write("0123456789ABCDEF"[buf[k] & 15]);
                }
            }
        }
    }
}
