using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Chuvadi.Markup.Internal;
using Chuvadi.Markup.Markdown.Parsing;
using Chuvadi.Markup.Sanitization;

namespace Chuvadi.Markup.Text
{
    /// <summary>How plain text is laid out in HTML.</summary>
    public enum TextLayout
    {
        /// <summary>A <c>div</c> with <c>white-space: pre-wrap</c>: every space and line break kept, long lines wrap. Default.</summary>
        PreWrap,
        /// <summary>A <c>pre</c> element (monospace, no wrapping unless styled).</summary>
        Preformatted,
        /// <summary>Paragraphs at blank lines, <c>&lt;br&gt;</c> for single line breaks, runs of spaces kept with <c>&amp;nbsp;</c>.</summary>
        Paragraphs,
    }

    /// <summary>Options for <see cref="PlainText.ToHtml"/>.</summary>
    public sealed class TextToHtmlOptions
    {
        /// <summary>Layout. Default <see cref="TextLayout.PreWrap"/>.</summary>
        public TextLayout Layout { get; set; } = TextLayout.PreWrap;

        /// <summary>Replace tabs with spaces up to the next multiple of <see cref="TabSize"/>. Default false (the CSS <c>tab-size</c> is set instead).</summary>
        public bool ExpandTabs { get; set; }

        /// <summary>Tab stop width. Default 8.</summary>
        public int TabSize { get; set; } = 8;

        /// <summary>Turn <c>http(s)://</c>, <c>ftp://</c>, <c>www.</c> addresses and email addresses into links. Default true.</summary>
        public bool Autolink { get; set; } = true;

        /// <summary>How detected links are emitted (as in the sanitizer). Default <see cref="LinkHandling.Keep"/>.</summary>
        public LinkHandling Links { get; set; } = LinkHandling.Keep;

        /// <summary>Attribute holding the destination of inert or rewritten links. Default <c>data-chuvadi-href</c>.</summary>
        public string? InertLinkAttribute { get; set; } = "data-chuvadi-href";

        /// <summary>Template for <see cref="LinkHandling.Rewrite"/>; <c>{url}</c> is the percent-encoded destination.</summary>
        public string LinkRewriteTemplate { get; set; } = "#{url}";

        /// <summary>Add <c>target="_blank" rel="noopener noreferrer"</c> to kept links. Default true.</summary>
        public bool LinksOpenInNewWindow { get; set; } = true;

        /// <summary>Render mail quoting (lines starting with <c>&gt;</c>) as nested <c>blockquote</c>s. Default true.</summary>
        public bool QuoteLevels { get; set; } = true;

        /// <summary>Decode RFC 3676 format=flowed (soft line breaks joined, space-stuffing removed). Default false.</summary>
        public bool FormatFlowed { get; set; }

        /// <summary>RFC 3676 DelSp=yes: remove the trailing space of soft-broken lines when joining. Default false.</summary>
        public bool DelSp { get; set; }

        /// <summary>Class of the wrapper element (null for none). Default <c>chuvadi-text</c>.</summary>
        public string? WrapperClass { get; set; } = "chuvadi-text";

        /// <summary>Limits (input length, output size). Default <c>new MarkupLimits()</c>.</summary>
        public MarkupLimits Limits { get; set; } = new MarkupLimits();
    }

    /// <summary>Converts plain text (TXT files, text/plain mail) to safe HTML. All text is escaped; the only markup produced is structural.</summary>
    public static class PlainText
    {
        private readonly record struct Line(int Depth, string Text, bool Flowed);

        /// <summary>Converts plain text to HTML.</summary>
        public static string ToHtml(string text, TextToHtmlOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(text);
            options ??= new TextToHtmlOptions();
            options.Limits.Validate();
            Guard.InputLength(text.Length, options.Limits);
            var poll = new CancellationPoll(cancellationToken);
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            if (text.EndsWith('\n')) text = text.Substring(0, text.Length - 1);

            var lines = SplitLines(text, options, ref poll);
            var o = new OutputSink(options.Limits.MaxOutputChars, text.Length + 256);
            bool pre = options.Layout != TextLayout.Paragraphs;
            string tag = options.Layout == TextLayout.Preformatted ? "pre" : "div";
            o.Write('<');
            o.Write(tag);
            if (!string.IsNullOrEmpty(options.WrapperClass) && SanitizerEngine.IsValidClass(options.WrapperClass))
            {
                o.Write(" class=\"");
                o.Write(options.WrapperClass);
                o.Write('"');
            }
            if (options.Layout == TextLayout.PreWrap)
            {
                o.Write(" style=\"white-space: pre-wrap");
                if (!options.ExpandTabs) o.Write("; tab-size: " + Math.Max(1, options.TabSize));
                o.Write('"');
            }
            else if (options.Layout == TextLayout.Preformatted && !options.ExpandTabs)
            {
                o.Write(" style=\"tab-size: " + Math.Max(1, options.TabSize) + "\"");
            }
            o.Write('>');

            int depth = 0;
            int i = 0;
            while (i < lines.Count)
            {
                poll.Tick();
                int d = lines[i].Depth;
                while (depth < d) { o.Write("<blockquote>"); depth++; }
                while (depth > d) { o.Write("</blockquote>"); depth--; }
                // collect the run at this depth
                int j = i;
                var run = new StringBuilder();
                while (j < lines.Count && lines[j].Depth == d)
                {
                    if (j > i && !lines[j - 1].Flowed) run.Append('\n');
                    run.Append(lines[j].Text);
                    j++;
                }
                string block = run.ToString();
                if (pre)
                {
                    // newlines around blockquotes are implied by the block boundary
                    WriteText(o, block, options);
                }
                else
                {
                    WriteParagraphs(o, block, options);
                }
                i = j;
            }
            while (depth > 0) { o.Write("</blockquote>"); depth--; }
            o.Write("</");
            o.Write(tag);
            o.Write('>');
            return o.ToString();
        }

