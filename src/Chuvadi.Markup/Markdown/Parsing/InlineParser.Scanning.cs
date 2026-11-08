using System;
using System.Collections.Generic;
using Chuvadi.Markup.Internal;

namespace Chuvadi.Markup.Markdown.Parsing
{
    internal sealed partial class InlineParser
    {
        // Forward searches whose answers are remembered, so that repeated failing searches over the same
        // text stay linear overall (the parser only ever moves forward).
        private readonly ForwardFinder _finder;
        private readonly QuoteFinder _quotes = new QuoteFinder();

        private bool[]? _escaped;
        private int[]? _stopAt;
        private int[]? _depth;
        private int[]? _nextLower;

        // TRACE R-MD-0154
        /// <summary>For every char, whether a backslash before it escapes it (left to right over the content).</summary>
        private bool[] Escaped()
        {
            if (_escaped is not null) return _escaped;
            var escaped = new bool[_n + 1];
            for (int k = 0; k < _n; k++)
            {
                if (_t[k] == '\\' && k + 1 < _n && CharUtil.IsAsciiPunctuation(_t[k + 1]))
                {
                    escaped[k + 1] = true;
                    k++;
                }
            }
            return _escaped = escaped;
        }

        // TRACE R-MD-0155
        /// <summary>
        /// A bare link destination starting at <paramref name="s"/> (CommonMark "Links": no spaces or control
        /// chars, parentheses only balanced or escaped). Uses tables built once per block so that every call is
        /// constant time.
        /// </summary>
        private int RawDestination(int s, out int contentStart, out int contentEnd)
        {
            contentStart = contentEnd = s;
            if (_stopAt is null) BuildDestinationTables();
            int stop = _stopAt![s];
            int lower = _nextLower![s];
            int end;
            if (lower <= _n && lower - 1 < stop) end = lower - 1;
            else if (_depth![stop] == _depth[s]) end = stop;
            else return -1;
            if (end == s) return -1;
            contentStart = s;
            contentEnd = end;
            return end;
        }

        private void BuildDestinationTables()
        {
            var escaped = Escaped();
            _work.Step(_n);
            var stopAt = new int[_n + 1];
            stopAt[_n] = _n;
            for (int k = _n - 1; k >= 0; k--)
            {
                char c = _t[k];
                stopAt[k] = c <= ' ' || c == '\u007f' ? k : stopAt[k + 1];
            }
            // depth[k]: parenthesis balance of the unescaped parentheses before k
            var depth = new int[_n + 1];
            for (int k = 0; k < _n; k++)
            {
                int d = depth[k];
                if (!escaped[k])
                {
                    if (_t[k] == '(') d++;
                    else if (_t[k] == ')') d--;
                }
                depth[k + 1] = d;
            }
            // nextLower[k]: first m > k with depth[m] < depth[k] (n + 1 if none)
            var nextLower = new int[_n + 1];
            var stack = new Stack<int>();
            for (int k = _n; k >= 0; k--)
            {
                while (stack.Count > 0 && depth[stack.Peek()] >= depth[k]) stack.Pop();
                nextLower[k] = stack.Count > 0 ? stack.Peek() : _n + 1;
                stack.Push(k);
            }
            _stopAt = stopAt;
            _depth = depth;
            _nextLower = nextLower;
        }

        private ForwardFinder? _titleDouble;
        private ForwardFinder? _titleSingle;
        private ForwardFinder? _titleParen;

        // TRACE R-MD-0156
        /// <summary>A link title at <paramref name="t"/>; returns the index after it or -1.</summary>
        private int TitleAt(int t, out int contentStart, out int contentEnd)
        {
            contentStart = contentEnd = t;
            char open = _t[t];
            var escaped = Escaped();
            int close;
            if (open == '(')
            {
                _titleParen ??= new ForwardFinder(_t);
                close = _titleParen.FindWhere(t + 1, k => !escaped[k] && (_t[k] == '(' || _t[k] == ')'));
                if (close < 0 || _t[close] == '(') return -1;
            }
            else
            {
                var finder = open == '"' ? (_titleDouble ??= new ForwardFinder(_t)) : (_titleSingle ??= new ForwardFinder(_t));
                close = finder.FindWhere(t + 1, k => !escaped[k] && _t[k] == open);
                if (close < 0) return -1;
            }
            contentStart = t + 1;
            contentEnd = close;
            return close + 1;
        }
    }

    // TRACE R-MD-0157
    /// <summary>
    /// A forward search that remembers its last answer: a later search starting between the last start and
    /// the last answer has the same answer, and once a search fails every later one fails too.
    /// </summary>
    internal sealed class ForwardFinder
    {
        private readonly Dictionary<string, (int From, int Result)> _byPattern = new(StringComparer.Ordinal);
        private readonly string _text;
        private int _lastFrom = int.MaxValue;
        private int _lastResult;

        public ForwardFinder(string text) => _text = text;

        public int Find(string pattern, int from)
        {
            if (_byPattern.TryGetValue(pattern, out var last) && from >= last.From && (last.Result < 0 || from <= last.Result))
                return last.Result;
            int found = from > _text.Length ? -1 : _text.IndexOf(pattern, from, StringComparison.Ordinal);
            _byPattern[pattern] = (from, found);
            return found;
        }

        public int FindWhere(int from, Func<int, bool> match)
        {
            var text = _text;
            if (from >= _lastFrom && (_lastResult < 0 || from <= _lastResult)) return _lastResult;
            int k = from;
            while (k < text.Length && !match(k)) k++;
            int result = k < text.Length ? k : -1;
            _lastFrom = from;
            _lastResult = result;
            return result;
        }
    }

    /// <summary>Remembered searches for the closing quote of an HTML attribute value.</summary>
    internal sealed class QuoteFinder
    {
        private (int From, int Result) _double = (int.MaxValue, 0);
        private (int From, int Result) _single = (int.MaxValue, 0);

        public int Find(string s, char quote, int from, int end)
        {
            var last = quote == '"' ? _double : _single;
            if (from >= last.From && (last.Result < 0 || from <= last.Result)) return last.Result;
            int found = from >= end ? -1 : s.IndexOf(quote, from, end - from);
            if (quote == '"') _double = (from, found);
            else _single = (from, found);
            return found;
        }
    }

    // TRACE R-MD-0158
    /// <summary>
    /// The inline-parsing work budget of one document: MaxParserWorkPerChar x (input length + 1024) + 1,000,000
    /// steps (MarkupLimits; Arun's rule R5-02). A step is one char scanned by the inline driver, one delimiter
    /// or bracket pushed, one opener examined, or one reference looked up.
    /// </summary>
    internal sealed class WorkBudget
    {
        private readonly long _limit;
        private long _used;
        private Chuvadi.Markup.Internal.CancellationPoll _poll;

        public WorkBudget(MarkupLimits limits, int inputLength, System.Threading.CancellationToken cancel = default)
        {
            _limit = (long)limits.MaxParserWorkPerChar * (inputLength + 1024L) + 1_000_000L;
            _poll = new Chuvadi.Markup.Internal.CancellationPoll(cancel);
        }

        // TRACE R-MD-0273
        // every step also polls the cancellation token, so inline parsing of one long block stops promptly
        public void Step(int count = 1)
        {
            _poll.Tick();
            _used += count;
            if (_used > _limit)
                throw new MarkupLimitExceededException(nameof(MarkupLimits.MaxParserWorkPerChar), _limit, _used);
        }
    }
}
