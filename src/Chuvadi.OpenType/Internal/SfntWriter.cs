using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Chuvadi.OpenType.Internal;

/// <summary>One table to write: its tag, its bytes and, optionally, the checksum to store for it.</summary>
internal readonly record struct TableData(uint Tag, ReadOnlyMemory<byte> Data, uint? Checksum = null);

/// <summary>Writes sfnt fonts and 'ttcf' collections from table data.</summary>
internal static class SfntWriter
{
    private const uint HeadTag = 0x68656164; // head
    private const uint DsigTag = 0x44534947; // DSIG

    // TRACE R-OT-0035
    internal static byte[] WriteFace(ReadOnlySpan<byte> source, uint sfntVersion, IReadOnlyList<TableRecord> tables)
    {
        byte[] copy = source.ToArray();
        List<TableData> keep = new List<TableData>(tables.Count);
        foreach (TableRecord t in tables)
        {
            if (t.Tag.Value != DsigTag)
            {
                keep.Add(new TableData(t.Tag.Value, new ReadOnlyMemory<byte>(copy, t.Offset, t.Length)));
            }
        }

        keep.Sort((a, b) => a.Tag.CompareTo(b.Tag));
        return WriteFont(sfntVersion, keep, recomputeChecksums: true);
    }

    /// <summary>
    /// Writes one sfnt. Table data is laid out in the order given; the directory is sorted by tag.
    /// With <paramref name="recomputeChecksums"/>, table checksums and head.checkSumAdjustment are computed;
    /// otherwise each table's given checksum is stored and the tables are copied unchanged.
    /// </summary>
    // TRACE R-OT-0035
    internal static byte[] WriteFont(uint sfntVersion, IReadOnlyList<TableData> tablesInDataOrder, bool recomputeChecksums)
    {
        int n = tablesInDataOrder.Count;
        long total = 12 + (16L * n);
        foreach (TableData t in tablesInDataOrder)
        {
            total += (t.Data.Length + 3L) & ~3L;
        }

        byte[] font = new byte[checked((int)total)];
        Span<byte> f = font;
        int[] offsets = new int[n];
        int offset = 12 + (16 * n);
        int headOffset = -1;
        for (int i = 0; i < n; i++)
        {
            TableData t = tablesInDataOrder[i];
            t.Data.Span.CopyTo(f.Slice(offset));
            offsets[i] = offset;
            if (recomputeChecksums && t.Tag == HeadTag && t.Data.Length >= 12)
            {
                // checkSumAdjustment is 0 while the table and whole-font checksums are computed.
                BinaryPrimitives.WriteUInt32BigEndian(f.Slice(offset + 8), 0);
                headOffset = offset;
            }

            offset += (t.Data.Length + 3) & ~3;
        }

        WriteDirectory(f, 0, sfntVersion, tablesInDataOrder, offsets, font, recomputeChecksums);
        if (headOffset >= 0)
        {
            BinaryPrimitives.WriteUInt32BigEndian(f.Slice(headOffset + 8), unchecked(0xB1B0AFBAu - Checksum(f)));
        }

        return font;
    }

