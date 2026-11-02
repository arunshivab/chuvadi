using System;
using System.Collections.Generic;

namespace Chuvadi.Layout.Fonts.Internal
{
    /// <summary>
    /// Pair kerning in font units. Uses the GPOS 'kern' feature (pair adjustment, lookup type 2,
    /// formats 1 and 2, including type-9 extension lookups) when the font has one, otherwise the
    /// legacy 'kern' table (Windows version 0, format 0 subtables, horizontal, additive).
    /// </summary>
    internal sealed class KerningTables
    {
        private const int MaxLookups = 2048;
        private const int MaxSubtables = 4096;

        private readonly Dictionary<uint, short>? _legacy;
        private readonly List<PairSubtable>[]? _gposLookups;

        private KerningTables(Dictionary<uint, short>? legacy, List<PairSubtable>[]? gposLookups)
        {
            _legacy = legacy;
            _gposLookups = gposLookups;
        }

        public static readonly KerningTables None = new KerningTables(null, null);

        public bool HasKerning => _legacy is not null || _gposLookups is not null;

        public static KerningTables Read(BigEndian? gpos, BigEndian? kern)
        {
            List<PairSubtable>[]? lookups = null;
            if (gpos is BigEndian g)
            {
                try
                {
                    lookups = ReadGpos(g);
                }
                catch (FontFormatException)
                {
                    lookups = null; // A broken GPOS degrades to legacy kerning, never fails layout.
                }
            }
            Dictionary<uint, short>? legacy = null;
            if (lookups is null && kern is BigEndian k)
            {
                try
                {
                    legacy = ReadLegacy(k);
                }
                catch (FontFormatException)
                {
                    legacy = null;
                }
            }
            return lookups is null && legacy is null ? None : new KerningTables(legacy, lookups);
        }

        public int Get(int left, int right)
        {
            if (_gposLookups is not null)
            {
                int total = 0;
                foreach (List<PairSubtable> lookup in _gposLookups)
                {
                    foreach (PairSubtable sub in lookup)
                    {
                        int v;
                        bool hit;
                        try
                        {
                            hit = sub.TryGet(left, right, out v);
                        }
                        catch (FontFormatException)
                        {
                            // A damaged subtable is evaluated lazily; treat it as "no adjustment".
                            hit = false;
                            v = 0;
                        }
                        if (hit)
                        {
                            total += v;
                            break;
                        }
                    }
                }
                return total;
            }
            if (_legacy is not null && _legacy.TryGetValue(((uint)left << 16) | (uint)right, out short value))
            {
                return value;
            }
            return 0;
        }

        private static Dictionary<uint, short>? ReadLegacy(BigEndian k)
        {
            if (k.U16(0) != 0)
            {
                return null; // Apple 'kern' (version 1.0) is not used by Office fonts.
            }
            int nTables = k.U16(2);
            Dictionary<uint, short> pairs = new Dictionary<uint, short>();
            long off = 4;
            for (int i = 0; i < nTables && k.Has(off, 6); i++)
            {
                int length = k.U16(off + 2);
                int coverage = k.U16(off + 4);
                int format = coverage >> 8;
                bool horizontal = (coverage & 1) != 0;
                bool minimum = (coverage & 2) != 0;
                bool crossStream = (coverage & 4) != 0;
                bool overrideValues = (coverage & 8) != 0;
                if (format == 0 && horizontal && !minimum && !crossStream)
                {
                    int n = k.U16(off + 6);
                    for (int p = 0; p < n; p++)
                    {
                        long rec = off + 14 + (6L * p);
                        if (!k.Has(rec, 6))
                        {
                            break;
                        }
                        uint key = ((uint)k.U16(rec) << 16) | k.U16(rec + 2);
                        short v = k.I16(rec + 4);
                        if (overrideValues || !pairs.TryGetValue(key, out short existing))
                        {
                            pairs[key] = v;
                        }
                        else
                        {
                            pairs[key] = (short)Math.Clamp(existing + v, short.MinValue, short.MaxValue);
                        }
                    }
                }
                if (length < 6)
                {
                    break;
                }
                off += length;
            }
            return pairs.Count == 0 ? null : pairs;
        }

