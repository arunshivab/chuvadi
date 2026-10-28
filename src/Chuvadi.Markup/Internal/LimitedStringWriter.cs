using System;
using System.IO;
using System.Text;

namespace Chuvadi.Markup.Internal
{
    /// <summary>
    /// Output sink for renderers and serializers. Enforces <see cref="MarkupLimits.MaxOutputChars"/>
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

        private void Account(int n)
        {
            _written += n;
            if (_written > _max)
                throw new MarkupLimitExceededException(nameof(MarkupLimits.MaxOutputChars), _max, _written);
        }

        public void Write(char c)
        {
            Account(1);
            if (_sb is not null) _sb.Append(c); else _writer!.Write(c);
            LastChar = c;
        }

        public void Write(string? s)
        {
            if (string.IsNullOrEmpty(s)) return;
            Account(s.Length);
            if (_sb is not null) _sb.Append(s); else _writer!.Write(s);
            LastChar = s[s.Length - 1];
        }

        public void Write(ReadOnlySpan<char> s)
        {
            if (s.IsEmpty) return;
            Account(s.Length);
            if (_sb is not null) _sb.Append(s); else _writer!.Write(s);
            LastChar = s[s.Length - 1];
        }

        public override string ToString() => _sb?.ToString() ?? string.Empty;
    }
}
