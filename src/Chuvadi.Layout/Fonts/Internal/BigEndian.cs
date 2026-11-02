using System;
using System.Buffers.Binary;

namespace Chuvadi.Layout.Fonts.Internal
{
    /// <summary>
    /// Bounds-checked big-endian reads over a font program. Every read validates the range and
    /// throws <see cref="FontFormatException"/> instead of an index exception, because font bytes
    /// come from untrusted documents.
    /// </summary>
    internal readonly struct BigEndian
    {
        private readonly ReadOnlyMemory<byte> _data;

        public BigEndian(ReadOnlyMemory<byte> data) => _data = data;

        public int Length => _data.Length;

        public ReadOnlyMemory<byte> Memory => _data;

        public bool Has(long offset, long count) => offset >= 0 && count >= 0 && offset + count <= _data.Length;

        private ReadOnlySpan<byte> Slice(long offset, int count)
        {
            if (!Has(offset, count))
            {
                throw new FontFormatException($"Font data truncated: need {count} byte(s) at offset {offset}, length {_data.Length}.");
            }
            return _data.Span.Slice((int)offset, count);
        }

        public byte U8(long offset) => Slice(offset, 1)[0];
        public sbyte I8(long offset) => unchecked((sbyte)Slice(offset, 1)[0]);
        public ushort U16(long offset) => BinaryPrimitives.ReadUInt16BigEndian(Slice(offset, 2));
        public short I16(long offset) => BinaryPrimitives.ReadInt16BigEndian(Slice(offset, 2));
        public uint U32(long offset) => BinaryPrimitives.ReadUInt32BigEndian(Slice(offset, 4));
        public int I32(long offset) => BinaryPrimitives.ReadInt32BigEndian(Slice(offset, 4));

        /// <summary>16.16 fixed-point value.</summary>
        public double Fixed(long offset) => I32(offset) / 65536.0;

        public string Tag(long offset)
        {
            ReadOnlySpan<byte> s = Slice(offset, 4);
            return new string(new[] { (char)s[0], (char)s[1], (char)s[2], (char)s[3] });
        }

        public ReadOnlySpan<byte> Bytes(long offset, int count) => Slice(offset, count);

        public BigEndian Sub(long offset, long count)
        {
            if (!Has(offset, count))
            {
                throw new FontFormatException($"Font table range {offset}+{count} lies outside the font data ({_data.Length} bytes).");
            }
            return new BigEndian(_data.Slice((int)offset, (int)count));
        }

        public static uint MakeTag(string tag)
            => ((uint)tag[0] << 24) | ((uint)tag[1] << 16) | ((uint)tag[2] << 8) | tag[3];
    }
}
