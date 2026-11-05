using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Chuvadi.Print;
using Chuvadi.Print.Windows;

namespace Chuvadi.Print.ManualTests
{
    /// <summary>
    /// Phase 4 (XPS) sample: two pages that use every part of the XPS writer, for visual checks in an XPS viewer.
    /// Text uses a TrueType font from the machine; glyphs are looked up through the font's own cmap table, so
    /// only scripts that need no shaping (Latin, Greek, Cyrillic) are drawn.
    /// </summary>
    public static class Phase4Scenarios
    {
        /// <summary>Writes the sample to <paramref name="path"/> and says what it holds.</summary>
        public static string WriteSample(string path, string? fontPath)
        {
            string? font = fontPath ?? FindSystemFont();
            IReadOnlyList<VectorPage> pages = SamplePages(font);
            if (!XpsWriter.CanWrite(pages, out string? reason)) throw new InvalidOperationException(reason);
            string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            XpsWriter.Write(pages, path);
            return pages.Count + " pages written to " + Path.GetFullPath(path) + " (" + new FileInfo(path).Length + " bytes), font: "
                   + (font ?? "none found, text left out");
        }

        /// <summary>A TrueType font that is normally installed: Arial on Windows, DejaVu Sans or Liberation Sans elsewhere.</summary>
        public static string? FindSystemFont()
        {
            var candidates = new List<string>();
            string windir = Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows";
            if (OperatingSystem.IsWindows()) candidates.Add(Path.Combine(windir, "Fonts", "arial.ttf"));
            candidates.Add("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf");
            candidates.Add("/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf");
            candidates.Add("/System/Library/Fonts/Supplemental/Arial.ttf");
            foreach (string c in candidates) if (File.Exists(c)) return c;
            return null;
        }

        public static IReadOnlyList<VectorPage> SamplePages(string? fontPath)
        {
            SampleFont? font = fontPath is null ? null : SampleFont.Load(fontPath);
            var black = new VectorSolidBrush(VectorColor.Black);

            // Page 1: A4 portrait.
            var page = new VectorPage(595.28, 841.89);
            if (font is not null)
            {
                page.Elements.Add(font.Run("Chuvadi.Print XPS sample", 72, 80, 24, black));
                page.Elements.Add(font.Run("Text, paths, gradients, images, groups and clips. \u03A9 \u0416 fi", 72, 110, 11, black));
            }

            // Filled and stroked shapes.
            page.Elements.Add(new VectorPath(VectorGeometry.FromRectangle(72, 140, 120, 80)) { Fill = new VectorSolidBrush(new VectorColor(220, 40, 40)) });
            page.Elements.Add(new VectorPath(VectorGeometry.FromRectangle(212, 140, 120, 80))
            {
                Fill = new VectorSolidBrush(new VectorColor(40, 120, 220)),
                Stroke = new VectorStroke(black, 3) { LineJoin = VectorLineJoin.Round }
            });
            var dashed = new VectorStroke(new VectorSolidBrush(new VectorColor(0, 140, 60)), 2) { LineCap = VectorLineCap.Round };
            dashed.DashArray.Add(8);
            dashed.DashArray.Add(4);
            page.Elements.Add(new VectorPath(new VectorGeometry().MoveTo(352, 140).LineTo(523, 220)) { Stroke = dashed });

            // Even-odd ring.
            var ring = new VectorGeometry { FillRule = VectorFillRule.EvenOdd };
            Circle(ring, 132, 300, 50);
            Circle(ring, 132, 300, 25);
            page.Elements.Add(new VectorPath(ring) { Fill = new VectorSolidBrush(new VectorColor(120, 60, 160)) });

            // Linear and radial gradients.
            var linear = new VectorLinearGradientBrush(new VectorPoint(212, 0), new VectorPoint(523, 0));
            linear.Stops.Add(new VectorGradientStop(0, new VectorColor(255, 200, 0)));
            linear.Stops.Add(new VectorGradientStop(0.5, new VectorColor(255, 60, 0)));
            linear.Stops.Add(new VectorGradientStop(1, new VectorColor(120, 0, 120)));
            page.Elements.Add(new VectorPath(VectorGeometry.FromRectangle(212, 260, 311, 40)) { Fill = linear });
            var radial = new VectorRadialGradientBrush(new VectorPoint(352, 360), 0, new VectorPoint(367, 375), 45);
            radial.Stops.Add(new VectorGradientStop(0, VectorColor.White));
            radial.Stops.Add(new VectorGradientStop(1, new VectorColor(0, 90, 160)));
            var disc = new VectorGeometry();
            Circle(disc, 367, 375, 45);
            page.Elements.Add(new VectorPath(disc) { Fill = radial });

            // Image: a generated 64 x 64 colour ramp, shown 120 pt square, and a 2 x 2 grey image scaled up (sharp checker).
            var pixels = new byte[64 * 64 * 3];
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    int i = (y * 64 + x) * 3;
                    pixels[i] = (byte)(x * 4);
                    pixels[i + 1] = (byte)(y * 4);
                    pixels[i + 2] = 160;
                }
            page.Elements.Add(new VectorImageElement(VectorImage.FromPixels(64, 64, VectorPixelFormat.Rgb24, pixels), 72, 440, 120, 120));
            page.Elements.Add(new VectorImageElement(VectorImage.FromPixels(2, 2, VectorPixelFormat.Gray8, new byte[] { 0, 255, 255, 0 }), 212, 440, 60, 60));

