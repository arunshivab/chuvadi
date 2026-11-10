using System.Collections.Generic;
using System.Text;
using Chuvadi.Markup.Internal;

namespace Chuvadi.Markup.Markdown.Parsing
{
    /// <summary>
    /// Phase 2 of parsing: turns the raw content of one paragraph or heading into inline nodes
    /// (CommonMark "Inlines" and "Phase 2: inline structure").
    /// </summary>
    internal sealed partial class InlineParser
    {
        /// <summary>A position in the run of inline nodes being built (a doubly linked list).</summary>
        private sealed class Slot
        {
            public Slot(MarkdownInline node) => Node = node;
            public MarkdownInline Node;
            public Slot? Prev;
            public Slot? Next;
        }

        /// <summary>A run of '*' or '_' that may open or close emphasis.</summary>
        private sealed class DelimiterRun
        {
            public char Char;
            public int Count;
            public int Original;
            public bool CanOpen;
            public bool CanClose;
            public bool Changed;
            public long Order;
            public Slot Slot = null!;
            public DelimiterRun? Prev;
            public DelimiterRun? Next;
        }

        /// <summary>An opening '[' or '![' waiting for its ']'.</summary>
        private sealed class BracketOpener
        {
            public bool Image;
            public int TextIndex;
            public long Order;
            public Slot Slot = null!;
            public DelimiterRun? RunsBelow;
            public BracketOpener? Prev;
        }

        private readonly string _t;
        private readonly int _n;
        private readonly InlineContent _content;
        private readonly MarkdownDocument _document;
        private readonly WorkBudget _work;
        private readonly int _emphasisDepthRoom;

        private Slot? _head;
        private Slot? _tail;
        private DelimiterRun? _runsHead;
        private DelimiterRun? _runsTail;
        private BracketOpener? _brackets;
        private long _order;
        private long _linkBarrier = -1;

        private readonly StringBuilder _pending = new StringBuilder();
        private int _pendingStart = -1;
        private int _pendingEnd;

        private readonly bool _strikethrough;
        private readonly bool _autolinks;

        public InlineParser(InlineContent content, MarkdownDocument document, WorkBudget work, int emphasisDepthRoom, bool strikethrough, bool autolinks)
        {
            _strikethrough = strikethrough;
            _autolinks = autolinks;
            _content = content;
            _t = content.Text;
            _n = _t.Length;
            _document = document;
            _work = work;
            _emphasisDepthRoom = emphasisDepthRoom;
            _finder = new ForwardFinder(_t);
        }

        private int Src(int textIndex) => _content.ToSource(textIndex);

        // TRACE R-MD-0166
        /// <summary>Spaces and tabs at the start of a line after a line break are not content.</summary>
        private int SkipIndentation(int i)
        {
            while (i < _n && (_t[i] == ' ' || _t[i] == '\t')) i++;
            return i;
        }

        /// <summary>Where a line break ends, given the text index of the first char of the next line.</summary>
        private int BreakEnd(int nextLineIndex) => _content.BreakEnds[_content.LineOf(nextLineIndex)];

        /// <summary>Source span of the text range [from, to).</summary>
        private SourceSpan SpanOf(int from, int to)
        {
            int s = Src(from);
            int e = to > from ? Src(to - 1) + 1 : s;
            return new SourceSpan(s, e - s);
        }

        // ---------------------------------------------------------------- driver

