using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Chuvadi.Layout.Fonts.Internal;

namespace Chuvadi.Layout.Fonts
{
    /// <summary>
    /// TrueType glyph outlines ('glyf'/'loca', simple and composite glyphs) as SVG path data in font units (y up).
    /// Paths are built on first use and cached per face; safe to use from several threads. Malformed glyph data
    /// gives an empty path. CFF outlines are not read (<see cref="HasOutlines"/> is false).
    /// </summary>
    internal sealed class GlyphOutlines
    {
        private static readonly ConditionalWeakTable<FontFace, GlyphOutlines> Cache = new ConditionalWeakTable<FontFace, GlyphOutlines>();

        private readonly BigEndian? _glyf;
        private readonly BigEndian? _loca;
        private readonly bool _longLoca;
        private readonly int _glyphCount;
        private readonly Dictionary<int, string> _paths = new Dictionary<int, string>();

        private GlyphOutlines(FontFace face)
        {
            try
            {
                SfntDirectory dir = SfntDirectory.Read(face.Data, face.FaceIndex);
                _glyf = dir.TryTable(face.Data, "glyf");
                _loca = dir.TryTable(face.Data, "loca");
                _longLoca = dir.Table(face.Data, "head").I16(50) != 0;
            }
            catch (FontFormatException)
            {
                _glyf = null;
                _loca = null;
            }
            _glyphCount = face.GlyphCount;
        }

        public static GlyphOutlines For(FontFace face) => Cache.GetValue(face, f => new GlyphOutlines(f));

        /// <summary>True when the face has TrueType outlines this reader can draw.</summary>
        public bool HasOutlines => _glyf is not null && _loca is not null;

        public string PathData(int glyph)
        {
            lock (_paths)
            {
                if (!_paths.TryGetValue(glyph, out string? d))
                {
                    StringBuilder sb = new StringBuilder();
                    try
                    {
                        Append(sb, glyph, 1, 0, 0, 1, 0, 0, 0);
                    }
                    catch (Exception ex) when (ex is FontFormatException or IndexOutOfRangeException or ArgumentOutOfRangeException)
                    {
                        sb.Clear();
                    }
                    d = sb.ToString();
                    _paths[glyph] = d;
                }
                return d;
            }
        }

        private (long Start, long End) Range(int glyph)
        {
            BigEndian loca = _loca!.Value;
            if (_longLoca)
            {
                return (loca.U32(4L * glyph), loca.U32(4L * (glyph + 1)));
            }
            return (loca.U16(2L * glyph) * 2L, loca.U16(2L * (glyph + 1)) * 2L);
        }

