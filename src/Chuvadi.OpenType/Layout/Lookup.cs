using System;
using Chuvadi.OpenType.Internal;

namespace Chuvadi.OpenType.Layout;

/// <summary>Lookup flags (OpenType common table formats, LookupFlag bit enumeration).</summary>
[Flags]
public enum LookupFlags
{
    /// <summary>No flags.</summary>
    None = 0,

    /// <summary>Right-to-left cursive attachment (GPOS type 3).</summary>
    RightToLeft = 0x0001,

    /// <summary>Skip base glyphs.</summary>
    IgnoreBaseGlyphs = 0x0002,

    /// <summary>Skip ligatures.</summary>
    IgnoreLigatures = 0x0004,

    /// <summary>Skip all combining marks.</summary>
    IgnoreMarks = 0x0008,

    /// <summary>Skip marks not in the mark filtering set named by the lookup.</summary>
    UseMarkFilteringSet = 0x0010,

    /// <summary>Mask for the mark attachment class filter in the high byte.</summary>
    MarkAttachmentClassMask = 0xFF00,
}

/// <summary>The lookup list of a layout table.</summary>
public readonly struct LookupList
{
    private readonly LayoutTableKind _kind;
    private readonly ReadOnlyMemory<byte> _t;
    private readonly int _o;

    internal LookupList(LayoutTableKind kind, ReadOnlyMemory<byte> table, int offset)
    {
        _kind = kind;
        _t = table;
        _o = offset;
    }

    /// <summary>The number of lookups.</summary>
    public int Count => _o > 0 ? BigEndian.U16(_t.Span, _o) : 0;

    /// <summary>Lookup <paramref name="index"/>; <see cref="Lookup.Exists"/> is false outside the list.</summary>
    public Lookup GetLookup(int index) =>
        (uint)index < (uint)Count ? new Lookup(_kind, _t, _o + BigEndian.U16(_t.Span, _o + 2 + (index * 2))) : default;
}

/// <summary>A lookup table: a type, flags and subtables.</summary>
public readonly struct Lookup
{
    private readonly LayoutTableKind _kind;
    private readonly ReadOnlyMemory<byte> _t;
    private readonly int _o;

    internal Lookup(LayoutTableKind kind, ReadOnlyMemory<byte> table, int offset)
    {
        _kind = kind;
        _t = table;
        _o = offset;
    }

    /// <summary>True when the lookup table exists.</summary>
    public bool Exists => _o > 0 && _o < _t.Length;

    /// <summary>GSUB or GPOS.</summary>
    public LayoutTableKind Kind => _kind;

    /// <summary>The lookup type as stored (an Extension lookup keeps type 7 in GSUB or 9 in GPOS here).</summary>
    public int LookupType => Exists ? BigEndian.U16(_t.Span, _o) : 0;

    /// <summary>The lookup flags.</summary>
    public LookupFlags Flags => Exists ? (LookupFlags)BigEndian.U16(_t.Span, _o + 2) : LookupFlags.None;

    /// <summary>The mark attachment class from the flags' high byte (0 = no filter).</summary>
    public int MarkAttachmentClass => ((int)Flags >> 8) & 0xFF;

    /// <summary>The number of subtables.</summary>
    public int SubtableCount => Exists ? BigEndian.U16(_t.Span, _o + 4) : 0;

    /// <summary>The mark filtering set index, or -1 when the UseMarkFilteringSet flag is clear.</summary>
    public int MarkFilteringSet =>
        (Flags & LookupFlags.UseMarkFilteringSet) != 0 ? BigEndian.U16(_t.Span, _o + 6 + (SubtableCount * 2)) : -1;

    /// <summary>
    /// Subtable <paramref name="index"/>, with an Extension subtable resolved to the subtable it points to.
    /// </summary>
    public LookupSubtable GetSubtable(int index)
    {
        // TRACE R-OT-0046
        if ((uint)index >= (uint)SubtableCount)
        {
            return default;
        }

        ReadOnlySpan<byte> s = _t.Span;
        int type = LookupType;
        int offset = _o + BigEndian.U16(s, _o + 6 + (index * 2));
        int extensionType = _kind == LayoutTableKind.Gsub ? 7 : 9;
        if (type == extensionType)
        {
            if (BigEndian.U16(s, offset) != 1)
            {
                return default;
            }

            int inner = BigEndian.U16(s, offset + 2);
            int target = BigEndian.Offset32(s, offset + 4);
            if (inner == extensionType || target <= 0 || (long)offset + target >= s.Length)
            {
                return default; // an Extension may not point to another Extension
            }

            return new LookupSubtable(_kind, inner, _t, offset + target);
        }

        return new LookupSubtable(_kind, type, _t, offset);
    }
}