        /// <summary>Parses the content and appends the inline nodes to <paramref name="block"/>.</summary>
        public void ParseInto(MarkdownBlock block)
        {
            int i = 0;
            while (i < _n)
            {
                _work.Step();
                char c = _t[i];
                if (_autolinks && IsAutolinkTrigger(i) && TryExtendedAutolink(ref i)) continue;
                switch (c)
                {
                    case '\n':
                        i = LineEnding(i, i);
                        break;
                    case ' ':
                    case '\t':
                        i = Spaces(i);
                        break;
                    case '\\':
                        i = Backslash(i);
                        break;
                    case '`':
                        i = CodeSpanAt(i);
                        break;
                    case '*':
                    case '_':
                        i = DelimiterRunAt(i);
                        break;
                    case '~':
                        // TRACE R-MD-0197
                        if (_strikethrough) i = DelimiterRunAt(i);
                        else i = PlainText(i);
                        break;
                    case '[':
                        i = OpenBracket(i, image: false);
                        break;
                    case '!':
                        if (i + 1 < _n && _t[i + 1] == '[') i = OpenBracket(i, image: true);
                        else i = PlainText(i);
                        break;
                    case ']':
                        i = CloseBracket(i);
                        break;
                    case '<':
                        i = AngleBracket(i);
                        break;
                    case '&':
                        i = Entity(i);
                        break;
                    default:
                        i = PlainText(i);
                        break;
                }
            }
            FlushText();
            // TRACE R-MD-0109
            ProcessEmphasis(null);
            for (var s = _head; s is not null; s = s.Next) block.AppendChildUnchecked(s.Node);
        }

        // ---------------------------------------------------------------- text

        private void AddText(string text, int from, int to)
        {
            if (_pendingStart < 0) _pendingStart = Src(from);
            _pending.Append(text);
            _pendingEnd = Src(to - 1) + 1;
        }

        private void AddText(char c, int from, int to)
        {
            if (_pendingStart < 0) _pendingStart = Src(from);
            _pending.Append(CharacterReferences.SafeChar(c));
            _pendingEnd = Src(to - 1) + 1;
        }

        private void FlushText()
        {
            if (_pendingStart < 0) return;
            var node = new TextInline(_pending.ToString()) { Span = new SourceSpan(_pendingStart, _pendingEnd - _pendingStart) };
            _pending.Clear();
            _pendingStart = -1;
            Append(node);
        }

        private Slot Append(MarkdownInline node)
        {
            var slot = new Slot(node) { Prev = _tail };
            if (_tail is null) _head = slot;
            else _tail.Next = slot;
            _tail = slot;
            return slot;
        }

        private Slot AddNode(MarkdownInline node)
        {
            FlushText();
            return Append(node);
        }

        private static bool IsSpecial(char c) => c switch
        {
            '\n' or ' ' or '\t' or '\\' or '`' or '*' or '_' or '~' or '[' or ']' or '!' or '<' or '&' => true,
            _ => false,
        };

        // TRACE R-MD-0110
        private int PlainText(int i)
        {
            int j = i + 1;
            while (j < _n && !IsSpecial(_t[j]) && !(_autolinks && IsAutolinkTrigger(j))) j++;
            string piece = _t.Substring(i, j - i);
            AddText(CharacterReferences.Safe(piece), i, j);
            return j;
        }

        // TRACE R-MD-0164
        /// <summary>
        /// A run of spaces and tabs: text, unless it ends the line; then it is dropped, and the spaces right
        /// before the line ending decide between a hard and a soft break.
        /// </summary>
        private int Spaces(int i)
        {
            int j = i;
            while (j < _n && (_t[j] == ' ' || _t[j] == '\t')) j++;
            if (j < _n && _t[j] == '\n')
            {
                int spaces = j;
                while (spaces > i && _t[spaces - 1] == ' ') spaces--;
                return LineEnding(j, spaces);
            }
            AddText(_t.Substring(i, j - i), i, j);
            return j;
        }

        // ---------------------------------------------------------------- line breaks

        /// <summary>
        /// The line ending at <paramref name="newline"/>, preceded by spaces from <paramref name="spacesStart"/>.
        /// </summary>
        private int LineEnding(int newline, int spacesStart)
        {
            // TRACE R-MD-0111
            bool hard = newline - spacesStart >= 2;
            MarkdownInline node = hard ? new HardBreak { IsBackslash = false } : new SoftBreak();
            // TRACE R-MD-0112
            int start = Src(newline);
            int end = BreakEnd(newline + 1);
            node.Span = new SourceSpan(start, end - start);
            AddNode(node);
            return SkipIndentation(newline + 1);
        }

