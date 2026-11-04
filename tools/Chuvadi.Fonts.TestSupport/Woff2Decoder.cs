// Test-support WOFF2 decoder (W3C WOFF File Format 2.0). It is compiled into the
// test projects and the local gate only; it does not ship in any library. If
// Arun approves WOFF/WOFF2 support in Chuvadi.OpenType, this file moves to
// src/Chuvadi.OpenType/Woff/ unchanged apart from its namespace and visibility.
// Single fonts only; WOFF2 collections are rejected.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO.Compression;

namespace Chuvadi.TestSupport;

/// <summary>Decodes a WOFF2 file into sfnt bytes.</summary>
public static class Woff2Decoder
{
    // TRACE R-OT-0031
    private static readonly string[] KnownTags =
    {
        "cmap", "head", "hhea", "hmtx", "maxp", "name", "OS/2", "post", "cvt ", "fpgm", "glyf", "loca", "prep", "CFF ", "VORG", "EBDT",
        "EBLC", "gasp", "hdmx", "kern", "LTSH", "PCLT", "VDMX", "vhea", "vmtx", "BASE", "GDEF", "GPOS", "GSUB", "EBSC", "JSTF", "MATH",
        "CBDT", "CBLC", "COLR", "CPAL", "SVG ", "sbix", "acnt", "avar", "bdat", "bloc", "bsln", "cvar", "fdsc", "feat", "fmtx", "fvar",
        "gvar", "hsty", "just", "lcar", "mort", "morx", "opbd", "prop", "trak", "Zapf", "Silf", "Glat", "Gloc", "Feat", "Sill",
    };

    /// <summary>Returns whether the bytes start with the WOFF2 signature.</summary>
    /// <param name="data">The file bytes.</param>
    /// <returns>True for 'wOF2'.</returns>
    public static bool IsWoff2(ReadOnlySpan<byte> data) => data.Length >= 4 && BinaryPrimitives.ReadUInt32BigEndian(data) == 0x774F4632u;

    /// <summary>Decodes WOFF2 to sfnt.</summary>
    /// <param name="woff2">The WOFF2 bytes.</param>
    /// <returns>The sfnt bytes.</returns>
    /// <exception cref="InvalidOperationException">The data is not a valid single-font WOFF2 file.</exception>
    public static byte[] Decode(byte[] woff2)
    {
        ArgumentNullException.ThrowIfNull(woff2);
        Reader r = new Reader(woff2, 0);
        if (r.U32() != 0x774F4632u)
        {
            throw Invalid("signature");
        }

        uint flavor = r.U32();
        if (flavor == 0x74746366u)
        {
            throw Invalid("collections are not supported by this decoder");
        }

        _ = r.U32(); // length
        int numTables = r.U16();
        _ = r.U16(); // reserved
        _ = r.U32(); // totalSfntSize
        int compressedSize = checked((int)r.U32());
        r.Skip(24); // majorVersion, minorVersion, metaOffset, metaLength, metaOrigLength, privOffset, privLength

        List<Entry> entries = new List<Entry>(numTables);
        long streamTotal = 0;
        for (int i = 0; i < numTables; i++)
        {
            int flags = r.U8();
            int tagIndex = flags & 0x3F;
            string tag = tagIndex == 63 ? TagString(r.U32()) : KnownTags[tagIndex];
            int transform = (flags >> 6) & 3;
            uint origLength = r.Base128();
            bool transformed = tag is "glyf" or "loca" ? transform != 3 : transform != 0;
            uint transformLength = transformed ? r.Base128() : origLength;
            entries.Add(new Entry(tag, transform, transformed, origLength, transformLength, streamTotal));
            streamTotal += transformLength;
        }

        byte[] stream = new byte[checked((int)streamTotal)];
        if (!BrotliDecoder.TryDecompress(woff2.AsSpan(r.Position, compressedSize), stream, out int written) || written != stream.Length)
        {
            throw Invalid("the Brotli stream does not decode to the table directory's total length");
        }

        Dictionary<string, byte[]> tables = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        Entry? glyfEntry = null;
        Entry? locaEntry = null;
        Entry? hmtxEntry = null;
        foreach (Entry e in entries)
        {
            if (e.Transformed && e.Tag == "glyf")
            {
                glyfEntry = e;
            }
            else if (e.Transformed && e.Tag == "loca")
            {
                locaEntry = e;
            }
            else if (e.Transformed && e.Tag == "hmtx")
            {
                hmtxEntry = e;
            }
            else if (e.Transformed)
            {
                throw Invalid("unknown transform for table " + e.Tag);
            }
            else
            {
                tables[e.Tag] = stream.AsSpan((int)e.StreamOffset, (int)e.TransformLength).ToArray();
            }
        }

        short[]? xMins = null;
        if (glyfEntry is not null)
        {
            if (locaEntry is null || locaEntry.TransformLength != 0)
            {
                throw Invalid("a transformed glyf table needs a transformed loca entry of length 0");
            }

            (byte[] glyf, byte[] loca, short[] mins, int indexFormat) = ReconstructGlyf(stream.AsSpan((int)glyfEntry.StreamOffset, (int)glyfEntry.TransformLength));
            tables["glyf"] = glyf;
            tables["loca"] = loca;
            xMins = mins;
            if (tables.TryGetValue("head", out byte[]? head) && head.Length >= 54)
            {
                BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(50), (short)indexFormat);
            }
        }
        else if (locaEntry is not null)
        {
            throw Invalid("loca is transformed but glyf is not");
        }