        private static List<Line> SplitLines(string text, TextToHtmlOptions options, ref CancellationPoll poll)
        {
            var result = new List<Line>();
            foreach (var raw in text.Split('\n'))
            {
                poll.Tick();
                string line = raw;
                int depth = 0;
                if (options.QuoteLevels || options.FormatFlowed)
                {
                    int k = 0;
                    while (k < line.Length && line[k] == '>')
                    {
                        depth++;
                        k++;
                        if (!options.FormatFlowed && k < line.Length && line[k] == ' ' && k + 1 < line.Length && line[k + 1] == '>') k++;
                    }
                    if (!options.QuoteLevels && !options.FormatFlowed) { depth = 0; k = 0; }
                    line = line.Substring(k);
                    if (depth > 0 && line.StartsWith(' ')) line = line.Substring(1);
                    else if (depth == 0 && options.FormatFlowed && line.StartsWith(' ')) line = line.Substring(1); // space-stuffing
                    if (!options.QuoteLevels)
                    {
                        line = raw;
                        depth = 0;
                    }
                }
                bool flowed = false;
                if (options.FormatFlowed && line.EndsWith(' ') && line != "-- ")
                {
                    flowed = true;
                    if (options.DelSp) line = line.Substring(0, line.Length - 1);
                }
                if (options.ExpandTabs && line.IndexOf('\t') >= 0) line = ExpandTabs(line, Math.Max(1, options.TabSize));
                depth = Math.Min(depth, Math.Max(1, options.Limits.MaxNestingDepth / 2));
                result.Add(new Line(depth, line, flowed));
            }
            return result;
        }

        private static string ExpandTabs(string s, int size)
        {
            var sb = new StringBuilder(s.Length + 16);
            int col = 0;
            foreach (char c in s)
            {
                if (c == '\t')
                {
                    int n = size - col % size;
                    sb.Append(' ', n);
                    col += n;
                }
                else
                {
                    sb.Append(c);
                    col++;
                }
            }
            return sb.ToString();
        }

        private static void WriteParagraphs(OutputSink o, string block, TextToHtmlOptions options)
        {
            var paras = block.Split("\n\n");
            foreach (var p in paras)
            {
                string t = p.Trim('\n');
                if (t.Length == 0) continue;
                o.Write("<p>");
                var lines = t.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    if (i > 0) o.Write("<br>");
                    WriteText(o, KeepSpaces(lines[i]), options, nbspAlreadyMarked: true);
                }
                o.Write("</p>");
            }
        }

        private const char NbspMarker = '';

