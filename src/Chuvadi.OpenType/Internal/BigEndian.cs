using System;
using System.Buffers.Binary;

namespace Chuvadi.OpenType.Internal;

/// <summary>
/// Bounds-checked big-endian reads. Every read outside the span returns zero
/// instead of throwing, so malformed or truncated tables read as empty data.
/// </summary>
internal static class BigEndian
{
    // TRACE R-OT-0001
    internal static bool Has(ReadOnlySpan<byte> s, int offset, int length) =>
        offset >= 0 && length >= 0 && (long)offset + length <= s.Length;

    internal static byte U8(ReadOnlySpan<byte> s, int offset) =>
        (uint)offset < (uint)s.Length ? s[offset] : (byte)0;

    internal static sbyte I8(ReadOnlySpan<byte> s, int offset) => unchecked((sbyte)U8(s, offset));

    internal static ushort U16(ReadOnlySpan<byte> s, int offset) =>
        Has(s, offset, 2) ? BinaryPrimitives.ReadUInt16BigEndian(s.Slice(offset)) : (ushort)0;

    internal static short I16(ReadOnlySpan<byte> s, int offset) =>
        Has(s, offset, 2) ? BinaryPrimitives.ReadInt16BigEndian(s.Slice(offset)) : (short)0;

    internal static int U24(ReadOnlySpan<byte> s, int offset) =>
        Has(s, offset, 3) ? (s[offset] << 16) | (s[offset + 1] << 8) | s[offset + 2] : 0;

    internal static uint U32(ReadOnlySpan<byte> s, int offset) =>
        Has(s, offset, 4) ? BinaryPrimitives.ReadUInt32BigEndian(s.Slice(offset)) : 0u;

    internal static int I32(ReadOnlySpan<byte> s, int offset) =>
        Has(s, offset, 4) ? BinaryPrimitives.ReadInt32BigEndian(s.Slice(offset)) : 0;

    internal static long I64(ReadOnlySpan<byte> s, int offset) =>
        Has(s, offset, 8) ? BinaryPrimitives.ReadInt64BigEndian(s.Slice(offset)) : 0L;

    /// <summary>Reads a uint32 offset and returns it as an int, or -1 when it does not fit.</summary>
    internal static int Offset32(ReadOnlySpan<byte> s, int offset)
    {
        uint v = U32(s, offset);
        return v > int.MaxValue ? -1 : (int)v;
    }

    /// <summary>Returns the slice [offset, offset+length) clamped to the span; empty when offset is outside.</summary>
    internal static ReadOnlySpan<byte> SliceClamped(ReadOnlySpan<byte> s, int offset, int length)
    {
        if (offset < 0 || offset >= s.Length || length <= 0)
        {
            return ReadOnlySpan<byte>.Empty;
        }

        int n = (int)Math.Min((long)length, s.Length - offset);
        return s.Slice(offset, n);
    }

    /// <summary>Returns the slice from offset to the end; empty when offset is outside.</summary>
    internal static ReadOnlySpan<byte> SliceFrom(ReadOnlySpan<byte> s, int offset) =>
        offset < 0 || offset >= s.Length ? ReadOnlySpan<byte>.Empty : s.Slice(offset);
}
