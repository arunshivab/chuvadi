using System;
using Chuvadi.OpenType.Internal;

namespace Chuvadi.OpenType.Layout;

/// <summary>Which layout table a <see cref="LayoutTable"/> view reads.</summary>
public enum LayoutTableKind
{
    /// <summary>The 'GSUB' table (glyph substitution).</summary>
    Gsub,

    /// <summary>The 'GPOS' table (glyph positioning).</summary>
    Gpos,
}

/// <summary>
/// A read-only view of a 'GSUB' or 'GPOS' table: script list, feature list, lookup list and feature variations.
/// Lenient: damaged parts read as empty; nothing throws.
/// </summary>
public sealed class LayoutTable
{
    private readonly ReadOnlyMemory<byte> _t;
    private readonly int _axisCount;

    internal LayoutTable(LayoutTableKind kind, ReadOnlyMemory<byte> table, int axisCount)
    {
        // TRACE R-OT-0040
        Kind = kind;
        _t = table;
        _axisCount = axisCount;
        ReadOnlySpan<byte> s = table.Span;
        MajorVersion = BigEndian.U16(s, 0);
        MinorVersion = BigEndian.U16(s, 2);
        Scripts = new ScriptList(table, BigEndian.U16(s, 4));
        Features = new FeatureList(table, BigEndian.U16(s, 6));
        Lookups = new LookupList(kind, table, BigEndian.U16(s, 8));
        FeatureVariationsOffset = MinorVersion >= 1 ? Math.Max(0, BigEndian.Offset32(s, 10)) : 0;
        DefaultInstanceVariation = FindDefaultInstanceVariation();
    }

    /// <summary>GSUB or GPOS.</summary>
    public LayoutTableKind Kind { get; }

    /// <summary>Major version (1).</summary>
    public int MajorVersion { get; }

    /// <summary>Minor version (0, or 1 with feature variations).</summary>
    public int MinorVersion { get; }

    /// <summary>The script list.</summary>
    public ScriptList Scripts { get; }

    /// <summary>The feature list.</summary>
    public FeatureList Features { get; }

    /// <summary>The lookup list.</summary>
    public LookupList Lookups { get; }

    /// <summary>Offset of the FeatureVariations table; 0 when there is none.</summary>
    public int FeatureVariationsOffset { get; }

    /// <summary>
    /// The index of the feature variation record used at the default instance of a variable font (D-194), or -1
    /// when none applies.
    /// </summary>
    public int DefaultInstanceVariation { get; }

    /// <summary>
    /// The feature at <paramref name="featureIndex"/> as it applies at the default instance: the feature list's
    /// table, or its substitute from the feature variation record that matches the default instance (D-194).
    /// </summary>
    public Feature GetFeatureAtDefaultInstance(int featureIndex)
    {
        // TRACE R-OT-0045
        Feature plain = Features.GetFeature(featureIndex);
        if (DefaultInstanceVariation < 0)
        {
            return plain;
        }

        ReadOnlySpan<byte> s = _t.Span;
        int fv = FeatureVariationsOffset;
        int rec = fv + 8 + (DefaultInstanceVariation * 8);
        int subst = fv + Math.Max(0, BigEndian.Offset32(s, rec + 4));
        int count = BigEndian.U16(s, subst + 4);
        for (int i = 0; i < count; i++)
        {
            int r = subst + 6 + (i * 6);
            int index = BigEndian.U16(s, r);
            if (index == featureIndex)
            {
                int alt = BigEndian.Offset32(s, r + 2);
                return alt <= 0 ? plain : new Feature(plain.Tag, _t, subst + alt);
            }

            if (index > featureIndex)
            {
                break;
            }
        }

        return plain;
    }

