using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Chuvadi.Markup.Internal;

namespace Chuvadi.Markup.Markdown.Parsing
{
    /// <summary>
    /// CommonMark block structure parser (the two-phase line algorithm of the reference implementation)
    /// with the GFM table and task list extensions.
    /// </summary>
    internal sealed class BlockParser
    {
        private const int TabStop = 4;
        private const int CodeIndent = 4;

        private readonly MarkdownOptions _options;
        private readonly MarkupLimits _limits;
        private readonly NodeBudget _budget;
        private CancellationPoll _poll;
        private readonly Dictionary<string, LinkReferenceDefinition> _refs;
        private int _tableCells;

        private readonly BlockNode _root;
        private BlockNode _current;

        private string _line = string.Empty;
        private int _lineStart;
        private int _lineNumber;
        private int _offset, _column, _firstNonspace, _firstNonspaceColumn, _indent;
        private bool _blank, _partiallyConsumedTab;
        private int _thematicBreakKillPos;

        public BlockParser(MarkdownOptions options, NodeBudget budget, CancellationToken ct, Dictionary<string, LinkReferenceDefinition> refs)
        {
            _options = options;
            _limits = options.Limits;
            _budget = budget;
            _poll = new CancellationPoll(ct);
            _refs = refs;
            _root = new BlockNode(BlockType.Document, null, 0, 1);
            _current = _root;
        }

        public BlockNode Parse(string text)
        {
            int pos = 0;
            int len = text.Length;
            while (pos < len)
            {
                _poll.Check();
                int eol = pos;
                while (eol < len && text[eol] != '\n' && text[eol] != '\r') eol++;
                string line = text.Substring(pos, eol - pos);
                if (line.IndexOf('\0') >= 0) line = line.Replace('\0', '�');
                ProcessLine(line, pos);
                pos = eol;
                if (pos < len && text[pos] == '\r') pos++;
                if (pos < len && text[pos] == '\n') pos++;
            }
            while (_current is not null && _current != _root) _current = Finalize(_current)!;
            Finalize(_root);
            return _root;
        }

        // ---- line state ----

        private char Peek(int i) => i < _line.Length ? _line[i] : '\0';

        private static bool IsSpaceOrTab(char c) => c == ' ' || c == '\t';

        private bool IsLineEnd(int i) => i >= _line.Length;

        private void FindFirstNonspace()
        {
            int charsToTab = TabStop - (_column % TabStop);
            if (_firstNonspace > _offset)
            {
                // still valid: only spaces/tabs lie between offset and the cached first non-space
                _indent = _firstNonspaceColumn - _column;
                _blank = IsLineEnd(_firstNonspace);
                return;
            }
            _firstNonspace = _offset;
            _firstNonspaceColumn = _column;
            while (_firstNonspace < _line.Length)
            {
                char c = _line[_firstNonspace];
                if (c == ' ')
                {
                    _firstNonspace++;
                    _firstNonspaceColumn++;
                    if (--charsToTab == 0) charsToTab = TabStop;
                }
                else if (c == '\t')
                {
                    _firstNonspace++;
                    _firstNonspaceColumn += charsToTab;
                    charsToTab = TabStop;
                }
                else break;
            }
            _indent = _firstNonspaceColumn - _column;
            _blank = IsLineEnd(_firstNonspace);
        }

        private void AdvanceOffset(int count, bool columns)
        {
            while (count > 0 && _offset < _line.Length)
            {
                char c = _line[_offset];
                if (c == '\t')
                {
                    int charsToTab = TabStop - (_column % TabStop);
                    if (columns)
                    {
                        _partiallyConsumedTab = charsToTab > count;
                        int charsToAdvance = Math.Min(count, charsToTab);
                        _column += charsToAdvance;
                        _offset += _partiallyConsumedTab ? 0 : 1;
                        count -= charsToAdvance;
                    }
                    else
                    {
                        _partiallyConsumedTab = false;
                        _column += charsToTab;
                        _offset++;
                        count--;
                    }
                }
                else
                {
                    _partiallyConsumedTab = false;
                    _offset++;
                    _column++;
                    count--;
                }
            }
        }

