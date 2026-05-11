// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  TIFF 6.0 specification (Aldus / Adobe, June 1992)
// PHASE: Phase 1.1.9 — Chuvadi.Pdf.Images TIFF support
//
// Baseline TIFF 6.0 reader. Supports:
//   - Both byte orders (II little-endian, MM big-endian)
//   - 1, 4, 8, 16 bits per channel
//   - Grayscale (PhotometricInterpretation 0 or 1), RGB (2)
//   - Compression: none (1), PackBits (32773), LZW (5)
//   - Multi-page (multiple IFDs)
//
// NOT supported in 1.0:
//   - CCITT G3/G4 (PhotometricInterpretation 0 with Compression 3/4) — medical
//     monochrome TIFFs need this; see BACKLOG.
//   - YCbCr, CIELab, palette, separated (CMYK split into planes)
//   - Tiled images (uses strips only)
//   - JPEG-in-TIFF

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Images;

/// <summary>
/// Decodes TIFF images per TIFF 6.0 baseline.
/// </summary>
/// <remarks>
/// Supports grayscale and RGB TIFFs at 1/4/8/16 bits per channel, with uncompressed,
/// PackBits, or LZW compression. Multi-page TIFFs return a list of frames in
/// document order.
/// </remarks>
public static class TiffDecoder
{
    /// <summary>Magic numbers for the two TIFF byte orders.</summary>
    private const ushort LittleEndianMarker = 0x4949; // "II"
    private const ushort BigEndianMarker    = 0x4D4D; // "MM"
    private const ushort TiffMagic          = 42;

    /// <summary>Decodes all pages from a TIFF byte stream.</summary>
    /// <param name="data">The TIFF file bytes.</param>
    /// <returns>One <see cref="ImageFrame"/> per page in document order.</returns>
    public static List<ImageFrame> Decode(byte[] data)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        if (data.Length < 8)
        {
            throw new TiffException("TIFF file too short (header is 8 bytes).");
        }

        bool littleEndian;
        ushort byteOrder = (ushort)((data[0] << 8) | data[1]);

        if (byteOrder == LittleEndianMarker)
        {
            littleEndian = true;
        }
        else if (byteOrder == BigEndianMarker)
        {
            littleEndian = false;
        }
        else
        {
            throw new TiffException("Not a TIFF file: bad byte-order marker.");
        }

        TiffReader r = new TiffReader(data, littleEndian);
        ushort magic = r.ReadU16(2);

        if (magic != TiffMagic)
        {
            throw new TiffException($"Not a TIFF file: bad magic {magic}, expected 42.");
        }

        uint firstIfdOffset = r.ReadU32(4);
        List<ImageFrame> frames = new List<ImageFrame>();
        uint ifdOffset = firstIfdOffset;

        while (ifdOffset != 0)
        {
            if (ifdOffset + 2 > data.Length)
            {
                throw new TiffException("Truncated TIFF: IFD offset past end of file.");
            }

            ushort entryCount = r.ReadU16((int)ifdOffset);
            Dictionary<ushort, IfdEntry> entries = new Dictionary<ushort, IfdEntry>();

            for (int i = 0; i < entryCount; i++)
            {
                int entryPos = (int)ifdOffset + 2 + (i * 12);
                IfdEntry entry = ParseEntry(r, entryPos);
                entries[entry.Tag] = entry;
            }

            frames.Add(DecodeFrame(r, entries));

            int nextOffsetPos = (int)ifdOffset + 2 + (entryCount * 12);
            ifdOffset = r.ReadU32(nextOffsetPos);
        }