        private int Backslash(int i)
        {
            if (i + 1 < _n)
            {
                char next = _t[i + 1];
                if (next == '\n')
                {
                    // TRACE R-MD-0113
                    var node = new HardBreak { IsBackslash = true };
                    int start = Src(i);
                    int end = BreakEnd(i + 2);
                    node.Span = new SourceSpan(start, end - start);
                    AddNode(node);
                    return SkipIndentation(i + 2);
                }
                // TRACE R-MD-0114
                if (CharUtil.IsAsciiPunctuation(next))
                {
                    AddText(next, i, i + 2);
                    return i + 2;
                }
            }
            AddText('\\', i, i + 1);
            return i + 1;
        }

        // ---------------------------------------------------------------- entities

        private int Entity(int i)
        {
            // TRACE R-MD-0115
            int len = CharacterReferences.Match(_t, i, _n, out var value);
            if (len > 0)
            {
                AddText(value, i, i + len);
                return i + len;
            }
            AddText('&', i, i + 1);
            return i + 1;
        }

        // ---------------------------------------------------------------- code spans

        private Dictionary<int, BacktickRuns>? _backtickRuns;

        /// <summary>Start positions of the backtick strings of one length, consumed in order.</summary>
        private sealed class BacktickRuns
        {
            public readonly List<int> Starts = new List<int>();
            public int Next;
        }

        // TRACE R-MD-0116
        private void IndexBacktickRuns()
        {
            _backtickRuns = new Dictionary<int, BacktickRuns>();
            int k = 0;
            while (k < _n)
            {
                if (_t[k] != '`')
                {
                    k++;
                    continue;
                }
                int start = k;
                while (k < _n && _t[k] == '`') k++;
                int len = k - start;
                if (!_backtickRuns.TryGetValue(len, out var runs)) _backtickRuns[len] = runs = new BacktickRuns();
                runs.Starts.Add(start);
            }
        }

        private int FindClosingBackticks(int length, int after)
        {
            if (_backtickRuns is null) IndexBacktickRuns();
            if (!_backtickRuns!.TryGetValue(length, out var runs)) return -1;
            while (runs.Next < runs.Starts.Count && runs.Starts[runs.Next] < after)
            {
                _work.Step();
                runs.Next++;
            }
            return runs.Next < runs.Starts.Count ? runs.Starts[runs.Next] : -1;
        }

        private int CodeSpanAt(int i)
        {
            int j = i;
            while (j < _n && _t[j] == '`') j++;
            int length = j - i;
            // TRACE R-MD-0117
            int close = FindClosingBackticks(length, j);
            if (close < 0)
            {
                AddText(_t.Substring(i, length), i, j);
                return j;
            }
            // TRACE R-MD-0118
            var code = new StringBuilder(close - j);
            for (int k = j; k < close; k++) code.Append(_t[k] == '\n' ? ' ' : CharacterReferences.SafeChar(_t[k]));
            // TRACE R-MD-0119
            if (code.Length >= 2 && code[0] == ' ' && code[code.Length - 1] == ' ')
            {
                bool allSpaces = true;
                for (int k = 0; k < code.Length; k++)
                {
                    if (code[k] != ' ')
                    {
                        allSpaces = false;
                        break;
                    }
                }
                if (!allSpaces)
                {
                    code.Remove(code.Length - 1, 1);
                    code.Remove(0, 1);
                }
            }
            var node = new CodeSpan(code.ToString()) { DelimiterLength = length, Span = SpanOf(i, close + length) };
            AddNode(node);
            return close + length;
        }

        // ---------------------------------------------------------------- emphasis delimiters

