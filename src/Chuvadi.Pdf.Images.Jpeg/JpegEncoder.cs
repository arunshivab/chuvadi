// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ITU-T T.81 (JPEG) — Information Technology: Digital compression and
//        coding of continuous-tone still images. Baseline DCT.
//        JFIF 1.02.
// PHASE: Phase 2.1 — JPEG encoder

using System;
using System.IO;

namespace Chuvadi.Pdf.Images.Jpeg;

/// <summary>
/// Pure-C# baseline DCT JPEG encoder. Produces JFIF-compliant JPEG byte
/// streams from raw RGB pixel data.
/// </summary>
/// <remarks>
/// <para>
/// Implements ITU-T T.81 baseline sequential DCT-based coding with Huffman
/// entropy coding. Uses the IJG standard quantization tables scaled by a
/// quality factor (1-100) and the standard Huffman tables.
/// </para>
/// <para>
/// Supports 24-bit RGB and 8-bit grayscale input. The output is a complete
/// JFIF container (SOI ... EOI) suitable for direct embedding as a
/// <c>data:image/jpeg;base64,...</c> URL or storage as a <c>.jpg</c> file.
/// </para>
/// </remarks>
public static class JpegEncoder
{
    /// <summary>Encodes RGB pixel data to JPEG bytes.</summary>
    /// <param name="rgb">Interleaved 24-bit RGB pixel data.</param>
    /// <param name="width">Pixel width.</param>
    /// <param name="height">Pixel height.</param>
    /// <param name="quality">Quality factor 1-100 (default 85).</param>
    public static byte[] EncodeRgb(byte[] rgb, int width, int height, int quality = 85)
    {
        ArgumentNullException.ThrowIfNull(rgb);
        if (width <= 0 || height <= 0) { throw new ArgumentOutOfRangeException(nameof(width)); }
        if (rgb.Length < width * height * 3) { throw new ArgumentException("RGB buffer too small.", nameof(rgb)); }
        quality = Math.Clamp(quality, 1, 100);

        Encoder enc = new(width, height, quality, isGrayscale: false);
        return enc.Encode(rgb);
    }

    /// <summary>Encodes grayscale pixel data to JPEG bytes.</summary>
    public static byte[] EncodeGrayscale(byte[] gray, int width, int height, int quality = 85)
    {
        ArgumentNullException.ThrowIfNull(gray);
        if (width <= 0 || height <= 0) { throw new ArgumentOutOfRangeException(nameof(width)); }
        if (gray.Length < width * height) { throw new ArgumentException("Gray buffer too small.", nameof(gray)); }
        quality = Math.Clamp(quality, 1, 100);

        Encoder enc = new(width, height, quality, isGrayscale: true);
        return enc.Encode(gray);
    }

    // ── Implementation ───────────────────────────────────────────────────

    private sealed class Encoder
    {
        private readonly int _width;
        private readonly int _height;
        private readonly bool _grayscale;
        private readonly int[] _quantLuma;
        private readonly int[] _quantChroma;
        private readonly MemoryStream _output;
        private int _bitBuffer;
        private int _bitCount;

        internal Encoder(int width, int height, int quality, bool isGrayscale)
        {
            _width = width;
            _height = height;
            _grayscale = isGrayscale;
            _quantLuma = ScaleQuantTable(StdQuantLuma, quality);
            _quantChroma = ScaleQuantTable(StdQuantChroma, quality);
            _output = new MemoryStream();
        }

        internal byte[] Encode(byte[] pixels)
        {
            WriteHeaders();
            WriteScanData(pixels);
            WriteMarker(0xFFD9);  // EOI
            return _output.ToArray();
        }

        private void WriteHeaders()
        {
            WriteMarker(0xFFD8);                                    // SOI
            WriteJfifApp0();
            WriteQuantTable(0, _quantLuma);
            if (!_grayscale) { WriteQuantTable(1, _quantChroma); }
            WriteStartOfFrame();
            WriteHuffmanTable(0, 0, BitsDcLuma, ValDcLuma);         // DC luma
            WriteHuffmanTable(1, 0, BitsAcLuma, ValAcLuma);         // AC luma
            if (!_grayscale)
            {
                WriteHuffmanTable(0, 1, BitsDcChroma, ValDcChroma); // DC chroma
                WriteHuffmanTable(1, 1, BitsAcChroma, ValAcChroma); // AC chroma
            }
            WriteStartOfScan();
        }