        private static List<PairSubtable>[]? ReadGpos(BigEndian g)
        {
            if (g.U16(0) != 1)
            {
                return null;
            }
            int scriptList = g.U16(4);
            int featureList = g.U16(6);
            int lookupList = g.U16(8);

            // Feature indices of 'kern' under DFLT, then latn, then the first script; default LangSys.
            List<int> featureIndices = FindKernFeatures(g, scriptList, featureList);
            if (featureIndices.Count == 0)
            {
                return null;
            }
            SortedSet<int> lookupIndices = new SortedSet<int>();
            foreach (int fi in featureIndices)
            {
                long rec = featureList + 2 + (6L * fi);
                int featureOffset = featureList + g.U16(rec + 4);
                int n = g.U16(featureOffset + 2);
                for (int i = 0; i < n; i++)
                {
                    lookupIndices.Add(g.U16(featureOffset + 4 + (2L * i)));
                }
            }

            int lookupCount = g.U16(lookupList);
            List<List<PairSubtable>> result = new List<List<PairSubtable>>();
            int subtablesSeen = 0;
            foreach (int li in lookupIndices)
            {
                if (li >= lookupCount || result.Count >= MaxLookups)
                {
                    continue;
                }
                long lookup = lookupList + g.U16(lookupList + 2 + (2L * li));
                int type = g.U16(lookup);
                int subCount = g.U16(lookup + 4);
                List<PairSubtable> subs = new List<PairSubtable>();
                for (int s = 0; s < subCount && subtablesSeen < MaxSubtables; s++, subtablesSeen++)
                {
                    long sub = lookup + g.U16(lookup + 6 + (2L * s));
                    int subType = type;
                    if (type == 9)
                    {
                        subType = g.U16(sub + 2);
                        sub += g.U32(sub + 4);
                    }
                    if (subType != 2)
                    {
                        continue;
                    }
                    PairSubtable? p = PairSubtable.Read(g, sub);
                    if (p is not null)
                    {
                        subs.Add(p);
                    }
                }
                if (subs.Count > 0)
                {
                    result.Add(subs);
                }
            }
            return result.Count == 0 ? null : result.ToArray();
        }

        private static List<int> FindKernFeatures(BigEndian g, int scriptList, int featureList)
        {
            List<int> found = new List<int>();
            int scriptCount = g.U16(scriptList);
            long chosen = -1;
            string[] wanted = { "DFLT", "latn" };
            foreach (string w in wanted)
            {
                for (int i = 0; i < scriptCount && chosen < 0; i++)
                {
                    long rec = scriptList + 2 + (6L * i);
                    if (g.Tag(rec) == w)
                    {
                        chosen = scriptList + g.U16(rec + 4);
                    }
                }
                if (chosen >= 0)
                {
                    break;
                }
            }
            if (chosen < 0 && scriptCount > 0)
            {
                chosen = scriptList + g.U16(scriptList + 2 + 4);
            }
            if (chosen < 0)
            {
                return found;
            }
            int defaultLangSys = g.U16(chosen);
            long langSys;
            if (defaultLangSys != 0)
            {
                langSys = chosen + defaultLangSys;
            }
            else if (g.U16(chosen + 2) > 0)
            {
                langSys = chosen + g.U16(chosen + 2 + 4);
            }
            else
            {
                return found;
            }
            int featureCount = g.U16(featureList);
            int required = g.U16(langSys + 2);
            int n = g.U16(langSys + 4);
            List<int> candidates = new List<int>();
            if (required != 0xFFFF)
            {
                candidates.Add(required);
            }
            for (int i = 0; i < n; i++)
            {
                candidates.Add(g.U16(langSys + 6 + (2L * i)));
            }
            foreach (int fi in candidates)
            {
                if (fi < featureCount && g.Tag(featureList + 2 + (6L * fi)) == "kern")
                {
                    found.Add(fi);
                }
            }
            return found;
        }

        private sealed class PairSubtable
        {
            private readonly BigEndian _g;
            private readonly long _offset;
            private readonly int _format;
            private readonly Coverage _coverage;
            private readonly int _valueFormat1;
            private readonly int _valueFormat2;
            private readonly ClassDef? _class1;
            private readonly ClassDef? _class2;
            private readonly int _class2Count;

            private PairSubtable(BigEndian g, long offset, int format, Coverage coverage, int vf1, int vf2,
                ClassDef? c1, ClassDef? c2, int class2Count)
            {
                _g = g;
                _offset = offset;
                _format = format;
                _coverage = coverage;
                _valueFormat1 = vf1;
                _valueFormat2 = vf2;
                _class1 = c1;
                _class2 = c2;
                _class2Count = class2Count;
            }

            public static PairSubtable? Read(BigEndian g, long off)
            {
                int format = g.U16(off);
                Coverage cov = new Coverage(g, off + g.U16(off + 2));
                int vf1 = g.U16(off + 4);
                int vf2 = g.U16(off + 6);
                if (format == 1)
                {
                    return new PairSubtable(g, off, 1, cov, vf1, vf2, null, null, 0);
                }
                if (format == 2)
                {
                    ClassDef c1 = new ClassDef(g, off + g.U16(off + 8));
                    ClassDef c2 = new ClassDef(g, off + g.U16(off + 10));
                    int class2Count = g.U16(off + 14);
                    return new PairSubtable(g, off, 2, cov, vf1, vf2, c1, c2, class2Count);
                }
                return null;
            }