        private void AddLine(BlockNode node)
        {
            var sb = new StringBuilder();
            if (_partiallyConsumedTab)
            {
                _offset++;
                int charsToTab = TabStop - (_column % TabStop);
                sb.Append(' ', charsToTab);
            }
            int src = _lineStart + Math.Min(_offset, _line.Length);
            if (_offset < _line.Length) sb.Append(_line, _offset, _line.Length - _offset);
            sb.Append('\n');
            node.AddContentLine(sb.ToString(), src);
            node.EndOffset = _lineStart + _line.Length;
        }

        // ---- per line ----

        private void ProcessLine(string line, int lineStart)
        {
            _line = line;
            _lineStart = lineStart;
            _lineNumber++;
            _offset = 0;
            _column = 0;
            _firstNonspace = 0;
            _firstNonspaceColumn = 0;
            _indent = 0;
            _blank = false;
            _partiallyConsumedTab = false;
            _thematicBreakKillPos = 0;

            var lastMatched = CheckOpenBlocks(out bool allMatched);
            if (lastMatched is null) return;
            var container = lastMatched;
            OpenNewBlocks(ref container, allMatched);
            AddTextToContainer(container, lastMatched);
        }

        private BlockNode? CheckOpenBlocks(out bool allMatched)
        {
            allMatched = true;
            var container = _root;
            while (container.LastChild is { Open: true } child)
            {
                container = child;
                FindFirstNonspace();
                bool ok;
                switch (container.Type)
                {
                    case BlockType.BlockQuote:
                        ok = ParseBlockQuotePrefix();
                        break;
                    case BlockType.Item:
                        ok = ParseItemPrefix(container);
                        break;
                    case BlockType.CodeBlock:
                        ok = ParseCodeBlockPrefix(container, out bool closed);
                        if (closed) return null;
                        break;
                    case BlockType.HtmlBlock:
                        ok = container.HtmlType <= 5 || !_blank;
                        break;
                    case BlockType.Paragraph:
                        ok = !_blank;
                        break;
                    case BlockType.Table:
                        ok = !_blank && RowFromString(_line, _firstNonspace) is { Count: > 0 };
                        break;
                    case BlockType.Heading:
                    case BlockType.TableRow:
                        ok = false;
                        break;
                    default:
                        ok = true; // lists (items decide) and thematic breaks, as in cmark
                        break;
                }
                if (!ok)
                {
                    allMatched = false;
                    container = container.Parent!;
                    break;
                }
                if (!_blank) container.EndOffset = _lineStart + _line.Length;
            }
            return container;
        }

        private bool ParseBlockQuotePrefix()
        {
            if (_indent <= 3 && Peek(_firstNonspace) == '>')
            {
                AdvanceOffset(_indent + 1, true);
                if (IsSpaceOrTab(Peek(_offset))) AdvanceOffset(1, true);
                return true;
            }
            return false;
        }

        private bool ParseItemPrefix(BlockNode item)
        {
            if (_indent >= item.MarkerOffset + item.Padding)
            {
                AdvanceOffset(item.MarkerOffset + item.Padding, true);
                return true;
            }
            if (_blank && item.Children.Count > 0)
            {
                AdvanceOffset(_firstNonspace - _offset, false);
                return true;
            }
            return false;
        }

        private bool ParseCodeBlockPrefix(BlockNode code, out bool closed)
        {
            closed = false;
            if (!code.Fenced)
            {
                if (_indent >= CodeIndent)
                {
                    AdvanceOffset(CodeIndent, true);
                    return true;
                }
                if (_blank)
                {
                    AdvanceOffset(_firstNonspace - _offset, false);
                    return true;
                }
                return false;
            }
            int matched = -1;
            if (_indent <= 3 && Peek(_firstNonspace) == code.FenceChar)
                matched = Scanners.CloseCodeFence(_line, _firstNonspace, code.FenceChar, code.FenceLength);
            if (matched >= code.FenceLength)
            {
                code.EndOffset = _lineStart + _line.Length;
                AdvanceOffset(matched, false);
                _current = Finalize(code)!;
                closed = true;
                return false;
            }
            int i = code.FenceOffset;
            while (i > 0 && IsSpaceOrTab(Peek(_offset)))
            {
                AdvanceOffset(1, true);
                i--;
            }
            return true;
        }

