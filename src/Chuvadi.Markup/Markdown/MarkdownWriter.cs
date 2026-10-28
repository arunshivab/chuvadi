using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Chuvadi.Markup.Internal;

namespace Chuvadi.Markup.Markdown
{
    /// <summary>Options for <see cref="MarkdownWriter"/>.</summary>
    public sealed class MarkdownWriterOptions
    {
        /// <summary>Bullet list marker ('-', '*' or '+'). Default '-'.</summary>
        public char BulletChar { get; set; } = '-';

        /// <summary>Emphasis delimiter ('*' or '_'). Default '*'.</summary>
        public char EmphasisChar { get; set; } = '*';

        /// <summary>Strong emphasis delimiter ('*' or '_', doubled). Default '*'.</summary>
        public char StrongChar { get; set; } = '*';

        /// <summary>Ordered list delimiter ('.' or ')'). Default '.'.</summary>
        public char OrderedDelimiter { get; set; } = '.';

        /// <summary>Fence character for code blocks ('`' or '~'). Default '`'.</summary>
        public char FenceChar { get; set; } = '`';

        /// <summary>Write indented code blocks (no info string) as indented rather than fenced. Default false.</summary>
        public bool PreserveIndentedCode { get; set; }

        /// <summary>Thematic break text. Default <c>---</c>.</summary>
        public string ThematicBreak { get; set; } = "---";

        /// <summary>Write hard breaks as a backslash (true) or two trailing spaces (false). Default true.</summary>
        public bool BackslashHardBreaks { get; set; } = true;

        /// <summary>Use setext (underlined) headings for levels 1 and 2. Default false (ATX <c>#</c>).</summary>
        public bool SetextHeadings { get; set; }

        /// <summary>Keep the original node styles where the AST records them (bullet, emphasis char, fences, setext, hard break kind). Default true.</summary>
        public bool PreserveSourceStyle { get; set; } = true;

        /// <summary>Pad table cells so columns line up. Default true.</summary>
        public bool AlignTableColumns { get; set; } = true;

        /// <summary>Write link reference definitions (from <see cref="MarkdownDocument.LinkDefinitions"/>) for reference-style links and keep the reference style. Default false: all links become inline links.</summary>
        public bool PreserveReferenceLinks { get; set; }

        /// <summary>Output limits. Default <c>new MarkupLimits()</c>.</summary>
        public MarkupLimits Limits { get; set; } = new MarkupLimits();
    }

    /// <summary>
    /// Writes a Markdown AST back to CommonMark/GFM text. Text is escaped so that re-parsing the output
    /// yields the same structure (verified against the CommonMark and GFM spec corpora).
    /// </summary>
    public static class MarkdownWriter
    {
        /// <summary>Writes a document (or any block or inline subtree) to Markdown text.</summary>
        public static string Write(MarkdownNode node, MarkdownWriterOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(node);
            options ??= new MarkdownWriterOptions();
            var w = new Impl(options, cancellationToken);
            string text = w.Run(node);
            if (text.Length > options.Limits.MaxOutputChars)
                throw new MarkupLimitExceededException(nameof(MarkupLimits.MaxOutputChars), options.Limits.MaxOutputChars, text.Length);
            return text;
        }

        /// <summary>Writes Markdown text to a writer.</summary>
        public static void Write(MarkdownNode node, TextWriter writer, MarkdownWriterOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.Write(Write(node, options, cancellationToken));
        }

        private sealed class Impl
        {
            private readonly MarkdownWriterOptions _o;
            private CancellationPoll _poll;
            private MarkdownDocument? _doc;
            private readonly List<LinkReferenceDefinition> _usedDefs = new();
            private readonly HashSet<string> _usedLabels = new(StringComparer.Ordinal);
            private int _depth;
            private char _lastBullet, _lastDelim;
            private bool _forceFence;
            private bool _inTableCell;

            public Impl(MarkdownWriterOptions o, CancellationToken ct)
            {
                _o = o;
                _poll = new CancellationPoll(ct);
            }

