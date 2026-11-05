using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO.Compression;
using Chuvadi.OpenType.Internal;

namespace Chuvadi.OpenType.Woff;

/// <summary>
/// Decodes WOFF 2.0 (W3C Recommendation) into an sfnt font or, for a WOFF2 collection, a 'ttcf' collection.
/// Brotli decompression comes from the .NET base class library.
/// </summary>
internal static class Woff2Decoder
{
    // TRACE R-OT-0031
    private static readonly string[] KnownTags =
    {
        "cmap", "head", "hhea", "hmtx", "maxp", "name", "OS/2", "post", "cvt ", "fpgm", "glyf", "loca", "prep", "CFF ", "VORG", "EBDT",
        "EBLC", "gasp", "hdmx", "kern", "LTSH", "PCLT", "VDMX", "vhea", "vmtx", "BASE", "GDEF", "GPOS", "GSUB", "EBSC", "JSTF", "MATH",
        "CBDT", "CBLC", "COLR", "CPAL", "SVG ", "sbix", "acnt", "avar", "bdat", "bloc", "bsln", "cvar", "fdsc", "feat", "fmtx", "fvar",
        "gvar", "hsty", "just", "lcar", "mort", "morx", "opbd", "prop", "trak", "Zapf", "Silf", "Glat", "Gloc", "Feat", "Sill",
    };

    /// <summary>Decodes WOFF2 data.</summary>
    /// <exception cref="OpenTypeFormatException">The data is not valid WOFF2, or decodes to more than the limit.</exception>
    internal static byte[] Decode(ReadOnlySpan<byte> input)
    {
        byte[] woff2 = input.ToArray();
        try
        {
            return DecodeCore(woff2);
        }
        catch (Exception e) when (e is OverflowException or IndexOutOfRangeException or ArgumentException or InvalidOperationException)
        {
            // Every structural fault in damaged data surfaces as OpenTypeFormatException.
            throw new OpenTypeFormatException("Invalid WOFF2 data: " + e.Message, e);
        }
    }