    private int FindDefaultInstanceVariation()
    {
        // TRACE R-OT-0044
        if (FeatureVariationsOffset <= 0 || FeatureVariationsOffset >= _t.Length)
        {
            return -1;
        }

        ReadOnlySpan<byte> s = _t.Span;
        int fv = FeatureVariationsOffset;
        if (BigEndian.U16(s, fv) != 1)
        {
            return -1;
        }

        long recordCount = BigEndian.U32(s, fv + 4);
        for (int i = 0; i < recordCount && fv + 8 + ((long)i * 8) + 8 <= s.Length; i++)
        {
            int rec = fv + 8 + (i * 8);
            int conditionSet = BigEndian.Offset32(s, rec);
            int substitution = BigEndian.Offset32(s, rec + 4);
            int match = conditionSet == 0 ? 1 : EvaluateConditionSetAtDefault(s, fv + conditionSet);
            if (match < 0)
            {
                continue; // record ignored (invalid axis index)
            }

            if (match == 0)
            {
                continue;
            }

            if (substitution <= 0 || BigEndian.U16(s, fv + substitution) != 1)
            {
                continue; // unsupported FeatureTableSubstitution version: rejected, try the next record
            }

            return i;
        }

        return -1;
    }

    // 1 = matches, 0 = does not match, -1 = record must be ignored.
    private int EvaluateConditionSetAtDefault(ReadOnlySpan<byte> s, int set)
    {
        int count = BigEndian.U16(s, set);
        for (int i = 0; i < count; i++)
        {
            int c = set + BigEndian.Offset32(s, set + 2 + (i * 4));
            int format = BigEndian.U16(s, c);
            if (format != 1)
            {
                return 0; // unrecognized condition format: the set does not match
            }

            int axis = BigEndian.U16(s, c + 2);
            if (axis >= _axisCount)
            {
                return -1;
            }

            // The default instance has every normalized axis coordinate at 0.
            short min = BigEndian.I16(s, c + 4);
            short max = BigEndian.I16(s, c + 6);
            if (min > 0 || max < 0)
            {
                return 0;
            }
        }

        return 1;
    }
}

/// <summary>The script list of a layout table.</summary>
public readonly struct ScriptList
{
    private readonly ReadOnlyMemory<byte> _t;
    private readonly int _o;

    internal ScriptList(ReadOnlyMemory<byte> table, int offset)
    {
        _t = table;
        _o = offset;
    }

    /// <summary>The number of script records.</summary>
    public int Count => _o > 0 ? BigEndian.U16(_t.Span, _o) : 0;

    /// <summary>The tag of script record <paramref name="index"/>.</summary>
    public Tag GetTag(int index) => (uint)index < (uint)Count ? new Tag(BigEndian.U32(_t.Span, _o + 2 + (index * 6))) : default;

    /// <summary>The script table of record <paramref name="index"/>.</summary>
    public Script GetScript(int index) =>
        (uint)index < (uint)Count ? new Script(GetTag(index), _t, _o + BigEndian.U16(_t.Span, _o + 2 + (index * 6) + 4)) : default;

    /// <summary>Finds a script by tag (the first record with that tag).</summary>
    public bool TryFind(Tag tag, out Script script)
    {
        for (int i = 0; i < Count; i++)
        {
            if (GetTag(i) == tag)
            {
                script = GetScript(i);
                return true;
            }
        }

        script = default;
        return false;
    }
}

/// <summary>A script table: a default language system and language-specific ones.</summary>
public readonly struct Script
{
    private readonly ReadOnlyMemory<byte> _t;
    private readonly int _o;

    internal Script(Tag tag, ReadOnlyMemory<byte> table, int offset)
    {
        Tag = tag;
        _t = table;
        _o = offset;
    }

    /// <summary>The script tag.</summary>
    public Tag Tag { get; }

    /// <summary>True when the script table exists.</summary>
    public bool Exists => _o > 0 && _o < _t.Length;

    /// <summary>The default language system; <see cref="LanguageSystem.Exists"/> is false when there is none.</summary>
    public LanguageSystem DefaultLanguageSystem
    {
        get
        {
            int off = Exists ? BigEndian.U16(_t.Span, _o) : 0;
            return off == 0 ? default : new LanguageSystem(default, _t, _o + off);
        }
    }

    /// <summary>The number of language-specific systems.</summary>
    public int LanguageSystemCount => Exists ? BigEndian.U16(_t.Span, _o + 2) : 0;

    /// <summary>The tag of language system record <paramref name="index"/>.</summary>
    public Tag GetLanguageTag(int index) =>
        (uint)index < (uint)LanguageSystemCount ? new Tag(BigEndian.U32(_t.Span, _o + 4 + (index * 6))) : default;

    /// <summary>Language system record <paramref name="index"/>.</summary>
    public LanguageSystem GetLanguageSystem(int index) =>
        (uint)index < (uint)LanguageSystemCount
            ? new LanguageSystem(GetLanguageTag(index), _t, _o + BigEndian.U16(_t.Span, _o + 4 + (index * 6) + 4))
            : default;

    /// <summary>Finds a language system by tag.</summary>
    public bool TryFindLanguageSystem(Tag tag, out LanguageSystem system)
    {
        for (int i = 0; i < LanguageSystemCount; i++)
        {
            if (GetLanguageTag(i) == tag)
            {
                system = GetLanguageSystem(i);
                return true;
            }
        }

        system = default;
        return false;
    }
}

