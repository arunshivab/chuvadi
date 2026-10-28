using System;
using System.Collections.Generic;
using System.Text;
using Chuvadi.Markup.Internal;

namespace Chuvadi.Markup.Markdown.Parsing
{
    /// <summary>
    /// CommonMark inline parser (delimiter-run and bracket algorithm of the reference implementation),
    /// with the GFM strikethrough and extended autolink extensions. Linear in the input apart from
    /// bounded rescans (link labels are capped at 999 chars, backtick and HTML-end scans are memoised).
    /// </summary>
    internal sealed class InlineParser
    {
        private const int MaxBackticks = 1000;
        private const int MaxLinkLabelLength = 999;

        private sealed class Delimiter
        {
            public Delimiter? Prev, Next;
            public InlineNode Inl = null!;
            public int Position;
            public char Char;
            public int Length;
            public int NumDelims;
            public bool CanOpen, CanClose;
        }

        private sealed class Bracket
        {
            public Bracket? Previous;
            public InlineNode Inl = null!;
            public int Position;
            public long Seq;
            public bool Image, BracketAfter;
        }

        private readonly MarkdownOptions _options;
        private readonly IReadOnlyDictionary<string, LinkReferenceDefinition> _refs;
        private readonly NodeBudget _budget;
        private CancellationPoll _poll;

        private string _s = string.Empty;
        private int _pos;
        private Delimiter? _lastDelim;
        private Bracket? _lastBracket;
        private readonly int[] _backticks = new int[MaxBackticks + 1];
        private bool _scannedForBackticks;
        private bool _noCommentEnd, _noPiEnd, _noCdataEnd, _noDeclEnd;
        private long _bracketSeq;
        private long _inactiveUpTo; // link openers with Seq <= this are inactive (a link has formed after them)
        private readonly long _maxWork;
        private long _work;
        // Delimiter runs whose text was shortened by emphasis matching; their literal is cut once, at the end
        // of ProcessEmphasis (cutting on every match copies the run each time: quadratic for long runs).
        private readonly List<InlineNode> _shortened = new List<InlineNode>();

        public InlineParser(MarkdownOptions options, IReadOnlyDictionary<string, LinkReferenceDefinition> refs, NodeBudget budget, CancellationPoll poll, int sourceLength)
        {
            _options = options;
            _refs = refs;
            _budget = budget;
            _poll = poll;
            _maxWork = (long)options.Limits.MaxParserWorkPerChar * ((long)sourceLength + 1024) + 1_000_000;
        }

        /// <summary>Counts inline-parser work against <see cref="MarkupLimits.MaxParserWorkPerChar"/> and polls cancellation.</summary>
        private void Work()
        {
            _poll.Tick();
            if (++_work > _maxWork)
                throw new MarkupLimitExceededException(nameof(MarkupLimits.MaxParserWorkPerChar), _maxWork, _work);
        }

        public InlineNode Parse(string subject)
        {
            _s = subject;
            _pos = 0;
            _lastDelim = null;
            _lastBracket = null;
            _scannedForBackticks = false;
            _noCommentEnd = _noPiEnd = _noCdataEnd = _noDeclEnd = false;
            _bracketSeq = 0;
            _inactiveUpTo = 0;
            var root = new InlineNode(InlineType.Container) { Start = 0, End = subject.Length };
            while (_pos < _s.Length)
            {
                Work();
                ParseInline(root);
            }
            ProcessEmphasis(0);
            while (_lastBracket is not null) _lastBracket = _lastBracket.Previous;
            if (_options.Autolinks)
                ExtendedAutolinks.ProcessEmails(root, _budget);
            return root;
        }

        private char Peek(int i) => i < _s.Length ? _s[i] : '\0';

        private InlineNode Make(InlineType type, int start, int end)
        {
            _budget.Take();
            return new InlineNode(type) { Start = start, End = end };
        }

        private InlineNode Text(string literal, int start, int end)
        {
            var n = Make(InlineType.Text, start, end);
            n.Literal = literal;
            return n;
        }

