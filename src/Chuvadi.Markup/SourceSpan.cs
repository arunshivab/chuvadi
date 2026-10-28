using System;

namespace Chuvadi.Markup
{
    /// <summary>A range of the source text, as a zero-based char offset and length.</summary>
    public readonly struct SourceSpan : IEquatable<SourceSpan>
    {
        /// <summary>Creates a span.</summary>
        public SourceSpan(int start, int length)
        {
            if (start < 0) throw new ArgumentOutOfRangeException(nameof(start));
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            Start = start;
            Length = length;
        }

        /// <summary>Zero-based offset of the first char.</summary>
        public int Start { get; }

        /// <summary>Number of chars.</summary>
        public int Length { get; }

        /// <summary>Offset one past the last char.</summary>
        public int End => Start + Length;

        /// <summary>An empty span at offset 0.</summary>
        public static SourceSpan Empty => default;

        /// <inheritdoc />
        public bool Equals(SourceSpan other) => Start == other.Start && Length == other.Length;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is SourceSpan s && Equals(s);

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(Start, Length);

        /// <summary>Equality.</summary>
        public static bool operator ==(SourceSpan a, SourceSpan b) => a.Equals(b);

        /// <summary>Inequality.</summary>
        public static bool operator !=(SourceSpan a, SourceSpan b) => !a.Equals(b);

        /// <inheritdoc />
        public override string ToString() => "[" + Start + ".." + End + ")";
    }

    /// <summary>A one-based line and column position in source text (column counted in UTF-16 chars).</summary>
    public readonly record struct SourcePosition(int Line, int Column);
}
