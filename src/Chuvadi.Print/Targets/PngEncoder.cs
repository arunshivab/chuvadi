using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace Chuvadi.Print
{
    /// <summary>
    /// Minimal pure-BCL PNG writer (8-bit grayscale or 8-bit RGB, no interlace, filter type 0).
    /// Rows are supplied one at a time so the image is never held twice in memory.
    /// </summary>
    internal sealed class PngEncoder
    {
        private static readonly byte[] Signature = { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>PNG colour type 0.</summary>
        public const byte ColorTypeGray = 0;
        /// <summary>PNG colour type 2.</summary>
        public const byte ColorTypeRgb = 2;

        private readonly int _width;
        private readonly int _height;
        private readonly byte _colorType;
        private readonly double _dpiX;
        private readonly double _dpiY;

        public PngEncoder(int width, int height, byte colorType, double dpiX, double dpiY)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (colorType != ColorTypeGray && colorType != ColorTypeRgb) throw new ArgumentOutOfRangeException(nameof(colorType));
            _width = width;
            _height = height;
            _colorType = colorType;
            _dpiX = dpiX;
            _dpiY = dpiY;
        }

        public int RowBytes => _colorType == ColorTypeRgb ? _width * 3 : _width;

        /// <summary>
        /// Writes the PNG. <paramref name="fillRow"/> is called once per row, top to bottom, with a
        /// buffer of <see cref="RowBytes"/> bytes to fill.
        /// </summary>
        public void Write(Stream output, Action<int, Span<byte>> fillRow)
        {
            if (output is null) throw new ArgumentNullException(nameof(output));
            if (fillRow is null) throw new ArgumentNullException(nameof(fillRow));

            output.Write(Signature, 0, Signature.Length);

            Span<byte> ihdr = stackalloc byte[13];
            BinaryPrimitives.WriteInt32BigEndian(ihdr, _width);
            BinaryPrimitives.WriteInt32BigEndian(ihdr.Slice(4), _height);
            ihdr[8] = 8;            // bit depth
            ihdr[9] = _colorType;
            ihdr[10] = 0;           // compression: deflate
            ihdr[11] = 0;           // filter method
            ihdr[12] = 0;           // no interlace
            WriteChunk(output, "IHDR", ihdr);

            Span<byte> phys = stackalloc byte[9];
            BinaryPrimitives.WriteUInt32BigEndian(phys, (uint)Math.Round(_dpiX / 0.0254));
            BinaryPrimitives.WriteUInt32BigEndian(phys.Slice(4), (uint)Math.Round(_dpiY / 0.0254));
            phys[8] = 1;            // unit: metre
            WriteChunk(output, "pHYs", phys);

            using (var compressed = new MemoryStream())
            {
                byte[] row = new byte[RowBytes + 1];
                using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                {
                    for (int y = 0; y < _height; y++)
                    {
                        row[0] = 0;  // filter: none
                        fillRow(y, row.AsSpan(1));
                        z.Write(row, 0, row.Length);
                    }
                }
                WriteChunk(output, "IDAT", new ReadOnlySpan<byte>(compressed.GetBuffer(), 0, (int)compressed.Length));
            }

            WriteChunk(output, "IEND", ReadOnlySpan<byte>.Empty);
        }

        private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
        {
            Span<byte> header = stackalloc byte[8];
            BinaryPrimitives.WriteInt32BigEndian(header, data.Length);
            for (int i = 0; i < 4; i++) header[4 + i] = (byte)type[i];
            output.Write(header);
            output.Write(data);
            uint crc = Crc32.Append(Crc32.Seed, header.Slice(4, 4));
            crc = Crc32.Finish(Crc32.Append(crc, data));
            Span<byte> crcBytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
            output.Write(crcBytes);
        }
    }
}
