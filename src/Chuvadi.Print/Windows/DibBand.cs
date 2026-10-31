using System;
using System.Buffers.Binary;

namespace Chuvadi.Print.Windows
{
    /// <summary>
    /// Converts horizontal bands of a <see cref="RasterPage"/> into GDI device-independent bitmaps
    /// (top-down, DWORD-aligned rows). Bands bound the extra memory to a few megabytes regardless of
    /// page size. Pure; portable.
    /// </summary>
    internal sealed class DibBand
    {
        /// <summary>Target size of one band's pixel buffer.</summary>
        internal const int DefaultBandBytes = 4 * 1024 * 1024;

        private readonly RasterPage _raster;

        public DibBand(RasterPage raster, int bandBytes = DefaultBandBytes)
        {
            _raster = raster ?? throw new ArgumentNullException(nameof(raster));
            if (bandBytes <= 0) throw new ArgumentOutOfRangeException(nameof(bandBytes));
            BitCount = raster.PixelFormat switch
            {
                RasterPixelFormat.Bgra32 => 32,
                RasterPixelFormat.Rgb24 => 24,
                RasterPixelFormat.Gray8 => 8,
                RasterPixelFormat.BlackWhite1 => 1,
                _ => throw new ArgumentOutOfRangeException(nameof(raster))
            };
            Stride = AlignedStride(raster.PixelWidth, BitCount);
            RowsPerBand = Math.Clamp(bandBytes / Stride, 1, raster.PixelHeight);
        }

        public int BitCount { get; }

        /// <summary>DIB row size in bytes (rounded up to a multiple of 4).</summary>
        public int Stride { get; }

        public int RowsPerBand { get; }

        public int BandCount => (_raster.PixelHeight + RowsPerBand - 1) / RowsPerBand;

        public static int AlignedStride(int width, int bitCount) => ((width * bitCount + 31) / 32) * 4;

        /// <summary>
        /// BITMAPINFOHEADER plus colour table for a top-down band of <paramref name="rows"/> rows.
        /// 8-bit uses a 256-level gray ramp; 1-bit uses index 0 = white, 1 = black (a set bit is ink).
        /// </summary>
        public byte[] BuildBitmapInfo(int rows)
        {
            int paletteEntries = BitCount switch { 8 => 256, 1 => 2, _ => 0 };
            var info = new byte[40 + paletteEntries * 4];
            Span<byte> h = info;
            BinaryPrimitives.WriteInt32LittleEndian(h, 40);                          // biSize
            BinaryPrimitives.WriteInt32LittleEndian(h.Slice(4), _raster.PixelWidth); // biWidth
            BinaryPrimitives.WriteInt32LittleEndian(h.Slice(8), -rows);              // biHeight < 0: top-down
            BinaryPrimitives.WriteInt16LittleEndian(h.Slice(12), 1);                 // biPlanes
            BinaryPrimitives.WriteInt16LittleEndian(h.Slice(14), (short)BitCount);   // biBitCount
            BinaryPrimitives.WriteInt32LittleEndian(h.Slice(16), 0);                 // BI_RGB
            BinaryPrimitives.WriteInt32LittleEndian(h.Slice(20), Stride * rows);     // biSizeImage
            BinaryPrimitives.WriteInt32LittleEndian(h.Slice(24), (int)Math.Round(_raster.DpiX / 0.0254));
            BinaryPrimitives.WriteInt32LittleEndian(h.Slice(28), (int)Math.Round(_raster.DpiY / 0.0254));
            BinaryPrimitives.WriteInt32LittleEndian(h.Slice(32), paletteEntries);    // biClrUsed
            BinaryPrimitives.WriteInt32LittleEndian(h.Slice(36), 0);                 // biClrImportant
            if (BitCount == 8)
            {
                for (int i = 0; i < 256; i++)
                {
                    int o = 40 + i * 4;
                    info[o] = info[o + 1] = info[o + 2] = (byte)i;
                }
            }
            else if (BitCount == 1)
            {
                info[40] = info[41] = info[42] = 255; // index 0: white
                // index 1: black (already zero)
            }
            return info;
        }

        /// <summary>
        /// Fills <paramref name="destination"/> (at least <see cref="Stride"/> x rows bytes) with rows
        /// [<paramref name="firstRow"/>, firstRow + rows) as DIB data. Bgra32 is composited over white.
        /// </summary>
        public void FillBand(int firstRow, int rows, Span<byte> destination)
        {
            if (firstRow < 0 || rows <= 0 || firstRow + rows > _raster.PixelHeight)
                throw new ArgumentOutOfRangeException(nameof(rows));
            if (destination.Length < Stride * rows) throw new ArgumentException("Destination too small.", nameof(destination));

            int width = _raster.PixelWidth;
            for (int r = 0; r < rows; r++)
            {
                ReadOnlySpan<byte> src = _raster.GetRow(firstRow + r);
                Span<byte> dst = destination.Slice(r * Stride, Stride);
                switch (_raster.PixelFormat)
                {
                    case RasterPixelFormat.Bgra32:
                        for (int x = 0; x < width; x++)
                        {
                            int i = x * 4;
                            int a = src[i + 3];
                            if (a == 255)
                            {
                                dst[i] = src[i]; dst[i + 1] = src[i + 1]; dst[i + 2] = src[i + 2];
                            }
                            else
                            {
                                dst[i] = Over(src[i], a); dst[i + 1] = Over(src[i + 1], a); dst[i + 2] = Over(src[i + 2], a);
                            }
                            dst[i + 3] = 0;
                        }
                        dst.Slice(width * 4).Clear();
                        break;
                    case RasterPixelFormat.Rgb24:
                        for (int x = 0; x < width; x++)
                        {
                            int i = x * 3;
                            dst[i] = src[i + 2]; dst[i + 1] = src[i + 1]; dst[i + 2] = src[i];
                        }
                        dst.Slice(width * 3).Clear();
                        break;
                    case RasterPixelFormat.Gray8:
                        src.Slice(0, width).CopyTo(dst);
                        dst.Slice(width).Clear();
                        break;
                    case RasterPixelFormat.BlackWhite1:
                        int bytes = (width + 7) / 8;
                        src.Slice(0, bytes).CopyTo(dst);
                        dst.Slice(bytes).Clear();
                        int spare = bytes * 8 - width;
                        if (spare > 0) dst[bytes - 1] &= (byte)(0xFF << spare);
                        break;
                }
            }
        }

        /// <summary>
        /// Device rows covered by band rows [firstRow, firstRow + rows) when the whole raster is
        /// stretched over <paramref name="destHeight"/> device rows starting at <paramref name="destY"/>.
        /// Consecutive bands tile exactly.
        /// </summary>
        public (int Y, int Height) DestinationRows(int firstRow, int rows, int destY, int destHeight)
        {
            int height = _raster.PixelHeight;
            int top = destY + (int)Math.Round((long)destHeight * firstRow / (double)height);
            int bottom = destY + (int)Math.Round((long)destHeight * (firstRow + rows) / (double)height);
            return (top, bottom - top);
        }

        private static byte Over(int channel, int alpha) => (byte)((channel * alpha + 255 * (255 - alpha) + 127) / 255);
    }
}