        if (hmtxEntry is not null)
        {
            if (hmtxEntry.Transform != 1 || xMins is null)
            {
                throw Invalid("unsupported hmtx transform");
            }

            tables["hmtx"] = ReconstructHmtx(stream.AsSpan((int)hmtxEntry.StreamOffset, (int)hmtxEntry.TransformLength), tables, xMins);
        }

        return BuildSfnt(flavor, tables);
    }

    // TRACE R-OT-0032
    private static (byte[] Glyf, byte[] Loca, short[] XMins, int IndexFormat) ReconstructGlyf(ReadOnlySpan<byte> t)
    {
        Reader h = new Reader(t.ToArray(), 0);
        _ = h.U16(); // reserved
        int optionFlags = h.U16();
        int numGlyphs = h.U16();
        int indexFormat = h.U16();
        int nContourSize = checked((int)h.U32());
        int nPointsSize = checked((int)h.U32());
        int flagSize = checked((int)h.U32());
        int glyphSize = checked((int)h.U32());
        int compositeSize = checked((int)h.U32());
        int bboxSize = checked((int)h.U32());
        int instructionSize = checked((int)h.U32());
        byte[] all = h.Data;
        int pos = h.Position;
        Reader nContours = new Reader(all, pos, nContourSize);
        pos += nContourSize;
        Reader nPoints = new Reader(all, pos, nPointsSize);
        pos += nPointsSize;
        Reader flagStream = new Reader(all, pos, flagSize);
        pos += flagSize;
        Reader glyphStream = new Reader(all, pos, glyphSize);
        pos += glyphSize;
        Reader composite = new Reader(all, pos, compositeSize);
        pos += compositeSize;
        int bboxBitmapLength = 4 * ((numGlyphs + 31) / 32);
        int bboxBitmap = pos;
        Reader bboxStream = new Reader(all, pos + bboxBitmapLength, bboxSize - bboxBitmapLength);
        pos += bboxSize;
        Reader instructions = new Reader(all, pos, instructionSize);
        pos += instructionSize;
        int overlapBitmap = (optionFlags & 1) != 0 ? pos : -1;

        List<byte> glyf = new List<byte>(t.Length * 2);
        uint[] offsets = new uint[numGlyphs + 1];
        short[] xMins = new short[numGlyphs];
        for (int g = 0; g < numGlyphs; g++)
        {
            offsets[g] = (uint)glyf.Count;
            bool explicitBox = (all[bboxBitmap + (g >> 3)] & (0x80 >> (g & 7))) != 0;
            short nc = nContours.I16();
            if (nc == 0)
            {
                if (explicitBox)
                {
                    throw Invalid("an empty glyph has an explicit bounding box");
                }

                continue;
            }

            List<byte> rec = new List<byte>();
            if (nc > 0)
            {
                int[] endPts = new int[nc];
                int total = 0;
                for (int c = 0; c < nc; c++)
                {
                    total += nPoints.Read255UInt16();
                    endPts[c] = total - 1;
                }

                int[] xs = new int[total];
                int[] ys = new int[total];
                bool[] onCurve = new bool[total];
                int x = 0;
                int y = 0;
                for (int p = 0; p < total; p++)
                {
                    int flag = flagStream.U8();
                    onCurve[p] = (flag & 0x80) == 0;
                    (int dx, int dy) = ReadTriplet(flag & 0x7F, ref glyphStream);
                    x += dx;
                    y += dy;
                    xs[p] = x;
                    ys[p] = y;
                }

                int instructionLength = glyphStream.Read255UInt16();
                byte[] code = instructions.Bytes(instructionLength);

                short xMin;
                short yMin;
                short xMax;
                short yMax;
                if (explicitBox)
                {
                    xMin = bboxStream.I16();
                    yMin = bboxStream.I16();
                    xMax = bboxStream.I16();
                    yMax = bboxStream.I16();
                }
                else
                {
                    int a = int.MaxValue, b = int.MaxValue, c2 = int.MinValue, d = int.MinValue;
                    for (int p = 0; p < total; p++)
                    {
                        a = Math.Min(a, xs[p]);
                        b = Math.Min(b, ys[p]);
                        c2 = Math.Max(c2, xs[p]);
                        d = Math.Max(d, ys[p]);
                    }

                    xMin = (short)(total == 0 ? 0 : a);
                    yMin = (short)(total == 0 ? 0 : b);
                    xMax = (short)(total == 0 ? 0 : c2);
                    yMax = (short)(total == 0 ? 0 : d);
                }

                xMins[g] = xMin;
                bool overlap = overlapBitmap >= 0 && (all[overlapBitmap + (g >> 3)] & (0x80 >> (g & 7))) != 0;
                Put16(rec, nc);
                Put16(rec, xMin);
                Put16(rec, yMin);
                Put16(rec, xMax);
                Put16(rec, yMax);
                foreach (int e in endPts)
                {
                    Put16(rec, e);
                }

                Put16(rec, instructionLength);
                rec.AddRange(code);
                // Standard TrueType point encoding (OpenType 'glyf', simple glyph flags): a delta of 0 uses the
                // "same" flag, |delta| <= 255 the one-byte short form, anything else an int16.
                List<byte> xBytes = new List<byte>();
                List<byte> yBytes = new List<byte>();
                int px = 0;
                int py = 0;
                for (int p = 0; p < total; p++)
                {
                    int f = onCurve[p] ? 0x01 : 0;
                    if (p == 0 && overlap)
                    {
                        f |= 0x40;
                    }

                    int dx = xs[p] - px;
                    int dy = ys[p] - py;
                    px = xs[p];
                    py = ys[p];
                    if (dx == 0)
                    {
                        f |= 0x10;
                    }
                    else if (dx >= -255 && dx <= 255)
                    {
                        f |= 0x02 | (dx > 0 ? 0x10 : 0);
                        xBytes.Add((byte)Math.Abs(dx));
                    }
                    else
                    {
                        Put16(xBytes, dx);
                    }

                    if (dy == 0)
                    {
                        f |= 0x20;
                    }
                    else if (dy >= -255 && dy <= 255)
                    {
                        f |= 0x04 | (dy > 0 ? 0x20 : 0);
                        yBytes.Add((byte)Math.Abs(dy));
                    }
                    else
                    {
                        Put16(yBytes, dy);
                    }

                    rec.Add((byte)f);
                }

                rec.AddRange(xBytes);
                rec.AddRange(yBytes);
            }
            else
            {
                if (!explicitBox)
                {
                    throw Invalid("a composite glyph has no explicit bounding box");
                }

                short xMin = bboxStream.I16();
                Put16(rec, -1);
                Put16(rec, xMin);
                Put16(rec, bboxStream.I16());
                Put16(rec, bboxStream.I16());
                Put16(rec, bboxStream.I16());
                xMins[g] = xMin;
                bool more;
                bool haveInstructions = false;
                do
                {
                    int flags = composite.U16();
                    Put16(rec, flags);
                    int argBytes = 2 + ((flags & 0x0001) != 0 ? 4 : 2);
                    if ((flags & 0x0008) != 0)
                    {
                        argBytes += 2;
                    }
                    else if ((flags & 0x0040) != 0)
                    {
                        argBytes += 4;
                    }
                    else if ((flags & 0x0080) != 0)
                    {
                        argBytes += 8;
                    }

                    rec.AddRange(composite.Bytes(argBytes));
                    haveInstructions |= (flags & 0x0100) != 0;
                    more = (flags & 0x0020) != 0;
                }
                while (more);

                if (haveInstructions)
                {
                    int instructionLength = glyphStream.Read255UInt16();
                    Put16(rec, instructionLength);
                    rec.AddRange(instructions.Bytes(instructionLength));
                }
            }

            glyf.AddRange(rec);
            while (glyf.Count % 4 != 0)
            {
                glyf.Add(0);
            }
        }

        offsets[numGlyphs] = (uint)glyf.Count;

        // Short offsets hold at most 2 x 65535 bytes. If the rebuilt table is larger, use long offsets
        // (the caller then sets head.indexToLocFormat to 1).
        if (indexFormat == 0 && glyf.Count > 0x1FFFE)
        {
            indexFormat = 1;
        }

        List<byte> loca = new List<byte>();
        foreach (uint o in offsets)
        {
            if (indexFormat == 0)
            {
                Put16(loca, (int)(o / 2));
            }
            else
            {
                Put32(loca, o);
            }
        }

        return (glyf.ToArray(), loca.ToArray(), xMins, indexFormat);
    }

    // TRACE R-OT-0033
    private static (int Dx, int Dy) ReadTriplet(int index, ref Reader s)
    {
        static int Sign(int value, bool positive) => positive ? value : -value;

        if (index < 10)
        {
            int b = s.U8();
            return (0, Sign(((index >> 1) * 256) + b, (index & 1) != 0));
        }

        if (index < 20)
        {
            int k = index - 10;
            int b = s.U8();
            return (Sign(((k >> 1) * 256) + b, (k & 1) != 0), 0);
        }

        if (index < 84)
        {
            int k = index - 20;
            int b = s.U8();
            int dx = 1 + ((k >> 4) * 16) + (b >> 4);
            int dy = 1 + (((k >> 2) & 3) * 16) + (b & 0x0F);
            return (Sign(dx, (k & 1) != 0), Sign(dy, (k & 2) != 0));
        }

        if (index < 120)
        {
            int k = index - 84;
            int b0 = s.U8();
            int b1 = s.U8();
            int dx = 1 + ((k / 12) * 256) + b0;
            int dy = 1 + (((k % 12) >> 2) * 256) + b1;
            return (Sign(dx, (k & 1) != 0), Sign(dy, (k & 2) != 0));
        }

        if (index < 124)
        {
            int k = index - 120;
            int b0 = s.U8();
            int b1 = s.U8();
            int b2 = s.U8();
            return (Sign((b0 << 4) | (b1 >> 4), (k & 1) != 0), Sign(((b1 & 0x0F) << 8) | b2, (k & 2) != 0));
        }

        {
            int k = index - 124;
            int x = s.U16();
            int y = s.U16();
            return (Sign(x, (k & 1) != 0), Sign(y, (k & 2) != 0));
        }
    }

    // TRACE R-OT-0034
    private static byte[] ReconstructHmtx(ReadOnlySpan<byte> t, Dictionary<string, byte[]> tables, short[] xMins)
    {
        byte[] hhea = tables["hhea"];
        byte[] maxp = tables["maxp"];
        int numberOfHMetrics = BinaryPrimitives.ReadUInt16BigEndian(hhea.AsSpan(34));
        int numGlyphs = BinaryPrimitives.ReadUInt16BigEndian(maxp.AsSpan(4));
        Reader r = new Reader(t.ToArray(), 0);
        int flags = r.U8();
        if ((flags & 3) == 0 || (flags & 0xFC) != 0)
        {
            throw Invalid("hmtx transform flags");
        }

        int[] advances = new int[numberOfHMetrics];
        for (int i = 0; i < numberOfHMetrics; i++)
        {
            advances[i] = r.U16();
        }

        List<byte> o = new List<byte>();
        for (int i = 0; i < numberOfHMetrics; i++)
        {
            Put16(o, advances[i]);
            Put16(o, (flags & 1) != 0 ? xMins[i] : r.I16());
        }

        for (int i = numberOfHMetrics; i < numGlyphs; i++)
        {
            Put16(o, (flags & 2) != 0 ? xMins[i] : r.I16());
        }

        return o.ToArray();
    }

    private static byte[] BuildSfnt(uint flavor, Dictionary<string, byte[]> tables)
    {
        List<string> tags = new List<string>(tables.Keys);
        tags.Sort(StringComparer.Ordinal);
        int n = tags.Count;
        int searchRange = 1;
        int entrySelector = 0;
        while (searchRange * 2 <= n)
        {
            searchRange *= 2;
            entrySelector++;
        }

        searchRange *= 16;
        int headerLength = 12 + (16 * n);
        long total = headerLength;
        foreach (string tag in tags)
        {
            total += (tables[tag].Length + 3) & ~3;
        }

        byte[] font = new byte[total];
        Span<byte> f = font;
        BinaryPrimitives.WriteUInt32BigEndian(f, flavor);
        BinaryPrimitives.WriteUInt16BigEndian(f.Slice(4), (ushort)n);
        BinaryPrimitives.WriteUInt16BigEndian(f.Slice(6), (ushort)searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(f.Slice(8), (ushort)entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(f.Slice(10), (ushort)((n * 16) - searchRange));
        int offset = headerLength;
        int headOffset = -1;
        for (int i = 0; i < n; i++)
        {
            byte[] data = tables[tags[i]];
            if (tags[i] == "head" && data.Length >= 12)
            {
                BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), 0);
                headOffset = offset;
            }

            data.CopyTo(f.Slice(offset));
            int rec = 12 + (16 * i);
            BinaryPrimitives.WriteUInt32BigEndian(f.Slice(rec), TagValue(tags[i]));
            BinaryPrimitives.WriteUInt32BigEndian(f.Slice(rec + 4), Checksum(f.Slice(offset, (data.Length + 3) & ~3)));
            BinaryPrimitives.WriteUInt32BigEndian(f.Slice(rec + 8), (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(f.Slice(rec + 12), (uint)data.Length);
            offset += (data.Length + 3) & ~3;
        }

        if (headOffset >= 0)
        {
            BinaryPrimitives.WriteUInt32BigEndian(f.Slice(headOffset + 8), unchecked(0xB1B0AFBAu - Checksum(f)));
        }

        return font;
    }

    private static uint Checksum(ReadOnlySpan<byte> s)
    {
        uint sum = 0;
        for (int i = 0; i + 3 < s.Length; i += 4)
        {
            sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(s.Slice(i)));
        }

        return sum;
    }

    private static void Put16(List<byte> l, int v)
    {
        l.Add((byte)(v >> 8));
        l.Add((byte)v);
    }

    private static void Put32(List<byte> l, uint v)
    {
        l.Add((byte)(v >> 24));
        l.Add((byte)(v >> 16));
        l.Add((byte)(v >> 8));
        l.Add((byte)v);
    }

    private static string TagString(uint v) =>
        new string(new[] { (char)(v >> 24), (char)((v >> 16) & 0xFF), (char)((v >> 8) & 0xFF), (char)(v & 0xFF) });

    private static uint TagValue(string tag) => ((uint)tag[0] << 24) | ((uint)tag[1] << 16) | ((uint)tag[2] << 8) | tag[3];

    private static InvalidOperationException Invalid(string what) => new InvalidOperationException("Invalid WOFF2 data: " + what + ".");

    private sealed record Entry(string Tag, int Transform, bool Transformed, uint OrigLength, uint TransformLength, long StreamOffset);

    private struct Reader
    {
        private readonly int _end;

        internal Reader(byte[] data, int position, int length = -1)
        {
            Data = data;
            Position = position;
            _end = length < 0 ? data.Length : Math.Min(data.Length, position + length);
        }

        internal byte[] Data { get; }

        internal int Position { get; private set; }

        internal int U8()
        {
            if (Position >= _end)
            {
                throw Invalid("a stream ends early");
            }

            return Data[Position++];
        }

        internal int U16() => (U8() << 8) | U8();

        internal short I16() => unchecked((short)U16());

        internal uint U32() => ((uint)U16() << 16) | (uint)U16();

        internal void Skip(int n) => Position += n;

        internal byte[] Bytes(int n)
        {
            if (n < 0 || Position + n > _end)
            {
                throw Invalid("a stream ends early");
            }

            byte[] b = Data.AsSpan(Position, n).ToArray();
            Position += n;
            return b;
        }

        // TRACE R-OT-0031
        internal uint Base128()
        {
            uint accum = 0;
            for (int i = 0; i < 5; i++)
            {
                int b = U8();
                if (i == 0 && b == 0x80)
                {
                    throw Invalid("UIntBase128 with a leading zero");
                }

                if ((accum & 0xFE000000u) != 0)
                {
                    throw Invalid("UIntBase128 overflow");
                }

                accum = (accum << 7) | (uint)(b & 0x7F);
                if ((b & 0x80) == 0)
                {
                    return accum;
                }
            }

            throw Invalid("UIntBase128 longer than 5 bytes");
        }

        // TRACE R-OT-0031
        internal int Read255UInt16()
        {
            int code = U8();
            return code switch
            {
                253 => U16(),
                255 => U8() + 253,
                254 => U8() + 506,
                _ => code,
            };
        }
    }
}