        private bool IsSpecial(char c)
        {
            switch (c)
            {
                case '\n':
                case '\r':
                case '`':
                case '\\':
                case '&':
                case '<':
                case '[':
                case ']':
                case '!':
                case '*':
                case '_':
                    return true;
                case '~':
                    return _options.Strikethrough;
                case ':':
                case 'w':
                    return _options.Autolinks;
                default:
                    return false;
            }
        }

        private void ParseInline(InlineNode parent)
        {
            int start = _pos;
            char c = _s[_pos];
            InlineNode? node = null;
            switch (c)
            {
                case '\n':
                case '\r':
                    node = HandleNewline();
                    break;
                case '`':
                    node = HandleBackticks();
                    break;
                case '\\':
                    node = HandleBackslash();
                    break;
                case '&':
                    node = HandleEntity();
                    break;
                case '<':
                    node = HandlePointyBrace();
                    break;
                case '*':
                case '_':
                    node = HandleDelim(c);
                    break;
                case '[':
                    _pos++;
                    node = Text("[", start, _pos);
                    parent.Append(node);
                    PushBracket(false, node);
                    return;
                case ']':
                    node = HandleCloseBracket(parent);
                    break;
                case '!':
                    _pos++;
                    if (Peek(_pos) == '[')
                    {
                        _pos++;
                        node = Text("![", start, _pos);
                        parent.Append(node);
                        PushBracket(true, node);
                        return;
                    }
                    node = Text("!", start, _pos);
                    break;
                default:
                    if (c == '~' && _options.Strikethrough)
                    {
                        node = HandleTilde();
                        break;
                    }
                    if ((c == ':' || c == 'w') && _options.Autolinks && _lastBracket is null)
                    {
                        node = c == ':' ? UrlMatch(parent) : WwwMatch();
                        if (node is not null) break;
                    }
                    node = HandleTextRun();
                    break;
            }
            if (node is not null) parent.Append(node);
        }

        private InlineNode HandleTextRun()
        {
            int start = _pos;
            int end = _pos + 1;
            while (end < _s.Length && !IsSpecial(_s[end])) end++;
            _pos = end;
            int textEnd = end;
            if (end < _s.Length && (_s[end] == '\n' || _s[end] == '\r'))
            {
                while (textEnd > start && (_s[textEnd - 1] == ' ' || _s[textEnd - 1] == '\t')) textEnd--;
            }
            return Text(_s.Substring(start, textEnd - start), start, textEnd);
        }

        private InlineNode HandleNewline()
        {
            int nlpos = _pos;
            if (_s[_pos] == '\r') _pos++;
            if (Peek(_pos) == '\n') _pos++;
            int end = _pos;
            while (_pos < _s.Length && (_s[_pos] == ' ' || _s[_pos] == '\t')) _pos++;
            if (nlpos > 1 && _s[nlpos - 1] == ' ' && _s[nlpos - 2] == ' ')
                return Make(InlineType.HardBreak, nlpos, end);
            return Make(InlineType.SoftBreak, nlpos, end);
        }

        private InlineNode HandleBackslash()
        {
            int start = _pos;
            _pos++;
            char c = Peek(_pos);
            if (CharUtil.IsAsciiPunctuation(c))
            {
                _pos++;
                return Text(c.ToString(), start, _pos);
            }
            if (c == '\n' || c == '\r')
            {
                if (c == '\r') _pos++;
                if (Peek(_pos) == '\n') _pos++;
                int end = _pos;
                while (_pos < _s.Length && (_s[_pos] == ' ' || _s[_pos] == '\t')) _pos++;
                var br = Make(InlineType.HardBreak, start, end);
                br.IsBackslash = true;
                return br;
            }
            return Text("\\", start, _pos);
        }

        private InlineNode HandleEntity()
        {
            int start = _pos;
            var sb = new StringBuilder();
            int e = Scanners.EntityReference(_s, _pos, sb);
            if (e < 0)
            {
                _pos++;
                return Text("&", start, _pos);
            }
            _pos = e;
            return Text(sb.ToString(), start, e);
        }

        // ---- code spans ----