            public string Run(MarkdownNode node)
            {
                for (var p = node; p is not null; p = p.Parent)
                    if (p is MarkdownDocument d) _doc = d;
                if (!node.IsBlock)
                    return Inlines(new[] { node }, false);
                var lines = new List<string>();
                if (node is MarkdownDocument or BlockQuote or ListItem or ListBlock or Table)
                {
                    if (node is MarkdownDocument) BlockSequence(node.Children, lines, false);
                    else Block(node, lines);
                }
                else Block(node, lines);
                if (_o.PreserveReferenceLinks && _usedDefs.Count > 0)
                {
                    if (lines.Count > 0) lines.Add(string.Empty);
                    foreach (var def in _usedDefs)
                    {
                        string line = "[" + EscapeLabel(def.Label) + "]: " + Destination(def.Destination);
                        if (def.Title is not null) line += " " + Title(def.Title);
                        lines.Add(line);
                    }
                }
                var sb = new StringBuilder();
                foreach (var l in lines) sb.Append(l).Append('\n');
                return sb.ToString();
            }

            // ---------------- blocks ----------------

            private void BlockSequence(IReadOnlyList<MarkdownNode> blocks, List<string> lines, bool tight)
            {
                MarkdownNode? prev = null;
                foreach (var b in blocks)
                {
                    _poll.Tick();
                    if (prev is not null)
                    {
                        bool needBlank = !tight || prev is HtmlBlock || (prev is ListBlock && b is ListBlock) || (prev is CodeBlock { IsFenced: false } && b is CodeBlock { IsFenced: false });
                        if (needBlank) lines.Add(string.Empty);
                        else if (prev is ListBlock && b is not ListBlock && b is not Paragraph) { }
                    }
                    _forceFence = prev is ListBlock;
                    if (b is ListBlock list && prev is ListBlock prevList && prevList.ListKind == list.ListKind)
                        Block(b, lines, alternate: true);
                    else
                        Block(b, lines);
                    _forceFence = false;
                    prev = b;
                }
            }

            private void Block(MarkdownNode b, List<string> lines, bool alternate = false)
            {
                if (++_depth > 10_000) throw new MarkupLimitExceededException(nameof(MarkupLimits.MaxNestingDepth), 10_000, _depth);
                try
                {
                    switch (b)
                    {
                        case Paragraph p:
                            foreach (var l in SplitLines(Inlines(p.Children, true))) lines.Add(l);
                            break;
                        case Heading h:
                            WriteHeading(h, lines);
                            break;
                        case ThematicBreak tb:
                            lines.Add(_o.PreserveSourceStyle && tb.Marker != '-' ? new string(tb.Marker, 3) : _o.ThematicBreak);
                            break;
                        case CodeBlock code:
                            WriteCode(code, lines);
                            break;
                        case HtmlBlock html:
                            foreach (var l in SplitLines(html.Html.TrimEnd('\n'))) lines.Add(l);
                            break;
                        case BlockQuote bq:
                        {
                            var inner = new List<string>();
                            BlockSequence(bq.Children, inner, false);
                            if (inner.Count == 0) lines.Add(">");
                            foreach (var l in inner) lines.Add(l.Length == 0 ? ">" : "> " + l);
                            break;
                        }
                        case ListBlock list:
                            WriteList(list, lines, alternate);
                            break;
                        case ListItem item:
                            WriteItem(item, "- ", lines, true);
                            break;
                        case Table table:
                            WriteTable(table, lines);
                            break;
                        case MarkdownDocument doc:
                            BlockSequence(doc.Children, lines, false);
                            break;
                        default:
                            foreach (var l in SplitLines(Inlines(b.Children, true))) lines.Add(l);
                            break;
                    }
                }
                finally
                {
                    _depth--;
                }
            }

