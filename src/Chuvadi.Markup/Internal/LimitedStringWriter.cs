using System;
using System.IO;
using System.Text;

namespace Chuvadi.Markup.Internal
{
    /// <summary>
    /// Output sink for renderers and serializers. Enforces <see cref="MarkupLimits.MaxOutputChars"/> exactly
    /// and writes either to an internal <see cref="StringBuilder"/> or to a caller's <see cref="TextWriter"/>.
    /// </summary>
    internal sealed class OutputSink
    {
        private readonly StringBuilder? _sb;
        private readonly TextWriter? _writer;
        private readonly long _max;
        private long _written;

        public OutputSink(long maxChars, int initialCapacity = 256)
        {
            _max = maxChars;
            _sb = new StringBuilder(Math.Max(16, initialCapacity));
        }

        public OutputSink(TextWriter writer, long maxChars)
        {
            _max = maxChars;
            _writer = writer;
        }

        public long Written => _written;

        public char LastChar { get; private set; } = '\n';

        // MaxOutputChars is exact (D-127 R5-03): output stops at exactly the limit. A write that would
        // pass it delivers the characters that still fit, then throws with Actual = limit + 1.
        private MarkupLimitExceededException Exceeded()
            => new MarkupLimitExceededException(nameof(MarkupLimits.MaxOutputChars), _max, _max + 1);

        public void Write(char c)
        {
            if (_written >= _max) throw Exceeded();
            _written++;
            if (_sb is not null) _sb.Append(c); else _writer!.Write(c);
            LastChar = c;
        }

        public void Write(string? s)
        {
            if (string.IsNullOrEmpty(s)) return;
            long room = _max - _written;
            if (s.Length > room)
            {
                WritePart(s.AsSpan(0, (int)room));
                throw Exceeded();
            }
            _written += s.Length;
            if (_sb is not null) _sb.Append(s); else _writer!.Write(s);
            LastChar = s[s.Length - 1];
        }

        public void Write(ReadOnlySpan<char> s)
        {
            if (s.IsEmpty) return;
            long room = _max - _written;
            if (s.Length > room)
            {
                WritePart(s.Slice(0, (int)room));
                throw Exceeded();
            }
            _written += s.Length;
            if (_sb is not null) _sb.Append(s); else _writer!.Write(s);
            LastChar = s[s.Length - 1];
        }

        private void WritePart(ReadOnlySpan<char> part)
        {
            if (part.IsEmpty) return;
            _written += part.Length;
            if (_sb is not null) _sb.Append(part); else _writer!.Write(part);
            LastChar = part[part.Length - 1];
        }

        public override string ToString() => _sb?.ToString() ?? string.Empty;
    }
}
