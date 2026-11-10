using System;
using Chuvadi.OpenType.Internal;

namespace Chuvadi.OpenType.Layout;

/// <summary>GSUB lookup type 1: replace one glyph with one glyph.</summary>
public readonly struct SingleSubstitution
{
    private readonly ReadOnlyMemory<byte> _t;
    private readonly int _o;

    internal SingleSubstitution(ReadOnlyMemory<byte> table, int offset)
    {
        _t = table;
        _o = offset;
    }

    /// <summary>True when this is a type 1 subtable.</summary>
    public bool Exists => _o > 0;

    /// <summary>The format, 1 (delta) or 2 (array).</summary>
    public int Format => Exists ? BigEndian.U16(_t.Span, _o) : 0;

    /// <summary>The input glyphs.</summary>
    public Coverage Coverage => Exists ? new Coverage(_t, _o + BigEndian.U16(_t.Span, _o + 2)) : default;

    /// <summary>The substitute for <paramref name="glyphId"/>, when it is covered.</summary>
    public bool TryGetSubstitute(int glyphId, out int substitute)
    {
        // TRACE R-OT-0047
        substitute = 0;
        int index = Coverage.GetIndex(glyphId);
        if (index < 0)
        {
            return false;
        }

        ReadOnlySpan<byte> s = _t.Span;
        if (Format == 1)
        {
            substitute = (glyphId + BigEndian.I16(s, _o + 4)) & 0xFFFF;
            return true;
        }

        if (Format == 2 && index < BigEndian.U16(s, _o + 4))
        {
            substitute = BigEndian.U16(s, _o + 6 + (index * 2));
            return true;
        }

        return false;
    }
}

/// <summary>GSUB lookup type 2: replace one glyph with a sequence of glyphs.</summary>
public readonly struct MultipleSubstitution
{
    private readonly ReadOnlyMemory<byte> _t;
    private readonly int _o;

    internal MultipleSubstitution(ReadOnlyMemory<byte> table, int offset)
    {
        _t = table;
        _o = offset;
    }

    /// <summary>True when this is a type 2 subtable of format 1.</summary>
    public bool Exists => _o > 0 && BigEndian.U16(_t.Span, _o) == 1;

    /// <summary>The input glyphs.</summary>
    public Coverage Coverage => Exists ? new Coverage(_t, _o + BigEndian.U16(_t.Span, _o + 2)) : default;

    /// <summary>The replacement sequence for <paramref name="glyphId"/>, when it is covered.</summary>
    public bool TryGetSequence(int glyphId, out UInt16List sequence)
    {
        // TRACE R-OT-0047
        sequence = default;
        int index = Coverage.GetIndex(glyphId);
        ReadOnlySpan<byte> s = _t.Span;
        if (index < 0 || index >= BigEndian.U16(s, _o + 4))
        {
            return false;
        }

        int seq = _o + BigEndian.U16(s, _o + 6 + (index * 2));
        sequence = new UInt16List(_t, seq + 2, BigEndian.U16(s, seq));
        return true;
    }
}

/// <summary>GSUB lookup type 3: replace one glyph with one of several alternates.</summary>
public readonly struct AlternateSubstitution
{
    private readonly ReadOnlyMemory<byte> _t;
    private readonly int _o;

    internal AlternateSubstitution(ReadOnlyMemory<byte> table, int offset)
    {
        _t = table;
        _o = offset;
    }

    /// <summary>True when this is a type 3 subtable of format 1.</summary>
    public bool Exists => _o > 0 && BigEndian.U16(_t.Span, _o) == 1;

    /// <summary>The input glyphs.</summary>
    public Coverage Coverage => Exists ? new Coverage(_t, _o + BigEndian.U16(_t.Span, _o + 2)) : default;

    /// <summary>The alternates for <paramref name="glyphId"/>, when it is covered.</summary>
    public bool TryGetAlternates(int glyphId, out UInt16List alternates)
    {
        // TRACE R-OT-0047
        alternates = default;
        int index = Coverage.GetIndex(glyphId);
        ReadOnlySpan<byte> s = _t.Span;
        if (index < 0 || index >= BigEndian.U16(s, _o + 4))
        {
            return false;
        }

        int set = _o + BigEndian.U16(s, _o + 6 + (index * 2));
        alternates = new UInt16List(_t, set + 2, BigEndian.U16(s, set));
        return true;
    }
}

