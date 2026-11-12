using System.Threading;
using Chuvadi.OpenType.Layout;

namespace Chuvadi.Text.Shaping.Engine;

// TRACE R-TS-0042
/// <summary>
/// Per-lookup glyph sets for one layout table (D-319 E11): whether a lookup can start at a glyph, that is whether any of
/// its subtables covers the glyph with the coverage the current glyph is tested against. Filled lazily, 256 glyphs per
/// page, and shared by every thread shaping with the font; a lookup is skipped at a glyph it cannot start at, which
/// changes no result.
/// </summary>
internal sealed class LookupDigest
{
    private const int PageShift = 8;
    private const int WordsPerHalf = 4;

    private readonly bool _positioning;
    private readonly int _pageCount;
    private readonly ulong[]?[]?[] _pages;

    internal LookupDigest(LayoutTable table, int glyphCount, bool positioning)
    {
        _positioning = positioning;
        _pageCount = (glyphCount + (1 << PageShift) - 1) >> PageShift;
        _pages = new ulong[]?[]?[table.Lookups.Count];
    }

    /// <summary>True when the lookup can start at the glyph (always true for a glyph id outside the font).</summary>
    internal bool MayStart(int lookupIndex, Lookup lookup, int glyph)
    {
        int page = glyph >> PageShift;
        if ((uint)lookupIndex >= (uint)_pages.Length || (uint)page >= (uint)_pageCount)
        {
            return true;
        }

        ulong[]?[] pages = _pages[lookupIndex] ?? Interlocked.CompareExchange(ref _pages[lookupIndex], new ulong[]?[_pageCount], null) ?? _pages[lookupIndex]!;
        ulong[] bits = pages[page] ?? Interlocked.CompareExchange(ref pages[page], new ulong[2 * WordsPerHalf], null) ?? pages[page]!;
        int inPage = glyph & ((1 << PageShift) - 1);
        int word = inPage >> 6;
        ulong bit = 1UL << (inPage & 63);
        if ((Volatile.Read(ref bits[word]) & bit) != 0)
        {
            return (Volatile.Read(ref bits[WordsPerHalf + word]) & bit) != 0;
        }

        bool covered = Covers(lookup, glyph);
        if (covered)
        {
            Interlocked.Or(ref bits[WordsPerHalf + word], bit);
        }

        Interlocked.Or(ref bits[word], bit);
        return covered;
    }

    private bool Covers(Lookup lookup, int glyph)
    {
        for (int s = 0; s < lookup.SubtableCount; s++)
        {
            LookupSubtable st = lookup.GetSubtable(s);
            if (StartCoverage(st).GetIndex(glyph) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private Coverage StartCoverage(LookupSubtable st)
    {
        if (_positioning)
        {
            return st.LookupType switch
            {
                1 => st.AsSinglePositioning().Coverage,
                2 => st.AsPairPositioning().Coverage,
                3 => st.AsCursivePositioning().Coverage,
                4 or 5 or 6 => st.AsMarkAttachment().MarkCoverage,
                7 => st.AsContext().Coverage,
                8 => st.AsChainedContext().Coverage,
                _ => default,
            };
        }

        return st.LookupType switch
        {
            1 => st.AsSingleSubstitution().Coverage,
            2 => st.AsMultipleSubstitution().Coverage,
            3 => st.AsAlternateSubstitution().Coverage,
            4 => st.AsLigatureSubstitution().Coverage,
            5 => st.AsContext().Coverage,
            6 => st.AsChainedContext().Coverage,
            8 => st.AsReverseChainSingleSubstitution().Coverage,
            _ => default,
        };
    }
}