    /// <summary>
    /// Writes a 'ttcf' version 1.0 collection. Each font lists indexes into <paramref name="tables"/>; a table
    /// listed by several fonts is stored once. Checksums are recomputed; each font's head.checkSumAdjustment is
    /// computed over that font's own table directory and tables.
    /// </summary>
    // TRACE R-OT-0037
    internal static byte[] WriteCollection(IReadOnlyList<TableData> tables, IReadOnlyList<(uint Flavor, int[] TableIndexes)> fonts)
    {
        int headerLength = 12 + (4 * fonts.Count);
        long total = headerLength;
        foreach ((uint _, int[] idx) in fonts)
        {
            total += 12 + (16L * idx.Length);
        }

        long dataStart = total;
        foreach (TableData t in tables)
        {
            total += (t.Data.Length + 3L) & ~3L;
        }

        byte[] file = new byte[checked((int)total)];
        Span<byte> f = file;
        BinaryPrimitives.WriteUInt32BigEndian(f, 0x74746366u);
        BinaryPrimitives.WriteUInt32BigEndian(f.Slice(4), 0x00010000u);
        BinaryPrimitives.WriteUInt32BigEndian(f.Slice(8), (uint)fonts.Count);

        int[] tableOffsets = new int[tables.Count];
        int offset = (int)dataStart;
        for (int i = 0; i < tables.Count; i++)
        {
            tables[i].Data.Span.CopyTo(f.Slice(offset));
            if (tables[i].Tag == HeadTag && tables[i].Data.Length >= 12)
            {
                BinaryPrimitives.WriteUInt32BigEndian(f.Slice(offset + 8), 0);
            }

            tableOffsets[i] = offset;
            offset += (tables[i].Data.Length + 3) & ~3;
        }

        int directory = headerLength;
        for (int k = 0; k < fonts.Count; k++)
        {
            (uint flavor, int[] idx) = fonts[k];
            BinaryPrimitives.WriteUInt32BigEndian(f.Slice(12 + (4 * k)), (uint)directory);
            List<TableData> own = new List<TableData>(idx.Length);
            int[] ownOffsets = new int[idx.Length];
            for (int i = 0; i < idx.Length; i++)
            {
                own.Add(tables[idx[i]]);
                ownOffsets[i] = tableOffsets[idx[i]];
            }

            WriteDirectory(f, directory, flavor, own, ownOffsets, file, recomputeChecksums: true);
            directory += 12 + (16 * idx.Length);
        }

        // head.checkSumAdjustment per font: its directory plus its tables, as if the font stood alone.
        directory = headerLength;
        for (int k = 0; k < fonts.Count; k++)
        {
            (uint _, int[] idx) = fonts[k];
            uint sum = Checksum(f.Slice(directory, 12 + (16 * idx.Length)));
            int head = -1;
            foreach (int i in idx)
            {
                sum = unchecked(sum + Checksum(f.Slice(tableOffsets[i], (tables[i].Data.Length + 3) & ~3)));
                if (tables[i].Tag == HeadTag && tables[i].Data.Length >= 12)
                {
                    head = tableOffsets[i];
                }
            }

            if (head >= 0)
            {
                BinaryPrimitives.WriteUInt32BigEndian(f.Slice(head + 8), unchecked(0xB1B0AFBAu - sum));
            }

            directory += 12 + (16 * idx.Length);
        }

        return file;
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

    // TRACE R-OT-0035
    private static void WriteDirectory(Span<byte> f, int at, uint sfntVersion, IReadOnlyList<TableData> tables, int[] offsets, byte[] file, bool recomputeChecksums)
    {
        int n = tables.Count;
        int searchRange = 1;
        int entrySelector = 0;
        while (searchRange * 2 <= n)
        {
            searchRange *= 2;
            entrySelector++;
        }

        searchRange *= 16;
        BinaryPrimitives.WriteUInt32BigEndian(f.Slice(at), sfntVersion);
        BinaryPrimitives.WriteUInt16BigEndian(f.Slice(at + 4), (ushort)n);
        BinaryPrimitives.WriteUInt16BigEndian(f.Slice(at + 6), (ushort)searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(f.Slice(at + 8), (ushort)entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(f.Slice(at + 10), (ushort)((n * 16) - searchRange));

        int[] order = new int[n];
        for (int i = 0; i < n; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (a, b) => tables[a].Tag.CompareTo(tables[b].Tag));
        for (int r = 0; r < n; r++)
        {
            int i = order[r];
            TableData t = tables[i];
            int rec = at + 12 + (16 * r);
            uint checksum = recomputeChecksums || t.Checksum is null
                ? Checksum(new ReadOnlySpan<byte>(file, offsets[i], (t.Data.Length + 3) & ~3))
                : t.Checksum.Value;
            BinaryPrimitives.WriteUInt32BigEndian(f.Slice(rec), t.Tag);
            BinaryPrimitives.WriteUInt32BigEndian(f.Slice(rec + 4), checksum);
            BinaryPrimitives.WriteUInt32BigEndian(f.Slice(rec + 8), (uint)offsets[i]);
            BinaryPrimitives.WriteUInt32BigEndian(f.Slice(rec + 12), (uint)t.Data.Length);
        }
    }
}