        private void Append(StringBuilder sb, int glyph, double a, double b, double c, double d, double dx, double dy, int depth)
        {
            if (_glyf is null || _loca is null || glyph < 0 || glyph >= _glyphCount || depth > 8)
            {
                return;
            }
            BigEndian g = _glyf.Value;
            (long start, long end) = Range(glyph);
            if (end <= start)
            {
                return;
            }
            int contours = g.I16(start);
            long p = start + 10;
            CultureInfo ci = CultureInfo.InvariantCulture;
            if (contours >= 0)
            {
                int[] endPts = new int[contours];
                for (int i = 0; i < contours; i++)
                {
                    endPts[i] = g.U16(p);
                    p += 2;
                }
                int count = contours == 0 ? 0 : endPts[^1] + 1;
                int instr = g.U16(p);
                p += 2 + instr;
                byte[] flags = new byte[count];
                for (int i = 0; i < count; i++)
                {
                    byte f = g.U8(p++);
                    flags[i] = f;
                    if ((f & 8) != 0)
                    {
                        int rep = g.U8(p++);
                        for (int r = 0; r < rep && i + 1 < count; r++)
                        {
                            flags[++i] = f;
                        }
                    }
                }
                int[] xs = new int[count];
                int[] ys = new int[count];
                int v = 0;
                for (int i = 0; i < count; i++)
                {
                    byte f = flags[i];
                    if ((f & 2) != 0)
                    {
                        int dv = g.U8(p++);
                        v += (f & 16) != 0 ? dv : -dv;
                    }
                    else if ((f & 16) == 0)
                    {
                        v += g.I16(p);
                        p += 2;
                    }
                    xs[i] = v;
                }
                v = 0;
                for (int i = 0; i < count; i++)
                {
                    byte f = flags[i];
                    if ((f & 4) != 0)
                    {
                        int dv = g.U8(p++);
                        v += (f & 32) != 0 ? dv : -dv;
                    }
                    else if ((f & 32) == 0)
                    {
                        v += g.I16(p);
                        p += 2;
                    }
                    ys[i] = v;
                }
                int s0 = 0;
                for (int ci2 = 0; ci2 < contours; ci2++)
                {
                    int e = endPts[ci2];
                    int n = e - s0 + 1;
                    if (n <= 0)
                    {
                        s0 = e + 1;
                        continue;
                    }
                    (double X, double Y) T(int i) => ((a * xs[i]) + (c * ys[i]) + dx, (b * xs[i]) + (d * ys[i]) + dy);
                    bool On(int i) => (flags[i] & 1) != 0;
                    // Find a starting on-curve point (or the midpoint of two off-curve points).
                    int first = -1;
                    for (int i = s0; i <= e; i++)
                    {
                        if (On(i))
                        {
                            first = i;
                            break;
                        }
                    }
                    (double X, double Y) startPt;
                    if (first < 0)
                    {
                        (double X, double Y) p0 = T(s0), p1 = T(s0 + (n > 1 ? 1 : 0));
                        startPt = ((p0.X + p1.X) / 2, (p0.Y + p1.Y) / 2);
                        first = s0;
                    }
                    else
                    {
                        startPt = T(first);
                    }
                    sb.Append(ci, $"M{startPt.X:0.#} {startPt.Y:0.#}");
                    (double X, double Y)? pending = null;
                    for (int k = 1; k <= n; k++)
                    {
                        int i = s0 + ((first - s0 + k) % n);
                        (double X, double Y) pt = T(i);
                        if (On(i))
                        {
                            if (pending is (double px, double py))
                            {
                                sb.Append(ci, $"Q{px:0.#} {py:0.#} {pt.X:0.#} {pt.Y:0.#}");
                                pending = null;
                            }
                            else
                            {
                                sb.Append(ci, $"L{pt.X:0.#} {pt.Y:0.#}");
                            }
                        }
                        else
                        {
                            if (pending is (double px, double py))
                            {
                                double mx = (px + pt.X) / 2, my = (py + pt.Y) / 2;
                                sb.Append(ci, $"Q{px:0.#} {py:0.#} {mx:0.#} {my:0.#}");
                            }
                            pending = pt;
                        }
                    }
                    if (pending is (double qx, double qy))
                    {
                        sb.Append(ci, $"Q{qx:0.#} {qy:0.#} {startPt.X:0.#} {startPt.Y:0.#}");
                    }
                    sb.Append('Z');
                    s0 = e + 1;
                }
                return;
            }
            // Composite glyph.
            while (true)
            {
                int flags = g.U16(p);
                int component = g.U16(p + 2);
                p += 4;
                double ox, oy;
                if ((flags & 1) != 0)
                {
                    ox = g.I16(p);
                    oy = g.I16(p + 2);
                    p += 4;
                }
                else
                {
                    ox = g.I8(p);
                    oy = g.I8(p + 1);
                    p += 2;
                }
                double ca = 1, cb = 0, cc = 0, cd = 1;
                if ((flags & 8) != 0)
                {
                    ca = cd = g.I16(p) / 16384.0;
                    p += 2;
                }
                else if ((flags & 0x40) != 0)
                {
                    ca = g.I16(p) / 16384.0;
                    cd = g.I16(p + 2) / 16384.0;
                    p += 4;
                }
                else if ((flags & 0x80) != 0)
                {
                    ca = g.I16(p) / 16384.0;
                    cb = g.I16(p + 2) / 16384.0;
                    cc = g.I16(p + 4) / 16384.0;
                    cd = g.I16(p + 6) / 16384.0;
                    p += 8;
                }
                if ((flags & 2) == 0)
                {
                    ox = 0;
                    oy = 0; // point-matching offsets (rare) are not supported
                }
                // Combined transform: parent ∘ component.
                double na = (a * ca) + (c * cb), nb = (b * ca) + (d * cb);
                double nc = (a * cc) + (c * cd), nd = (b * cc) + (d * cd);
                double ndx = (a * ox) + (c * oy) + dx, ndy = (b * ox) + (d * oy) + dy;
                Append(sb, component, na, nb, nc, nd, ndx, ndy, depth + 1);
                if ((flags & 0x20) == 0)
                {
                    break;
                }
            }
        }
    }
}
