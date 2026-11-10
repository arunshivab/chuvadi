using System;
using System.Collections.Generic;
using Chuvadi.OpenType.Internal;

namespace Chuvadi.OpenType;

/// <summary>How a font supports a Unicode variation sequence (base character + variation selector).</summary>
public enum VariationSequenceKind
{
    /// <summary>The font's format 14 subtable does not list the sequence (or there is no format 14 subtable).</summary>
    NotListed,

    /// <summary>The sequence is listed as using the base character's default glyph.</summary>
    DefaultGlyph,

    /// <summary>The sequence maps to its own glyph.</summary>
    OwnGlyph,
}

/// <summary>
/// The font's Unicode character map: one subtable chosen from 'cmap' (see
/// <see cref="Selected"/>), plus the format 14 subtable for variation sequences.
/// </summary>
/// <remarks>
/// Choice of subtable: Unicode subtables (platform 0 except encoding 5, or
/// platform 3 with encoding 1 or 10) in a format this reader maps; among them a
/// 32-bit format (10, 12, 13) beats a 16-bit one, then platform 3 beats platform 0,
/// then the higher encoding ID, then the earlier record. Without a Unicode
/// subtable, (3, 0) Symbol is used with code points looked up as they are, then
/// (1, 0) Mac Roman with code points converted to Mac Roman bytes.
/// </remarks>
public sealed class CharacterMap
{
    private readonly CmapSubtable[] _subtables;
    private readonly CmapSubtable? _variation;
    private readonly bool _macRoman;

    // TRACE R-OT-0016
    internal CharacterMap(ReadOnlyMemory<byte> table, int glyphCount)
    {
        ReadOnlySpan<byte> s = table.Span;
        int count = BigEndian.U16(s, 2);
        List<CmapSubtable> list = new List<CmapSubtable>(count);
        for (int i = 0; i < count; i++)
        {
            int r = 4 + (8 * i);
            if (!BigEndian.Has(s, r, 8))
            {
                break;
            }

            int platform = BigEndian.U16(s, r);
            int encoding = BigEndian.U16(s, r + 2);
            int offset = BigEndian.Offset32(s, r + 4);
            if (offset < 0 || !BigEndian.Has(s, offset, 2))
            {
                continue;
            }

            CmapSubtable sub = new CmapSubtable(table, offset, platform, encoding, glyphCount);
            list.Add(sub);
        }

        _subtables = list.ToArray();

        CmapSubtable? best = null;
        int bestRank = int.MaxValue;
        for (int i = 0; i < _subtables.Length; i++)
        {
            CmapSubtable sub = _subtables[i];
            if (!sub.IsSupported)
            {
                continue;
            }

            bool unicode = (sub.PlatformId == 0 && sub.EncodingId != 5) || (sub.PlatformId == 3 && (sub.EncodingId == 1 || sub.EncodingId == 10));
            int rank;
            if (unicode)
            {
                rank = ((sub.Is32Bit ? 0 : 1) * 1_000_000) + ((sub.PlatformId == 3 ? 0 : 1) * 100_000) + ((99 - Math.Min(sub.EncodingId, 99)) * 1_000) + Math.Min(i, 999);
            }
            else if (sub.PlatformId == 3 && sub.EncodingId == 0)
            {
                rank = 10_000_000 + i;
            }
            else if (sub.PlatformId == 1 && sub.EncodingId == 0)
            {
                rank = 20_000_000 + (sub.Language == 0 ? 0 : 1_000) + i;
            }
            else
            {
                continue;
            }

            if (rank < bestRank)
            {
                bestRank = rank;
                best = sub;
            }
        }

        Selected = best;
        _macRoman = best is not null && best.PlatformId == 1;

        foreach (CmapSubtable sub in _subtables)
        {
            if (sub.Format == 14 && sub.PlatformId == 0 && sub.EncodingId == 5)
            {
                _variation = sub;
                break;
            }
        }
    }

    /// <summary>Gets every subtable of 'cmap' in record order.</summary>
    public IReadOnlyList<CmapSubtable> Subtables => _subtables;

    /// <summary>Gets the subtable used for Unicode lookups, or null when the font has none usable.</summary>
    public CmapSubtable? Selected { get; }