        private void WriteJfifApp0()
        {
            WriteMarker(0xFFE0);
            byte[] app0 =
            {
                0x00, 0x10,        // length
                0x4A, 0x46, 0x49, 0x46, 0x00,  // "JFIF\0"
                0x01, 0x02,        // version 1.02
                0x00,              // units (none)
                0x00, 0x01,        // x density
                0x00, 0x01,        // y density
                0x00, 0x00,        // no thumbnail
            };
            _output.Write(app0, 0, app0.Length);
        }

        private void WriteQuantTable(int id, int[] table)
        {
            WriteMarker(0xFFDB);
            _output.WriteByte(0);   // length high
            _output.WriteByte(0x43); // length low (67)
            _output.WriteByte((byte)id);
            for (int i = 0; i < 64; i++)
            {
                _output.WriteByte((byte)table[ZigZag[i]]);
            }
        }

        private void WriteStartOfFrame()
        {
            WriteMarker(0xFFC0);  // SOF0 baseline DCT
            int components = _grayscale ? 1 : 3;
            int length = 8 + 3 * components;
            _output.WriteByte((byte)(length >> 8));
            _output.WriteByte((byte)length);
            _output.WriteByte(8);  // precision
            _output.WriteByte((byte)(_height >> 8));
            _output.WriteByte((byte)_height);
            _output.WriteByte((byte)(_width >> 8));
            _output.WriteByte((byte)_width);
            _output.WriteByte((byte)components);
            // Y: id=1, h/v sampling 1/1, qt 0
            _output.WriteByte(1); _output.WriteByte(0x11); _output.WriteByte(0);
            if (!_grayscale)
            {
                _output.WriteByte(2); _output.WriteByte(0x11); _output.WriteByte(1);  // Cb
                _output.WriteByte(3); _output.WriteByte(0x11); _output.WriteByte(1);  // Cr
            }
        }

        private void WriteHuffmanTable(int classId, int destId, byte[] bits, byte[] values)
        {
            WriteMarker(0xFFC4);
            int length = 2 + 1 + 16 + values.Length;
            _output.WriteByte((byte)(length >> 8));
            _output.WriteByte((byte)length);
            _output.WriteByte((byte)((classId << 4) | destId));
            _output.Write(bits, 0, 16);
            _output.Write(values, 0, values.Length);
        }

        private void WriteStartOfScan()
        {
            WriteMarker(0xFFDA);
            int components = _grayscale ? 1 : 3;
            int length = 6 + 2 * components;
            _output.WriteByte((byte)(length >> 8));
            _output.WriteByte((byte)length);
            _output.WriteByte((byte)components);
            _output.WriteByte(1); _output.WriteByte(0x00);    // Y: DC0 AC0
            if (!_grayscale)
            {
                _output.WriteByte(2); _output.WriteByte(0x11); // Cb: DC1 AC1
                _output.WriteByte(3); _output.WriteByte(0x11); // Cr: DC1 AC1
            }
            _output.WriteByte(0);     // Ss
            _output.WriteByte(0x3F);  // Se
            _output.WriteByte(0);     // Ah/Al
        }