            private void WriteHeading(Heading h, List<string> lines)
            {
                string text = Inlines(h.Children, true);
                bool multiLine = text.IndexOf('\n') >= 0;
                bool setext = h.Level <= 2 && (multiLine || (_o.PreserveSourceStyle ? h.IsSetext : _o.SetextHeadings));
                if (setext && text.Trim().Length > 0)
                {
                    var tl = SplitLines(text);
                    lines.AddRange(tl);
                    int w = 3;
                    foreach (var l in tl) w = Math.Max(w, Math.Min(l.Length, 80));
                    lines.Add(new string(h.Level == 1 ? '=' : '-', w));
                    return;
                }
                text = text.Replace('\n', ' ');
                string prefix = new string('#', h.Level);
                if (text.Length == 0) { lines.Add(prefix); return; }
                // a trailing run of #'s would be taken as a closing sequence
                if (text.EndsWith('#')) text = text.Substring(0, text.Length - 1) + "\\#";
                lines.Add(prefix + " " + text);
            }

            private void WriteCode(CodeBlock code, List<string> lines)
            {
                string body = code.Code;
                if (body.Length > 0 && !body.EndsWith('\n')) body += "\n";
                bool indented = !_forceFence && !code.IsFenced && (_o.PreserveIndentedCode || (_o.PreserveSourceStyle && !code.IsFenced)) && code.Info.Length == 0
                    && body.Length > 0 && body.Trim('\n', ' ').Length > 0 && !StartsWithBlankLine(body);
                if (indented)
                {
                    foreach (var l in SplitLines(body.Substring(0, body.Length - 1))) lines.Add(l.Length == 0 ? string.Empty : "    " + l);
                    return;
                }
                char fc = _o.PreserveSourceStyle && code.IsFenced ? code.FenceChar : _o.FenceChar;
                if (fc == '`' && code.Info.IndexOf('`') >= 0) fc = '~';
                int longest = LongestLineStartRun(body, fc);
                int len = Math.Max(Math.Max(3, longest + 1), _o.PreserveSourceStyle && code.IsFenced && code.FenceChar == fc ? code.FenceLength : 3);
                string fence = new string(fc, len);
                lines.Add(fence + EscapeInfo(code.Info));
                if (body.Length > 0)
                    foreach (var l in SplitLines(body.Substring(0, body.Length - 1))) lines.Add(l);
                lines.Add(fence);
            }

            private static bool StartsWithBlankLine(string body)
            {
                int nl = body.IndexOf('\n');
                return nl >= 0 && body.Substring(0, nl).Trim(' ', '\t').Length == 0;
            }

            private static string EscapeInfo(string info)
            {
                if (info.Length == 0) return info;
                var sb = new StringBuilder();
                foreach (char c in info)
                {
                    if (c == '\\' || c == '&' || c == '`') sb.Append('\\');
                    sb.Append(c);
                }
                return sb.ToString();
            }

            private static int LongestLineStartRun(string body, char fc)
            {
                int longest = 0;
                foreach (var raw in body.Split('\n'))
                {
                    string l = raw.TrimStart(' ');
                    int n = 0;
                    while (n < l.Length && l[n] == fc) n++;
                    longest = Math.Max(longest, n);
                }
                return longest;
            }

            private void WriteList(ListBlock list, List<string> lines, bool alternate)
            {
                int number = list.Start;
                char bullet = _o.PreserveSourceStyle ? list.BulletChar : _o.BulletChar;
                char delim = _o.PreserveSourceStyle ? list.Delimiter : _o.OrderedDelimiter;
                if (alternate)
                {
                    if (list.ListKind == ListKind.Bullet && bullet == _lastBullet) bullet = bullet == '-' ? '*' : '-';
                    else if (list.ListKind == ListKind.Ordered && delim == _lastDelim) delim = delim == '.' ? ')' : '.';
                }
                if (list.ListKind == ListKind.Bullet) _lastBullet = bullet; else _lastDelim = delim;
                bool first = true;
                foreach (var child in list.Children)
                {
                    if (child is not ListItem item) continue;
                    if (!first && !list.IsTight) lines.Add(string.Empty);
                    string marker = list.ListKind == ListKind.Bullet
                        ? bullet + " "
                        : number.ToString(CultureInfo.InvariantCulture) + delim + " ";
                    WriteItem(item, marker, lines, list.IsTight);
                    number++;
                    first = false;
                }
            }

