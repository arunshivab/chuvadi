using System;
using Chuvadi.OpenType.Internal;

namespace Chuvadi.OpenType;

/// <summary>
/// A four-byte OpenType tag (table, script, language system or feature tag),
/// stored as a big-endian uint32. Tags shorter than four characters are padded
/// with spaces, for example <c>"cvt "</c>.
/// </summary>
/// <param name="Value">The tag as a big-endian unsigned 32-bit value.</param>
public readonly record struct Tag(uint Value) : IComparable<Tag>
{
    /// <summary>Parses a tag of one to four printable ASCII characters (U+0020 to U+007E), padding with spaces.</summary>
    /// <param name="text">The tag text, for example <c>"GSUB"</c> or <c>"cvt"</c>.</param>
    /// <returns>The tag.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="text"/> is empty, longer than four characters or not printable ASCII.</exception>
    public static Tag Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!TryParse(text, out Tag tag))
        {
            throw new ArgumentException("A tag is one to four printable ASCII characters.", nameof(text));
        }

        return tag;
    }

    /// <summary>Tries to parse a tag of one to four printable ASCII characters, padding with spaces.</summary>
    /// <param name="text">The tag text.</param>
    /// <param name="tag">The parsed tag, or default when parsing fails.</param>
    /// <returns>True when <paramref name="text"/> is a valid tag.</returns>
    public static bool TryParse(string? text, out Tag tag)
    {
        tag = default;
        if (text is null || text.Length == 0 || text.Length > 4)
        {
            return false;
        }

        // TRACE R-OT-0002
        uint v = 0;
        for (int i = 0; i < 4; i++)
        {
            char c = i < text.Length ? text[i] : ' ';
            if (c < 0x20 || c > 0x7E)
            {
                return false;
            }

            v = (v << 8) | c;
        }

        tag = new Tag(v);
        return true;
    }

    internal static Tag Read(ReadOnlySpan<byte> s, int offset) => new Tag(BigEndian.U32(s, offset));

    /// <summary>Returns the four characters of the tag. Bytes outside printable ASCII are shown as '?'.</summary>
    /// <returns>The tag text, always four characters.</returns>
    public override string ToString()
    {
        Span<char> chars = stackalloc char[4];
        for (int i = 0; i < 4; i++)
        {
            byte b = (byte)(Value >> (24 - (8 * i)));
            chars[i] = b >= 0x20 && b <= 0x7E ? (char)b : '?';
        }

        return new string(chars);
    }

    /// <summary>Compares tags by their numeric value, which is the order the OpenType table directory uses.</summary>
    /// <param name="other">The other tag.</param>
    /// <returns>Negative, zero or positive.</returns>
    public int CompareTo(Tag other) => Value.CompareTo(other.Value);
}