        private void OpenNewBlocks(ref BlockNode container, bool allMatched)
        {
            bool maybeLazy = _current.Type == BlockType.Paragraph;
            var contType = container.Type;
            int maxDepth = _limits.MaxNestingDepth;

            while (contType != BlockType.CodeBlock && contType != BlockType.HtmlBlock)
            {
                FindFirstNonspace();
                bool indented = _indent >= CodeIndent;
                bool canNest = container.Depth + 2 <= maxDepth;
                char fc = Peek(_firstNonspace);

                if (!indented && fc == '>' && canNest)
                {
                    int startPos = _firstNonspace;
                    AdvanceOffset(_firstNonspace + 1 - _offset, false);
                    if (IsSpaceOrTab(Peek(_offset))) AdvanceOffset(1, true);
                    container = AddChild(container, BlockType.BlockQuote, startPos);
                }
                else if (!indented && fc == '#' && Scanners.AtxHeadingStart(_line, _firstNonspace, out int level) >= 0)
                {
                    int startPos = _firstNonspace;
                    int matched = Scanners.AtxHeadingStart(_line, _firstNonspace, out _);
                    AdvanceOffset(matched - _offset, false);
                    container = AddChild(container, BlockType.Heading, startPos);
                    container.Level = level;
                    container.Setext = false;
                }
                else if (!indented && (fc == '`' || fc == '~') && Scanners.OpenCodeFence(_line, _firstNonspace, out char fenceChar, out int fenceLength) >= 0)
                {
                    int startPos = _firstNonspace;
                    container = AddChild(container, BlockType.CodeBlock, startPos);
                    container.Fenced = true;
                    container.FenceChar = fenceChar;
                    container.FenceLength = fenceLength;
                    container.FenceOffset = _firstNonspace - _offset;
                    string info = _line.Substring(_firstNonspace + fenceLength).Trim(' ', '\t');
                    container.Info = Scanners.Unescape(info);
                    AdvanceOffset(_line.Length - _offset, false);
                }
                else if (!indented && fc == '<' && HtmlStart(contType) is int htmlType && htmlType > 0)
                {
                    container = AddChild(container, BlockType.HtmlBlock, _firstNonspace);
                    container.HtmlType = htmlType;
                }
                else if (!indented && contType == BlockType.Paragraph && Scanners.SetextHeadingLine(_line, _firstNonspace) is int lev && lev > 0)
                {
                    bool hasContent = ResolveReferenceDefinitions(container);
                    if (hasContent)
                    {
                        container.Type = BlockType.Heading;
                        container.Level = lev;
                        container.Setext = true;
                        container.EndOffset = _lineStart + _line.Length;
                        AdvanceOffset(_line.Length - _offset, false);
                    }
                }
                else if (!indented && !(contType == BlockType.Paragraph && !allMatched)
                    && _thematicBreakKillPos <= _firstNonspace && ScanThematicBreak(_firstNonspace, out char marker))
                {
                    container = AddChild(container, BlockType.ThematicBreak, _firstNonspace);
                    container.Marker = marker;
                    container.EndOffset = _lineStart + _line.Length;
                    AdvanceOffset(_line.Length - _offset, false);
                }
                else if ((!indented || contType == BlockType.List) && _indent < 4 && canNest
                    && ParseListMarker(_firstNonspace, container.Type == BlockType.Paragraph, out var data, out int markerLen))
                {
                    AdvanceOffset(_firstNonspace + markerLen - _offset, false);
                    bool saveTab = _partiallyConsumedTab;
                    int saveOffset = _offset, saveColumn = _column;
                    while (_column - saveColumn <= 5 && IsSpaceOrTab(Peek(_offset)))
                        AdvanceOffset(1, true);
                    int i = _column - saveColumn;
                    if (i >= 5 || i < 1 || IsLineEnd(_offset))
                    {
                        data.Padding = markerLen + 1;
                        _offset = saveOffset;
                        _column = saveColumn;
                        _partiallyConsumedTab = saveTab;
                        if (i > 0) AdvanceOffset(1, true);
                    }
                    else
                    {
                        data.Padding = markerLen + i;
                    }
                    data.MarkerOffset = _indent;

                    if (contType != BlockType.List || !ListsMatch(container, data))
                    {
                        container = AddChild(container, BlockType.List, _firstNonspace);
                        CopyListData(data, container);
                    }
                    container = AddChild(container, BlockType.Item, _firstNonspace);
                    CopyListData(data, container);
                }
                else if (indented && !maybeLazy && !_blank)
                {
                    AdvanceOffset(CodeIndent, true);
                    container = AddChild(container, BlockType.CodeBlock, _offset);
                    container.Fenced = false;
                }
                else if (_options.Tables && !indented && contType == BlockType.Paragraph && TryOpenTableHeader(container))
                {
                    container.Type = BlockType.Table;
                }
                else if (_options.Tables && !indented && contType == BlockType.Table && TryOpenTableRow(container))
                {
                    // row added; offset advanced to end of line
                }
                else
                {
                    break;
                }

                if (AcceptsLines(container.Type)) break;
                contType = container.Type;
                maybeLazy = false;
            }
        }