            private void WriteItem(ListItem item, string marker, List<string> lines, bool tight)
            {
                var inner = new List<string>();
                BlockSequence(item.Children, inner, tight);
                if (item.TaskState != TaskState.None)
                {
                    string box = item.TaskState == TaskState.Checked ? "[x] " : "[ ] ";
                    if (inner.Count == 0 || item.Children.Count == 0 || item.Children[0] is not Paragraph) inner.Insert(0, box.TrimEnd());
                    else inner[0] = box + inner[0];
                }
                string pad = new string(' ', marker.Length);
                if (inner.Count == 0)
                {
                    lines.Add(marker.TrimEnd());
                    return;
                }
                // a first line that is itself indented code needs the item to start empty
                if (inner[0].StartsWith("    ", StringComparison.Ordinal))
                {
                    lines.Add(marker.TrimEnd());
                    foreach (var l in inner) lines.Add(l.Length == 0 ? string.Empty : pad + l);
                    return;
                }
                if (item.Children.Count > 0 && item.Children[0] is ThematicBreak && inner[0].Length > 0 && inner[0].Trim().Trim(marker[0]).Length == 0)
                    inner[0] = marker[0] == '_' ? "***" : "___"; // "- ---" would read as one thematic break
                for (int i = 0; i < inner.Count; i++)
                {
                    string l = inner[i];
                    if (i == 0) lines.Add(marker + l);
                    else lines.Add(l.Length == 0 ? string.Empty : pad + l);
                }
            }

            private void WriteTable(Table table, List<string> lines)
            {
                var rows = new List<List<string>>();
                int cols = table.Alignments.Count;
                foreach (var r in table.Children)
                {
                    var cells = new List<string>();
                    foreach (var c in r.Children)
                    {
                        _inTableCell = true;
                        string t;
                        try { t = Inlines(c.Children, false).Replace("\n", " "); }
                        finally { _inTableCell = false; }
                        cells.Add(t.Length == 0 ? string.Empty : t);
                    }
                    cols = Math.Max(cols, cells.Count);
                    rows.Add(cells);
                }
                if (cols == 0) cols = 1;
                var widths = new int[cols];
                for (int i = 0; i < cols; i++) widths[i] = 3;
                if (_o.AlignTableColumns)
                    foreach (var r in rows)
                        for (int i = 0; i < r.Count; i++) widths[i] = Math.Max(widths[i], r[i].Length);
                string Row(List<string> cells)
                {
                    var sb = new StringBuilder("|");
                    for (int i = 0; i < cols; i++)
                    {
                        string c = i < cells.Count ? cells[i] : string.Empty;
                        sb.Append(' ').Append(_o.AlignTableColumns ? c.PadRight(widths[i]) : c).Append(" |");
                    }
                    return sb.ToString();
                }
                if (rows.Count == 0) rows.Add(new List<string>());
                lines.Add(Row(rows[0]));
                var d = new StringBuilder("|");
                for (int i = 0; i < cols; i++)
                {
                    var a = i < table.Alignments.Count ? table.Alignments[i] : TableAlignment.None;
                    int w = _o.AlignTableColumns ? widths[i] : 3;
                    string dash = new string('-', Math.Max(1, w - (a == TableAlignment.Center ? 2 : a == TableAlignment.None ? 0 : 1)));
                    d.Append(' ').Append(a switch
                    {
                        TableAlignment.Left => ":" + dash,
                        TableAlignment.Right => dash + ":",
                        TableAlignment.Center => ":" + dash + ":",
                        _ => dash,
                    }).Append(" |");
                }
                lines.Add(d.ToString());
                for (int i = 1; i < rows.Count; i++) lines.Add(Row(rows[i]));
            }

            private static List<string> SplitLines(string s)
            {
                var list = new List<string>(s.Split('\n'));
                return list;
            }

            // ---------------- inlines ----------------

