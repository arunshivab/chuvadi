// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 10918-1:1994 — JPEG (Baseline Sequential DCT, SOF0)
//        §B — Compressed data format; §F — Huffman coding
//        §A — Discrete cosine transform
// PHASE: Phase 2 — Chuvadi.Pdf.Images
// Baseline JPEG decoder: Huffman decode, dequantise, IDCT, YCbCr→RGB.

using System;
using System.IO;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Images;

/// <summary>
/// Decodes a baseline sequential DCT JPEG (SOF0) into an <see cref="ImageFrame"/>.
/// </summary>
/// <remarks>
/// Supports:
/// <list type="bullet">
///   <item>Baseline DCT (SOF0 marker) — covers 95%+ of JPEG in PDFs</item>
///   <item>YCbCr → RGB colour conversion</item>
///   <item>Grayscale (1 component)</item>
///   <item>4:2:0, 4:2:2, 4:4:4 chroma subsampling</item>
///   <item>Up to 4 Huffman tables (DC + AC per component)</item>
///   <item>Up to 4 quantisation tables</item>
/// </list>
///
/// Not supported: progressive JPEG (SOF2), arithmetic coding (SOF9),
/// lossless JPEG, JFIF/EXIF metadata (ignored).
///
/// ISO 10918-1:1994 — Digital compression and coding of continuous-tone images.
/// </remarks>
public static class JpegDecoder
{
    // ── JPEG marker constants (ISO 10918-1 §B.1.1.3) ─────────────────────
    private const byte MarkerPrefix = 0xFF;
    private const byte MarkerSOI = 0xD8; // Start of image
    private const byte MarkerEOI = 0xD9; // End of image
    private const byte MarkerSOF0 = 0xC0; // Baseline DCT frame
    private const byte MarkerDHT = 0xC4; // Define Huffman table
    private const byte MarkerDQT = 0xDB; // Define quantisation table
    private const byte MarkerSOS = 0xDA; // Start of scan
    private const byte MarkerDRI = 0xDD; // Define restart interval

