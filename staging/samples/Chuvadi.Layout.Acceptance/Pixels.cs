using System;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Layout.Pdf;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.Rendering;

namespace Chuvadi.Layout.Acceptance
{
    /// <summary>
    /// Pixel comparison of a laid-out page with its reference page. Both are rasterized by the same renderer
    /// (Chuvadi.Pdf.Rendering, from the reference PDF and from Layout's PDF of the page), in grey, at the same DPI.
    /// </summary>
    /// <remarks>
    /// The score is the share of the page's content that differs: a pixel counts as different when its grey value is
    /// more than <see cref="GreyTolerance"/> from the other image's pixel at the same place and at each of its eight
    /// neighbours (so a one-pixel shift of an edge is not a difference). It is divided by the number of pixels that
    /// are not white in either image. 0% is identical; a line of text moved by a whole line height scores about as
    /// much as the text it covers.
    /// </remarks>
    internal static class Pixels
    {
        public const int GreyTolerance = 64;
        private const int White = 245;

        /// <summary>A rasterized page in grey (0 black to 255 white).</summary>
        internal sealed record GreyImage(int Width, int Height, byte[] Grey);

        public static List<GreyImage> RenderPdf(byte[] pdf, double dpi, int maxPages)
        {
            List<GreyImage> pages = new List<GreyImage>();
            using MemoryStream ms = new MemoryStream(pdf, writable: false);
            using PdfDocument doc = PdfDocument.Open(ms);
            PageRasterizer r = new PageRasterizer(doc.Objects, new RenderOptions { Dpi = dpi, AntiAlias = true });
            for (int i = 0; i < doc.Pages.Count && i < maxPages; i++)
            {
                pages.Add(Grey(r.Rasterize(doc.Pages[i])));
            }
            return pages;
        }

        public static GreyImage RenderLayout(LaidOutPage page, double dpi)
        {
            byte[] pdf = LayoutPdf.ToPdf(page);
            return RenderPdf(pdf, dpi, 1)[0];
        }

        private static GreyImage Grey(PixelBuffer p)
        {
            byte[] g = new byte[p.Width * p.Height];
            ReadOnlySpan<byte> px = p.Pixels;
            for (int i = 0, j = 0; i < g.Length; i++, j += 4)
            {
                // BGRA over white.
                int a = px[j + 3];
                int lum = ((px[j + 2] * 299) + (px[j + 1] * 587) + (px[j] * 114)) / 1000;
                g[i] = (byte)(((lum * a) + (255 * (255 - a))) / 255);
            }
            return new GreyImage(p.Width, p.Height, g);
        }

        /// <summary>The share (0 to 1) of content pixels that differ between two pages.</summary>
        public static double Score(GreyImage a, GreyImage b)
        {
            int w = Math.Min(a.Width, b.Width), h = Math.Min(a.Height, b.Height);
            long content = 0, different = 0;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int ga = a.Grey[(y * a.Width) + x], gb = b.Grey[(y * b.Width) + x];
                    if (ga >= White && gb >= White)
                    {
                        continue;
                    }
                    content++;
                    if (Math.Abs(ga - gb) <= GreyTolerance)
                    {
                        continue;
                    }
                    if (!Near(ga, b, x, y) || !Near(gb, a, x, y))
                    {
                        different++;
                    }
                }
            }
            // Content outside the common area (a page of another size) is all different.
            long extra = Outside(a, w, h) + Outside(b, w, h);
            content += extra;
            different += extra;
            return content == 0 ? 0 : (double)different / content;
        }

        private static bool Near(int value, GreyImage other, int x, int y)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= other.Width || ny >= other.Height)
                    {
                        continue;
                    }
                    if (Math.Abs(value - other.Grey[(ny * other.Width) + nx]) <= GreyTolerance)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static long Outside(GreyImage img, int w, int h)
        {
            long n = 0;
            for (int y = 0; y < img.Height; y++)
            {
                for (int x = 0; x < img.Width; x++)
                {
                    if ((x >= w || y >= h) && img.Grey[(y * img.Width) + x] < White)
                    {
                        n++;
                    }
                }
            }
            return n;
        }

        /// <summary>Writes a page as a binary PGM (greyscale) for looking at a difference.</summary>
        public static void WritePgm(GreyImage img, string path)
        {
            using FileStream fs = File.Create(path);
            byte[] header = System.Text.Encoding.ASCII.GetBytes($"P5\n{img.Width} {img.Height}\n255\n");
            fs.Write(header);
            fs.Write(img.Grey);
        }
    }
}