        private void WriteScanData(byte[] pixels)
        {
            int[] dcPrev = { 0, 0, 0 };
            double[] yBlock = new double[64];
            double[] cbBlock = new double[64];
            double[] crBlock = new double[64];
            int[] yq = new int[64];
            int[] cbq = new int[64];
            int[] crq = new int[64];

            int blocksX = (_width + 7) / 8;
            int blocksY = (_height + 7) / 8;
            for (int by = 0; by < blocksY; by++)
            {
                for (int bx = 0; bx < blocksX; bx++)
                {
                    if (_grayscale)
                    {
                        LoadGrayBlock(pixels, bx, by, yBlock);
                        Dct(yBlock);
                        Quantize(yBlock, _quantLuma, yq);
                        EncodeBlock(yq, ref dcPrev[0], CodesDcLuma, CodesAcLuma);
                    }
                    else
                    {
                        LoadRgbBlock(pixels, bx, by, yBlock, cbBlock, crBlock);
                        Dct(yBlock); Dct(cbBlock); Dct(crBlock);
                        Quantize(yBlock, _quantLuma, yq);
                        Quantize(cbBlock, _quantChroma, cbq);
                        Quantize(crBlock, _quantChroma, crq);
                        EncodeBlock(yq, ref dcPrev[0], CodesDcLuma, CodesAcLuma);
                        EncodeBlock(cbq, ref dcPrev[1], CodesDcChroma, CodesAcChroma);
                        EncodeBlock(crq, ref dcPrev[2], CodesDcChroma, CodesAcChroma);
                    }
                }
            }
            FlushBits();
        }

        private void LoadGrayBlock(byte[] pixels, int bx, int by, double[] block)
        {
            for (int j = 0; j < 8; j++)
            {
                int sy = Math.Min(by * 8 + j, _height - 1);
                for (int i = 0; i < 8; i++)
                {
                    int sx = Math.Min(bx * 8 + i, _width - 1);
                    block[j * 8 + i] = pixels[sy * _width + sx] - 128.0;
                }
            }
        }

        private void LoadRgbBlock(byte[] pixels, int bx, int by,
            double[] yBlock, double[] cbBlock, double[] crBlock)
        {
            for (int j = 0; j < 8; j++)
            {
                int sy = Math.Min(by * 8 + j, _height - 1);
                for (int i = 0; i < 8; i++)
                {
                    int sx = Math.Min(bx * 8 + i, _width - 1);
                    int pi = (sy * _width + sx) * 3;
                    byte r = pixels[pi];
                    byte g = pixels[pi + 1];
                    byte b = pixels[pi + 2];
                    // ITU-R BT.601 RGB → YCbCr
                    double y  =  0.299    * r + 0.587    * g + 0.114    * b - 128.0;
                    double cb = -0.168736 * r - 0.331264 * g + 0.5      * b;
                    double cr =  0.5      * r - 0.418688 * g - 0.081312 * b;
                    int k = j * 8 + i;
                    yBlock[k] = y;
                    cbBlock[k] = cb;
                    crBlock[k] = cr;
                }
            }
        }

        // 2D DCT-II via Loeffler-style 8-point DCT
        private static void Dct(double[] block)
        {
            double[] tmp = new double[64];
            for (int row = 0; row < 8; row++)
            {
                Dct1D(block, row * 8, 1, tmp, row * 8, 1);
            }
            for (int col = 0; col < 8; col++)
            {
                Dct1D(tmp, col, 8, block, col, 8);
            }
        }

        private static void Dct1D(double[] src, int srcOff, int srcStride, double[] dst, int dstOff, int dstStride)
        {
            // Straight 8-point DCT-II via the matrix; not the fastest but compact and clear.
            for (int k = 0; k < 8; k++)
            {
                double sum = 0;
                for (int n = 0; n < 8; n++)
                {
                    sum += src[srcOff + n * srcStride] * DctTable[k, n];
                }
                double scale = k == 0 ? 1.0 / Math.Sqrt(8) : Math.Sqrt(2.0 / 8.0);
                dst[dstOff + k * dstStride] = sum * scale;
            }
        }

        private static readonly double[,] DctTable = BuildDctTable();
        private static double[,] BuildDctTable()
        {
            double[,] t = new double[8, 8];
            for (int k = 0; k < 8; k++)
            {
                for (int n = 0; n < 8; n++)
                {
                    t[k, n] = Math.Cos((2 * n + 1) * k * Math.PI / 16);
                }
            }
            return t;
        }

        private static void Quantize(double[] block, int[] table, int[] result)
        {
            for (int i = 0; i < 64; i++)
            {
                result[ZigZag[i]] = (int)Math.Round(block[i] / table[i]);
            }
            // Apply zigzag in place: copy to local, write back in zigzag order
            // (above ZigZag inversion already puts coefficient i in zigzag position)
        }