/// <summary>A language system table: the features used for a script and language.</summary>
public readonly struct LanguageSystem
{
    private readonly ReadOnlyMemory<byte> _t;
    private readonly int _o;

    internal LanguageSystem(Tag tag, ReadOnlyMemory<byte> table, int offset)
    {
        Tag = tag;
        _t = table;
        _o = offset;
    }

    /// <summary>The language tag (default for the default language system).</summary>
    public Tag Tag { get; }

    /// <summary>True when the table exists.</summary>
    public bool Exists => _o > 0 && _o < _t.Length;

    /// <summary>The required feature's index, or -1 when there is none (0xFFFF in the font).</summary>
    public int RequiredFeatureIndex
    {
        get
        {
            int v = Exists ? BigEndian.U16(_t.Span, _o + 2) : 0xFFFF;
            return v == 0xFFFF ? -1 : v;
        }
    }

    /// <summary>The feature indices, into the feature list.</summary>
    public UInt16List FeatureIndices => Exists ? new UInt16List(_t, _o + 6, BigEndian.U16(_t.Span, _o + 4)) : default;
}

/// <summary>The feature list of a layout table.</summary>
public readonly struct FeatureList
{
    private readonly ReadOnlyMemory<byte> _t;
    private readonly int _o;

    internal FeatureList(ReadOnlyMemory<byte> table, int offset)
    {
        _t = table;
        _o = offset;
    }

    /// <summary>The number of feature records.</summary>
    public int Count => _o > 0 ? BigEndian.U16(_t.Span, _o) : 0;

    /// <summary>The tag of feature record <paramref name="index"/>.</summary>
    public Tag GetTag(int index) => (uint)index < (uint)Count ? new Tag(BigEndian.U32(_t.Span, _o + 2 + (index * 6))) : default;

    /// <summary>Feature record <paramref name="index"/> (without feature variations; see <see cref="LayoutTable.GetFeatureAtDefaultInstance"/>).</summary>
    public Feature GetFeature(int index) =>
        (uint)index < (uint)Count ? new Feature(GetTag(index), _t, _o + BigEndian.U16(_t.Span, _o + 2 + (index * 6) + 4)) : default;
}

/// <summary>A feature table: the lookups that implement a feature.</summary>
public readonly struct Feature
{
    private readonly ReadOnlyMemory<byte> _t;
    private readonly int _o;

    internal Feature(Tag tag, ReadOnlyMemory<byte> table, int offset)
    {
        Tag = tag;
        _t = table;
        _o = offset;
    }

    /// <summary>The feature tag.</summary>
    public Tag Tag { get; }

    /// <summary>True when the feature table exists.</summary>
    public bool Exists => _o > 0 && _o < _t.Length;

    /// <summary>Offset of the feature parameters from the feature table; 0 when there are none.</summary>
    public int FeatureParamsOffset => Exists ? BigEndian.U16(_t.Span, _o) : 0;

    /// <summary>The lookup indices, into the lookup list, in the order stored.</summary>
    public UInt16List LookupIndices => Exists ? new UInt16List(_t, _o + 4, BigEndian.U16(_t.Span, _o + 2)) : default;
}
