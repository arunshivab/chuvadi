using System;
using System.Buffers;

namespace Chuvadi.Print
{
    /// <summary>
    /// Pixel-format conversion (colour to gray to black-and-white). This is not rasterisation:
    /// it only re-encodes pixels a rasteriser already produced. Works row by row, so the only
    /// extra memory is the destination page plus a few rows.
    /// </summary>
    internal static class PixelConverter
    {
        // 8x8 Bayer matrix, values 0..63.
        private static readonly byte[] Bayer8 =
        {
             0, 32,  8, 40,  2, 34, 10, 42,
            48, 16, 56, 24, 50, 18, 58, 26,
            12, 44,  4, 36, 14, 46,  6, 38,
            60, 28, 52, 20, 62, 30, 54, 22,
             3, 35, 11, 43,  1, 33,  9, 41,
            51, 19, 59, 27, 49, 17, 57, 25,
            15, 47,  7, 39, 13, 45,  5, 37,
            63, 31, 55, 23, 61, 29, 53, 21
        };

        /// <summary>
        /// Returns a new pooled page in <paramref name="target"/> format. The source is not disposed.
        /// Supported sources: Bgra32, Rgb24, Gray8 (and BlackWhite1 when the target is also BlackWhite1).
        /// </summary>
        public static RasterPage Convert(RasterPage source, RasterPixelFormat target,
                                         MonochromeConversion monochrome, byte threshold)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            int w = source.PixelWidth, h = source.PixelHeight;
            RasterPage dest = RasterPage.Rent(w, h, target, source.DpiX, source.DpiY);
            try
            {
                if (source.PixelFormat == RasterPixelFormat.BlackWhite1)
                {
                    if (target != RasterPixelFormat.BlackWhite1)
                        ExpandBlackWhite(source, dest);
                    else
                        for (int y = 0; y < h; y++)
                            source.GetRow(y).Slice(0, dest.Stride).CopyTo(dest.GetRow(y));
                    return dest;
                }

                switch (target)
                {
                    case RasterPixelFormat.Bgra32:
                    case RasterPixelFormat.Rgb24:
                        for (int y = 0; y < h; y++) WriteColorRow(source.GetRow(y), source.PixelFormat, dest.GetRow(y), target, w);
                        break;
                    case RasterPixelFormat.Gray8:
                        for (int y = 0; y < h; y++) WriteGrayRow(source.GetRow(y), source.PixelFormat, dest.GetRow(y), w);
                        break;
                    case RasterPixelFormat.BlackWhite1:
                        ToBlackWhite(source, dest, monochrome, threshold);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(target));
                }
                return dest;
            }
            catch
            {
                dest.Dispose();
                throw;
            }
        }

        /// <summary>Luma (ITU-R BT.601) of one pixel, composited over white when it has alpha.</summary>
        private static byte GrayAt(ReadOnlySpan<byte> row, RasterPixelFormat format, int x)
        {
            switch (format)
            {
                case RasterPixelFormat.Gray8:
                    return row[x];
                case RasterPixelFormat.Rgb24:
                {
                    int i = x * 3;
                    return Luma(row[i], row[i + 1], row[i + 2]);
                }
                case RasterPixelFormat.Bgra32:
                {
                    int i = x * 4;
                    int a = row[i + 3];
                    return Luma(OverWhite(row[i + 2], a), OverWhite(row[i + 1], a), OverWhite(row[i], a));
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(format));
            }
        }

        private static byte Luma(int r, int g, int b) => (byte)((r * 299 + g * 587 + b * 114 + 500) / 1000);

        private static int OverWhite(int channel, int alpha) => (channel * alpha + 255 * (255 - alpha) + 127) / 255;

        private static void WriteGrayRow(ReadOnlySpan<byte> src, RasterPixelFormat format, Span<byte> dst, int width)
        {
            for (int x = 0; x < width; x++) dst[x] = GrayAt(src, format, x);
        }

        private static void WriteColorRow(ReadOnlySpan<byte> src, RasterPixelFormat srcFormat,
                                          Span<byte> dst, RasterPixelFormat dstFormat, int width)
        {
            for (int x = 0; x < width; x++)
            {
                int r, g, b, a = 255;
                switch (srcFormat)
                {
                    case RasterPixelFormat.Gray8:
                        r = g = b = src[x];
                        break;
                    case RasterPixelFormat.Rgb24:
                        r = src[x * 3]; g = src[x * 3 + 1]; b = src[x * 3 + 2];
                        break;
                    case RasterPixelFormat.Bgra32:
                        b = src[x * 4]; g = src[x * 4 + 1]; r = src[x * 4 + 2]; a = src[x * 4 + 3];
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(srcFormat));
                }
                if (dstFormat == RasterPixelFormat.Bgra32)
                {
                    int i = x * 4;
                    dst[i] = (byte)b; dst[i + 1] = (byte)g; dst[i + 2] = (byte)r; dst[i + 3] = (byte)a;
                }
                else
                {
                    // Rgb24 has no alpha: composite over white paper.
                    int i = x * 3;
                    dst[i] = (byte)OverWhite(r, a); dst[i + 1] = (byte)OverWhite(g, a); dst[i + 2] = (byte)OverWhite(b, a);
                }
            }
        }

        private static void ToBlackWhite(RasterPage source, RasterPage dest, MonochromeConversion mode, byte threshold)
        {
            int w = source.PixelWidth, h = source.PixelHeight;
            byte[] gray = ArrayPool<byte>.Shared.Rent(w);
            int[]? errCurrent = null, errNext = null;
            try
            {
                if (mode == MonochromeConversion.ErrorDiffusion)
                {
                    errCurrent = ArrayPool<int>.Shared.Rent(w + 2);
                    errNext = ArrayPool<int>.Shared.Rent(w + 2);
                    Array.Clear(errCurrent, 0, w + 2);
                    Array.Clear(errNext, 0, w + 2);
                }

                for (int y = 0; y < h; y++)
                {
                    Span<byte> grayRow = gray.AsSpan(0, w);
                    WriteGrayRow(source.GetRow(y), source.PixelFormat, grayRow, w);
                    Span<byte> outRow = dest.GetRow(y);
                    outRow.Clear();

                    switch (mode)
                    {
                        case MonochromeConversion.Threshold:
                            for (int x = 0; x < w; x++)
                                if (grayRow[x] < threshold) SetBlack(outRow, x);
                            break;

                        case MonochromeConversion.OrderedDither:
                        {
                            int rowBase = (y & 7) * 8;
                            for (int x = 0; x < w; x++)
                            {
                                int t = Bayer8[rowBase + (x & 7)] * 4 + 2;
                                if (grayRow[x] < t) SetBlack(outRow, x);
                            }
                            break;
                        }

                        case MonochromeConversion.ErrorDiffusion:
                        {
                            // Floyd-Steinberg; buffers are offset by one so x-1 and x+1 never go out of range.
                            int[] cur = errCurrent!, next = errNext!;
                            for (int x = 0; x < w; x++)
                            {
                                int value = grayRow[x] + (cur[x + 1] >> 4);
                                int quantised = value < 128 ? 0 : 255;
                                if (quantised == 0) SetBlack(outRow, x);
                                int e = value - quantised;
                                cur[x + 2] += e * 7;
                                next[x] += e * 3;
                                next[x + 1] += e * 5;
                                next[x + 2] += e;
                            }
                            errCurrent = next;
                            errNext = cur;
                            Array.Clear(errNext, 0, w + 2);
                            break;
                        }

                        default:
                            throw new ArgumentOutOfRangeException(nameof(mode));
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(gray);
                if (errCurrent is not null) ArrayPool<int>.Shared.Return(errCurrent);
                if (errNext is not null) ArrayPool<int>.Shared.Return(errNext);
            }
        }

        private static void ExpandBlackWhite(RasterPage source, RasterPage dest)
        {
            int w = source.PixelWidth, h = source.PixelHeight;
            for (int y = 0; y < h; y++)
            {
                ReadOnlySpan<byte> src = source.GetRow(y);
                Span<byte> dst = dest.GetRow(y);
                for (int x = 0; x < w; x++)
                {
                    byte v = (src[x >> 3] & (0x80 >> (x & 7))) != 0 ? (byte)0 : (byte)255;
                    switch (dest.PixelFormat)
                    {
                        case RasterPixelFormat.Gray8: dst[x] = v; break;
                        case RasterPixelFormat.Rgb24: dst[x * 3] = dst[x * 3 + 1] = dst[x * 3 + 2] = v; break;
                        case RasterPixelFormat.Bgra32:
                            dst[x * 4] = dst[x * 4 + 1] = dst[x * 4 + 2] = v; dst[x * 4 + 3] = 255; break;
                        default: throw new ArgumentOutOfRangeException(nameof(dest));
                    }
                }
            }
        }

        private static void SetBlack(Span<byte> row, int x) => row[x >> 3] |= (byte)(0x80 >> (x & 7));
    }
}
