using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Chuvadi.OpenType.Internal;

/// <summary>Writes one face's tables as a standalone sfnt font file.</summary>
internal static class SfntWriter
{
    private const uint HeadTag = 0x68656164; // head
    private const uint DsigTag = 0x44534947; // DSIG

    // TRACE R-OT-0035
    internal static byte[] WriteFace(ReadOnlySpan<byte> source, uint sfntVersion, IReadOnlyList<TableRecord> tables)
    {
        List<TableRecord> keep = new List<TableRecord>(tables.Count);
        foreach (TableRecord t in tables)
        {
            if (t.Tag.Value != DsigTag)
            {
                keep.Add(t);
            }
        }

        keep.Sort((a, b) => a.Tag.Value.CompareTo(b.Tag.Value));
        int n = keep.Count;
        int searchRange = 1;
        int entrySelector = 0;
        while (searchRange * 2 <= n)
        {
            searchRange *= 2;
            entrySelector++;
        }

        searchRange *= 16;
        long total = 12 + (16L * n);
        foreach (TableRecord t in keep)
        {
            total += (t.Length + 3L) & ~3L;
        }

        byte[] font = new byte[checked((int)total)];
        Span<byte> f = font;
        BinaryPrimitives.WriteUInt32BigEndian(f, sfntVersion);
        BinaryPrimitives.WriteUInt16BigEndian(f.Slice(4), (ushort)n);
        BinaryPrimitives.WriteUInt16BigEndian(f.Slice(6), (ushort)searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(f.Slice(8), (ushort)entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(f.Slice(10), (ushort)((n * 16) - searchRange));

        int offset = 12 + (16 * n);
        int headOffset = -1;
        for (int i = 0; i < n; i++)
        {
            TableRecord t = keep[i];
            source.Slice(t.Offset, t.Length).CopyTo(f.Slice(offset));
            if (t.Tag.Value == HeadTag && t.Length >= 12)
            {
                // checkSumAdjustment is set to 0 before the table and whole-font checksums are computed.
                BinaryPrimitives.WriteUInt32BigEndian(f.Slice(offset + 8), 0);
                headOffset = offset;
            }

            int padded = (t.Length + 3) & ~3;
            int r = 12 + (16 * i);
            BinaryPrimitives.WriteUInt32BigEndian(f.Slice(r), t.Tag.Value);
            BinaryPrimitives.WriteUInt32BigEndian(f.Slice(r + 4), Checksum(f.Slice(offset, padded)));
            BinaryPrimitives.WriteUInt32BigEndian(f.Slice(r + 8), (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(f.Slice(r + 12), (uint)t.Length);
            offset += padded;
        }

        if (headOffset >= 0)
        {
            BinaryPrimitives.WriteUInt32BigEndian(f.Slice(headOffset + 8), unchecked(0xB1B0AFBAu - Checksum(f)));
        }

        return font;
    }

    internal static uint Checksum(ReadOnlySpan<byte> s)
    {
        uint sum = 0;
        int i = 0;
        for (; i + 3 < s.Length; i += 4)
        {
            sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(s.Slice(i)));
        }

        if (i < s.Length)
        {
            Span<byte> last = stackalloc byte[4];
            last.Clear();
            s.Slice(i).CopyTo(last);
            sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(last));
        }

        return sum;
    }
}