    /// <summary>Decodes a JPEG from a byte array.</summary>
    public static ImageFrame Decode(byte[] data)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        using (MemoryStream ms = new MemoryStream(data))
        {
            return Decode(ms);
        }
    }

    /// <summary>Decodes a JPEG from a stream.</summary>
    public static ImageFrame Decode(Stream input)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        JpegContext ctx = new JpegContext(input);
        ctx.Parse();
        return ctx.BuildFrame();
    }

    // ── Internal decoder context ──────────────────────────────────────────

    private sealed class JpegContext
    {
        private readonly Stream _input;

        // Quantisation tables (up to 4, each 64 entries)
        private readonly int[][] _quantTables = new int[4][];

        // Huffman tables: [tableClass 0=DC/1=AC][tableId 0-3]
        private readonly HuffmanTable?[][] _huffTables = new HuffmanTable?[2][];

        // Frame header (SOF0)
        private int _width;
        private int _height;
        private int _numComponents;
        private ComponentInfo[] _components = [];

        // Restart interval (MCUs between RST markers)
        private int _restartInterval;

        // Decoded MCU data (Y, Cb, Cr blocks)
        private byte[]? _yPlane;
        private byte[]? _cbPlane;
        private byte[]? _crPlane;
        private int _mcuWidth;
        private int _mcuHeight;

        internal JpegContext(Stream input)
        {
            _input = input;
            _huffTables[0] = new HuffmanTable?[4];
            _huffTables[1] = new HuffmanTable?[4];
        }

        internal void Parse()
        {
            // Verify SOI
            int b0 = _input.ReadByte();
            int b1 = _input.ReadByte();

            if (b0 != MarkerPrefix || b1 != MarkerSOI)
            {
                throw new ImageException("Not a valid JPEG: missing SOI marker.");
            }

            while (true)
            {
                // Find next marker
                int markerByte = FindNextMarker();

                if (markerByte == MarkerEOI)
                {
                    break;
                }

                switch (markerByte)
                {
                    case MarkerSOF0:
                        ParseSOF0();
                        break;

                    case MarkerDHT:
                        ParseDHT();
                        break;

                    case MarkerDQT:
                        ParseDQT();
                        break;

                    case MarkerDRI:
                        ParseDRI();
                        break;

                    case MarkerSOS:
                        ParseSOS();
                        return; // Entropy-coded data parsed; stop segment loop

                    default:
                        // Skip unknown segment
                        int segLen = ReadUInt16Be() - 2;

                        if (segLen > 0)
                        {
                            byte[] skip = new byte[segLen];
                            ReadExact(skip, segLen);
                        }
                        break;
                }
            }
        }

        // ── Marker parsing ────────────────────────────────────────────────

        private int FindNextMarker()
        {
            int b;

            do
            {
                b = _input.ReadByte();
            } while (b != MarkerPrefix && b >= 0);

            while (b == MarkerPrefix)
            {
                b = _input.ReadByte();
            }

            return b;
        }

        private void ParseSOF0()
        {
            int length = ReadUInt16Be() - 2;
            byte precision = ReadByte();

            if (precision != 8)
            {
                throw new ImageException($"JPEG precision {precision} not supported (only 8-bit).");
            }

            _height = ReadUInt16Be();
            _width = ReadUInt16Be();
            _numComponents = ReadByte();

            _components = new ComponentInfo[_numComponents];

            for (int i = 0; i < _numComponents; i++)
            {
                byte id = ReadByte();
                byte sampling = ReadByte();
                byte qtId = ReadByte();
                _components[i] = new ComponentInfo
                {
                    Id = id,
                    HorizontalSampling = (sampling >> 4) & 0xF,
                    VerticalSampling = sampling & 0xF,
                    QuantTableId = qtId,
                };
            }
        }

        private void ParseDHT()
        {
            int length = ReadUInt16Be() - 2;
            int bytesRead = 0;

            while (bytesRead < length)
            {
                byte tcTn = ReadByte();
                bytesRead++;

                int tableClass = (tcTn >> 4) & 0xF; // 0=DC, 1=AC
                int tableId = tcTn & 0xF;

                if (tableClass > 1 || tableId > 3)
                {
                    throw new ImageException($"Invalid Huffman table specifier: class={tableClass} id={tableId}.");
                }

                // 16 bytes: count of codes for each code length 1..16
                byte[] counts = new byte[16];
                ReadExact(counts, 16);
                bytesRead += 16;

                int totalSymbols = 0;

                foreach (byte c in counts)
                {
                    totalSymbols += c;
                }

                byte[] symbols = new byte[totalSymbols];
                ReadExact(symbols, totalSymbols);
                bytesRead += totalSymbols;

                _huffTables[tableClass][tableId] = new HuffmanTable(counts, symbols);
            }
        }

        private void ParseDQT()
        {
            int length = ReadUInt16Be() - 2;
            int bytesRead = 0;

            while (bytesRead < length)
            {
                byte pqTq = ReadByte();
                bytesRead++;

                int precision = (pqTq >> 4) & 0xF; // 0=8-bit, 1=16-bit
                int tableId = pqTq & 0xF;

                if (tableId > 3)
                {
                    throw new ImageException($"Invalid quantisation table ID: {tableId}.");
                }

                int[] qtable = new int[64];

                for (int i = 0; i < 64; i++)
                {
                    qtable[i] = precision == 0 ? ReadByte() : ReadUInt16Be();
                    bytesRead += precision == 0 ? 1 : 2;
                }

                _quantTables[tableId] = qtable;
            }
        }

        private void ParseDRI()
        {
            ReadUInt16Be(); // length (always 4)
            _restartInterval = ReadUInt16Be();
        }

        private void ParseSOS()
        {
            int length = ReadUInt16Be() - 2;
            int numScanComponents = ReadByte();

            int[] compDcTable = new int[numScanComponents];
            int[] compAcTable = new int[numScanComponents];

            for (int i = 0; i < numScanComponents; i++)
            {
                ReadByte(); // component selector
                byte tableIds = ReadByte();
                compDcTable[i] = (tableIds >> 4) & 0xF;
                compAcTable[i] = tableIds & 0xF;
            }

            ReadByte(); // Ss (spectral start, 0 for baseline)
            ReadByte(); // Se (spectral end, 63 for baseline)
            ReadByte(); // Ah/Al (successive approximation, 0 for baseline)

            // Read entropy-coded scan data
            DecodeEntropyScan(numScanComponents, compDcTable, compAcTable);
        }

        // ── Entropy decoding ──────────────────────────────────────────────

        private void DecodeEntropyScan(
            int numScanComponents,
            int[] compDcTable,
            int[] compAcTable)
        {
            // Max sampling factors
            int maxH = 1;
            int maxV = 1;

            foreach (ComponentInfo comp in _components)
            {
                if (comp.HorizontalSampling > maxH)
                {
                    maxH = comp.HorizontalSampling;
                }

                if (comp.VerticalSampling > maxV)
                {
                    maxV = comp.VerticalSampling;
                }
            }

            // MCU size in pixels
            int mcuPixelW = maxH * 8;
            int mcuPixelH = maxV * 8;

            _mcuWidth = (_width + mcuPixelW - 1) / mcuPixelW;
            _mcuHeight = (_height + mcuPixelH - 1) / mcuPixelH;

            // Allocate output planes (one byte per pixel, full resolution)
            int planeW = _mcuWidth * mcuPixelW;
            int planeH = _mcuHeight * mcuPixelH;

            _yPlane = new byte[planeW * planeH];
            _cbPlane = _numComponents > 1 ? new byte[planeW * planeH] : null;
            _crPlane = _numComponents > 1 ? new byte[planeW * planeH] : null;

            // Bit reader over entropy-coded data
            BitReader bits = new BitReader(_input);

            int[] dcPrev = new int[_numComponents];
            int mcuCount = 0;

            for (int mcuRow = 0; mcuRow < _mcuHeight; mcuRow++)
            {
                for (int mcuCol = 0; mcuCol < _mcuWidth; mcuCol++)
                {
                    // Restart interval handling
                    if (_restartInterval > 0 && mcuCount > 0 &&
                        mcuCount % _restartInterval == 0)
                    {
                        bits.AlignByte();

                        // Skip RST marker (0xFF 0xD0–0xD7)
                        int rb = bits.ReadByte();

                        while (rb == 0xFF)
                        {
                            rb = bits.ReadByte();
                        }

                        Array.Fill(dcPrev, 0);
                    }

                    mcuCount++;

                    // Decode each component in the MCU
                    for (int ci = 0; ci < numScanComponents && ci < _components.Length; ci++)
                    {
                        ComponentInfo comp = _components[ci];
                        int dcId = compDcTable[ci < compDcTable.Length ? ci : 0];
                        int acId = compAcTable[ci < compAcTable.Length ? ci : 0];

                        HuffmanTable? dcHuff = _huffTables[0][dcId];
                        HuffmanTable? acHuff = _huffTables[1][acId];

                        if (dcHuff is null || acHuff is null)
                        {
                            throw new ImageException("JPEG missing Huffman table.");
                        }

                        for (int bv = 0; bv < comp.VerticalSampling; bv++)
                        {
                            for (int bh = 0; bh < comp.HorizontalSampling; bh++)
                            {
                                // Decode one 8×8 block
                                int[] block = DecodeBlock(bits, dcHuff, acHuff, ref dcPrev[ci]);

                                // Dequantise
                                int[]? qt = _quantTables[comp.QuantTableId < 4 ? comp.QuantTableId : 0];

                                if (qt is not null)
                                {
                                    for (int k = 0; k < 64; k++)
                                    {
                                        block[k] *= qt[k];
                                    }
                                }

                                // IDCT and write to plane
                                byte[] pixels = IDCT(block);
                                WritePlane(pixels, ci, mcuRow, mcuCol, bv, bh,
                                    comp.VerticalSampling, comp.HorizontalSampling,
                                    planeW);
                            }
                        }
                    }
                }
            }
        }

        private static int[] DecodeBlock(
            BitReader bits,
            HuffmanTable dcHuff,
            HuffmanTable acHuff,
            ref int dcPrev)
        {
            int[] block = new int[64];

            // DC coefficient
            int dcLen = dcHuff.Decode(bits);
            int dcDiff = dcLen > 0 ? bits.ReadSignedBits(dcLen) : 0;
            dcPrev += dcDiff;
            block[0] = dcPrev;

            // AC coefficients (zig-zag order, 63 total)
            int k = 1;

            while (k < 64)
            {
                int acSymbol = acHuff.Decode(bits);

                if (acSymbol == 0x00)
                {
                    break; // EOB
                }

                if (acSymbol == 0xF0)
                {
                    k += 16; // ZRL: 16 zeros
                    continue;
                }

                int runLen = (acSymbol >> 4) & 0xF;
                int acLen = acSymbol & 0xF;

                k += runLen;

                if (k < 64 && acLen > 0)
                {
                    block[ZigZag[k]] = bits.ReadSignedBits(acLen);
                }

                k++;
            }

            return block;
        }

        // ── IDCT (AAN algorithm) ──────────────────────────────────────────

        // AAN IDCT scaling factors (Arai, Agui, Nakajima 1988)
        private static readonly double[] AanScales =
        [
            1.0,
            1.387039845, 1.306562965, 1.175875602,
            1.0,         0.785694958, 0.541196100, 0.275899379,
        ];

        private static byte[] IDCT(int[] input)
        {
            double[] work = new double[64];
            byte[] output = new byte[64];

            // Row IDCT
            for (int row = 0; row < 8; row++)
            {
                int off = row * 8;
                double v0 = input[off] * AanScales[0];
                double v1 = input[off + 1] * AanScales[1];
                double v2 = input[off + 2] * AanScales[2];
                double v3 = input[off + 3] * AanScales[3];
                double v4 = input[off + 4] * AanScales[4];
                double v5 = input[off + 5] * AanScales[5];
                double v6 = input[off + 6] * AanScales[6];
                double v7 = input[off + 7] * AanScales[7];

                double t0 = v0 + v4;
                double t1 = v0 - v4;
                double t2 = v2 + v6;
                double t3 = (v2 - v6) * 1.414213562 - t2;
                double t4 = t0 + t2;
                double t5 = t0 - t2;
                double t6 = t1 + t3;
                double t7 = t1 - t3;
                double m0 = v5 + v3;
                double m1 = v5 - v3;
                double m2 = v1 + v7;
                double m3 = v1 - v7;
                double s0 = (m0 - m2) * 1.847759065;
                double s1 = m1 * 2.613125930 + s0;
                double s2 = m3 * 1.082392201 - s0;
                double s3 = m2 * 1.847759065;
                double s4 = s1 - s3;
                double s5 = s2 + s3;
                double n0 = s4 + s5;
                double n1 = s4 - s5;
                double n2 = (m1 - m3) * 1.414213562 - n0;
                double n3 = n2 + n1 * 2.0;

                work[off] = t4 + n0;
                work[off + 7] = t4 - n0;
                work[off + 1] = t6 + n3;
                work[off + 6] = t6 - n3;
                work[off + 2] = t7 + n2;
                work[off + 5] = t7 - n2;
                work[off + 3] = t5 + n1;
                work[off + 4] = t5 - n1;
            }

            // Column IDCT
            for (int col = 0; col < 8; col++)
            {
                double v0 = work[col] * AanScales[0];
                double v1 = work[col + 8] * AanScales[1];
                double v2 = work[col + 16] * AanScales[2];
                double v3 = work[col + 24] * AanScales[3];
                double v4 = work[col + 32] * AanScales[4];
                double v5 = work[col + 40] * AanScales[5];
                double v6 = work[col + 48] * AanScales[6];
                double v7 = work[col + 56] * AanScales[7];

                double t0 = v0 + v4;
                double t1 = v0 - v4;
                double t2 = v2 + v6;
                double t3 = (v2 - v6) * 1.414213562 - t2;
                double t4 = t0 + t2;
                double t5 = t0 - t2;
                double t6 = t1 + t3;
                double t7 = t1 - t3;
                double m0 = v5 + v3;
                double m1 = v5 - v3;
                double m2 = v1 + v7;
                double m3 = v1 - v7;
                double s0 = (m0 - m2) * 1.847759065;
                double s1 = m1 * 2.613125930 + s0;
                double s2 = m3 * 1.082392201 - s0;
                double s3 = m2 * 1.847759065;
                double s4 = s1 - s3;
                double s5 = s2 + s3;
                double n0 = s4 + s5;
                double n1 = s4 - s5;
                double n2 = (m1 - m3) * 1.414213562 - n0;
                double n3 = n2 + n1 * 2.0;

                double scale = 1.0 / 8.0;
                output[col] = Clamp(t4 + n0, scale);
                output[col + 56] = Clamp(t4 - n0, scale);
                output[col + 8] = Clamp(t6 + n3, scale);
                output[col + 48] = Clamp(t6 - n3, scale);
                output[col + 16] = Clamp(t7 + n2, scale);
                output[col + 40] = Clamp(t7 - n2, scale);
                output[col + 24] = Clamp(t5 + n1, scale);
                output[col + 32] = Clamp(t5 - n1, scale);
            }

            return output;
        }

        private static byte Clamp(double val, double scale)
        {
            int v = (int)(val * scale + 128.5);
            return v < 0 ? (byte)0 : v > 255 ? (byte)255 : (byte)v;
        }

        private void WritePlane(
            byte[] pixels, int componentIndex,
            int mcuRow, int mcuCol,
            int blockRow, int blockCol,
            int vSampling, int hSampling,
            int planeWidth)
        {
            byte[]? plane = componentIndex == 0 ? _yPlane
                          : componentIndex == 1 ? _cbPlane
                          : _crPlane;

            if (plane is null)
            {
                return;
            }

            int blockPixelX = (mcuCol * hSampling + blockCol) * 8;
            int blockPixelY = (mcuRow * vSampling + blockRow) * 8;

            for (int py = 0; py < 8; py++)
            {
                for (int px = 0; px < 8; px++)
                {
                    int imgX = blockPixelX + px;
                    int imgY = blockPixelY + py;

                    if (imgX < planeWidth && imgY < plane.Length / planeWidth)
                    {
                        plane[imgY * planeWidth + imgX] = pixels[py * 8 + px];
                    }
                }
            }
        }

        // ── Frame building ────────────────────────────────────────────────

        internal ImageFrame BuildFrame()
        {
            if (_width <= 0 || _height <= 0)
            {
                throw new ImageException("JPEG SOF0 frame header was not decoded — invalid or unsupported JPEG.");
            }

            if (_yPlane is null)
            {
                throw new ImageException("JPEG scan data was not decoded.");
            }

            int planeW = _mcuWidth * (GetMaxHSampling() * 8);
            PixelBuffer buffer = new PixelBuffer(_width, _height);

            bool isGray = _numComponents == 1;

            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    int idx = y * planeW + x;

                    if (isGray || _cbPlane is null || _crPlane is null)
                    {
                        byte luma = idx < _yPlane.Length ? _yPlane[idx] : (byte)128;
                        buffer.SetPixelBgra(x, y, luma, luma, luma, 255);
                    }
                    else
                    {
                        // YCbCr → RGB conversion (ISO 10918-1 §A.3.3)
                        double yv = idx < _yPlane.Length ? _yPlane[idx] : 128;
                        double cb = idx < _cbPlane.Length ? _cbPlane[idx] : 128;
                        double cr = idx < _crPlane.Length ? _crPlane[idx] : 128;

                        int r = (int)(yv + 1.402 * (cr - 128));
                        int g = (int)(yv - 0.34414 * (cb - 128) - 0.71414 * (cr - 128));
                        int bv = (int)(yv + 1.772 * (cb - 128));

                        buffer.SetPixelBgra(
                            x, y,
                            (byte)(bv < 0 ? 0 : bv > 255 ? 255 : bv),
                            (byte)(g < 0 ? 0 : g > 255 ? 255 : g),
                            (byte)(r < 0 ? 0 : r > 255 ? 255 : r),
                            255);
                    }
                }
            }

            return new ImageFrame(buffer, isGray ? ImageColorFormat.Gray8 : ImageColorFormat.Rgb24);
        }

        private int GetMaxHSampling()
        {
            int max = 1;

            foreach (ComponentInfo c in _components)
            {
                if (c.HorizontalSampling > max)
                {
                    max = c.HorizontalSampling;
                }
            }

            return max;
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private byte ReadByte()
        {
            int b = _input.ReadByte();

            if (b < 0)
            {
                throw new ImageException("Unexpected end of JPEG stream.");
            }

            return (byte)b;
        }

        private int ReadUInt16Be()
        {
            int hi = ReadByte();
            int lo = ReadByte();
            return (hi << 8) | lo;
        }

        private void ReadExact(byte[] buf, int count)
        {
            int read = 0;

            while (read < count)
            {
                int n = _input.Read(buf, read, count - read);

                if (n == 0)
                {
                    throw new ImageException("JPEG data truncated.");
                }

                read += n;
            }
        }

        // JPEG zig-zag scan order (ISO 10918-1 §A.3.6)
        private static readonly int[] ZigZag =
        [
             0,  1,  8, 16,  9,  2,  3, 10,
            17, 24, 32, 25, 18, 11,  4,  5,
            12, 19, 26, 33, 40, 48, 41, 34,
            27, 20, 13,  6,  7, 14, 21, 28,
            35, 42, 49, 56, 57, 50, 43, 36,
            29, 22, 15, 23, 30, 37, 44, 51,
            58, 59, 52, 45, 38, 31, 39, 46,
            53, 60, 61, 54, 47, 55, 62, 63,
        ];
    }

    // ── Component info ────────────────────────────────────────────────────

    private struct ComponentInfo
    {
        internal byte Id;
        internal int HorizontalSampling;
        internal int VerticalSampling;
        internal byte QuantTableId;
    }

    // ── Huffman table (ISO 10918-1 §F.2) ─────────────────────────────────

    private sealed class HuffmanTable
    {
        private readonly int[] _codes;
        private readonly int[] _maxCodes;
        private readonly int[] _minCodes;
        private readonly int[] _valPtrs;
        private readonly byte[] _symbols;

        internal HuffmanTable(byte[] counts, byte[] symbols)
        {
            _symbols = symbols;
            _codes = new int[16];
            _maxCodes = new int[17];
            _minCodes = new int[16];
            _valPtrs = new int[16];

            // Generate codes from counts (ISO 10918-1 §C.2)
            int code = 0;
            int si = 0;

            for (int len = 0; len < 16; len++)
            {
                _minCodes[len] = code;
                _codes[len] = counts[len];

                for (int k = 0; k < counts[len]; k++, si++)
                {
                    code++;
                }

                _maxCodes[len] = code;
                _valPtrs[len] = si - counts[len];
                code <<= 1;
            }

            _maxCodes[16] = int.MaxValue;
        }

        internal int Decode(BitReader bits)
        {
            int code = 0;

            for (int len = 0; len < 16; len++)
            {
                code = (code << 1) | bits.ReadBit();

                if (_codes[len] > 0 && code < _maxCodes[len])
                {
                    int idx = _valPtrs[len] + code - _minCodes[len];

                    if (idx >= 0 && idx < _symbols.Length)
                    {
                        return _symbols[idx];
                    }
                }
            }

            return 0;
        }
    }

    // ── Bit reader for entropy-coded segment ──────────────────────────────

    private sealed class BitReader
    {
        private readonly Stream _stream;
        private int _buffer;
        private int _bitsLeft;

        internal BitReader(Stream stream)
        {
            _stream = stream;
            _buffer = 0;
            _bitsLeft = 0;
        }

        internal int ReadBit()
        {
            if (_bitsLeft == 0)
            {
                _buffer = _stream.ReadByte();

                // Byte stuffing: 0xFF 0x00 → 0xFF (ISO 10918-1 §F.1.2.3)
                if (_buffer == 0xFF)
                {
                    int next = _stream.ReadByte();

                    if (next != 0x00)
                    {
                        // RST marker or other — push back and return 0
                        _buffer = 0;
                    }
                }

                _bitsLeft = 8;
            }

            _bitsLeft--;
            return (_buffer >> _bitsLeft) & 1;
        }

        internal int ReadSignedBits(int count)
        {
            if (count == 0)
            {
                return 0;
            }

            int val = 0;

            for (int i = 0; i < count; i++)
            {
                val = (val << 1) | ReadBit();
            }

            // Two's complement for negative values
            if (val < (1 << (count - 1)))
            {
                val -= (1 << count) - 1;
            }

            return val;
        }

        internal void AlignByte()
        {
            _bitsLeft = 0;
        }

        internal int ReadByte()
        {
            _bitsLeft = 0;
            return _stream.ReadByte();
        }
    }
}