    /// <summary>Gets the format 14 (variation sequences) subtable, or null.</summary>
    public CmapSubtable? VariationSequences => _variation;

    /// <summary>Gets the glyph for a Unicode code point.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>The glyph index, or 0 when the font does not map it.</returns>
    public int GetGlyph(int codePoint)
    {
        if (Selected is null || codePoint < 0 || codePoint > 0x10FFFF)
        {
            return 0;
        }

        if (_macRoman)
        {
            int b = MacRoman.FromUnicode(codePoint);
            return b < 0 ? 0 : Selected.GetGlyph(b);
        }

        return Selected.GetGlyph(codePoint);
    }

    /// <summary>Returns whether the font maps a code point to a glyph other than .notdef.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <returns>True when the glyph is not 0.</returns>
    public bool HasGlyph(int codePoint) => GetGlyph(codePoint) != 0;

    /// <summary>True when the font has a Windows Symbol (3, 0) subtable.</summary>
    public bool HasSymbolSubtable
    {
        get
        {
            foreach (CmapSubtable s in _subtables)
            {
                if (s.PlatformId == 3 && s.EncodingId == 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Gets the glyph for a Unicode code point.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <param name="glyphId">The glyph index, or 0.</param>
    /// <returns>True when the font maps the code point to a non-zero glyph.</returns>
    public bool TryGetGlyph(int codePoint, out int glyphId)
    {
        glyphId = GetGlyph(codePoint);
        return glyphId != 0;
    }

    /// <summary>
    /// Gets the glyph for a variation sequence: the sequence's own glyph when the font lists one,
    /// otherwise the base character's default glyph.
    /// </summary>
    /// <param name="codePoint">The base code point.</param>
    /// <param name="variationSelector">The variation selector code point.</param>
    /// <returns>The glyph index, or 0 when even the base character is unmapped.</returns>
    public int GetGlyph(int codePoint, int variationSelector)
    {
        int own = LookupVariation(codePoint, variationSelector, out VariationSequenceKind kind);
        return kind == VariationSequenceKind.OwnGlyph ? own : GetGlyph(codePoint);
    }

    /// <summary>Gets how the font's format 14 subtable lists a variation sequence.</summary>
    /// <param name="codePoint">The base code point.</param>
    /// <param name="variationSelector">The variation selector code point.</param>
    /// <returns>The kind of support.</returns>
    public VariationSequenceKind GetVariationKind(int codePoint, int variationSelector)
    {
        _ = LookupVariation(codePoint, variationSelector, out VariationSequenceKind kind);
        return kind;
    }

    /// <summary>Enumerates (code point, glyph) pairs of the selected subtable with a non-zero glyph, in ascending order.</summary>
    /// <returns>The mappings.</returns>
    public IEnumerable<KeyValuePair<int, int>> EnumerateMappings()
    {
        if (Selected is null)
        {
            yield break;
        }

        if (!_macRoman)
        {
            foreach (KeyValuePair<int, int> kv in Selected.EnumerateMappings())
            {
                if (kv.Key <= 0x10FFFF)
                {
                    yield return kv;
                }
            }

            yield break;
        }

        SortedDictionary<int, int> sorted = new SortedDictionary<int, int>();
        foreach (KeyValuePair<int, int> kv in Selected.EnumerateMappings())
        {
            if (kv.Key <= 0xFF)
            {
                sorted[MacRoman.ToUnicode((byte)kv.Key)] = kv.Value;
            }
        }

        foreach (KeyValuePair<int, int> kv in sorted)
        {
            yield return kv;
        }
    }

    /// <summary>
    /// Enumerates every variation sequence the format 14 subtable lists, by selector then code point: default
    /// sequences (the base character's own glyph is used) and sequences with a glyph of their own.
    /// </summary>
    /// <returns>(base code point, selector, kind, glyph); for a default sequence the glyph is the base character's.</returns>
    public IEnumerable<(int CodePoint, int VariationSelector, VariationSequenceKind Kind, int GlyphId)> EnumerateVariationSequences()
    {
        // TRACE R-OT-0058
        if (_variation is null)
        {
            yield break;
        }

        ReadOnlyMemory<byte> table = _variation.Memory;
        int records = (int)Math.Min(BigEndian.U32(table.Span, 6), (uint)Math.Max(0, (table.Length - 10) / 11));
        for (int r = 0; r < records; r++)
        {
            int rec = 10 + (11 * r);
            int vs = BigEndian.U24(table.Span, rec);
            int defaultOffset = BigEndian.Offset32(table.Span, rec + 3);
            int nonDefaultOffset = BigEndian.Offset32(table.Span, rec + 7);
            if (defaultOffset > 0)
            {
                int ranges = (int)Math.Min(BigEndian.U32(table.Span, defaultOffset), (uint)Math.Max(0, (table.Length - defaultOffset - 4) / 4));
                for (int i = 0; i < ranges; i++)
                {
                    int start = BigEndian.U24(table.Span, defaultOffset + 4 + (4 * i));
                    int extra = BigEndian.U8(table.Span, defaultOffset + 4 + (4 * i) + 3);
                    for (int cp = start; cp <= start + extra && cp <= 0x10FFFF; cp++)
                    {
                        yield return (cp, vs, VariationSequenceKind.DefaultGlyph, GetGlyph(cp));
                    }
                }
            }

            if (nonDefaultOffset > 0)
            {
                int count = (int)Math.Min(BigEndian.U32(table.Span, nonDefaultOffset), (uint)Math.Max(0, (table.Length - nonDefaultOffset - 4) / 5));
                for (int i = 0; i < count; i++)
                {
                    int m = nonDefaultOffset + 4 + (5 * i);
                    yield return (BigEndian.U24(table.Span, m), vs, VariationSequenceKind.OwnGlyph, BigEndian.U16(table.Span, m + 3));
                }
            }
        }
    }

    // TRACE R-OT-0024
    private int LookupVariation(int codePoint, int selector, out VariationSequenceKind kind)
    {
        kind = VariationSequenceKind.NotListed;
        if (_variation is null)
        {
            return 0;
        }

        ReadOnlySpan<byte> s = _variation.Span;
        uint numRecords = BigEndian.U32(s, 6);
        int n = (int)Math.Min(numRecords, (uint)Math.Max(0, (s.Length - 10) / 11));
        int lo = 0;
        int hi = n - 1;
        int record = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            int vs = BigEndian.U24(s, 10 + (11 * mid));
            if (vs == selector)
            {
                record = 10 + (11 * mid);
                break;
            }

            if (vs < selector)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        if (record < 0)
        {
            return 0;
        }

        int defaultOffset = BigEndian.Offset32(s, record + 3);
        int nonDefaultOffset = BigEndian.Offset32(s, record + 7);

        if (nonDefaultOffset > 0)
        {
            uint count = BigEndian.U32(s, nonDefaultOffset);
            int m = (int)Math.Min(count, (uint)Math.Max(0, (s.Length - nonDefaultOffset - 4) / 5));
            int a = 0;
            int b = m - 1;
            while (a <= b)
            {
                int mid = (a + b) >>> 1;
                int p = nonDefaultOffset + 4 + (5 * mid);
                int u = BigEndian.U24(s, p);
                if (u == codePoint)
                {
                    kind = VariationSequenceKind.OwnGlyph;
                    return BigEndian.U16(s, p + 3);
                }

                if (u < codePoint)
                {
                    a = mid + 1;
                }
                else
                {
                    b = mid - 1;
                }
            }
        }

        if (defaultOffset > 0)
        {
            uint count = BigEndian.U32(s, defaultOffset);
            int m = (int)Math.Min(count, (uint)Math.Max(0, (s.Length - defaultOffset - 4) / 4));
            int a = 0;
            int b = m - 1;
            while (a <= b)
            {
                int mid = (a + b) >>> 1;
                int p = defaultOffset + 4 + (4 * mid);
                int start = BigEndian.U24(s, p);
                int extra = BigEndian.U8(s, p + 3);
                if (codePoint < start)
                {
                    b = mid - 1;
                }
                else if (codePoint > start + extra)
                {
                    a = mid + 1;
                }
                else
                {
                    kind = VariationSequenceKind.DefaultGlyph;
                    return 0;
                }
            }
        }

        return 0;
    }
}