        private int HtmlStart(BlockType contType)
        {
            int t = Scanners.HtmlBlockStart(_line, _firstNonspace);
            if (t > 0) return t;
            if (contType != BlockType.Paragraph && Scanners.HtmlBlockStart7(_line, _firstNonspace)) return 7;
            return 0;
        }

        private bool ScanThematicBreak(int i, out char marker)
        {
            marker = Peek(i);
            if (marker != '*' && marker != '-' && marker != '_')
            {
                _thematicBreakKillPos = i + 1;
                return false;
            }
            int count = 0;
            int j = i;
            for (; j < _line.Length; j++)
            {
                char c = _line[j];
                if (c == marker) count++;
                else if (!IsSpaceOrTab(c))
                {
                    _thematicBreakKillPos = j;
                    return false;
                }
            }
            if (count >= 3) return true;
            _thematicBreakKillPos = j;
            return false;
        }

        private static bool AcceptsLines(BlockType t) => t == BlockType.Paragraph || t == BlockType.Heading || t == BlockType.CodeBlock;

        private sealed class ListData
        {
            public ListKind Kind;
            public char Bullet;
            public int Start;
            public char Delim;
            public int MarkerOffset;
            public int Padding;
        }

        private static void CopyListData(ListData d, BlockNode n)
        {
            n.ListKind = d.Kind;
            n.BulletChar = d.Bullet;
            n.ListStart = d.Start;
            n.ListDelimiter = d.Delim;
            n.MarkerOffset = d.MarkerOffset;
            n.Padding = d.Padding;
            n.Tight = true;
        }

        private static bool ListsMatch(BlockNode list, ListData d)
            => list.ListKind == d.Kind && list.ListDelimiter == d.Delim && list.BulletChar == d.Bullet;

        private bool ParseListMarker(int pos, bool interruptsParagraph, out ListData data, out int length)
        {
            data = new ListData();
            length = 0;
            char c = Peek(pos);
            int startPos = pos;
            if (c == '*' || c == '-' || c == '+')
            {
                pos++;
                if (!IsLineEnd(pos) && !IsSpaceOrTab(Peek(pos))) return false;
                if (interruptsParagraph)
                {
                    int i = pos;
                    while (IsSpaceOrTab(Peek(i))) i++;
                    if (IsLineEnd(i)) return false;
                }
                data.Kind = ListKind.Bullet;
                data.Bullet = c;
                data.Delim = '\0';
                data.Start = 0;
            }
            else if (CharUtil.IsAsciiDigit(c))
            {
                int start = 0;
                int digits = 0;
                do
                {
                    start = 10 * start + (Peek(pos) - '0');
                    pos++;
                    digits++;
                } while (digits < 9 && CharUtil.IsAsciiDigit(Peek(pos)));
                if (interruptsParagraph && start != 1) return false;
                c = Peek(pos);
                if (c != '.' && c != ')') return false;
                pos++;
                if (!IsLineEnd(pos) && !IsSpaceOrTab(Peek(pos))) return false;
                if (interruptsParagraph)
                {
                    int i = pos;
                    while (IsSpaceOrTab(Peek(i))) i++;
                    if (IsLineEnd(i)) return false;
                }
                data.Kind = ListKind.Ordered;
                data.Bullet = '\0';
                data.Delim = c;
                data.Start = start;
            }
            else
            {
                return false;
            }
            length = pos - startPos;
            return true;
        }

        private BlockNode AddChild(BlockNode parent, BlockType type, int linePos)
        {
            while (!CanContain(parent.Type, type)) parent = Finalize(parent)!;
            _budget.Take();
            var child = new BlockNode(type, parent, _lineStart + Math.Min(linePos, _line.Length), _lineNumber)
            {
                Depth = parent.Depth + 1,
            };
            child.EndOffset = _lineStart + _line.Length;
            parent.Children.Add(child);
            return child;
        }