        private int DelimiterRunAt(int i)
        {
            char c = _t[i];
            int j = i;
            while (j < _n && _t[j] == c) j++;
            int count = j - i;

            // TRACE R-MD-0120
            int before = i == 0 ? '\n' : CharUtil.CodePointBefore(_t, i);
            int after = j >= _n ? '\n' : CharUtil.CodePointAt(_t, j);
            bool spaceBefore = CharUtil.IsUnicodeWhitespace(before);
            bool spaceAfter = CharUtil.IsUnicodeWhitespace(after);
            bool punctBefore = CharUtil.IsUnicodePunctuation(before);
            bool punctAfter = CharUtil.IsUnicodePunctuation(after);
            bool leftFlanking = !spaceAfter && (!punctAfter || spaceBefore || punctBefore);
            bool rightFlanking = !spaceBefore && (!punctBefore || spaceAfter || punctAfter);

            bool canOpen, canClose;
            if (c == '~')
            {
                // TRACE R-MD-0198
                canOpen = leftFlanking && count <= 2;
                canClose = rightFlanking && count <= 2;
            }
            else if (c == '*')
            {
                // TRACE R-MD-0121
                canOpen = leftFlanking;
                canClose = rightFlanking;
            }
            else
            {
                // TRACE R-MD-0122
                canOpen = leftFlanking && (!rightFlanking || punctBefore);
                canClose = rightFlanking && (!leftFlanking || punctAfter);
            }

            // TRACE R-MD-0246
            if (!canOpen && !canClose)
            {
                AddText(new string(c, count), i, j);
                return j;
            }
            FlushText();
            var text = new TextInline(new string(c, count)) { Span = SpanOf(i, j) };
            var slot = Append(text);
            {
                _work.Step();
                var run = new DelimiterRun
                {
                    Char = c,
                    Count = count,
                    Original = count,
                    CanOpen = canOpen,
                    CanClose = canClose,
                    Order = _order++,
                    Slot = slot,
                    Prev = _runsTail,
                };
                if (_runsTail is null) _runsHead = run;
                else _runsTail.Next = run;
                _runsTail = run;
            }
            return j;
        }

        // TRACE R-MD-0123
        private static bool BreaksRuleOfThree(DelimiterRun opener, DelimiterRun closer)
        {
            if (!opener.CanClose && !closer.CanOpen) return false;
            if ((opener.Original + closer.Original) % 3 != 0) return false;
            return !(opener.Original % 3 == 0 && closer.Original % 3 == 0);
        }

        private void RemoveRun(DelimiterRun run)
        {
            if (run.Changed)
            {
                ((TextInline)run.Slot.Node).Text = new string(run.Char, run.Count);
                run.Changed = false;
            }
            if (run.Prev is null) _runsHead = run.Next;
            else run.Prev.Next = run.Next;
            if (run.Next is null) _runsTail = run.Prev;
            else run.Next.Prev = run.Prev;
            run.Prev = run.Next = null;
        }

        private void RemoveSlot(Slot slot)
        {
            if (slot.Prev is null) _head = slot.Next;
            else slot.Prev.Next = slot.Next;
            if (slot.Next is null) _tail = slot.Prev;
            else slot.Next.Prev = slot.Prev;
            slot.Prev = slot.Next = null;
        }