            private string Inlines(IReadOnlyList<MarkdownNode> nodes, bool block)
            {
                var sb = new StringBuilder();
                bool atLineStart = true;
                InlineList(nodes, sb, ref atLineStart);
                return sb.ToString();
            }

            private void InlineList(IReadOnlyList<MarkdownNode> nodes, StringBuilder sb, ref bool atLineStart)
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    _poll.Tick();
                    var n = nodes[i];
                    var next = i + 1 < nodes.Count ? nodes[i + 1] : null;
                    Inline(n, next, sb, ref atLineStart);
                }
            }

            private void Inline(MarkdownNode n, MarkdownNode? next, StringBuilder sb, ref bool atLineStart)
            {
                if (++_depth > 10_000) throw new MarkupLimitExceededException(nameof(MarkupLimits.MaxNestingDepth), 10_000, _depth);
                try
                {
                    switch (n)
                    {
                        case TextInline t:
                            EscapeText(t.Text, sb, ref atLineStart, next);
                            break;
                        case SoftBreak:
                            TrimTrailingSpaces(sb);
                            sb.Append('\n');
                            atLineStart = true;
                            break;
                        case HardBreak hb:
                            TrimTrailingSpaces(sb);
                            bool backslash = _o.PreserveSourceStyle ? hb.IsBackslash || _o.BackslashHardBreaks : _o.BackslashHardBreaks;
                            sb.Append(backslash ? "\\\n" : "  \n");
                            atLineStart = true;
                            break;
                        case CodeSpan cs:
                            sb.Append(_inTableCell ? CodeSpanText(cs.Code).Replace("|", "\\|") : CodeSpanText(cs.Code));
                            atLineStart = false;
                            break;
                        case HtmlInline hi:
                            sb.Append(_inTableCell ? hi.Html.Replace("|", "\\|") : hi.Html);
                            atLineStart = hi.Html.EndsWith('\n');
                            break;
                        case Emphasis em:
                            Delimited(em.Children, new string(EmphasisChar(em, _o.PreserveSourceStyle ? em.Delimiter : _o.EmphasisChar), 1), sb, ref atLineStart);
                            break;
                        case Strong st:
                            Delimited(st.Children, new string(EmphasisChar(st, _o.PreserveSourceStyle ? st.Delimiter : _o.StrongChar), 2), sb, ref atLineStart);
                            break;
                        case Strikethrough s:
                            Delimited(s.Children, "~~", sb, ref atLineStart);
                            break;
                        case Link link:
                            WriteLink(link, sb, ref atLineStart);
                            break;
                        case Image img:
                            sb.Append("![");
                            bool a2 = false;
                            InlineList(img.Children, sb, ref a2);
                            sb.Append(']');
                            WriteTarget(img, sb);
                            atLineStart = false;
                            break;
                        default:
                            InlineList(n.Children, sb, ref atLineStart);
                            break;
                    }
                }
                finally
                {
                    _depth--;
                }
            }

            private readonly Stack<char> _emphasisChars = new();

            /// <summary>
            /// An emphasis at the very start or end of an enclosing emphasis that uses the same character
            /// would merge with it (*(*a*)* reads as **a**); such a node switches to the other character,
            /// which is always flanking-safe there because the enclosing delimiters are punctuation.
            /// </summary>
            private char EmphasisChar(MarkdownNode node, char wanted)
            {
                // only a single-delimiter emphasis merges: *(**a**)* and **(**a**)** re-read correctly
                if (node is not Emphasis || _emphasisChars.Count == 0 || node.Parent is null) return wanted;
                char enclosing = _emphasisChars.Peek();
                if (enclosing != wanted) return wanted;
                var siblings = node.Parent.Children;
                bool atEdge = siblings.Count > 0 && (ReferenceEquals(siblings[0], node) || ReferenceEquals(siblings[siblings.Count - 1], node));
                if (!atEdge) return wanted;
                return wanted == '*' ? '_' : '*';
            }