        return frames;
    }

    // ── IFD parsing ───────────────────────────────────────────────────────

    private static IfdEntry ParseEntry(TiffReader r, int pos)
    {
        ushort tag = r.ReadU16(pos);
        ushort type = r.ReadU16(pos + 2);
        uint count = r.ReadU32(pos + 4);
        uint valueOrOffset = r.ReadU32(pos + 8);

        // The value-or-offset field lives at pos + 8. For inline values, that
        // is where the data actually sits.
        return new IfdEntry(tag, type, count, valueOrOffset, pos + 8);
    }

    private static uint GetUInt(TiffReader r, IfdEntry entry, int idx = 0)
    {
        // For short and long types when count is small the value is inline.
        int size = TypeSize(entry.Type);
        int totalBytes = (int)entry.Count * size;

        if (totalBytes <= 4)
        {
            // Inline value. Encoded in valueOrOffset field directly per byte order.
            // We re-extract from the raw bytes at the entry value position.
            int valuePos = entry.ValuePos;
            return ReadValue(r, entry.Type, valuePos + (idx * size));
        }
        else
        {
            return ReadValue(r, entry.Type, (int)entry.ValueOrOffset + (idx * size));
        }
    }

    private static uint ReadValue(TiffReader r, ushort type, int pos)
    {
        return type switch
        {
            1 or 2 or 7 => r.ReadU8(pos),    // BYTE / ASCII / UNDEFINED
            3           => r.ReadU16(pos),   // SHORT
            4           => r.ReadU32(pos),   // LONG
            _           => throw new TiffException($"Cannot read tag value of TIFF type {type} as uint."),
        };
    }

    private static int TypeSize(ushort type)
    {
        return type switch
        {
            1 or 2 or 7 => 1,    // BYTE, ASCII, UNDEFINED
            3           => 2,    // SHORT
            4 or 9 or 11 => 4,   // LONG, SLONG, FLOAT
            5 or 10 or 12 => 8,  // RATIONAL, SRATIONAL, DOUBLE
            _           => 1,
        };
    }

    // ── Frame decoding ────────────────────────────────────────────────────

    private static ImageFrame DecodeFrame(TiffReader r, Dictionary<ushort, IfdEntry> entries)
    {
        int width  = (int)GetTagOrDefault(r, entries, 256, 0);  // ImageWidth
        int height = (int)GetTagOrDefault(r, entries, 257, 0);  // ImageLength

        if (width <= 0 || height <= 0)
        {
            throw new TiffException($"Bad image dimensions: {width}×{height}.");
        }

        int bitsPerSample = (int)GetTagOrDefault(r, entries, 258, 1);
        int compression = (int)GetTagOrDefault(r, entries, 259, 1);
        int photometric = (int)GetTagOrDefault(r, entries, 262, 0);
        int samplesPerPixel = (int)GetTagOrDefault(r, entries, 277, 1);
        int rowsPerStrip = (int)GetTagOrDefault(r, entries, 278, (uint)height);

        // BitsPerSample tag has SamplesPerPixel entries. We assume all equal.
        if (entries.TryGetValue(258, out IfdEntry bpsEntry) && bpsEntry.Count > 1)
        {
            bitsPerSample = (int)GetUInt(r, bpsEntry, 0);
        }

        IfdEntry stripOffsets = entries[273];   // StripOffsets — required
        IfdEntry stripBytes   = entries[279];   // StripByteCounts — required

        int stripsPerImage = (height + rowsPerStrip - 1) / rowsPerStrip;
        byte[] uncompressed = new byte[CalcRowBytes(width, samplesPerPixel, bitsPerSample) * height];
        int destOffset = 0;

        for (int s = 0; s < stripsPerImage; s++)
        {
            int offset = (int)GetUInt(r, stripOffsets, s);
            int length = (int)GetUInt(r, stripBytes, s);

            byte[] stripBytesArr = new byte[length];
            Array.Copy(r.Data, offset, stripBytesArr, 0, length);

            byte[] decoded = compression switch
            {
                1     => stripBytesArr,
                5     => LzwDecompress(stripBytesArr),
                32773 => PackBitsDecompress(stripBytesArr),
                _     => throw new TiffException($"Unsupported compression: {compression}."),
            };

            Array.Copy(decoded, 0, uncompressed, destOffset, Math.Min(decoded.Length, uncompressed.Length - destOffset));
            destOffset += decoded.Length;
        }

        return BuildFrame(uncompressed, width, height, bitsPerSample, samplesPerPixel, photometric);
    }

    private static uint GetTagOrDefault(
        TiffReader r, Dictionary<ushort, IfdEntry> entries, ushort tag, uint defaultValue)
    {
        if (entries.TryGetValue(tag, out IfdEntry entry))
        {
            return GetUInt(r, entry, 0);
        }

        return defaultValue;
    }

    private static int CalcRowBytes(int width, int samplesPerPixel, int bitsPerSample)
    {
        int bits = width * samplesPerPixel * bitsPerSample;
        return (bits + 7) / 8;
    }

    private static ImageFrame BuildFrame(
        byte[] raw, int width, int height, int bps, int spp, int photometric)
    {
        PixelBuffer buf = new PixelBuffer(width, height);
        int rowBytes = CalcRowBytes(width, spp, bps);

        for (int y = 0; y < height; y++)
        {
            int rowStart = y * rowBytes;

            for (int x = 0; x < width; x++)
            {
                byte rr, gg, bb;

                if (spp == 1 && (photometric == 0 || photometric == 1))
                {
                    // Grayscale
                    int sample = ReadSample(raw, rowStart, x, bps);
                    int max = (1 << bps) - 1;
                    int normalised = max <= 0 ? 0 : (sample * 255) / max;

                    if (photometric == 0)
                    {
                        // WhiteIsZero: invert
                        normalised = 255 - normalised;
                    }

                    rr = gg = bb = (byte)normalised;
                }
                else if (spp >= 3 && photometric == 2)
                {
                    // RGB
                    int r0 = ReadSample(raw, rowStart, x * spp,     bps);
                    int g0 = ReadSample(raw, rowStart, x * spp + 1, bps);
                    int b0 = ReadSample(raw, rowStart, x * spp + 2, bps);
                    int max = (1 << bps) - 1;
                    rr = (byte)((r0 * 255) / max);
                    gg = (byte)((g0 * 255) / max);
                    bb = (byte)((b0 * 255) / max);
                }
                else
                {
                    throw new TiffException(
                        $"Unsupported photometric/spp combination: photo={photometric}, spp={spp}.");
                }

                buf.SetPixelBgra(x, y, bb, gg, rr, 255);
            }
        }

        ImageColorFormat fmt = (spp == 1) ? ImageColorFormat.Gray8 : ImageColorFormat.Rgb24;
        return new ImageFrame(buf, fmt);
    }

    private static int ReadSample(byte[] row, int rowStart, int sampleIndex, int bps)
    {
        if (bps == 8)
        {
            return row[rowStart + sampleIndex];
        }
        else if (bps == 16)
        {
            int b0 = row[rowStart + sampleIndex * 2];
            int b1 = row[rowStart + sampleIndex * 2 + 1];
            return (b0 << 8) | b1;
        }
        else if (bps == 1)
        {
            int byteIdx = sampleIndex >> 3;
            int bitIdx = 7 - (sampleIndex & 7);
            return (row[rowStart + byteIdx] >> bitIdx) & 1;
        }
        else if (bps == 4)
        {
            int byteIdx = sampleIndex >> 1;
            bool hi = (sampleIndex & 1) == 0;
            byte v = row[rowStart + byteIdx];
            return hi ? (v >> 4) & 0xF : v & 0xF;
        }
        else
        {
            throw new TiffException($"Unsupported bits per sample: {bps}.");
        }
    }

    // ── Decompressors ─────────────────────────────────────────────────────

    private static byte[] PackBitsDecompress(byte[] input)
    {
        // TIFF 6.0 §9 — Apple PackBits run-length encoding.
        List<byte> output = new List<byte>(input.Length * 2);
        int i = 0;

        while (i < input.Length)
        {
            sbyte n = (sbyte)input[i++];

            if (n >= 0)
            {
                // Copy next n+1 bytes literally.
                int count = n + 1;
                for (int k = 0; k < count && i < input.Length; k++)
                {
                    output.Add(input[i++]);
                }
            }
            else if (n != -128)
            {
                // Repeat next byte (-n + 1) times.
                int count = -n + 1;
                if (i >= input.Length)
                {
                    break;
                }
                byte rep = input[i++];
                for (int k = 0; k < count; k++)
                {
                    output.Add(rep);
                }
            }
            // n == -128 is a no-op
        }

        return output.ToArray();
    }

    private static byte[] LzwDecompress(byte[] input)
    {
        // TIFF 6.0 §13 LZW — Note: TIFF LZW uses early-change (codeBits increments
        // one code earlier than the GIF variant) and big-endian bit packing.
        const int ClearCode = 256;
        const int EndCode   = 257;
        const int MinBits   = 9;
        const int MaxBits   = 12;

        List<byte> output = new List<byte>(input.Length * 4);
        List<byte[]> table = new List<byte[]>(4096);

        for (int i = 0; i < 256; i++)
        {
            table.Add(new byte[] { (byte)i });
        }

        table.Add(Array.Empty<byte>()); // 256: clear
        table.Add(Array.Empty<byte>()); // 257: end

        int codeBits = MinBits;
        int bitPos = 0;
        int prevCode = -1;

        while (true)
        {
            int code = ReadBigEndianBits(input, ref bitPos, codeBits);

            if (code < 0 || code == EndCode)
            {
                break;
            }

            if (code == ClearCode)
            {
                table.RemoveRange(258, table.Count - 258);
                codeBits = MinBits;
                prevCode = -1;
                continue;
            }

            byte[] entry;

            if (code < table.Count)
            {
                entry = table[code];
            }
            else if (code == table.Count && prevCode >= 0)
            {
                byte[] prev = table[prevCode];
                entry = new byte[prev.Length + 1];
                Array.Copy(prev, entry, prev.Length);
                entry[prev.Length] = prev[0];
            }
            else
            {
                throw new TiffException($"Invalid LZW code {code} (table size {table.Count}).");
            }

            output.AddRange(entry);

            if (prevCode >= 0)
            {
                byte[] prev = table[prevCode];
                byte[] newEntry = new byte[prev.Length + 1];
                Array.Copy(prev, newEntry, prev.Length);
                newEntry[prev.Length] = entry[0];
                table.Add(newEntry);

                // TIFF early-change: code-width grows ONE code earlier than GIF.
                if (table.Count == ((1 << codeBits) - 1) && codeBits < MaxBits)
                {
                    codeBits++;
                }
            }

            prevCode = code;
        }

        return output.ToArray();
    }

    private static int ReadBigEndianBits(byte[] input, ref int bitPos, int count)
    {
        int byteIdx = bitPos >> 3;

        if (byteIdx >= input.Length)
        {
            return -1;
        }

        int result = 0;
        int remaining = count;

        while (remaining > 0)
        {
            byteIdx = bitPos >> 3;
            if (byteIdx >= input.Length)
            {
                return -1;
            }

            int bitInByte = 7 - (bitPos & 7);
            int bitsAvail = bitInByte + 1;
            int take = Math.Min(bitsAvail, remaining);
            int shift = bitsAvail - take;
            int mask = (1 << take) - 1;
            int bits = (input[byteIdx] >> shift) & mask;

            result = (result << take) | bits;
            bitPos += take;
            remaining -= take;
        }

        return result;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private readonly struct IfdEntry
    {
        public IfdEntry(ushort tag, ushort type, uint count, uint valueOrOffset, int valuePos)
        {
            Tag = tag;
            Type = type;
            Count = count;
            ValueOrOffset = valueOrOffset;
            // ValuePos is the byte offset of the entry's value-or-offset field. For inline
            // values (total bytes <= 4) the data lives there directly; for larger values
            // ValueOrOffset is itself a file offset, and ValuePos is unused.
            ValuePos = valuePos;
        }

        public ushort Tag { get; }
        public ushort Type { get; }
        public uint Count { get; }
        public uint ValueOrOffset { get; }
        public int ValuePos { get; }
    }

    private sealed class TiffReader
    {
        public TiffReader(byte[] data, bool littleEndian)
        {
            Data = data;
            LittleEndian = littleEndian;
        }

        public byte[] Data { get; }
        public bool LittleEndian { get; }

        public byte ReadU8(int pos) => Data[pos];

        public ushort ReadU16(int pos)
        {
            return LittleEndian
                ? (ushort)(Data[pos] | (Data[pos + 1] << 8))
                : (ushort)((Data[pos] << 8) | Data[pos + 1]);
        }

        public uint ReadU32(int pos)
        {
            return LittleEndian
                ? (uint)(Data[pos] | (Data[pos + 1] << 8) | (Data[pos + 2] << 16) | (Data[pos + 3] << 24))
                : (uint)((Data[pos] << 24) | (Data[pos + 1] << 16) | (Data[pos + 2] << 8) | Data[pos + 3]);
        }
    }
}