/// <summary>GSUB lookup type 4: replace a sequence of glyphs with a ligature glyph.</summary>
public readonly struct LigatureSubstitution
{
    private readonly ReadOnlyMemory<byte> _t;
    private readonly int _o;

    internal LigatureSubstitution(ReadOnlyMemory<byte> table, int offset)
    {
        _t = table;
        _o = offset;
    }

    /// <summary>True when this is a type 4 subtable of format 1.</summary>
    public bool Exists => _o > 0 && BigEndian.U16(_t.Span, _o) == 1;

    /// <summary>The first-component glyphs.</summary>
    public Coverage Coverage => Exists ? new Coverage(_t, _o + BigEndian.U16(_t.Span, _o + 2)) : default;

    /// <summary>The number of ligatures starting with <paramref name="firstGlyphId"/>, in preference order.</summary>
    public int GetLigatureCount(int firstGlyphId)
    {
        int set = LigatureSet(firstGlyphId);
        return set > 0 ? BigEndian.U16(_t.Span, set) : 0;
    }

    /// <summary>Ligature <paramref name="index"/> of those starting with <paramref name="firstGlyphId"/>.</summary>
    public Ligature GetLigature(int firstGlyphId, int index)
    {
        // TRACE R-OT-0047
        int set = LigatureSet(firstGlyphId);
        if (set <= 0 || (uint)index >= (uint)BigEndian.U16(_t.Span, set))
        {
            return default;
        }

        return new Ligature(_t, set + BigEndian.U16(_t.Span, set + 2 + (index * 2)));
    }

    private int LigatureSet(int glyphId)
    {
        int index = Coverage.GetIndex(glyphId);
        ReadOnlySpan<byte> s = _t.Span;
        if (index < 0 || index >= BigEndian.U16(s, _o + 4))
        {
            return 0;
        }

        return _o + BigEndian.U16(s, _o + 6 + (index * 2));
    }
}

/// <summary>One ligature: the ligature glyph and the components after the first.</summary>
public readonly struct Ligature
{
    private readonly ReadOnlyMemory<byte> _t;
    private readonly int _o;

    internal Ligature(ReadOnlyMemory<byte> table, int offset)
    {
        _t = table;
        _o = offset;
    }

    /// <summary>True when the ligature table exists.</summary>
    public bool Exists => _o > 0 && _o < _t.Length;

    /// <summary>The ligature glyph.</summary>
    public int LigatureGlyph => Exists ? BigEndian.U16(_t.Span, _o) : 0;

    /// <summary>The number of components, including the first.</summary>
    public int ComponentCount => Exists ? BigEndian.U16(_t.Span, _o + 2) : 0;

    /// <summary>The components after the first (ComponentCount - 1 of them).</summary>
    public UInt16List FollowingComponents => new UInt16List(_t, _o + 4, ComponentCount - 1);
}

/// <summary>GSUB lookup type 8: reverse chaining contextual single substitution.</summary>
public readonly struct ReverseChainSingleSubstitution
{
    private readonly ReadOnlyMemory<byte> _t;
    private readonly int _o;

    internal ReverseChainSingleSubstitution(ReadOnlyMemory<byte> table, int offset)
    {
        _t = table;
        _o = offset;
    }

    /// <summary>True when this is a type 8 subtable of format 1.</summary>
    public bool Exists => _o > 0 && BigEndian.U16(_t.Span, _o) == 1;

    /// <summary>The input glyphs.</summary>
    public Coverage Coverage => Exists ? new Coverage(_t, _o + BigEndian.U16(_t.Span, _o + 2)) : default;

    /// <summary>The number of backtrack coverages.</summary>
    public int BacktrackCount => Exists ? BigEndian.U16(_t.Span, _o + 4) : 0;

    /// <summary>Backtrack coverage <paramref name="index"/> (0 is the glyph nearest the input).</summary>
    public Coverage GetBacktrack(int index) =>
        (uint)index < (uint)BacktrackCount ? new Coverage(_t, _o + BigEndian.U16(_t.Span, _o + 6 + (index * 2))) : default;

    private int LookaheadAt => _o + 6 + (BacktrackCount * 2);

    /// <summary>The number of lookahead coverages.</summary>
    public int LookaheadCount => Exists ? BigEndian.U16(_t.Span, LookaheadAt) : 0;

    /// <summary>Lookahead coverage <paramref name="index"/>.</summary>
    public Coverage GetLookahead(int index) =>
        (uint)index < (uint)LookaheadCount ? new Coverage(_t, _o + BigEndian.U16(_t.Span, LookaheadAt + 2 + (index * 2))) : default;

    /// <summary>The substitute glyphs, by coverage index.</summary>
    public UInt16List Substitutes
    {
        get
        {
            if (!Exists)
            {
                return default;
            }

            int at = LookaheadAt + 2 + (LookaheadCount * 2);
            return new UInt16List(_t, at + 2, BigEndian.U16(_t.Span, at));
        }
    }
}