            // Group at 50 % opacity: the two squares overlap without darkening each other.
            var group = new VectorGroup { Opacity = 0.5 };
            group.Children.Add(new VectorPath(VectorGeometry.FromRectangle(300, 440, 80, 80)) { Fill = new VectorSolidBrush(new VectorColor(0, 0, 0)) });
            group.Children.Add(new VectorPath(VectorGeometry.FromRectangle(340, 480, 80, 80)) { Fill = new VectorSolidBrush(new VectorColor(0, 0, 0)) });
            page.Elements.Add(group);

            // Clip: a striped square clipped to a circle.
            var stripes = new VectorGroup();
            var clip = new VectorGeometry();
            Circle(clip, 480, 500, 40);
            stripes.Clip = clip;
            for (int i = 0; i < 8; i++)
                stripes.Children.Add(new VectorPath(VectorGeometry.FromRectangle(440, 460 + i * 10, 80, 5)) { Fill = new VectorSolidBrush(new VectorColor(200, 0, 80)) });
            page.Elements.Add(stripes);

            // Rotated and coloured text.
            if (font is not null)
            {
                VectorGlyphRun rotated = font.Run("Rotated 30 degrees", 0, 0, 14, new VectorSolidBrush(new VectorColor(0, 100, 0)));
                double a = -30 * Math.PI / 180;
                rotated.Transform = new VectorMatrix(Math.Cos(a), Math.Sin(a), -Math.Sin(a), Math.Cos(a), 80, 680);
                page.Elements.Add(rotated);
                page.Elements.Add(font.Run("Half-transparent text", 300, 640, 18, new VectorSolidBrush(new VectorColor(0, 0, 200, 128))));
                page.Elements.Add(font.Run("Bottom line at 72 pt from the bottom edge", 72, 841.89 - 72, 10, black));
            }

            // Page 2: A5 landscape with a frame at the page edge (checks page size) and centre marks.
            var page2 = new VectorPage(595.28, 419.53);
            page2.Elements.Add(new VectorPath(VectorGeometry.FromRectangle(1, 1, 593.28, 417.53)) { Stroke = new VectorStroke(black, 2) });
            page2.Elements.Add(new VectorPath(new VectorGeometry().MoveTo(297.64, 0).LineTo(297.64, 419.53).MoveTo(0, 209.765).LineTo(595.28, 209.765))
            {
                Stroke = new VectorStroke(new VectorSolidBrush(new VectorColor(150, 150, 150)), 0.5)
            });
            if (font is not null) page2.Elements.Add(font.Run("Page 2: A5 landscape, frame on the page edge", 72, 72, 14, black));
            return new[] { page, page2 };
        }

        /// <summary>Adds a circle as four Béziers.</summary>
        private static void Circle(VectorGeometry g, double cx, double cy, double r)
        {
            const double k = 0.5522847498;
            g.MoveTo(cx + r, cy)
             .CubicTo(cx + r, cy + k * r, cx + k * r, cy + r, cx, cy + r)
             .CubicTo(cx - k * r, cy + r, cx - r, cy + k * r, cx - r, cy)
             .CubicTo(cx - r, cy - k * r, cx - k * r, cy - r, cx, cy - r)
             .CubicTo(cx + k * r, cy - r, cx + r, cy - k * r, cx + r, cy)
             .Close();
        }