        private void EncodeBlock(int[] block, ref int dcPrev,
            (int Code, int Length)[] dcCodes, (int Code, int Length)[] acCodes)
        {
            // DC coefficient
            int dc = block[0];
            int diff = dc - dcPrev;
            dcPrev = dc;
            int dcSize = BitSize(diff);
            WriteBits(dcCodes[dcSize]);
            if (dcSize > 0) { WriteSignedBits(diff, dcSize); }

            // AC coefficients
            int zeroRun = 0;
            for (int i = 1; i < 64; i++)
            {
                int ac = block[i];
                if (ac == 0)
                {
                    zeroRun++;
                }
                else
                {
                    while (zeroRun >= 16)
                    {
                        WriteBits(acCodes[0xF0]);
                        zeroRun -= 16;
                    }
                    int acSize = BitSize(ac);
                    int rs = (zeroRun << 4) | acSize;
                    WriteBits(acCodes[rs]);
                    WriteSignedBits(ac, acSize);
                    zeroRun = 0;
                }
            }
            if (zeroRun > 0) { WriteBits(acCodes[0]); }   // EOB
        }

        private static int BitSize(int v)
        {
            int abs = Math.Abs(v);
            int size = 0;
            while (abs != 0) { abs >>= 1; size++; }
            return size;
        }

        private void WriteBits((int Code, int Length) c) => WriteBits(c.Code, c.Length);

        private void WriteBits(int code, int length)
        {
            _bitBuffer |= code << (24 - _bitCount - length);
            _bitCount += length;
            while (_bitCount >= 8)
            {
                byte b = (byte)((_bitBuffer >> 16) & 0xFF);
                _output.WriteByte(b);
                if (b == 0xFF) { _output.WriteByte(0); }   // byte-stuffing
                _bitBuffer <<= 8;
                _bitCount -= 8;
            }
        }

        private void WriteSignedBits(int v, int length)
        {
            int code = v < 0 ? v - 1 + (1 << length) : v;
            int mask = (1 << length) - 1;
            WriteBits(code & mask, length);
        }

        private void FlushBits()
        {
            if (_bitCount > 0)
            {
                _bitBuffer |= 0xFF << (24 - _bitCount - 8);   // pad with 1s
                while (_bitCount > 0)
                {
                    byte b = (byte)((_bitBuffer >> 16) & 0xFF);
                    _output.WriteByte(b);
                    if (b == 0xFF) { _output.WriteByte(0); }
                    _bitBuffer <<= 8;
                    _bitCount -= 8;
                }
            }
        }

        private void WriteMarker(int marker)
        {
            _output.WriteByte((byte)(marker >> 8));
            _output.WriteByte((byte)marker);
        }
    }

    private static int[] ScaleQuantTable(int[] baseTable, int quality)
    {
        int scale = quality < 50 ? 5000 / quality : 200 - quality * 2;
        int[] result = new int[64];
        for (int i = 0; i < 64; i++)
        {
            int v = (baseTable[i] * scale + 50) / 100;
            result[i] = Math.Clamp(v, 1, 255);
        }
        return result;
    }

    // ── Standard tables ──────────────────────────────────────────────────

    private static readonly int[] StdQuantLuma =
    {
        16, 11, 10, 16, 24, 40, 51, 61,
        12, 12, 14, 19, 26, 58, 60, 55,
        14, 13, 16, 24, 40, 57, 69, 56,
        14, 17, 22, 29, 51, 87, 80, 62,
        18, 22, 37, 56, 68, 109, 103, 77,
        24, 35, 55, 64, 81, 104, 113, 92,
        49, 64, 78, 87, 103, 121, 120, 101,
        72, 92, 95, 98, 112, 100, 103, 99,
    };

    private static readonly int[] StdQuantChroma =
    {
        17, 18, 24, 47, 99, 99, 99, 99,
        18, 21, 26, 66, 99, 99, 99, 99,
        24, 26, 56, 99, 99, 99, 99, 99,
        47, 66, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99,
    };