            private static int ValueSize(int format)
            {
                int bits = 0;
                for (int f = format & 0xFF; f != 0; f >>= 1)
                {
                    bits += f & 1;
                }
                return bits * 2;
            }

            /// <summary>XAdvance of the first glyph's value record (horizontal kerning).</summary>
            private int XAdvance(long valueRecord, int format)
            {
                if ((format & 0x0004) == 0)
                {
                    return 0;
                }
                int skip = 0;
                if ((format & 0x0001) != 0)
                {
                    skip += 2;
                }
                if ((format & 0x0002) != 0)
                {
                    skip += 2;
                }
                return _g.I16(valueRecord + skip);
            }

            public bool TryGet(int left, int right, out int value)
            {
                value = 0;
                int ci = _coverage.IndexOf(left);
                if (ci < 0)
                {
                    return false;
                }
                int size1 = ValueSize(_valueFormat1);
                int size2 = ValueSize(_valueFormat2);
                if (_format == 1)
                {
                    int setCount = _g.U16(_offset + 8);
                    if (ci >= setCount)
                    {
                        return false;
                    }
                    long set = _offset + _g.U16(_offset + 10 + (2L * ci));
                    int n = _g.U16(set);
                    int recordSize = 2 + size1 + size2;
                    int lo = 0;
                    int hi = n - 1;
                    while (lo <= hi)
                    {
                        int mid = (lo + hi) >> 1;
                        long rec = set + 2 + ((long)recordSize * mid);
                        int second = _g.U16(rec);
                        if (second < right)
                        {
                            lo = mid + 1;
                        }
                        else if (second > right)
                        {
                            hi = mid - 1;
                        }
                        else
                        {
                            value = XAdvance(rec + 2, _valueFormat1);
                            return true;
                        }
                    }
                    return false;
                }

                int c1 = _class1!.ClassOf(left);
                int c2 = _class2!.ClassOf(right);
                int class1Count = _g.U16(_offset + 12);
                if (c1 >= class1Count || c2 >= _class2Count)
                {
                    return true;
                }
                long record = _offset + 16 + (((long)c1 * _class2Count) + c2) * (size1 + size2);
                value = XAdvance(record, _valueFormat1);
                return true;
            }
        }

        private sealed class Coverage
        {
            private readonly BigEndian _g;
            private readonly long _off;
            private readonly int _format;
            private readonly int _count;

            public Coverage(BigEndian g, long off)
            {
                _g = g;
                _off = off;
                _format = g.U16(off);
                _count = g.U16(off + 2);
                if (_format != 1 && _format != 2)
                {
                    throw new FontFormatException($"Unknown coverage format {_format}.");
                }
            }

            public int IndexOf(int glyph)
            {
                int lo = 0;
                int hi = _count - 1;
                if (_format == 1)
                {
                    while (lo <= hi)
                    {
                        int mid = (lo + hi) >> 1;
                        int g = _g.U16(_off + 4 + (2L * mid));
                        if (g < glyph)
                        {
                            lo = mid + 1;
                        }
                        else if (g > glyph)
                        {
                            hi = mid - 1;
                        }
                        else
                        {
                            return mid;
                        }
                    }
                    return -1;
                }
                while (lo <= hi)
                {
                    int mid = (lo + hi) >> 1;
                    long rec = _off + 4 + (6L * mid);
                    int start = _g.U16(rec);
                    int end = _g.U16(rec + 2);
                    if (glyph < start)
                    {
                        hi = mid - 1;
                    }
                    else if (glyph > end)
                    {
                        lo = mid + 1;
                    }
                    else
                    {
                        return _g.U16(rec + 4) + (glyph - start);
                    }
                }
                return -1;
            }
        }

        private sealed class ClassDef
        {
            private readonly BigEndian _g;
            private readonly long _off;
            private readonly int _format;

            public ClassDef(BigEndian g, long off)
            {
                _g = g;
                _off = off;
                _format = g.U16(off);
            }

            public int ClassOf(int glyph)
            {
                if (_format == 1)
                {
                    int start = _g.U16(_off + 2);
                    int n = _g.U16(_off + 4);
                    int i = glyph - start;
                    return i >= 0 && i < n ? _g.U16(_off + 6 + (2L * i)) : 0;
                }
                if (_format == 2)
                {
                    int n = _g.U16(_off + 2);
                    int lo = 0;
                    int hi = n - 1;
                    while (lo <= hi)
                    {
                        int mid = (lo + hi) >> 1;
                        long rec = _off + 4 + (6L * mid);
                        int start = _g.U16(rec);
                        int end = _g.U16(rec + 2);
                        if (glyph < start)
                        {
                            hi = mid - 1;
                        }
                        else if (glyph > end)
                        {
                            lo = mid + 1;
                        }
                        else
                        {
                            return _g.U16(rec + 4);
                        }
                    }
                }
                return 0;
            }
        }
    }
}