/// <summary>
/// One lookup subtable, already resolved through any Extension subtable. Use the typed view that matches
/// <see cref="LookupType"/> (for example <see cref="AsSingleSubstitution"/>).
/// </summary>
public readonly struct LookupSubtable
{
    private readonly ReadOnlyMemory<byte> _t;
    private readonly int _o;

    internal LookupSubtable(LayoutTableKind kind, int type, ReadOnlyMemory<byte> table, int offset)
    {
        Kind = kind;
        LookupType = type;
        _t = table;
        _o = offset;
    }

    /// <summary>GSUB or GPOS.</summary>
    public LayoutTableKind Kind { get; }

    /// <summary>The effective lookup type (never the Extension type); 0 when the subtable is missing.</summary>
    public int LookupType { get; }

    /// <summary>True when the subtable exists.</summary>
    public bool Exists => LookupType != 0 && _o > 0 && _o < _t.Length;

    /// <summary>The subtable format.</summary>
    public int Format => Exists ? BigEndian.U16(_t.Span, _o) : 0;

    /// <summary>GSUB type 1 view.</summary>
    public SingleSubstitution AsSingleSubstitution() => new SingleSubstitution(_t, Is(LayoutTableKind.Gsub, 1));

    /// <summary>GSUB type 2 view.</summary>
    public MultipleSubstitution AsMultipleSubstitution() => new MultipleSubstitution(_t, Is(LayoutTableKind.Gsub, 2));

    /// <summary>GSUB type 3 view.</summary>
    public AlternateSubstitution AsAlternateSubstitution() => new AlternateSubstitution(_t, Is(LayoutTableKind.Gsub, 3));

    /// <summary>GSUB type 4 view.</summary>
    public LigatureSubstitution AsLigatureSubstitution() => new LigatureSubstitution(_t, Is(LayoutTableKind.Gsub, 4));

    /// <summary>GSUB type 5 or GPOS type 7 view (sequence context).</summary>
    public ContextualSubtable AsContext() =>
        new ContextualSubtable(_t, Kind == LayoutTableKind.Gsub ? Is(LayoutTableKind.Gsub, 5) : Is(LayoutTableKind.Gpos, 7), chained: false);

    /// <summary>GSUB type 6 or GPOS type 8 view (chained sequence context).</summary>
    public ContextualSubtable AsChainedContext() =>
        new ContextualSubtable(_t, Kind == LayoutTableKind.Gsub ? Is(LayoutTableKind.Gsub, 6) : Is(LayoutTableKind.Gpos, 8), chained: true);

    /// <summary>GSUB type 8 view.</summary>
    public ReverseChainSingleSubstitution AsReverseChainSingleSubstitution() =>
        new ReverseChainSingleSubstitution(_t, Is(LayoutTableKind.Gsub, 8));

    /// <summary>GPOS type 1 view.</summary>
    public SinglePositioning AsSinglePositioning() => new SinglePositioning(_t, Is(LayoutTableKind.Gpos, 1));

    /// <summary>GPOS type 2 view.</summary>
    public PairPositioning AsPairPositioning() => new PairPositioning(_t, Is(LayoutTableKind.Gpos, 2));

    /// <summary>GPOS type 3 view.</summary>
    public CursivePositioning AsCursivePositioning() => new CursivePositioning(_t, Is(LayoutTableKind.Gpos, 3));

    /// <summary>GPOS type 4, 5 or 6 view (mark-to-base, mark-to-ligature, mark-to-mark).</summary>
    public MarkAttachment AsMarkAttachment() =>
        new MarkAttachment(_t, Kind == LayoutTableKind.Gpos && LookupType is 4 or 5 or 6 && Exists ? _o : 0, LookupType);

    private int Is(LayoutTableKind kind, int type) => Kind == kind && LookupType == type && Exists ? _o : 0;
}