            private void Delimited(IReadOnlyList<MarkdownNode> children, string delim, StringBuilder sb, ref bool atLineStart)
            {
                if (delim[0] == '*' || delim[0] == '_') _emphasisChars.Push(delim[0]);
                try
                {
                    DelimitedCore(children, delim, sb, ref atLineStart);
                }
                finally
                {
                    if (delim[0] == '*' || delim[0] == '_') _emphasisChars.Pop();
                }
            }

            private void DelimitedCore(IReadOnlyList<MarkdownNode> children, string delim, StringBuilder sb, ref bool atLineStart)
            {
                var inner = new StringBuilder();
                bool a = false;
                InlineList(children, inner, ref a);
                string s = inner.ToString();
                if (s.Length == 0) return;
                // whitespace at the edges would stop the delimiters from flanking: move it outside
                int lead = 0;
                while (lead < s.Length && (s[lead] == ' ' || s[lead] == '\t')) lead++;
                int trail = 0;
                while (trail < s.Length - lead && (s[s.Length - 1 - trail] == ' ' || s[s.Length - 1 - trail] == '\t')) trail++;
                string core = s.Substring(lead, s.Length - lead - trail);
                if (lead > 0) { sb.Append(atLineStart ? "&#32;" : s.Substring(0, lead)); atLineStart = false; }
                if (core.Length == 0) return;
                sb.Append(delim).Append(core).Append(delim);
                if (trail > 0) sb.Append(s, s.Length - trail, trail);
                atLineStart = false;
            }

            private void WriteLink(Link link, StringBuilder sb, ref bool atLineStart)
            {
                string text = link.ToPlainText();
                if (link.Style == LinkStyle.Autolink && link.Children.Count == 1 && link.Children[0] is TextInline t)
                {
                    string expect = link.IsEmail ? "mailto:" + t.Text : t.Text;
                    if (expect == link.Destination && t.Text.IndexOfAny(new[] { '<', '>', ' ', '\n' }) < 0)
                    {
                        sb.Append('<').Append(t.Text).Append('>');
                        atLineStart = false;
                        return;
                    }
                }
                if (link.Style == LinkStyle.ExtendedAutolink && link.Children.Count == 1 && link.Children[0] is TextInline et)
                {
                    string d = link.Destination;
                    if (d == et.Text && Scanners_IsAutolinkSafe(d))
                    {
                        sb.Append('<').Append(d).Append('>');
                        atLineStart = false;
                        return;
                    }
                }
                sb.Append('[');
                bool a = false;
                var inner = new StringBuilder();
                InlineList(link.Children, inner, ref a);
                sb.Append(inner);
                sb.Append(']');
                WriteTarget(link, sb);
                atLineStart = false;
            }

            private void WriteTarget(LinkBase link, StringBuilder sb)
            {
                if (_o.PreserveReferenceLinks && link.Label is not null && _doc is not null
                    && _doc.LinkDefinitions.TryGetValue(LinkReferenceDefinition.NormalizeLabel(link.Label), out var def)
                    && def.Destination == link.Destination && def.Title == link.Title)
                {
                    if (_usedLabels.Add(LinkReferenceDefinition.NormalizeLabel(def.Label))) _usedDefs.Add(def);
                    sb.Append('[').Append(EscapeLabel(link.Label)).Append(']');
                    return;
                }
                sb.Append('(').Append(Destination(link.Destination));
                if (link.Title is not null) sb.Append(' ').Append(Title(link.Title));
                sb.Append(')');
            }

            private static bool Scanners_IsAutolinkSafe(string d)
                => d.IndexOfAny(new[] { '<', '>', ' ', '\n', '\r', '\t' }) < 0 && Parsing.Scanners.AutolinkUri(d + ">", 0) == d.Length + 1;

            private static string EscapeLabel(string label)
            {
                var sb = new StringBuilder();
                foreach (char c in label)
                {
                    if (c == '[' || c == ']' || c == '\\') sb.Append('\\');
                    sb.Append(c);
                }
                return sb.ToString();
            }