    private static readonly int[] ZigZag =
    {
        0,  1,  8,  16, 9,  2,  3,  10,
        17, 24, 32, 25, 18, 11, 4,  5,
        12, 19, 26, 33, 40, 48, 41, 34,
        27, 20, 13, 6,  7,  14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36,
        29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46,
        53, 60, 61, 54, 47, 55, 62, 63,
    };

    private static readonly byte[] BitsDcLuma = { 0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0 };
    private static readonly byte[] ValDcLuma = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
    private static readonly byte[] BitsDcChroma = { 0, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0 };
    private static readonly byte[] ValDcChroma = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };

    private static readonly byte[] BitsAcLuma = { 0, 2, 1, 3, 3, 2, 4, 3, 5, 5, 4, 4, 0, 0, 1, 0x7D };
    private static readonly byte[] ValAcLuma =
    {
        0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12, 0x21, 0x31, 0x41, 0x06, 0x13, 0x51, 0x61, 0x07,
        0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xA1, 0x08, 0x23, 0x42, 0xB1, 0xC1, 0x15, 0x52, 0xD1, 0xF0,
        0x24, 0x33, 0x62, 0x72, 0x82, 0x09, 0x0A, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x25, 0x26, 0x27, 0x28,
        0x29, 0x2A, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49,
        0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5A, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
        0x6A, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89,
        0x8A, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9A, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7,
        0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3, 0xC4, 0xC5,
        0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA, 0xE1, 0xE2,
        0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9, 0xEA, 0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8,
        0xF9, 0xFA,
    };

    private static readonly byte[] BitsAcChroma = { 0, 2, 1, 2, 4, 4, 3, 4, 7, 5, 4, 4, 0, 1, 2, 0x77 };
    private static readonly byte[] ValAcChroma =
    {
        0x00, 0x01, 0x02, 0x03, 0x11, 0x04, 0x05, 0x21, 0x31, 0x06, 0x12, 0x41, 0x51, 0x07, 0x61, 0x71,
        0x13, 0x22, 0x32, 0x81, 0x08, 0x14, 0x42, 0x91, 0xA1, 0xB1, 0xC1, 0x09, 0x23, 0x33, 0x52, 0xF0,
        0x15, 0x62, 0x72, 0xD1, 0x0A, 0x16, 0x24, 0x34, 0xE1, 0x25, 0xF1, 0x17, 0x18, 0x19, 0x1A, 0x26,
        0x27, 0x28, 0x29, 0x2A, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48,
        0x49, 0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5A, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68,
        0x69, 0x6A, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87,
        0x88, 0x89, 0x8A, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9A, 0xA2, 0xA3, 0xA4, 0xA5,
        0xA6, 0xA7, 0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3,
        0xC4, 0xC5, 0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA,
        0xE2, 0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9, 0xEA, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8,
        0xF9, 0xFA,
    };

    private static readonly (int Code, int Length)[] CodesDcLuma = BuildCodeTable(BitsDcLuma, ValDcLuma, 16);
    private static readonly (int Code, int Length)[] CodesDcChroma = BuildCodeTable(BitsDcChroma, ValDcChroma, 16);
    private static readonly (int Code, int Length)[] CodesAcLuma = BuildCodeTable(BitsAcLuma, ValAcLuma, 256);
    private static readonly (int Code, int Length)[] CodesAcChroma = BuildCodeTable(BitsAcChroma, ValAcChroma, 256);

    private static (int Code, int Length)[] BuildCodeTable(byte[] bits, byte[] values, int size)
    {
        (int Code, int Length)[] result = new (int, int)[size];
        int code = 0;
        int idx = 0;
        for (int bitLen = 1; bitLen <= 16; bitLen++)
        {
            int count = bits[bitLen - 1];
            for (int i = 0; i < count; i++)
            {
                if (idx < values.Length)
                {
                    result[values[idx]] = (code, bitLen);
                    idx++;
                }
                code++;
            }
            code <<= 1;
        }
        return result;
    }
}