    private static byte[] DecodeCore(byte[] woff2)
    {
        Reader r = new Reader(woff2, 0);
        if (r.U32() != 0x774F4632u)
        {
            throw Invalid("signature");
        }

        uint flavor = r.U32();
        _ = r.U32(); // length
        int numTables = r.U16();
        _ = r.U16(); // reserved: a decoder must not reject a non-zero value
        _ = r.U32(); // totalSfntSize: reference only
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

        // TRACE R-OT-0037
        List<(uint Flavor, int[] Indexes)> fonts = new List<(uint, int[])>();
        if (flavor == 0x74746366u)
        {
            _ = r.U32(); // collection version: output is always written as version 1.0
            int numFonts = r.Read255UInt16();
            for (int k = 0; k < numFonts; k++)
            {
                int n = r.Read255UInt16();
                uint fontFlavor = r.U32();
                int[] idx = new int[n];
                for (int i = 0; i < n; i++)
                {
                    idx[i] = r.Read255UInt16();
                    if (idx[i] >= entries.Count)
                    {
                        throw Invalid("a collection font refers to a table that is not in the directory");
                    }
                }

                fonts.Add((fontFlavor, idx));
            }
        }
        else
        {
            int[] all = new int[entries.Count];
            for (int i = 0; i < all.Length; i++)
            {
                all[i] = i;
            }

            fonts.Add((flavor, all));
        }

        // TRACE R-OT-0038
        if (streamTotal > WoffLimits.MaxDecodedBytes)
        {
            throw new OpenTypeFormatException("The WOFF2 data would decode to more than " + WoffLimits.MaxDecodedBytes + " bytes.");
        }

        byte[] stream = new byte[checked((int)streamTotal)];
        if (r.Position + (long)compressedSize > woff2.Length ||
            !BrotliDecoder.TryDecompress(woff2.AsSpan(r.Position, compressedSize), stream, out int written) || written != stream.Length)
        {
            throw Invalid("the Brotli stream does not decode to the table directory's total length");
        }

        byte[]?[] data = new byte[entries.Count][];
        short[]?[] xMinsByGlyf = new short[entries.Count][];
        int[] locaFormatByGlyf = new int[entries.Count];
        Dictionary<int, int> locaOf = new Dictionary<int, int>();
        for (int i = 0; i < entries.Count; i++)
        {
            Entry e = entries[i];
            if (!e.Transformed)
            {
                data[i] = stream.AsSpan((int)e.StreamOffset, (int)e.TransformLength).ToArray();
            }
            else if (e.Tag == "glyf")
            {
                // TRACE R-OT-0039
                // Its transformed loca is the next transformed loca entry in the directory, with transformLength 0.
                // (Encoders must put it right after glyf; fonts in the wild sometimes have tables in between.)
                int l = -1;
                for (int k = i + 1; k < entries.Count && l < 0; k++)
                {
                    if (entries[k].Tag == "loca" && entries[k].Transformed)
                    {
                        l = k;
                    }
                }

                if (l < 0 || entries[l].TransformLength != 0 || locaOf.ContainsValue(l))
                {
                    throw Invalid("a transformed glyf table needs its own transformed loca entry of length 0");
                }

                ReadOnlySpan<byte> t = stream.AsSpan((int)e.StreamOffset, (int)e.TransformLength);
                if (t.Length < 36)
                {
                    throw Invalid("the transformed glyf header is truncated");
                }

                int numGlyphs = BinaryPrimitives.ReadUInt16BigEndian(t.Slice(4));
                int indexFormatDeclared = BinaryPrimitives.ReadUInt16BigEndian(t.Slice(6));
                if (entries[l].OrigLength != (uint)((numGlyphs + 1) * (indexFormatDeclared == 0 ? 2 : 4)))
                {
                    throw Invalid("the loca entry's original length does not match the glyf table's glyph count and index format");
                }

                (byte[] glyf, byte[] loca, short[] mins, int indexFormat) = ReconstructGlyf(t);
                data[i] = glyf;
                data[l] = loca;
                locaOf[i] = l;
                xMinsByGlyf[i] = mins;
                locaFormatByGlyf[i] = indexFormat;
            }
            else if (e.Tag == "loca")
            {
                // Filled in with its glyf; checked below.
            }
            else if (e.Tag != "hmtx")
            {
                throw Invalid("unknown transform for table " + e.Tag);
            }
        }

        // Per font: the transformed hmtx needs that font's hhea, maxp and glyf; head gets the loca format used.
        foreach ((uint _, int[] idx) in fonts)
        {
            int glyf = Find(entries, idx, "glyf");
            int loca = Find(entries, idx, "loca");
            int head = Find(entries, idx, "head");
            if ((glyf >= 0) != (loca >= 0) || (glyf >= 0 && entries[glyf].Transformed != entries[loca].Transformed) ||
                (glyf >= 0 && entries[glyf].Transformed && (!locaOf.TryGetValue(glyf, out int pairedLoca) || pairedLoca != loca)))
            {
                throw Invalid("a font's glyf and loca tables are not a pair");
            }

            if (glyf >= 0 && entries[glyf].Transformed && head >= 0 && data[head]!.Length >= 54)
            {
                BinaryPrimitives.WriteInt16BigEndian(data[head].AsSpan(50), (short)locaFormatByGlyf[glyf]);
            }

            int hmtx = Find(entries, idx, "hmtx");
            if (hmtx >= 0 && entries[hmtx].Transformed && data[hmtx] is null)
            {
                int hhea = Find(entries, idx, "hhea");
                int maxp = Find(entries, idx, "maxp");
                if (entries[hmtx].Transform != 1 || glyf < 0 || xMinsByGlyf[glyf] is null || hhea < 0 || maxp < 0)
                {
                    throw Invalid("unsupported hmtx transform");
                }

                data[hmtx] = ReconstructHmtx(stream.AsSpan((int)entries[hmtx].StreamOffset, (int)entries[hmtx].TransformLength), data[hhea]!, data[maxp]!, xMinsByGlyf[glyf]!);
            }
        }

        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].Tag == "loca" && entries[i].Transformed && data[i] is null)
            {
                throw Invalid("loca is transformed but no transformed glyf goes with it");
            }
        }

        TableData[] tables = new TableData[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            tables[i] = new TableData(TagValue(entries[i].Tag), data[i] ?? Array.Empty<byte>());
        }

        if (flavor != 0x74746366u)
        {
            return SfntWriter.WriteFont(flavor, tables, recomputeChecksums: true);
        }

        return SfntWriter.WriteCollection(tables, fonts);
    }

    private static int Find(List<Entry> entries, int[] idx, string tag)
    {
        foreach (int i in idx)
        {
            if (entries[i].Tag == tag)
            {
                return i;
            }
        }

        return -1;
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
    private static byte[] ReconstructHmtx(ReadOnlySpan<byte> t, byte[] hhea, byte[] maxp, short[] xMins)
    {
        if (hhea.Length < 36 || maxp.Length < 6)
        {
            throw Invalid("a transformed hmtx needs the font's hhea and maxp");
        }

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
    private static OpenTypeFormatException Invalid(string what) => new OpenTypeFormatException("Invalid WOFF2 data: " + what + ".");

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