            private static string Destination(string dest)
            {
                var sb = new StringBuilder("<");
                foreach (char c in dest)
                {
                    if (c == '<' || c == '>' || c == '\\') sb.Append('\\').Append(c);
                    else if (c == '\n') sb.Append("%0A");
                    else if (c == '\r') sb.Append("%0D");
                    else if (c == '&') sb.Append("\\&");
                    else sb.Append(c);
                }
                return sb.Append('>').ToString();
            }

            private static string Title(string title)
            {
                var sb = new StringBuilder("\"");
                foreach (char c in title)
                {
                    if (c == '"' || c == '\\' || c == '&') sb.Append('\\');
                    sb.Append(c);
                }
                return sb.Append('"').ToString();
            }

            private static string CodeSpanText(string code)
            {
                int longest = 0, run = 0;
                foreach (char c in code)
                {
                    run = c == '`' ? run + 1 : 0;
                    longest = Math.Max(longest, run);
                }
                // choose a fence length not used inside the code
                int n = 1;
                while (ContainsRunOf(code, n)) n++;
                string fence = new string('`', n);
                bool pad = code.Length > 0 && (code[0] == '`' || code[code.Length - 1] == '`'
                    || (code[0] == ' ' && code[code.Length - 1] == ' ' && code.Trim(' ').Length > 0));
                if (code.Length == 0) return fence + " " + fence; // cannot be written exactly; nearest form
                return pad ? fence + " " + code + " " + fence : fence + code + fence;
            }

            private static bool ContainsRunOf(string s, int n)
            {
                int run = 0;
                for (int i = 0; i <= s.Length; i++)
                {
                    if (i < s.Length && s[i] == '`') { run++; continue; }
                    if (run == n) return true;
                    run = 0;
                }
                return false;
            }

            private static void TrimTrailingSpaces(StringBuilder sb)
            {
                // trailing spaces before a soft break would turn it into a hard break
                int n = sb.Length;
                while (n > 0 && (sb[n - 1] == ' ' || sb[n - 1] == '\t')) n--;
                if (n < sb.Length)
                {
                    string spaces = sb.ToString(n, sb.Length - n);
                    sb.Length = n;
                    foreach (char c in spaces) sb.Append(c == ' ' ? "&#32;" : "&#9;");
                }
            }

            private const string AlwaysEscape = "\\`*_[]<>|~";

            private void EscapeText(string text, StringBuilder sb, ref bool atLineStart, MarkdownNode? next)
            {
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    if (atLineStart)
                    {
                        if (c == ' ' || c == '\t')
                        {
                            sb.Append(c == ' ' ? "&#32;" : "&#9;");
                            continue;
                        }
                        if (c == '#' || c == '-' || c == '+' || c == '=' || c == '>')
                        {
                            sb.Append('\\').Append(c);
                            atLineStart = false;
                            continue;
                        }
                        if (CharUtil.IsAsciiDigit(c))
                        {
                            int j = i;
                            while (j < text.Length && CharUtil.IsAsciiDigit(text[j]) && j - i < 10) j++;
                            if (j < text.Length && (text[j] == '.' || text[j] == ')'))
                            {
                                sb.Append(text, i, j - i).Append('\\').Append(text[j]);
                                i = j;
                                atLineStart = false;
                                continue;
                            }
                        }
                        atLineStart = false;
                    }
                    if (c == '\n')
                    {
                        sb.Append("&#10;");
                        continue;
                    }
                    if (c == '\r')
                    {
                        sb.Append("&#13;");
                        continue;
                    }
                    if (AlwaysEscape.IndexOf(c) >= 0)
                    {
                        sb.Append('\\').Append(c);
                        continue;
                    }
                    if (c == '!' && ((i + 1 < text.Length && text[i + 1] == '[') || (i + 1 == text.Length && next is Link)))
                    {
                        sb.Append("\\!");
                        continue;
                    }
                    if (c == '&')
                    {
                        sb.Append("&amp;");
                        continue;
                    }
                    if (c == ':' || c == '@' || c == 'w')
                    {
                        // bare autolink triggers are harmless here: the parser re-links them only as text
                        sb.Append(c);
                        continue;
                    }
                    sb.Append(c);
                }
            }
        }
    }
}