        private static bool CanContain(BlockType parent, BlockType child)
        {
            switch (parent)
            {
                case BlockType.Document:
                case BlockType.BlockQuote:
                case BlockType.Item:
                    return child != BlockType.Item && child != BlockType.TableRow;
                case BlockType.List:
                    return child == BlockType.Item;
                case BlockType.Table:
                    return child == BlockType.TableRow;
                default:
                    return false;
            }
        }

        private void AddTextToContainer(BlockNode container, BlockNode lastMatched)
        {
            FindFirstNonspace();
            if (_blank && container.LastChild is not null)
                container.LastChild.LastLineBlank = true;

            var ctype = container.Type;
            bool lastLineBlank = _blank
                && ctype != BlockType.BlockQuote
                && ctype != BlockType.Heading
                && ctype != BlockType.ThematicBreak
                && !(ctype == BlockType.CodeBlock && container.Fenced)
                && !(ctype == BlockType.Item && container.Children.Count == 0 && container.StartLine == _lineNumber);
            container.LastLineBlank = lastLineBlank;
            for (var tmp = container; tmp.Parent is not null; tmp = tmp.Parent)
                tmp.Parent.LastLineBlank = false;

            if (_current != lastMatched && container == lastMatched && !_blank && _current.Type == BlockType.Paragraph)
            {
                // lazy continuation line
                AddLine(_current);
                return;
            }

            while (_current != lastMatched)
                _current = Finalize(_current)!;

            if (container.Type == BlockType.CodeBlock)
            {
                // the opening fence line carries the info string, not content
                if (!(container.Fenced && container.StartLine == _lineNumber && container.Content is null))
                    AddLine(container);
            }
            else if (container.Type == BlockType.HtmlBlock)
            {
                AddLine(container);
                if (Scanners.HtmlBlockEnd(container.HtmlType, _line, _firstNonspace))
                    container = Finalize(container)!;
            }
            else if (_blank)
            {
                // nothing
            }
            else if (AcceptsLines(container.Type))
            {
                if (container.Type == BlockType.Heading && !container.Setext)
                    ChopTrailingHashes();
                AdvanceOffset(_firstNonspace - _offset, false);
                AddLine(container);
            }
            else
            {
                container = AddChild(container, BlockType.Paragraph, _firstNonspace);
                AdvanceOffset(_firstNonspace - _offset, false);
                AddLine(container);
            }
            _current = container;
        }

        private void ChopTrailingHashes()
        {
            int len = _line.Length;
            while (len > 0 && char.IsWhiteSpace(_line[len - 1]) && _line[len - 1] <= ' ') len--;
            int n = len - 1;
            int orig = n;
            while (n >= 0 && _line[n] == '#') n--;
            if (n != orig && n >= 0 && IsSpaceOrTab(_line[n]))
            {
                len = n;
                while (len > 0 && IsSpaceOrTab(_line[len - 1])) len--;
            }
            if (len < _line.Length) _line = _line.Substring(0, len);
        }

        // ---- finalization ----

        private BlockNode? Finalize(BlockNode b)
        {
            var parent = b.Parent;
            b.Open = false;
            switch (b.Type)
            {
                case BlockType.Paragraph:
                {
                    bool hasContent = ResolveReferenceDefinitions(b);
                    if (!hasContent) parent?.Children.Remove(b);
                    break;
                }
                case BlockType.CodeBlock:
                    if (!b.Fenced) RemoveTrailingBlankLines(b);
                    break;
                case BlockType.List:
                {
                    b.Tight = true;
                    for (int i = 0; i < b.Children.Count; i++)
                    {
                        var item = b.Children[i];
                        bool hasNext = i + 1 < b.Children.Count;
                        if (item.LastLineBlank && hasNext)
                        {
                            b.Tight = false;
                            break;
                        }
                        for (int j = 0; j < item.Children.Count; j++)
                        {
                            var sub = item.Children[j];
                            if ((hasNext || j + 1 < item.Children.Count) && EndsWithBlankLine(sub))
                            {
                                b.Tight = false;
                                break;
                            }
                        }
                        if (!b.Tight) break;
                    }
                    break;
                }
            }
            return parent;
        }

