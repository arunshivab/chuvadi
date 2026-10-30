using System;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Print.Pdf
{
    /// <summary>Copies a Chuvadi.Pdf <see cref="PixelBuffer"/> (BGRA, top-down) into a <see cref="RasterPage"/>.</summary>
    internal static class PixelCopy
    {
        /// <summary>
        /// Copies <paramref name="source"/> into <paramref name="target"/> in the target's format. Same size: a
        /// straight copy. Different size (rounding, anisotropic DPI, MaxRenderDpi): bilinear resampling.
        /// Pixels are composited over white, so partly transparent output prints as on paper.
        /// </summary>
        public static void Copy(PixelBuffer source, RasterPage target)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(target);
            ReadOnlySpan<byte> src = source.Pixels;
            int sw = source.Width;
            int sh = source.Height;
            int srcStride = source.Stride;
            int tw = target.PixelWidth;
            int th = target.PixelHeight;
            bool same = sw == tw && sh == th;
            double fx = (double)sw / tw;
            double fy = (double)sh / th;

            for (int y = 0; y < th; y++)
            {
                Span<byte> row = target.GetRow(y);
                for (int x = 0; x < tw; x++)
                {
                    byte b;
                    byte g;
                    byte r;
                    if (same)
                    {
                        int o = (y * srcStride) + (x * 4);
                        Over(src[o], src[o + 1], src[o + 2], src[o + 3], out b, out g, out r);
                    }
                    else
                    {
                        Sample(src, srcStride, sw, sh, ((x + 0.5) * fx) - 0.5, ((y + 0.5) * fy) - 0.5, out b, out g, out r);
                    }

                    switch (target.PixelFormat)
                    {
                        case RasterPixelFormat.Bgra32:
                            row[x * 4] = b;
                            row[(x * 4) + 1] = g;
                            row[(x * 4) + 2] = r;
                            row[(x * 4) + 3] = 255;
                            break;
                        case RasterPixelFormat.Rgb24:
                            row[x * 3] = r;
                            row[(x * 3) + 1] = g;
                            row[(x * 3) + 2] = b;
                            break;
                        case RasterPixelFormat.Gray8:
                            row[x] = Gray(r, g, b);
                            break;
                        default:
                            throw new NotSupportedException("PixelCopy writes Bgra32, Rgb24 or Gray8.");
                    }
                }
            }
        }

        internal static byte Gray(byte r, byte g, byte b) =>
            (byte)(((r * 299) + (g * 587) + (b * 114) + 500) / 1000);

        private static void Over(byte b, byte g, byte r, byte a, out byte outB, out byte outG, out byte outR)
        {
            if (a == 255)
            {
                outB = b;
                outG = g;
                outR = r;
                return;
            }

            int inv = 255 - a;
            outB = (byte)(((b * a) + (255 * inv) + 127) / 255);
            outG = (byte)(((g * a) + (255 * inv) + 127) / 255);
            outR = (byte)(((r * a) + (255 * inv) + 127) / 255);
        }

        private static void Sample(ReadOnlySpan<byte> src, int stride, int w, int h, double sx, double sy,
                                   out byte b, out byte g, out byte r)
        {
            sx = Math.Clamp(sx, 0, w - 1);
            sy = Math.Clamp(sy, 0, h - 1);
            int x0 = (int)sx;
            int y0 = (int)sy;
            int x1 = Math.Min(x0 + 1, w - 1);
            int y1 = Math.Min(y0 + 1, h - 1);
            double ax = sx - x0;
            double ay = sy - y0;

            double cb = 0;
            double cg = 0;
            double cr = 0;
            Accumulate(src, stride, x0, y0, (1 - ax) * (1 - ay), ref cb, ref cg, ref cr);
            Accumulate(src, stride, x1, y0, ax * (1 - ay), ref cb, ref cg, ref cr);
            Accumulate(src, stride, x0, y1, (1 - ax) * ay, ref cb, ref cg, ref cr);
            Accumulate(src, stride, x1, y1, ax * ay, ref cb, ref cg, ref cr);
            b = (byte)Math.Clamp((int)Math.Round(cb), 0, 255);
            g = (byte)Math.Clamp((int)Math.Round(cg), 0, 255);
            r = (byte)Math.Clamp((int)Math.Round(cr), 0, 255);
        }

        private static void Accumulate(ReadOnlySpan<byte> src, int stride, int x, int y, double weight,
                                       ref double cb, ref double cg, ref double cr)
        {
            if (weight <= 0)
            {
                return;
            }

            int o = (y * stride) + (x * 4);
            Over(src[o], src[o + 1], src[o + 2], src[o + 3], out byte b, out byte g, out byte r);
            cb += b * weight;
            cg += g * weight;
            cr += r * weight;
        }
    }
}