        /// <summary>
        /// Matches closers with openers above <paramref name="bottom"/> (the whole list when null), wrapping the
        /// nodes between them in emphasis or strong emphasis (CommonMark "Emphasis and strong emphasis", rules 1-17).
        /// </summary>
        private void ProcessEmphasis(DelimiterRun? bottom)
        {
            long bottomOrder = bottom is null ? -1 : bottom.Order;
            // TRACE R-MD-0124
            var floors = new long[14];
            for (int k = 0; k < floors.Length; k++) floors[k] = bottomOrder;

            var closer = bottom is null ? _runsHead : bottom.Next;
            while (closer is not null)
            {
                _work.Step();
                if (!closer.CanClose)
                {
                    closer = closer.Next;
                    continue;
                }
                int key = closer.Char == '~' ? 12 + (closer.Original - 1)
                    : (closer.Char == '*' ? 0 : 6) + (closer.Original % 3) * 2 + (closer.CanOpen ? 1 : 0);
                long floor = floors[key];
                DelimiterRun? opener = closer.Prev;
                DelimiterRun? found = null;
                while (opener is not null && opener.Order > floor)
                {
                    _work.Step();
                    // TRACE R-MD-0229
                    // a closing-only tilde run that reaches an opening-only tilde run of another length
                    // discards that run and every run between them, and matches nothing
                    if (closer.Char == '~' && !closer.CanOpen && opener.Char == '~' && opener.CanOpen && !opener.CanClose
                        && opener.Count != closer.Count)
                    {
                        while (opener.Next is not null && !ReferenceEquals(opener.Next, closer)) RemoveRun(opener.Next);
                        RemoveRun(opener);
                        break;
                    }
                    // TRACE R-MD-0125
                    if (opener.Char == closer.Char && opener.CanOpen
                        && (closer.Char == '~' ? opener.Count == closer.Count : !BreaksRuleOfThree(opener, closer)))
                    {
                        found = opener;
                        break;
                    }
                    opener = opener.Prev;
                }

                if (found is null)
                {
                    // TRACE R-MD-0126
                    floors[key] = closer.Prev?.Order ?? bottomOrder;
                    if (floors[key] < bottomOrder) floors[key] = bottomOrder;
                    var next = closer.Next;
                    if (!closer.CanOpen) RemoveRun(closer);
                    closer = next;
                    continue;
                }

                // TRACE R-MD-0127
                int use;
                MarkdownInline wrapper;
                if (closer.Char == '~')
                {
                    // TRACE R-MD-0199
                    use = closer.Count;
                    wrapper = new Strikethrough { DelimiterLength = use };
                }
                else
                {
                    use = closer.Count >= 2 && found.Count >= 2 ? 2 : 1;
                    wrapper = use == 2 ? new Strong { Delimiter = found.Char } : new Emphasis { Delimiter = found.Char };
                }
                // TRACE R-MD-0128
                var openText = (TextInline)found.Slot.Node;
                var closeText = (TextInline)closer.Slot.Node;
                int openStart = openText.Span.Start;
                int closeStart = closeText.Span.Start;
                found.Count -= use;
                closer.Count -= use;
                wrapper.Span = new SourceSpan(openStart + found.Count, closeStart + use - (openStart + found.Count));
                // the run texts are rewritten once, when the runs leave the list
                openText.Span = new SourceSpan(openStart, found.Count);
                closeText.Span = new SourceSpan(closeStart + use, closer.Count);
                found.Changed = closer.Changed = true;

                // move the nodes between the two runs into the wrapper
                var inner = found.Slot.Next;
                while (inner is not null && !ReferenceEquals(inner, closer.Slot))
                {
                    var nextInner = inner.Next;
                    RemoveSlot(inner);
                    wrapper.AppendChildUnchecked(inner.Node);
                    inner = nextInner;
                }
                var wrapperSlot = new Slot(wrapper) { Prev = found.Slot, Next = closer.Slot };
                found.Slot.Next = wrapperSlot;
                closer.Slot.Prev = wrapperSlot;

                // runs between the opener and the closer can no longer match anything
                // TRACE R-MD-0129
                while (found.Next is not null && !ReferenceEquals(found.Next, closer)) RemoveRun(found.Next);

                if (found.Count == 0)
                {
                    RemoveSlot(found.Slot);
                    RemoveRun(found);
                }
                if (closer.Count == 0)
                {
                    var next = closer.Next;
                    RemoveSlot(closer.Slot);
                    RemoveRun(closer);
                    closer = next;
                }
            }

            // TRACE R-MD-0130
            if (bottom is null)
            {
                while (_runsHead is not null) RemoveRun(_runsHead);
            }
            else
            {
                while (bottom.Next is not null) RemoveRun(bottom.Next);
            }
        }
    }
}