        /// <summary>Marks runs of spaces (and leading spaces) so they survive HTML whitespace collapsing.</summary>
        private static string KeepSpaces(string line)
        {
            if (line.IndexOf("  ", StringComparison.Ordinal) < 0 && !line.StartsWith(' ')) return line;
            var sb = new StringBuilder(line.Length);
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                bool keep = c == ' ' && (i == 0 || line[i - 1] == ' ' || line[i - 1] == NbspMarker);
                sb.Append(keep ? NbspMarker : c);
            }
            return sb.ToString();
        }

        private static void WriteText(OutputSink o, string text, TextToHtmlOptions options, bool nbspAlreadyMarked = false)
        {
            int pos = 0;
            if (options.Autolink)
            {
                foreach (var (start, length, href) in FindLinks(text))
                {
                    if (start > pos) Escape(o, text.AsSpan(pos, start - pos), nbspAlreadyMarked);
                    WriteLink(o, href, text.Substring(start, length), options);
                    pos = start + length;
                }
            }
            if (pos < text.Length) Escape(o, text.AsSpan(pos), nbspAlreadyMarked);
        }

        private static void Escape(OutputSink o, ReadOnlySpan<char> s, bool nbsp)
        {
            int start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                string? rep = s[i] switch
                {
                    '&' => "&amp;",
                    '<' => "&lt;",
                    '>' => "&gt;",
                    '"' => "&quot;",
                    '\0' => "�",
                    NbspMarker when nbsp => "&nbsp;",
                    _ => null,
                };
                if (rep is null) continue;
                if (i > start) o.Write(s.Slice(start, i - start));
                o.Write(rep);
                start = i + 1;
            }
            if (start < s.Length) o.Write(s.Slice(start));
        }

        private static void WriteLink(OutputSink o, string href, string text, TextToHtmlOptions options)
        {
            switch (options.Links)
            {
                case LinkHandling.Inert:
                    o.Write("<a");
                    if (options.InertLinkAttribute is not null)
                    {
                        o.Write(' ');
                        o.Write(options.InertLinkAttribute);
                        o.Write("=\"");
                        Escape(o, href, false);
                        o.Write('"');
                    }
                    o.Write(" title=\"");
                    Escape(o, href, false);
                    o.Write("\">");
                    break;
                case LinkHandling.Rewrite:
                    o.Write("<a href=\"");
                    Escape(o, options.LinkRewriteTemplate.Replace("{url}", Uri.EscapeDataString(href), StringComparison.Ordinal), false);
                    o.Write('"');
                    if (options.InertLinkAttribute is not null)
                    {
                        o.Write(' ');
                        o.Write(options.InertLinkAttribute);
                        o.Write("=\"");
                        Escape(o, href, false);
                        o.Write('"');
                    }
                    o.Write('>');
                    break;
                default:
                    o.Write("<a href=\"");
                    Escape(o, href, false);
                    o.Write('"');
                    if (options.LinksOpenInNewWindow) o.Write(" target=\"_blank\" rel=\"noopener noreferrer\"");
                    o.Write('>');
                    break;
            }
            Escape(o, text, true);
            o.Write("</a>");
        }

        /// <summary>Finds URLs and email addresses (start, length, destination).</summary>
        internal static IEnumerable<(int Start, int Length, string Href)> FindLinks(string s)
        {
            string[] prefixes = { "https://", "http://", "ftp://", "www.", "mailto:" };
            var next = new int[prefixes.Length];
            for (int p = 0; p < prefixes.Length; p++) next[p] = -2;
            int nextAt = -2;
            int i = 0;
            while (i < s.Length)
            {
                // cached next occurrences keep the scan linear
                int best = -1;
                string? kind = null;
                for (int p = 0; p < prefixes.Length; p++)
                {
                    if (next[p] != -1 && next[p] < i) next[p] = s.IndexOf(prefixes[p], i, StringComparison.OrdinalIgnoreCase);
                    if (next[p] >= 0 && (best < 0 || next[p] < best)) { best = next[p]; kind = prefixes[p]; }
                }
                if (nextAt != -1 && nextAt < i) nextAt = s.IndexOf('@', i);
                int at = nextAt;
                int emailStart = -1, emailLen = 0;
                if (at > 0 && (best < 0 || at < best + 2048))
                {
                    int a = at;
                    while (a > i && at - a < 256 && (CharUtil.IsAsciiAlnum(s[a - 1]) || ".+-_".IndexOf(s[a - 1]) >= 0)) a--;
                    int b = at + 1;
                    int dots = 0;
                    while (b < s.Length && b - at < 256 && (CharUtil.IsAsciiAlnum(s[b]) || s[b] == '-' || s[b] == '.' || s[b] == '_'))
                    {
                        if (s[b] == '.' && b + 1 < s.Length && CharUtil.IsAsciiAlnum(s[b + 1])) dots++;
                        b++;
                    }
                    while (b > at + 1 && (s[b - 1] == '.' || s[b - 1] == '-' || s[b - 1] == '_')) b--;
                    if (a < at && b > at + 1 && dots > 0 && CharUtil.IsAsciiAlpha(s[b - 1]) && (a == 0 || !CharUtil.IsAsciiAlnum(s[a - 1])))
                    {
                        emailStart = a;
                        emailLen = b - a;
                    }
                }
                if (best < 0 && at < 0) yield break;
                if (emailStart >= 0 && (best < 0 || emailStart < best) && !(best >= 0 && best < emailStart + emailLen))
                {
                    yield return (emailStart, emailLen, "mailto:" + s.Substring(emailStart, emailLen));
                    i = emailStart + emailLen;
                    continue;
                }
                if (best < 0 || (at >= 0 && at < best && emailStart < 0))
                {
                    if (best < 0 || at < best)
                    {
                        i = at + 1;
                        continue;
                    }
                }
                // must start at a word boundary
                if (best > 0 && (CharUtil.IsAsciiAlnum(s[best - 1]) || s[best - 1] == '/' || s[best - 1] == '@'))
                {
                    i = best + kind!.Length;
                    continue;
                }
                int end = best + kind!.Length;
                while (end < s.Length && !char.IsWhiteSpace(s[end]) && s[end] != '<' && s[end] != '>' && s[end] != '"' && s[end] != '\uFFFD')
                    end++;
                int len = ExtendedAutolinks.AutolinkDelim(s, best, end - best);
                if (len <= kind.Length)
                {
                    i = best + kind.Length;
                    continue;
                }
                string text = s.Substring(best, len);
                string href = kind == "www." ? "http://" + text : text;
                yield return (best, len, href);
                i = best + len;
            }
        }
    }
}
