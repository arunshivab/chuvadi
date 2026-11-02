using System;
using System.Collections.Generic;

namespace Chuvadi.Layout.Fonts.Internal
{
    /// <summary>
    /// Character-to-glyph mapping from the 'cmap' table. Supports formats 0, 4, 6, 10, 12 and 13;
    /// picks the best Unicode subtable, falling back to the Windows Symbol subtable (where
    /// U+0020..U+00FF are also looked up at U+F020..U+F0FF, as Windows does).
    /// </summary>
    internal sealed class CmapTable
    {
        private readonly ISubtable _map;
        private readonly bool _symbol;

        private CmapTable(ISubtable map, bool symbol)
        {
            _map = map;
            _symbol = symbol;
        }

        public bool IsSymbol => _symbol;

        public static CmapTable Read(BigEndian t)
        {
            int count = t.U16(2);
            // Candidates, best first: (platform, encoding).
            (int Platform, int Encoding)[] preference =
            {
                (3, 10), (0, 6), (0, 4), (3, 1), (0, 3), (0, 2), (0, 1), (0, 0), (3, 0), (1, 0),
            };
            Dictionary<(int, int), long> offsets = new Dictionary<(int, int), long>();
            for (int i = 0; i < count; i++)
            {
                long rec = 4 + (8L * i);
                if (!t.Has(rec, 8))
                {
                    break;
                }
                int p = t.U16(rec);
                int e = t.U16(rec + 2);
                long off = t.U32(rec + 4);
                if (!offsets.ContainsKey((p, e)))
                {
                    offsets[(p, e)] = off;
                }
            }
            foreach ((int p, int e) in preference)
            {
                if (offsets.TryGetValue((p, e), out long off) && t.Has(off, 2))
                {
                    ISubtable? sub = ReadSubtable(t, off);
                    if (sub is not null)
                    {
                        return new CmapTable(sub, p == 3 && e == 0);
                    }
                }
            }
            return new CmapTable(new EmptySubtable(), false);
        }

        public int GetGlyph(int codepoint)
        {
            if (codepoint < 0 || codepoint > 0x10FFFF)
            {
                return 0;
            }
            int g = _map.Get(codepoint);
            if (g == 0 && _symbol && codepoint >= 0x20 && codepoint <= 0xFF)
            {
                g = _map.Get(0xF000 + codepoint);
            }
            return g;
        }

        private static ISubtable? ReadSubtable(BigEndian t, long off)
        {
            int format = t.U16(off);
            switch (format)
            {
                case 0:
                    return new Format0(t, off);
                case 4:
                    return new Format4(t, off);
                case 6:
                    return new Format6(t, off);
                case 10:
                    return new Format10(t, off);
                case 12:
                case 13:
                    return new Format12(t, off, format == 13);
                default:
                    return null;
            }
        }

        private interface ISubtable
        {
            int Get(int cp);
        }

        private sealed class EmptySubtable : ISubtable
        {
            public int Get(int cp) => 0;
        }

        private sealed class Format0 : ISubtable
        {
            private readonly byte[] _glyphs = new byte[256];

            public Format0(BigEndian t, long off)
            {
                t.Bytes(off + 6, 256).CopyTo(_glyphs);
            }

            public int Get(int cp) => cp < 256 ? _glyphs[cp] : 0;
        }

        private sealed class Format6 : ISubtable
        {
            private readonly int _first;
            private readonly ushort[] _glyphs;

            public Format6(BigEndian t, long off)
            {
                _first = t.U16(off + 6);
                int n = t.U16(off + 8);
                _glyphs = new ushort[n];
                for (int i = 0; i < n; i++)
                {
                    _glyphs[i] = t.U16(off + 10 + (2L * i));
                }
            }

            public int Get(int cp)
            {
                int i = cp - _first;
                return i >= 0 && i < _glyphs.Length ? _glyphs[i] : 0;
            }
        }

        private sealed class Format10 : ISubtable
        {
            private readonly long _first;
            private readonly ushort[] _glyphs;