        private InlineNode HandleBackticks()
        {
            int start = _pos;
            while (_pos < _s.Length && _s[_pos] == '`') _pos++;
            int n = _pos - start;
            int endpos = ScanToClosingBackticks(n);
            if (endpos == 0)
            {
                _pos = start + n;
                return Text(new string('`', n), start, _pos);
            }
            string raw = _s.Substring(start + n, endpos - n - (start + n));
            var sb = new StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c == '\r')
                {
                    sb.Append(' ');
                    if (i + 1 < raw.Length && raw[i + 1] == '\n') i++;
                }
                else if (c == '\n') sb.Append(' ');
                else sb.Append(c);
            }
            string code = sb.ToString();
            if (code.Length >= 2 && code[0] == ' ' && code[code.Length - 1] == ' ' && code.AsSpan().ContainsAnyExcept(' '))
                code = code.Substring(1, code.Length - 2);
            var node = Make(InlineType.Code, start, endpos);
            node.Literal = code;
            node.DelimiterLength = n;
            return node;
        }

        private int ScanToClosingBackticks(int openLength)
        {
            if (openLength > MaxBackticks) return 0;
            if (_scannedForBackticks && _backticks[openLength] <= _pos) return 0;
            while (true)
            {
                while (_pos < _s.Length && _s[_pos] != '`') _pos++;
                if (_pos >= _s.Length) break;
                int st = _pos;
                while (_pos < _s.Length && _s[_pos] == '`') _pos++;
                int numticks = _pos - st;
                if (numticks <= MaxBackticks) _backticks[numticks] = st;
                if (numticks == openLength) return _pos;
            }
            _scannedForBackticks = true;
            return 0;
        }

        // ---- autolinks and raw HTML ----

        private InlineNode HandlePointyBrace()
        {
            int start = _pos;
            int e = Scanners.AutolinkUri(_s, _pos + 1);
            if (e > 0)
            {
                string contents = _s.Substring(start + 1, e - start - 2);
                _pos = e;
                return MakeAutolink(DecodeEntities(contents), DecodeEntities(contents), false, start, e);
            }
            e = Scanners.AutolinkEmail(_s, _pos + 1);
            if (e > 0)
            {
                string contents = _s.Substring(start + 1, e - start - 2);
                _pos = e;
                return MakeAutolink("mailto:" + DecodeEntities(contents), DecodeEntities(contents), true, start, e);
            }
            e = HtmlTag(start);
            if (e > 0)
            {
                _pos = e;
                var h = Make(InlineType.Html, start, e);
                h.Literal = _s.Substring(start, e - start);
                return h;
            }
            _pos++;
            return Text("<", start, _pos);
        }

        private InlineNode MakeAutolink(string url, string text, bool email, int start, int end)
        {
            var link = Make(InlineType.Link, start, end);
            link.Destination = url;
            link.Style = LinkStyle.Autolink;
            link.IsEmail = email;
            link.Append(Text(text, start + 1, end - 1));
            return link;
        }

        private static string DecodeEntities(string s)
        {
            if (s.IndexOf('&') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '&')
                {
                    int e = Scanners.EntityReference(s, i, sb);
                    if (e > 0) { i = e - 1; continue; }
                }
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        private int HtmlTag(int i)
        {
            int e = Scanners.OpenTag(_s, i);
            if (e > 0) return e;
            e = Scanners.ClosingTag(_s, i);
            if (e > 0) return e;
            if (Peek(i + 1) == '!')
            {
                if (Peek(i + 2) == '-' && Peek(i + 3) == '-')
                {
                    if (Peek(i + 4) == '>') return i + 5;
                    if (Peek(i + 4) == '-' && Peek(i + 5) == '>') return i + 6;
                    if (_noCommentEnd) return -1;
                    int x = _s.IndexOf("-->", i + 4, StringComparison.Ordinal);
                    if (x < 0) { _noCommentEnd = true; return -1; }
                    return x + 3;
                }
                if (string.CompareOrdinal(_s, i, "<![CDATA[", 0, 9) == 0)
                {
                    if (_noCdataEnd) return -1;
                    int x = _s.IndexOf("]]>", i + 9, StringComparison.Ordinal);
                    if (x < 0) { _noCdataEnd = true; return -1; }
                    return x + 3;
                }
                if (CharUtil.IsAsciiAlpha(Peek(i + 2)))
                {
                    if (_noDeclEnd) return -1;
                    int x = _s.IndexOf('>', i + 2);
                    if (x < 0) { _noDeclEnd = true; return -1; }
                    return x + 1;
                }
                return -1;
            }
            if (Peek(i + 1) == '?')
            {
                if (_noPiEnd) return -1;
                int x = _s.IndexOf("?>", i + 2, StringComparison.Ordinal);
                if (x < 0) { _noPiEnd = true; return -1; }
                return x + 2;
            }
            return -1;
        }

        // ---- emphasis and strikethrough delimiters ----

        private void ScanDelims(char c, out int numdelims, out bool canOpen, out bool canClose)
        {
            int before = _pos == 0 ? '\n' : CharUtil.CodePointBefore(_s, _pos);
            int st = _pos;
            while (_pos < _s.Length && _s[_pos] == c) _pos++;
            numdelims = _pos - st;
            int after = _pos >= _s.Length ? '\n' : CharUtil.CodePointAt(_s, _pos);
            bool beforeWs = CharUtil.IsUnicodeWhitespace(before), afterWs = CharUtil.IsUnicodeWhitespace(after);
            bool beforePunct = CharUtil.IsUnicodePunctuation(before), afterPunct = CharUtil.IsUnicodePunctuation(after);
            bool left = numdelims > 0 && !afterWs && (!afterPunct || beforeWs || beforePunct);
            bool right = numdelims > 0 && !beforeWs && (!beforePunct || afterWs || afterPunct);
            if (c == '_')
            {
                canOpen = left && (!right || beforePunct);
                canClose = right && (!left || afterPunct);
            }
            else
            {
                canOpen = left;
                canClose = right;
            }
        }

        private InlineNode HandleDelim(char c)
        {
            int start = _pos;
            ScanDelims(c, out int n, out bool canOpen, out bool canClose);
            var node = Text(_s.Substring(start, n), start, _pos);
            if (canOpen || canClose) PushDelimiter(c, canOpen, canClose, node, n);
            return node;
        }

        private InlineNode HandleTilde()
        {
            int start = _pos;
            ScanDelims('~', out int n, out bool left, out bool right);
            var node = Text(_s.Substring(start, n), start, _pos);
            if ((left || right) && (n == 1 || n == 2)) PushDelimiter('~', left, right, node, n);
            return node;
        }

        private void PushDelimiter(char c, bool canOpen, bool canClose, InlineNode inl, int length)
        {
            var d = new Delimiter
            {
                Char = c,
                CanOpen = canOpen,
                CanClose = canClose,
                Inl = inl,
                Length = length,
                NumDelims = length,
                Position = _pos,
                Prev = _lastDelim,
            };
            if (_lastDelim is not null) _lastDelim.Next = d;
            _lastDelim = d;
        }

        private void RemoveDelimiter(Delimiter d)
        {
            if (d.Next is not null) d.Next.Prev = d.Prev;
            else _lastDelim = d.Prev;
            if (d.Prev is not null) d.Prev.Next = d.Next;
            d.Prev = d.Next = null;
        }

        // openers_bottom is keyed by delimiter char x closer can-open x closer length % 3: every closer in a
        // bucket fails against exactly the same openers, so the lower bound is exact for each bucket.
        private const int BottomBuckets = 18;

        private static int BottomIndex(Delimiter closer) =>
            (closer.Char switch { '*' => 0, '_' => 6, _ => 12 }) + (closer.CanOpen ? 3 : 0) + closer.Length % 3;

        private void ProcessEmphasis(int stackBottom)
        {
            Span<int> openersBottom = stackalloc int[BottomBuckets];
            openersBottom.Fill(stackBottom);

            Delimiter? closer = null;
            for (var cand = _lastDelim; cand is not null && cand.Position >= stackBottom; cand = cand.Prev)
                closer = cand;

            while (closer is not null)
            {
                Work();
                if (!closer.CanClose)
                {
                    closer = closer.Next;
                    continue;
                }
                int idx = BottomIndex(closer);
                var opener = closer.Prev;
                bool found = false;
                while (opener is not null && opener.Position >= stackBottom && opener.Position >= openersBottom[idx])
                {
                    Work();
                    if (opener.CanOpen && opener.Char == closer.Char)
                    {
                        if (closer.Char == '~')
                        {
                            if (!(closer.CanOpen || opener.CanClose) || closer.Length % 3 == 0 || (opener.Length + closer.Length) % 3 != 0)
                            {
                                found = true;
                                break;
                            }
                        }
                        else
                        {
                            bool oddMatch = (closer.CanOpen || opener.CanClose)
                                && (opener.Length + closer.Length) % 3 == 0
                                && !(opener.Length % 3 == 0 && closer.Length % 3 == 0);
                            if (!oddMatch)
                            {
                                found = true;
                                break;
                            }
                        }
                    }
                    opener = opener.Prev;
                }
                var oldCloser = closer;
                if (closer.Char == '~')
                    closer = found ? InsertStrike(opener!, closer) : closer.Next;
                else
                    closer = found ? InsertEmph(opener!, closer) : closer.Next;
                if (!found)
                {
                    openersBottom[idx] = oldCloser.Position;
                    if (!oldCloser.CanOpen) RemoveDelimiter(oldCloser);
                }
            }
            while (_lastDelim is not null && _lastDelim.Position >= stackBottom)
                RemoveDelimiter(_lastDelim);
            foreach (var inl in _shortened)
                if (inl.PendingLength >= 0)
                {
                    inl.Literal = inl.Literal.Substring(0, inl.PendingLength);
                    inl.PendingLength = -1;
                }
            _shortened.Clear();
        }

        private void Shorten(InlineNode inl, int by)
        {
            if (inl.PendingLength < 0)
            {
                inl.PendingLength = inl.Literal.Length;
                _shortened.Add(inl);
            }
            inl.PendingLength -= by;
        }

        private Delimiter? InsertEmph(Delimiter opener, Delimiter closer)
        {
            var openerInl = opener.Inl;
            var closerInl = closer.Inl;
            int use = closer.NumDelims >= 2 && opener.NumDelims >= 2 ? 2 : 1;
            opener.NumDelims -= use;
            closer.NumDelims -= use;
            Shorten(openerInl, use);
            Shorten(closerInl, use);

            for (var d = closer.Prev; d is not null && d != opener;)
            {
                Work();
                var prev = d.Prev;
                RemoveDelimiter(d);
                d = prev;
            }

            var emph = Make(use == 1 ? InlineType.Emphasis : InlineType.Strong, openerInl.End - use, closerInl.Start + use);
            emph.Delimiter = opener.Char;
            openerInl.End -= use;
            closerInl.Start += use;
            for (var tmp = openerInl.Next; tmp is not null && tmp != closerInl;)
            {
                Work();
                var next = tmp.Next;
                tmp.Unlink();
                emph.Append(tmp);
                tmp = next;
            }
            openerInl.InsertAfter(emph);

            if (opener.NumDelims == 0)
            {
                openerInl.Unlink();
                RemoveDelimiter(opener);
            }
            if (closer.NumDelims == 0)
            {
                var next = closer.Next;
                closerInl.Unlink();
                RemoveDelimiter(closer);
                return next;
            }
            return closer;
        }

        private Delimiter? InsertStrike(Delimiter opener, Delimiter closer)
        {
            var res = closer.Next;
            var openerInl = opener.Inl;
            var closerInl = closer.Inl;
            if (openerInl.Literal.Length == closerInl.Literal.Length)
            {
                var strike = Make(InlineType.Strikethrough, openerInl.Start, closerInl.End);
                strike.DelimiterLength = openerInl.Literal.Length;
                for (var tmp = openerInl.Next; tmp is not null && tmp != closerInl;)
                {
                    Work();
                    var next = tmp.Next;
                    tmp.Unlink();
                    strike.Append(tmp);
                    tmp = next;
                }
                openerInl.InsertAfter(strike);
                openerInl.Unlink();
                closerInl.Unlink();
            }
            for (var d = closer; d is not null && d != opener;)
            {
                var prev = d.Prev;
                RemoveDelimiter(d);
                d = prev;
            }
            RemoveDelimiter(opener);
            return res;
        }

        // ---- links and images ----

        private void PushBracket(bool image, InlineNode inl)
        {
            if (_lastBracket is not null) _lastBracket.BracketAfter = true;
            _lastBracket = new Bracket
            {
                Previous = _lastBracket,
                Inl = inl,
                Position = _pos,
                Image = image,
                Seq = ++_bracketSeq,
            };
        }

        private int SkipWhitespace(int i)
        {
            while (i < _s.Length && (_s[i] == ' ' || _s[i] == '\t' || _s[i] == '\n' || _s[i] == '\r' || _s[i] == '\f' || _s[i] == '\v')) i++;
            return i;
        }

        /// <summary>Scans a link label at i ('['); returns end index after ']' or -1. Label is trimmed raw text.</summary>
        private int LinkLabel(int i, out string label)
        {
            label = string.Empty;
            if (Peek(i) != '[') return -1;
            int j = i + 1;
            int length = 0;
            while (j < _s.Length)
            {
                char c = _s[j];
                if (c == '[' || c == ']') break;
                if (c == '\\')
                {
                    j++;
                    length++;
                    if (j < _s.Length && CharUtil.IsAsciiPunctuation(_s[j])) { j++; length++; }
                }
                else
                {
                    j++;
                    length++;
                }
                if (length > MaxLinkLabelLength) return -1;
            }
            if (j >= _s.Length || _s[j] != ']') return -1;
            label = _s.Substring(i + 1, j - i - 1).Trim(' ', '\t', '\n', '\r', '\f');
            return j + 1;
        }

        private InlineNode? HandleCloseBracket(InlineNode parent)
        {
            int start = _pos;
            _pos++;
            int initialPos = _pos;
            var opener = _lastBracket;
            if (opener is null)
                return Text("]", start, _pos);
            if (!opener.Image && opener.Seq <= _inactiveUpTo)
            {
                _lastBracket = opener.Previous;
                return Text("]", start, _pos);
            }
            bool isImage = opener.Image;
            int afterLinkTextPos = _pos;
            string url = string.Empty;
            string? title = null;
            string? label = null;
            LinkStyle style = LinkStyle.Inline;
            bool matched = false;

            if (Peek(_pos) == '(')
            {
                int i = SkipWhitespace(_pos + 1);
                int n = Scanners.LinkDestination(_s, i, out string rawUrl);
                if (n == -2) { n = i; rawUrl = string.Empty; }
                if (n >= 0)
                {
                    int endUrl = n;
                    int startTitle = SkipWhitespace(endUrl);
                    int endTitle = startTitle;
                    string? rawTitle = null;
                    if (startTitle != endUrl)
                    {
                        int t = Scanners.LinkTitle(_s, startTitle, out string rt);
                        if (t >= 0) { endTitle = t; rawTitle = rt; }
                    }
                    int endAll = SkipWhitespace(endTitle);
                    if (Peek(endAll) == ')')
                    {
                        _pos = endAll + 1;
                        url = Scanners.Unescape(rawUrl);
                        title = rawTitle is null ? null : Scanners.Unescape(rawTitle);
                        matched = true;
                    }
                }
                if (!matched) _pos = afterLinkTextPos;
            }

            if (!matched)
            {
                int e = LinkLabel(_pos, out string rawLabel);
                bool found = e > 0;
                if (found)
                {
                    _pos = e;
                    style = rawLabel.Length == 0 ? LinkStyle.ReferenceCollapsed : LinkStyle.ReferenceFull;
                }
                else
                {
                    _pos = afterLinkTextPos;
                    style = LinkStyle.ReferenceShortcut;
                }
                if ((!found || rawLabel.Length == 0) && !opener.BracketAfter)
                {
                    rawLabel = _s.Substring(opener.Position, initialPos - opener.Position - 1);
                    found = true;
                }
                if (found && rawLabel.Length <= MaxLinkLabelLength
                    && _refs.TryGetValue(LinkReferenceDefinition.NormalizeLabel(rawLabel), out var def))
                {
                    url = def.Destination;
                    title = def.Title;
                    label = rawLabel;
                    matched = true;
                }
            }

            if (!matched)
            {
                _lastBracket = opener.Previous;
                _pos = initialPos;
                return Text("]", start, _pos);
            }

            var inl = Make(isImage ? InlineType.Image : InlineType.Link, opener.Inl.Start, _pos);
            inl.Destination = url;
            inl.Title = title;
            inl.Style = style;
            inl.Label = label;
            for (var tmp = opener.Inl.Next; tmp is not null;)
            {
                var next = tmp.Next;
                tmp.Unlink();
                inl.Append(tmp);
                tmp = next;
            }
            parent.Append(inl);
            opener.Inl.Unlink();

            ProcessEmphasis(opener.Position);
            _lastBracket = opener.Previous;
            if (!isImage)
                _inactiveUpTo = _bracketSeq; // no links inside links: every earlier link opener is now inactive
            return null;
        }

        // ---- GFM extended autolinks (www. and scheme://) ----

        private InlineNode? WwwMatch()
        {
            int maxRewind = _pos;
            if (maxRewind > 0)
            {
                char p = _s[maxRewind - 1];
                if ("*_~(".IndexOf(p) < 0 && !ExtendedAutolinks.IsCSpace(p)) return null;
            }
            int size = _s.Length - _pos;
            if (size < 4 || string.CompareOrdinal(_s, _pos, "www.", 0, 4) != 0) return null;
            int linkEnd = ExtendedAutolinks.CheckDomain(_s, _pos, size, false);
            if (linkEnd == 0) return null;
            while (linkEnd < size && !ExtendedAutolinks.IsCSpace(_s[_pos + linkEnd]) && _s[_pos + linkEnd] != '<') linkEnd++;
            linkEnd = ExtendedAutolinks.AutolinkDelim(_s, _pos, linkEnd);
            if (linkEnd == 0) return null;
            int start = _pos;
            string text = _s.Substring(_pos, linkEnd);
            _pos += linkEnd;
            var link = Make(InlineType.Link, start, _pos);
            link.Destination = "http://" + text;
            link.Style = LinkStyle.ExtendedAutolink;
            link.Append(Text(text, start, _pos));
            return link;
        }

        private InlineNode? UrlMatch(InlineNode parent)
        {
            int maxRewind = _pos;
            int size = _s.Length - _pos;
            if (size < 4 || _s[_pos + 1] != '/' || _s[_pos + 2] != '/') return null;
            int rewind = 0;
            while (rewind < maxRewind && CharUtil.IsAsciiAlpha(_s[_pos - rewind - 1])) rewind++;
            if (!ExtendedAutolinks.IsSafeScheme(_s, _pos - rewind, size + rewind)) return null;
            int linkEnd = 3;
            int domainLen = ExtendedAutolinks.CheckDomain(_s, _pos + linkEnd, size - linkEnd, true);
            if (domainLen == 0) return null;
            linkEnd += domainLen;
            while (linkEnd < size && !ExtendedAutolinks.IsCSpace(_s[_pos + linkEnd]) && _s[_pos + linkEnd] != '<') linkEnd++;
            linkEnd = ExtendedAutolinks.AutolinkDelim(_s, _pos, linkEnd);
            if (linkEnd == 0) return null;
            Unput(parent, rewind);
            int start = _pos - rewind;
            _pos += linkEnd;
            string url = _s.Substring(start, _pos - start);
            var link = Make(InlineType.Link, start, _pos);
            link.Destination = url;
            link.Style = LinkStyle.ExtendedAutolink;
            link.Append(Text(url, start, _pos));
            return link;
        }

        private static void Unput(InlineNode parent, int n)
        {
            var node = parent.Last;
            while (n > 0 && node is not null && node.Type == InlineType.Text)
            {
                if (node.Literal.Length < n)
                {
                    n -= node.Literal.Length;
                    node.Literal = string.Empty;
                }
                else
                {
                    node.Literal = node.Literal.Substring(0, node.Literal.Length - n);
                    node.End -= n;
                    n = 0;
                }
                node = node.Prev;
            }
        }
    }
}
