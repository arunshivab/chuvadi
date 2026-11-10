using System;
using Chuvadi.OpenType.Internal;

namespace Chuvadi.OpenType.Layout;

/// <summary>
/// A sequence-context subtable (GSUB type 5, GPOS type 7) or a chained sequence-context subtable (GSUB type 6,
/// GPOS type 8), formats 1 (glyphs), 2 (classes) and 3 (coverages).
/// </summary>
public readonly struct ContextualSubtable
{
    private readonly ReadOnlyMemory<byte> _t;
    private readonly int _o;

    internal ContextualSubtable(ReadOnlyMemory<byte> table, int offset, bool chained)
    {
        _t = table;
        _o = offset;
        IsChained = chained;
    }

    /// <summary>True when the subtable exists and has format 1, 2 or 3.</summary>
    public bool Exists => _o > 0 && Format is 1 or 2 or 3;

    /// <summary>True for the chained form (backtrack and lookahead sequences).</summary>
    public bool IsChained { get; }

    /// <summary>The format: 1 (glyph sequences), 2 (class sequences), 3 (coverage sequences).</summary>
    public int Format => _o > 0 ? BigEndian.U16(_t.Span, _o) : 0;

    /// <summary>Formats 1 and 2: the glyphs that can start a match. Format 3: the first input coverage.</summary>
    public Coverage Coverage
    {
        get
        {
            if (!Exists)
            {
                return default;
            }

            return Format == 3 ? GetFormat3Rule().GetInputCoverage(0) : new Coverage(_t, _o + BigEndian.U16(_t.Span, _o + 2));
        }
    }

    /// <summary>Format 2: the class definition for the input sequence.</summary>
    public ClassDefinition InputClassDefinition =>
        Format != 2 ? default : new ClassDefinition(_t, _o + BigEndian.U16(_t.Span, _o + (IsChained ? 6 : 4)));

    /// <summary>Format 2, chained: the class definition for the backtrack sequence.</summary>
    public ClassDefinition BacktrackClassDefinition =>
        Format != 2 || !IsChained ? default : new ClassDefinition(_t, _o + BigEndian.U16(_t.Span, _o + 4));

    /// <summary>Format 2, chained: the class definition for the lookahead sequence.</summary>
    public ClassDefinition LookaheadClassDefinition =>
        Format != 2 || !IsChained ? default : new ClassDefinition(_t, _o + BigEndian.U16(_t.Span, _o + 8));

    private int SetCountAt => Format == 1 ? _o + 4 : (IsChained ? _o + 10 : _o + 6);

    /// <summary>Formats 1 and 2: the number of rule sets (by coverage index, or by input class).</summary>
    public int RuleSetCount => Format is 1 or 2 ? BigEndian.U16(_t.Span, SetCountAt) : 0;

    /// <summary>Formats 1 and 2: the number of rules in rule set <paramref name="setIndex"/> (0 when it is empty).</summary>
    public int GetRuleCount(int setIndex)
    {
        int set = RuleSet(setIndex);
        return set > 0 ? BigEndian.U16(_t.Span, set) : 0;
    }

    /// <summary>Formats 1 and 2: rule <paramref name="ruleIndex"/> of rule set <paramref name="setIndex"/>, in preference order.</summary>
    public ContextRule GetRule(int setIndex, int ruleIndex)
    {
        // TRACE R-OT-0048
        int set = RuleSet(setIndex);
        if (set <= 0 || (uint)ruleIndex >= (uint)BigEndian.U16(_t.Span, set))
        {
            return default;
        }

        return new ContextRule(_t, set + BigEndian.U16(_t.Span, set + 2 + (ruleIndex * 2)), IsChained, coverageBase: 0);
    }

    /// <summary>Format 3: the single rule, whose sequences are coverages.</summary>
    public ContextRule GetFormat3Rule() =>
        Format == 3 ? new ContextRule(_t, _o + 2, IsChained, coverageBase: _o) : default;

    private int RuleSet(int setIndex)
    {
        if ((uint)setIndex >= (uint)RuleSetCount)
        {
            return 0;
        }

        int off = BigEndian.U16(_t.Span, SetCountAt + 2 + (setIndex * 2));
        return off == 0 ? 0 : _o + off;
    }
}