        private static bool EndsWithBlankLine(BlockNode node)
        {
            for (var n = node; n is not null;)
            {
                if (n.LastLineBlank) return true;
                if (n.Type == BlockType.List || n.Type == BlockType.Item) n = n.LastChild;
                else return false;
            }
            return false;
        }

        private static void RemoveTrailingBlankLines(BlockNode b)
        {
            if (b.Content is null) return;
            string s = b.Content.ToString();
            int i = s.Length - 1;
            while (i >= 0 && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i--;
            if (i < 0)
            {
                b.Content.Clear();
                return;
            }
            int nl = s.IndexOf('\n', i);
            if (nl >= 0) b.Content.Length = nl + 1;
        }

        /// <summary>
        /// Consumes link reference definitions at the start of a paragraph. Returns true if any content remains.
        /// </summary>
        private bool ResolveReferenceDefinitions(BlockNode para)
        {
            if (para.Content is null) return false;
            string s = para.Content.ToString();
            int pos = 0;
            while (pos < s.Length && s[pos] == '[')
            {
                int e = ParseReferenceDefinition(s, pos, para);
                if (e <= pos) break;
                pos = e;
            }
            if (pos > 0) RemoveContentPrefix(para, pos);
            string rest = para.Content.ToString();
            for (int i = 0; i < rest.Length; i++)
                if (!char.IsWhiteSpace(rest[i])) return true;
            return false;
        }

        private static void RemoveContentPrefix(BlockNode para, int n)
        {
            para.Content!.Remove(0, n);
            var mc = para.MapContent!;
            var ms = para.MapSource!;
            int keepFrom = 0;
            for (int i = 0; i < mc.Count; i++)
                if (mc[i] <= n) keepFrom = i;
            int delta = n - mc[keepFrom];
            mc.RemoveRange(0, keepFrom);
            ms.RemoveRange(0, keepFrom);
            if (mc.Count > 0)
            {
                ms[0] += delta;
                for (int i = 0; i < mc.Count; i++) mc[i] = Math.Max(0, mc[i] - n);
            }
        }

        private int ParseReferenceDefinition(string s, int pos, BlockNode para)
        {
            // label
            int j = pos + 1;
            int length = 0;
            bool nonWs = false;
            while (j < s.Length)
            {
                char c = s[j];
                if (c == '[') return -1;
                if (c == ']') break;
                if (c == '\\' && j + 1 < s.Length && CharUtil.IsAsciiPunctuation(s[j + 1]))
                {
                    j += 2;
                    length += 2;
                    nonWs = true;
                }
                else
                {
                    if (!char.IsWhiteSpace(c)) nonWs = true;
                    j++;
                    length++;
                }
                if (length > 999) return -1;
            }
            if (j >= s.Length || !nonWs) return -1;
            string label = s.Substring(pos + 1, j - pos - 1);
            j++;
            if (j >= s.Length || s[j] != ':') return -1;
            j++;
            j = Scanners.SpaceNewlineSpace(s, j);
            int d = Scanners.LinkDestination(s, j, out string rawDest);
            if (d < 0) return -1;
            int beforeTitle = d;
            int t = Scanners.SpaceNewlineSpace(s, d);
            string? title = null;
            int afterTitle = -1;
            if (t != beforeTitle)
            {
                int te = Scanners.LinkTitle(s, t, out string rawTitle);
                if (te >= 0)
                {
                    title = rawTitle;
                    afterTitle = te;
                }
            }
            int end = -1;
            if (afterTitle >= 0)
            {
                end = SkipToLineEnd(s, afterTitle);
                if (end < 0) title = null;
            }
            if (end < 0)
            {
                end = SkipToLineEnd(s, beforeTitle);
                if (end < 0) return -1;
            }
            string normalized = LinkReferenceDefinition.NormalizeLabel(label);
            if (normalized.Length > 0 && !_refs.ContainsKey(normalized))
            {
                if (_refs.Count >= _limits.MaxLinkReferenceDefinitions) return -1;
                int srcStart = MapToSource(para, pos);
                _refs[normalized] = new LinkReferenceDefinition(label, Scanners.Unescape(rawDest), title is null ? null : Scanners.Unescape(title))
                {
                    Span = new SourceSpan(srcStart, Math.Max(0, MapToSource(para, end) - srcStart)),
                };
            }
            return end;
        }

        private static int SkipToLineEnd(string s, int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t')) i++;
            if (i >= s.Length) return i;
            if (s[i] == '\n') return i + 1;
            if (s[i] == '\r') return i + 1 < s.Length && s[i + 1] == '\n' ? i + 2 : i + 1;
            return -1;
        }

        internal static int MapToSource(BlockNode b, int contentIndex)
        {
            var mc = b.MapContent;
            var ms = b.MapSource;
            if (mc is null || ms is null || mc.Count == 0) return b.StartOffset;
            int lo = 0, hi = mc.Count - 1, idx = 0;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (mc[mid] <= contentIndex) { idx = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return ms[idx] + (contentIndex - mc[idx]);
        }

        // ---- GFM tables ----

        private static bool IsTableSpace(char c) => c == ' ' || c == '\t' || c == '\v' || c == '\f';

        private static int CellEnd(string s, int i)
        {
            if (i >= s.Length || s[i] != '|') return 0;
            int j = i + 1;
            while (j < s.Length && IsTableSpace(s[j])) j++;
            return j - i;
        }

        private static bool IsTableEscapable(char c) => "|!\"#$%&'()*+,./:;<=>?@[\\]^_`{}~-".IndexOf(c) >= 0;

        private static int CellContent(string s, int i)
        {
            int j = i;
            while (j < s.Length)
            {
                char c = s[j];
                if (c == '\\' && j + 1 < s.Length && IsTableEscapable(s[j + 1])) { j += 2; continue; }
                if (c == '|' || c == '\n' || c == '\r') break;
                j++;
            }
            return j - i;
        }

        /// <summary>Parses one row from s[start..]; returns cells (text, offset in s) or null.</summary>
        private static List<(string Text, int Offset)>? RowFromString(string s, int start)
        {
            var cells = new List<(string, int)>();
            int offset = start + CellEnd(s, start);
            bool expectMore = true;
            int len = s.Length;
            while (offset < len && expectMore)
            {
                int cellMatched = CellContent(s, offset);
                int pipeMatched = CellEnd(s, offset + cellMatched);
                if (cellMatched > 0 || pipeMatched > 0)
                {
                    string raw = s.Substring(offset, cellMatched);
                    string text = raw.Replace("\\|", "|").Trim(' ', '\t', '\n', '\r', '\f');
                    int lead = 0;
                    while (lead < raw.Length && (raw[lead] == ' ' || raw[lead] == '\t')) lead++;
                    cells.Add((text, offset + lead));
                    if (cells.Count > ushort.MaxValue) return null;
                }
                offset += cellMatched + pipeMatched;
                if (pipeMatched > 0)
                {
                    expectMore = true;
                }
                else
                {
                    while (offset < len && IsTableSpace(s[offset])) offset++;
                    expectMore = false;
                }
            }
            if (offset != len || cells.Count == 0) return null;
            return cells;
        }

        private static bool ScanTableStart(string s, int i)
        {
            // [|]? table_marker ([|] table_marker)* [|]? spacechar* EOL ; table_marker = sp* :? -+ :? sp*
            int j = i;
            if (j < s.Length && s[j] == '|') j++;
            int markers = 0;
            while (true)
            {
                int k = j;
                while (k < s.Length && IsTableSpace(s[k])) k++;
                if (k < s.Length && s[k] == ':') k++;
                int dashStart = k;
                while (k < s.Length && s[k] == '-') k++;
                if (k == dashStart) break;
                if (k < s.Length && s[k] == ':') k++;
                while (k < s.Length && IsTableSpace(s[k])) k++;
                markers++;
                j = k;
                if (j < s.Length && s[j] == '|')
                {
                    // either a separator before another marker, or the optional trailing pipe
                    int save = j;
                    j++;
                    int k2 = j;
                    while (k2 < s.Length && IsTableSpace(s[k2])) k2++;
                    if (k2 < s.Length && (s[k2] == ':' || s[k2] == '-')) continue;
                    j = save + 1;
                    break;
                }
                break;
            }
            if (markers == 0) return false;
            while (j < s.Length && IsTableSpace(s[j])) j++;
            return j == s.Length;
        }

        private bool TryOpenTableHeader(BlockNode para)
        {
            if (para.TableVisited || para.Content is null) return false;
            if (!ScanTableStart(_line, _firstNonspace)) return false;
            var delimiter = RowFromString(_line, _firstNonspace);
            if (delimiter is null) return false;

            string content = para.Content.ToString();
            int lastStart = content.Length > 0 ? content.LastIndexOf('\n', content.Length - 2) + 1 : 0;
            if (content.Length < 2) lastStart = 0;
            string lastLine = content.Substring(lastStart).TrimEnd('\n', '\r');
            var header = RowFromString(lastLine, 0);
            if (header is null || header.Count != delimiter.Count || header.Count > _limits.MaxTableColumns)
            {
                para.TableVisited = true;
                return false;
            }
            if (_tableCells + header.Count > _limits.MaxTableCells)
            {
                para.TableVisited = true;
                return false;
            }

            var alignments = new List<TableAlignment>(delimiter.Count);
            foreach (var (text, _) in delimiter)
            {
                bool left = text.Length > 0 && text[0] == ':';
                bool right = text.Length > 0 && text[text.Length - 1] == ':';
                alignments.Add(left && right ? TableAlignment.Center : left ? TableAlignment.Left : right ? TableAlignment.Right : TableAlignment.None);
            }

            int headerSource = MapToSource(para, lastStart);
            if (lastStart > 0)
            {
                // the lines before the header row stay a paragraph, inserted before the table
                _budget.Take();
                var before = new BlockNode(BlockType.Paragraph, para.Parent, para.StartOffset, para.StartLine) { Depth = para.Depth, Open = false };
                before.AddContentLine(content.Substring(0, lastStart), para.StartOffset);
                before.MapContent!.Clear();
                before.MapSource!.Clear();
                for (int i = 0; i < para.MapContent!.Count; i++)
                {
                    if (para.MapContent[i] >= lastStart) break;
                    before.MapContent.Add(para.MapContent[i]);
                    before.MapSource.Add(para.MapSource![i]);
                }
                before.EndOffset = headerSource;
                var parent = para.Parent!;
                parent.Children.Insert(parent.Children.IndexOf(para), before);
                if (!ResolveReferenceDefinitions(before)) parent.Children.Remove(before);
                para.StartOffset = headerSource;
            }

            para.Content = null;
            para.MapContent = null;
            para.MapSource = null;
            para.Alignments = alignments;
            _budget.Take();
            var row = new BlockNode(BlockType.TableRow, para, headerSource, _lineNumber - 1)
            {
                Depth = para.Depth + 1,
                Open = false,
                IsHeaderRow = true,
                Cells = new List<CellData>(header.Count),
                EndOffset = headerSource + lastLine.Length,
            };
            foreach (var (text, off) in header)
            {
                _budget.Take();
                row.Cells.Add(new CellData { Text = text, SourceOffset = headerSource + off });
            }
            para.Children.Add(row);
            para.TableRows = 1;
            para.TableNonEmptyCells = header.Count;
            _tableCells += header.Count;
            para.EndOffset = _lineStart + _line.Length;
            AdvanceOffset(_line.Length - _offset, false);
            return true;
        }

        private bool TryOpenTableRow(BlockNode table)
        {
            if (_blank) return false;
            int columns = table.Alignments!.Count;
            if (_tableCells + columns > _limits.MaxTableCells) return false;
            var cells = RowFromString(_line, _firstNonspace);
            if (cells is null) return false;
            _budget.Take();
            var row = new BlockNode(BlockType.TableRow, table, _lineStart + _firstNonspace, _lineNumber)
            {
                Depth = table.Depth + 1,
                Open = false,
                Cells = new List<CellData>(columns),
                EndOffset = _lineStart + _line.Length,
            };
            int i = 0;
            for (; i < cells.Count && i < columns; i++)
            {
                _budget.Take();
                row.Cells.Add(new CellData { Text = cells[i].Text, SourceOffset = _lineStart + cells[i].Offset });
            }
            for (; i < columns; i++)
            {
                _budget.Take();
                row.Cells.Add(new CellData { Text = string.Empty, SourceOffset = _lineStart + _line.Length });
            }
            table.Children.Add(row);
            table.TableRows++;
            _tableCells += columns;
            table.EndOffset = _lineStart + _line.Length;
            AdvanceOffset(_line.Length - _offset, false);
            return true;
        }
    }
}