        /// <summary>Reads just enough of a TrueType font for the sample: cmap (formats 4 and 12), hmtx and unitsPerEm.</summary>
        private sealed class SampleFont
        {
            private readonly Dictionary<int, ushort> _cmap = new();
            private ushort[] _advances = Array.Empty<ushort>();
            private int _unitsPerEm = 1000;
            private VectorFont _font = null!;

            public static SampleFont Load(string path)
            {
                byte[] data = File.ReadAllBytes(path);
                var f = new SampleFont { _font = new VectorFont(data, Path.GetFileNameWithoutExtension(path)) };
                var tables = new Dictionary<string, (int Offset, int Length)>();
                int n = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4));
                for (int i = 0; i < n; i++)
                {
                    int rec = 12 + i * 16;
                    string tag = System.Text.Encoding.ASCII.GetString(data, rec, 4);
                    tables[tag] = ((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(rec + 8)), (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(rec + 12)));
                }
                f._unitsPerEm = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(tables["head"].Offset + 18));
                int numberOfHMetrics = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(tables["hhea"].Offset + 34));
                f._advances = new ushort[numberOfHMetrics];
                for (int i = 0; i < numberOfHMetrics; i++) f._advances[i] = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(tables["hmtx"].Offset + i * 4));
                f.ReadCmap(data, tables["cmap"].Offset);
                return f;
            }

            private void ReadCmap(byte[] d, int cmap)
            {
                int count = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(cmap + 2));
                int best = -1, bestFormat = 0;
                for (int i = 0; i < count; i++)
                {
                    int rec = cmap + 4 + i * 8;
                    int platform = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(rec));
                    int encoding = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(rec + 2));
                    int sub = cmap + (int)BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(rec + 4));
                    int format = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(sub));
                    if (platform == 3 && (encoding == 1 || encoding == 10) && (format == 4 || format == 12) && format > bestFormat)
                    {
                        best = sub;
                        bestFormat = format;
                    }
                }
                if (best < 0) throw new InvalidDataException("The font has no Unicode cmap.");
                if (bestFormat == 12)
                {
                    int groups = (int)BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(best + 12));
                    for (int g = 0; g < groups; g++)
                    {
                        int p = best + 16 + g * 12;
                        int start = (int)BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(p));
                        int end = (int)BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(p + 4));
                        int gid = (int)BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(p + 8));
                        for (int c = start; c <= end && c <= 0xFFFF; c++) _cmap[c] = (ushort)(gid + c - start);
                    }
                    return;
                }
                int segX2 = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(best + 6));
                int ends = best + 14, starts = ends + segX2 + 2, deltas = starts + segX2, ranges = deltas + segX2;
                for (int s = 0; s < segX2 / 2; s++)
                {
                    int end = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(ends + s * 2));
                    int start = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(starts + s * 2));
                    short delta = BinaryPrimitives.ReadInt16BigEndian(d.AsSpan(deltas + s * 2));
                    int rangeOffset = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(ranges + s * 2));
                    for (int c = start; c <= end && c != 0xFFFF; c++)
                    {
                        int gid;
                        if (rangeOffset == 0) gid = (c + delta) & 0xFFFF;
                        else
                        {
                            int at = ranges + s * 2 + rangeOffset + (c - start) * 2;
                            gid = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(at));
                            if (gid != 0) gid = (gid + delta) & 0xFFFF;
                        }
                        if (gid != 0) _cmap[c] = (ushort)gid;
                    }
                }
            }

            /// <summary>One line of text, one glyph per character, placed by the font's advances.</summary>
            public VectorGlyphRun Run(string text, double x, double y, double size, VectorBrush fill)
            {
                var run = new VectorGlyphRun(_font, size, fill);
                foreach (char ch in text)
                {
                    ushort gid = _cmap.TryGetValue(ch, out ushort g) ? g : (ushort)0;
                    double advance = _advances[Math.Min(gid, _advances.Length - 1)] * size / _unitsPerEm;
                    run.Glyphs.Add(new VectorGlyph(gid, x, y, advance, ch.ToString()));
                    x += advance;
                }
                return run;
            }
        }
    }
}