/// <summary>
/// One context rule: backtrack, input and lookahead sequences and the lookups to apply. In formats 1 and 2 the
/// sequences hold glyph ids or class values; in format 3 they are coverages (use the Get...Coverage methods).
/// </summary>
public readonly struct ContextRule
{
    private readonly ReadOnlyMemory<byte> _t;
    private readonly int _o;
    private readonly bool _chained;
    private readonly int _coverageBase;

    internal ContextRule(ReadOnlyMemory<byte> table, int offset, bool chained, int coverageBase)
    {
        _t = table;
        _o = offset;
        _chained = chained;
        _coverageBase = coverageBase;
    }

    /// <summary>True when the rule exists.</summary>
    public bool Exists => _o > 0 && _o < _t.Length;

    /// <summary>True for a format 3 rule (sequences are coverages).</summary>
    public bool UsesCoverages => _coverageBase > 0;

    private ReadOnlySpan<byte> S => _t.Span;

    private int ValueSize => 2;

    /// <summary>The number of backtrack items (0 for a non-chained rule).</summary>
    public int BacktrackCount => _chained && Exists ? BigEndian.U16(S, _o) : 0;

    private int InputCountAt => _chained ? _o + 2 + (BacktrackCount * ValueSize) : _o;

    /// <summary>The number of input items, including the first.</summary>
    public int InputCount => Exists ? BigEndian.U16(S, InputCountAt) : 0;

    // Non-chained formats 1 and 2 store the lookup count right after the input count; format 3 too.
    private int FirstInputAt
    {
        get
        {
            if (_chained)
            {
                return InputCountAt + 2;
            }

            return InputCountAt + 4;
        }
    }

    // Format 3 stores every input coverage; formats 1 and 2 store the inputs after the first.
    private int StoredInputCount => UsesCoverages ? InputCount : Math.Max(0, InputCount - 1);

    private int LookaheadCountAt => FirstInputAt + (StoredInputCount * ValueSize);

    /// <summary>The number of lookahead items (0 for a non-chained rule).</summary>
    public int LookaheadCount => _chained && Exists ? BigEndian.U16(S, LookaheadCountAt) : 0;

    private int LookupCountAt => _chained ? LookaheadCountAt + 2 + (LookaheadCount * ValueSize) : InputCountAt + 2;

    /// <summary>The number of sequence lookup records.</summary>
    public int LookupRecordCount => Exists ? BigEndian.U16(S, LookupCountAt) : 0;

    private int RecordsAt => _chained ? LookupCountAt + 2 : FirstInputAt + (StoredInputCount * ValueSize);

    /// <summary>Formats 1 and 2: backtrack value <paramref name="index"/> (0 is nearest the input).</summary>
    public int GetBacktrack(int index) =>
        !UsesCoverages && (uint)index < (uint)BacktrackCount ? BigEndian.U16(S, _o + 2 + (index * 2)) : 0;

    /// <summary>Formats 1 and 2: input value <paramref name="index"/>, for 1 &lt;= index &lt; InputCount.</summary>
    public int GetInput(int index) =>
        !UsesCoverages && index >= 1 && index < InputCount ? BigEndian.U16(S, FirstInputAt + ((index - 1) * 2)) : 0;

    /// <summary>Formats 1 and 2: lookahead value <paramref name="index"/>.</summary>
    public int GetLookahead(int index) =>
        !UsesCoverages && (uint)index < (uint)LookaheadCount ? BigEndian.U16(S, LookaheadCountAt + 2 + (index * 2)) : 0;

    /// <summary>Format 3: backtrack coverage <paramref name="index"/>.</summary>
    public Coverage GetBacktrackCoverage(int index) =>
        UsesCoverages && (uint)index < (uint)BacktrackCount ? new Coverage(_t, _coverageBase + BigEndian.U16(S, _o + 2 + (index * 2))) : default;

    /// <summary>Format 3: input coverage <paramref name="index"/>, for 0 &lt;= index &lt; InputCount.</summary>
    public Coverage GetInputCoverage(int index) =>
        UsesCoverages && (uint)index < (uint)InputCount ? new Coverage(_t, _coverageBase + BigEndian.U16(S, FirstInputAt + (index * 2))) : default;

    /// <summary>Format 3: lookahead coverage <paramref name="index"/>.</summary>
    public Coverage GetLookaheadCoverage(int index) =>
        UsesCoverages && (uint)index < (uint)LookaheadCount ? new Coverage(_t, _coverageBase + BigEndian.U16(S, LookaheadCountAt + 2 + (index * 2))) : default;

    /// <summary>Sequence lookup record <paramref name="index"/>: the input position and the lookup to apply there.</summary>
    public (int SequenceIndex, int LookupListIndex) GetLookupRecord(int index)
    {
        if ((uint)index >= (uint)LookupRecordCount)
        {
            return (0, 0);
        }

        int r = RecordsAt + (index * 4);
        return (BigEndian.U16(S, r), BigEndian.U16(S, r + 2));
    }
}
