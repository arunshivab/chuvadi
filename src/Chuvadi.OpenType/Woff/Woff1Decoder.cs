using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Chuvadi.OpenType.Internal;

namespace Chuvadi.OpenType.Woff;

/// <summary>Decodes WOFF 1.0 (W3C Recommendation) into the sfnt font it packages. zlib comes from the .NET base class library.</summary>
internal static class Woff1Decoder
{
    // TRACE R-OT-0036
    internal static byte[] Decode(ReadOnlySpan<byte> s)
    {
        if (s.Length < 44 || BigEndian.U32(s, 0) != 0x774F4646u)
        {
            throw Invalid("the header is missing or the signature is wrong");
        }

        uint flavor = BigEndian.U32(s, 4);
        int numTables = BigEndian.U16(s, 12);
        if (BigEndian.U16(s, 14) != 0)
        {
            throw Invalid("the reserved header field is not zero");
        }

        uint totalSfntSize = BigEndian.U32(s, 16);
        uint metaOffset = BigEndian.U32(s, 24);
        uint metaLength = BigEndian.U32(s, 28);
        uint privOffset = BigEndian.U32(s, 36);
        uint privLength = BigEndian.U32(s, 40);
        if ((long)44 + (20L * numTables) > s.Length)
        {
            throw Invalid("the table directory runs past the end of the file");
        }

        List<(long Start, long End)> ranges = new List<(long, long)> { (0, 44 + (20L * numTables)) };
        AddBlock(ranges, metaOffset, metaLength, s.Length, "metadata");
        AddBlock(ranges, privOffset, privLength, s.Length, "private data");

        long expectedSize = 12 + (16L * numTables);
        List<(uint Tag, uint Offset, uint CompLength, uint OrigLength, uint Checksum)> dir = new List<(uint, uint, uint, uint, uint)>(numTables);
        for (int i = 0; i < numTables; i++)
        {
            int e = 44 + (20 * i);
            uint tag = BigEndian.U32(s, e);
            uint offset = BigEndian.U32(s, e + 4);
            uint compLength = BigEndian.U32(s, e + 8);
            uint origLength = BigEndian.U32(s, e + 12);
            uint checksum = BigEndian.U32(s, e + 16);
            if (compLength > origLength)
            {
                throw Invalid("a table's compressed length is greater than its original length");
            }

            AddBlock(ranges, offset, compLength, s.Length, "a font table");
            expectedSize += (origLength + 3L) & ~3L;
            dir.Add((tag, offset, compLength, origLength, checksum));
        }

        ranges.Sort();
        for (int i = 1; i < ranges.Count; i++)
        {
            if (ranges[i].Start < ranges[i - 1].End)
            {
                throw Invalid("data blocks or tables overlap");
            }
        }

        if (totalSfntSize != expectedSize || totalSfntSize % 4 != 0)
        {
            throw Invalid("totalSfntSize does not match the tables");
        }

        // TRACE R-OT-0038
        if (totalSfntSize > WoffLimits.MaxDecodedBytes)
        {
            throw new OpenTypeFormatException("The WOFF data would decode to more than " + WoffLimits.MaxDecodedBytes + " bytes.");
        }

        // Tables keep their order in the file (the input font's order); the directory is sorted by tag;
        // stored checksums are kept, so a well-formed input font comes back byte for byte.
        dir.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        List<TableData> tables = new List<TableData>(numTables);
        foreach ((uint tag, uint offset, uint compLength, uint origLength, uint checksum) in dir)
        {
            ReadOnlySpan<byte> stored = s.Slice((int)offset, (int)compLength);
            byte[] table = compLength == origLength ? stored.ToArray() : Inflate(stored, (int)origLength);
            tables.Add(new TableData(tag, table, checksum));
        }

        return SfntWriter.WriteFont(flavor, tables, recomputeChecksums: false);
    }

    private static byte[] Inflate(ReadOnlySpan<byte> compressed, int origLength)
    {
        byte[] output = new byte[origLength];
        try
        {
            using MemoryStream input = new MemoryStream(compressed.ToArray(), writable: false);
            using ZLibStream z = new ZLibStream(input, CompressionMode.Decompress);
            int total = 0;
            while (total < origLength)
            {
                int n = z.Read(output, total, origLength - total);
                if (n == 0)
                {
                    break;
                }

                total += n;
            }

            if (total != origLength || z.Read(new byte[1], 0, 1) != 0)
            {
                throw Invalid("a compressed table does not decompress to its original length");
            }
        }
        catch (InvalidDataException e)
        {
            throw new OpenTypeFormatException("Invalid WOFF data: a table does not decompress.", e);
        }

        return output;
    }

    private static void AddBlock(List<(long Start, long End)> ranges, uint offset, uint length, int fileLength, string what)
    {
        if (offset == 0 && length == 0)
        {
            return;
        }

        if ((long)offset + length > fileLength)
        {
            throw Invalid(what + " extends past the end of the file");
        }

        ranges.Add((offset, (long)offset + length));
    }

    private static OpenTypeFormatException Invalid(string what) => new OpenTypeFormatException("Invalid WOFF data: " + what + ".");
}