            public Format10(BigEndian t, long off)
            {
                _first = t.U32(off + 12);
                uint n = t.U32(off + 16);
                if (n > 0x110000)
                {
                    throw new FontFormatException("cmap format 10 declares too many characters.");
                }
                _glyphs = new ushort[n];
                for (int i = 0; i < n; i++)
                {
                    _glyphs[i] = t.U16(off + 20 + (2L * i));
                }
            }

            public int Get(int cp)
            {
                long i = cp - _first;
                return i >= 0 && i < _glyphs.Length ? _glyphs[i] : 0;
            }
        }

        private sealed class Format4 : ISubtable
        {
            private readonly ushort[] _end;
            private readonly ushort[] _start;
            private readonly short[] _delta;
            private readonly ushort[] _rangeOffset;
            private readonly BigEndian _t;
            private readonly long _rangeOffsetBase;

            public Format4(BigEndian t, long off)
            {
                _t = t;
                int segX2 = t.U16(off + 6);
                int seg = segX2 / 2;
                _end = new ushort[seg];
                _start = new ushort[seg];
                _delta = new short[seg];
                _rangeOffset = new ushort[seg];
                long endBase = off + 14;
                long startBase = endBase + segX2 + 2;
                long deltaBase = startBase + segX2;
                _rangeOffsetBase = deltaBase + segX2;
                for (int i = 0; i < seg; i++)
                {
                    _end[i] = t.U16(endBase + (2L * i));
                    _start[i] = t.U16(startBase + (2L * i));
                    _delta[i] = t.I16(deltaBase + (2L * i));
                    _rangeOffset[i] = t.U16(_rangeOffsetBase + (2L * i));
                }
            }

            public int Get(int cp)
            {
                if (cp > 0xFFFF)
                {
                    return 0;
                }
                int lo = 0;
                int hi = _end.Length - 1;
                while (lo < hi)
                {
                    int mid = (lo + hi) >> 1;
                    if (_end[mid] < cp)
                    {
                        lo = mid + 1;
                    }
                    else
                    {
                        hi = mid;
                    }
                }
                if (lo >= _end.Length || cp > _end[lo] || cp < _start[lo])
                {
                    return 0;
                }
                if (_rangeOffset[lo] == 0)
                {
                    return (cp + _delta[lo]) & 0xFFFF;
                }
                long addr = _rangeOffsetBase + (2L * lo) + _rangeOffset[lo] + (2L * (cp - _start[lo]));
                if (!_t.Has(addr, 2))
                {
                    return 0;
                }
                int g = _t.U16(addr);
                return g == 0 ? 0 : (g + _delta[lo]) & 0xFFFF;
            }
        }

        private sealed class Format12 : ISubtable
        {
            private readonly uint[] _startChar;
            private readonly uint[] _endChar;
            private readonly uint[] _startGlyph;
            private readonly bool _manyToOne;

            public Format12(BigEndian t, long off, bool manyToOne)
            {
                _manyToOne = manyToOne;
                uint n = t.U32(off + 12);
                if (n > 0x110000 || !t.Has(off + 16, 12L * n))
                {
                    throw new FontFormatException("cmap format 12/13 group count exceeds the table.");
                }
                _startChar = new uint[n];
                _endChar = new uint[n];
                _startGlyph = new uint[n];
                for (int i = 0; i < n; i++)
                {
                    long g = off + 16 + (12L * i);
                    _startChar[i] = t.U32(g);
                    _endChar[i] = t.U32(g + 4);
                    _startGlyph[i] = t.U32(g + 8);
                }
            }

            public int Get(int cp)
            {
                uint c = (uint)cp;
                int lo = 0;
                int hi = _endChar.Length - 1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) >> 1;
                    if (c < _startChar[mid])
                    {
                        hi = mid - 1;
                    }
                    else if (c > _endChar[mid])
                    {
                        lo = mid + 1;
                    }
                    else
                    {
                        uint g = _manyToOne ? _startGlyph[mid] : _startGlyph[mid] + (c - _startChar[mid]);
                        return g > 0xFFFF ? 0 : (int)g;
                    }
                }
                return 0;
            }
        }
    }
}
