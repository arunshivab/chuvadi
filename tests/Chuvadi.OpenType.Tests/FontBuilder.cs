using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace Chuvadi.OpenType.Tests;

/// <summary>Builds small synthetic sfnt fonts for tests, table by table, following the OpenType table layouts.</summary>
internal sealed class FontBuilder
{
    private readonly SortedDictionary<string, byte[]> _tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);

    internal uint SfntVersion { get; set; } = 0x00010000u;

    internal FontBuilder Table(string tag, byte[] data)
    {
        _tables[tag] = data;
        return this;
    }

    internal FontBuilder Remove(string tag)
    {
        _ = _tables.Remove(tag);
        return this;
    }

    /// <summary>A font with head, maxp, hhea and hmtx for the given advances (numberOfHMetrics = advances.Length).</summary>
    internal static FontBuilder Basic(int glyphCount, int unitsPerEm = 1000, int[]? advances = null, short[]? lsbs = null, int indexToLocFormat = 0)
    {
        advances ??= Enumerable.Range(0, glyphCount).Select(g => 500 + g).ToArray();
        lsbs ??= Enumerable.Range(0, glyphCount).Select(g => (short)g).ToArray();
        return new FontBuilder()
            .Table("head", Head(unitsPerEm, indexToLocFormat))
            .Table("maxp", Maxp(glyphCount))
            .Table("hhea", Hhea(advances.Length))
            .Table("hmtx", Hmtx(advances, lsbs));
    }

    internal byte[] Build() => BuildDirectory(SfntVersion, _tables, 0, out _);

    internal static byte[] Collection(params FontBuilder[] fonts)
    {
        List<byte> data = new List<byte>();
        int headerLength = 12 + (4 * fonts.Length);
        byte[] header = new byte[headerLength];
        BinaryPrimitives.WriteUInt32BigEndian(header, 0x74746366u);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), 0x00010000u);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), (uint)fonts.Length);
        data.AddRange(header);
        for (int i = 0; i < fonts.Length; i++)
        {
            int offset = data.Count;
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12 + (4 * i)), (uint)offset);
            data.AddRange(BuildDirectory(fonts[i].SfntVersion, fonts[i]._tables, offset, out _));
        }

        byte[] result = data.ToArray();
        header.CopyTo(result, 0);
        return result;
    }

    internal static byte[] Head(int unitsPerEm, int indexToLocFormat = 0)
    {
        byte[] b = new byte[54];
        W16(b, 0, 1);
        W32(b, 12, 0x5F0F3CF5u);
        W16(b, 18, unitsPerEm);
        W16(b, 36, unchecked((ushort)-100));
        W16(b, 40, 1000);
        W16(b, 42, 900);
        W16(b, 50, indexToLocFormat);
        return b;
    }

    internal static byte[] Maxp(int glyphCount)
    {
        byte[] b = new byte[6];
        W32(b, 0, 0x00005000u);
        W16(b, 4, glyphCount);
        return b;
    }

    internal static byte[] Hhea(int numberOfHMetrics)
    {
        byte[] b = new byte[36];
        W16(b, 0, 1);
        W16(b, 4, 800);
        W16(b, 6, unchecked((ushort)-200));
        W16(b, 34, numberOfHMetrics);
        return b;
    }

    internal static byte[] Hmtx(int[] advances, short[] lsbs)
    {
        List<byte> b = new List<byte>();
        for (int i = 0; i < lsbs.Length; i++)
        {
            if (i < advances.Length)
            {
                P16(b, advances[i]);
            }

            P16(b, lsbs[i]);
        }

        return b.ToArray();
    }

    /// <summary>A 'cmap' with the given (platform, encoding, subtable bytes) records.</summary>
    internal static byte[] Cmap(params (int Platform, int Encoding, byte[] Subtable)[] records)
    {
        List<byte> b = new List<byte>();
        P16(b, 0);
        P16(b, records.Length);
        int offset = 4 + (8 * records.Length);
        List<byte> body = new List<byte>();
        foreach ((int p, int e, byte[] sub) in records)
        {
            P16(b, p);
            P16(b, e);
            P32(b, (uint)(offset + body.Count));
            body.AddRange(sub);
        }

        b.AddRange(body);
        return b.ToArray();
    }

    internal static byte[] CmapFormat0(IDictionary<int, int> map)
    {
        byte[] b = new byte[262];
        W16(b, 0, 0);
        W16(b, 2, 262);
        foreach (KeyValuePair<int, int> kv in map)
        {
            b[6 + kv.Key] = (byte)kv.Value;
        }

        return b;
    }

    /// <summary>Format 4 with one segment per contiguous code run; idRangeOffset glyph array for segments when useArray.</summary>
    internal static byte[] CmapFormat4(IDictionary<int, int> map, bool useArray)
    {
        List<(int Start, int End)> segs = new List<(int, int)>();
        foreach (int c in map.Keys.OrderBy(k => k))
        {
            if (segs.Count > 0 && segs[^1].End == c - 1)
            {
                segs[^1] = (segs[^1].Start, c);
            }
            else
            {
                segs.Add((c, c));
            }
        }

        segs.Add((0xFFFF, 0xFFFF));
        int segCount = segs.Count;
        List<byte> ends = new List<byte>();
        List<byte> starts = new List<byte>();
        List<byte> deltas = new List<byte>();
        List<byte> rangeOffsets = new List<byte>();
        List<byte> glyphArray = new List<byte>();
        for (int i = 0; i < segCount; i++)
        {
            (int s, int e) = segs[i];
            P16(ends, e);
            P16(starts, s);
            if (s == 0xFFFF)
            {
                P16(deltas, 1);
                P16(rangeOffsets, 0);
            }
            else if (useArray)
            {
                P16(deltas, 0);
                int distance = ((segCount - i) * 2) + glyphArray.Count;
                P16(rangeOffsets, distance);
                for (int c = s; c <= e; c++)
                {
                    P16(glyphArray, map[c]);
                }
            }
            else
            {
                P16(deltas, (map[s] - s) & 0xFFFF);
                P16(rangeOffsets, 0);
            }
        }

        List<byte> b = new List<byte>();
        P16(b, 4);
        P16(b, 0); // length, patched below
        P16(b, 0);
        P16(b, segCount * 2);
        P16(b, 0);
        P16(b, 0);
        P16(b, 0);
        b.AddRange(ends);
        P16(b, 0);
        b.AddRange(starts);
        b.AddRange(deltas);
        b.AddRange(rangeOffsets);
        b.AddRange(glyphArray);
        byte[] r = b.ToArray();
        W16(r, 2, r.Length);
        return r;
    }

    internal static byte[] CmapFormat6(int firstCode, int[] glyphs)
    {
        List<byte> b = new List<byte>();
        P16(b, 6);
        P16(b, 10 + (2 * glyphs.Length));
        P16(b, 0);
        P16(b, firstCode);
        P16(b, glyphs.Length);
        foreach (int g in glyphs)
        {
            P16(b, g);
        }

        return b.ToArray();
    }

    internal static byte[] CmapFormat10(int firstCode, int[] glyphs)
    {
        List<byte> b = new List<byte>();
        P16(b, 10);
        P16(b, 0);
        P32(b, (uint)(20 + (2 * glyphs.Length)));
        P32(b, 0);
        P32(b, (uint)firstCode);
        P32(b, (uint)glyphs.Length);
        foreach (int g in glyphs)
        {
            P16(b, g);
        }

        return b.ToArray();
    }

    internal static byte[] CmapFormat12Or13(int format, params (int Start, int End, int Glyph)[] groups)
    {
        List<byte> b = new List<byte>();
        P16(b, format);
        P16(b, 0);
        P32(b, (uint)(16 + (12 * groups.Length)));
        P32(b, 0);
        P32(b, (uint)groups.Length);
        foreach ((int s, int e, int g) in groups)
        {
            P32(b, (uint)s);
            P32(b, (uint)e);
            P32(b, (uint)g);
        }

        return b.ToArray();
    }

    /// <summary>Format 2 with single bytes 0x00–0x7F mapped through subHeader 0 and one lead byte 0x81 with trail bytes 0x40..0x42.</summary>
    internal static byte[] CmapFormat2(int[] singleByteGlyphs, int[] leadByte81Glyphs)
    {
        List<byte> b = new List<byte>();
        P16(b, 2);
        P16(b, 0);
        P16(b, 0);
        for (int hi = 0; hi < 256; hi++)
        {
            P16(b, hi == 0x81 ? 8 : 0);
        }

        // subHeader 0: firstCode 0, entryCount 128; subHeader 1: firstCode 0x40, entryCount 3.
        int subHeaders = b.Count;
        int arrayStart = subHeaders + 16;
        P16(b, 0);
        P16(b, 128);
        P16(b, 0);
        P16(b, arrayStart - (subHeaders + 6));
        P16(b, 0x40);
        P16(b, leadByte81Glyphs.Length);
        P16(b, 0);
        P16(b, arrayStart + (2 * 128) - (subHeaders + 8 + 6));
        for (int i = 0; i < 128; i++)
        {
            P16(b, i < singleByteGlyphs.Length ? singleByteGlyphs[i] : 0);
        }

        foreach (int g in leadByte81Glyphs)
        {
            P16(b, g);
        }

        byte[] r = b.ToArray();
        W16(r, 2, r.Length);
        return r;
    }

    internal static byte[] CmapFormat14(int selector, (int Start, int Extra)[] defaults, (int Cp, int Glyph)[] own)
    {
        List<byte> b = new List<byte>();
        P16(b, 14);
        P32(b, 0);
        P32(b, 1);
        int defaultOffset = 10 + 11;
        int nonDefaultOffset = defaultOffset + 4 + (4 * defaults.Length);
        P24(b, selector);
        P32(b, (uint)defaultOffset);
        P32(b, (uint)nonDefaultOffset);
        P32(b, (uint)defaults.Length);
        foreach ((int s, int e) in defaults)
        {
            P24(b, s);
            b.Add((byte)e);
        }

        P32(b, (uint)own.Length);
        foreach ((int c, int g) in own)
        {
            P24(b, c);
            P16(b, g);
        }

        byte[] r = b.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(r.AsSpan(2), (uint)r.Length);
        return r;
    }

    /// <summary>'glyf' and 'loca' for glyphs whose bounding box is given (null = empty glyph).</summary>
    internal static (byte[] Glyf, byte[] Loca) GlyfLoca((short XMin, short YMin, short XMax, short YMax)?[] boxes, int indexToLocFormat)
    {
        List<byte> glyf = new List<byte>();
        List<uint> offsets = new List<uint>();
        foreach ((short, short, short, short)? box in boxes)
        {
            offsets.Add((uint)glyf.Count);
            if (box is (short x0, short y0, short x1, short y1))
            {
                P16(glyf, 1);
                P16(glyf, x0);
                P16(glyf, y0);
                P16(glyf, x1);
                P16(glyf, y1);
                P16(glyf, 0); // endPtsOfContours[0]
                P16(glyf, 0); // instructionLength
                glyf.Add(1); // one on-curve point at (0,0) as int16 deltas
                P16(glyf, 0);
                P16(glyf, 0);
                while (glyf.Count % 4 != 0)
                {
                    glyf.Add(0);
                }
            }
        }

        offsets.Add((uint)glyf.Count);
        List<byte> loca = new List<byte>();
        foreach (uint o in offsets)
        {
            if (indexToLocFormat == 0)
            {
                P16(loca, (int)(o / 2));
            }
            else
            {
                P32(loca, o);
            }
        }

        return (glyf.ToArray(), loca.ToArray());
    }

    internal static byte[] Name(params (int Platform, int Encoding, int Language, int NameId, byte[] Bytes)[] records)
    {
        List<byte> b = new List<byte>();
        P16(b, 0);
        P16(b, records.Length);
        P16(b, 6 + (12 * records.Length));
        List<byte> storage = new List<byte>();
        foreach ((int p, int e, int l, int n, byte[] s) in records)
        {
            P16(b, p);
            P16(b, e);
            P16(b, l);
            P16(b, n);
            P16(b, s.Length);
            P16(b, storage.Count);
            storage.AddRange(s);
        }

        b.AddRange(storage);
        return b.ToArray();
    }

    internal static byte[] KernFormat0(params (int Left, int Right, short Value)[] pairs)
    {
        List<byte> b = new List<byte>();
        P16(b, 0);
        P16(b, 1);
        P16(b, 0);
        P16(b, 14 + (6 * pairs.Length));
        P16(b, 0x0001);
        P16(b, pairs.Length);
        P16(b, 0);
        P16(b, 0);
        P16(b, 0);
        foreach ((int l, int r, short v) in pairs.OrderBy(p => ((uint)p.Left << 16) | (uint)p.Right))
        {
            P16(b, l);
            P16(b, r);
            P16(b, v);
        }

        return b.ToArray();
    }

    internal static void W16(byte[] b, int o, int v) => BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(o), unchecked((ushort)v));

    internal static void W32(byte[] b, int o, uint v) => BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(o), v);

    internal static void P16(List<byte> b, int v)
    {
        b.Add((byte)(v >> 8));
        b.Add((byte)v);
    }

    internal static void P24(List<byte> b, int v)
    {
        b.Add((byte)(v >> 16));
        b.Add((byte)(v >> 8));
        b.Add((byte)v);
    }

    internal static void P32(List<byte> b, uint v)
    {
        b.Add((byte)(v >> 24));
        b.Add((byte)(v >> 16));
        b.Add((byte)(v >> 8));
        b.Add((byte)v);
    }

    private static byte[] BuildDirectory(uint sfntVersion, SortedDictionary<string, byte[]> tables, int baseOffset, out int length)
    {
        int n = tables.Count;
        int header = 12 + (16 * n);
        List<byte> data = new List<byte>(new byte[header]);
        byte[] dir = new byte[header];
        W32(dir, 0, sfntVersion);
        W16(dir, 4, n);
        int i = 0;
        foreach (KeyValuePair<string, byte[]> kv in tables)
        {
            int offset = baseOffset + data.Count;
            int r = 12 + (16 * i);
            uint tag = ((uint)kv.Key[0] << 24) | ((uint)kv.Key[1] << 16) | ((uint)kv.Key[2] << 8) | kv.Key[3];
            W32(dir, r, tag);
            W32(dir, r + 8, (uint)offset);
            W32(dir, r + 12, (uint)kv.Value.Length);
            data.AddRange(kv.Value);
            while (data.Count % 4 != 0)
            {
                data.Add(0);
            }

            i++;
        }

        byte[] result = data.ToArray();
        dir.CopyTo(result, 0);
        length = result.Length;
        return result;
    }
}
