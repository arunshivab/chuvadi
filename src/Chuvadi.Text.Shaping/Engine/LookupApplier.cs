using System;
using Chuvadi.OpenType;
using Chuvadi.OpenType.Layout;

namespace Chuvadi.Text.Shaping.Engine;

/// <summary>Which glyphs a lookup skips (OpenType chapter2, LookupFlag; GDEF mark classes and sets).</summary>
internal readonly struct GlyphFilter
{
    private readonly LookupFlags _flags;
    private readonly int _markAttachClass;
    private readonly int _markFilteringSet;
    private readonly GdefTable? _gdef;
    private readonly bool _positioning;

    internal GlyphFilter(Lookup lookup, GdefTable? gdef, bool positioning)
    {
        _flags = lookup.Flags;
        _markAttachClass = lookup.MarkAttachmentClass;
        _markFilteringSet = lookup.MarkFilteringSet;
        _gdef = gdef;
        _positioning = positioning;
    }

    // TRACE R-TS-0020
    internal bool Skip(in GlyphInfo g)
    {
        // Default ignorables are invisible to positioning; substitution still sees ZWJ and ZWNJ (E8 a).
        if (g.Ignorable && (_positioning || (g.CodePoint != 0x200C && g.CodePoint != 0x200D)))
        {
            return true;
        }

        switch ((GlyphClass)g.Class)
        {
            case GlyphClass.Base:
                return (_flags & LookupFlags.IgnoreBaseGlyphs) != 0;
            case GlyphClass.Ligature:
                return (_flags & LookupFlags.IgnoreLigatures) != 0;
            case GlyphClass.Mark:
                if ((_flags & LookupFlags.IgnoreMarks) != 0)
                {
                    return true;
                }

                if ((_flags & LookupFlags.UseMarkFilteringSet) != 0)
                {
                    return _gdef is null || !_gdef.IsInMarkGlyphSet(_markFilteringSet, g.Glyph);
                }

                return _markAttachClass != 0 && (_gdef is null || _gdef.GetMarkAttachmentClass(g.Glyph) != _markAttachClass);
            default:
                return false;
        }
    }
}

/// <summary>
/// Applies GSUB and GPOS lookups to a glyph run (OpenType GSUB and GPOS chapters; E6: LookupList order, one pass per
/// table; E12: nesting and operation limits).
/// </summary>
internal sealed class LookupApplier
{
    private GlyphRun _run = null!;
    private GdefTable? _gdef;
    private int _maxDepth;
    private long _budget;

    /// <summary>Prepares the applier for one call (instances are reused per thread).</summary>
    internal void Reset(GlyphRun run, OpenTypeFont font, ShapingLimits limits)
    {
        _run = run;
        _gdef = font.Gdef;
        _maxDepth = limits.MaxNestingDepth;
        _budget = limits.BudgetFor(run.Count);
        _ligatureIds = 0;
        LimitHit = null;
    }

    /// <summary>Gets which limit stopped shaping early, or null.</summary>
    internal string? LimitHit { get; private set; }

    // TRACE R-TS-0041
    internal void ApplyGsub(LayoutTable table, PlannedLookup[] lookups, LookupDigest digest)
    {
        foreach (PlannedLookup p in lookups)
        {
            if (LimitHit is not null)
            {
                return;
            }

            Lookup lookup = table.Lookups.GetLookup(p.Index);
            if (lookup.LookupType == 8 || (lookup.LookupType == 7 && lookup.SubtableCount > 0 && lookup.GetSubtable(0).LookupType == 8))
            {
                // TRACE R-TS-0023
                for (int i = _run.Count - 1; i >= 0 && LimitHit is null; i--)
                {
                    if ((_run[i].Mask & p.Mask) != 0 && digest.MayStart(p.Index, lookup, _run[i].Glyph))
                    {
                        Substitute(table, lookup, i, p.Value, 0);
                    }
                }

                continue;
            }

            int pos = 0;
            while (pos < _run.Count && LimitHit is null)
            {
                if ((_run[pos].Mask & p.Mask) == 0 || !digest.MayStart(p.Index, lookup, _run[pos].Glyph))
                {
                    pos++;
                    continue;
                }

                pos = Substitute(table, lookup, pos, p.Value, 0) is int next ? next : pos + 1;
            }
        }
    }

    internal void ApplyGpos(LayoutTable table, PlannedLookup[] lookups, LookupDigest digest)
    {
        foreach (PlannedLookup p in lookups)
        {
            if (LimitHit is not null)
            {
                return;
            }

            Lookup lookup = table.Lookups.GetLookup(p.Index);
            int pos = 0;
            while (pos < _run.Count && LimitHit is null)
            {
                if ((_run[pos].Mask & p.Mask) == 0 || !digest.MayStart(p.Index, lookup, _run[pos].Glyph))
                {
                    pos++;
                    continue;
                }

                pos = Position(table, lookup, pos, 0) is int next ? next : pos + 1;
            }
        }
    }

    // ---- Substitution ----

    /// <summary>Applies a GSUB lookup at one position; returns the position to continue from, or null when nothing applied.</summary>
    private int? Substitute(LayoutTable table, Lookup lookup, int i, uint value, int depth)
    {
        GlyphFilter filter = new GlyphFilter(lookup, _gdef, positioning: false);
        if (!Spend() || filter.Skip(_run[i]))
        {
            return null;
        }

        for (int s = 0; s < lookup.SubtableCount; s++)
        {
            LookupSubtable st = lookup.GetSubtable(s);
            int? next = st.LookupType switch
            {
                1 => SingleSubst(st.AsSingleSubstitution(), i),
                2 => MultipleSubst(st.AsMultipleSubstitution(), i),
                3 => AlternateSubst(st.AsAlternateSubstitution(), i, value),
                4 => LigatureSubst(st.AsLigatureSubstitution(), i, filter),
                5 => Context(table, st.AsContext(), i, filter, depth, gsub: true),
                6 => Context(table, st.AsChainedContext(), i, filter, depth, gsub: true),
                8 => ReverseSubst(st.AsReverseChainSingleSubstitution(), i, filter),
                _ => null,
            };
            if (next is not null)
            {
                return next;
            }
        }

        return null;
    }

    // TRACE R-TS-0021
    private int? SingleSubst(SingleSubstitution st, int i)
    {
        if (!st.TryGetSubstitute(_run[i].Glyph, out int g))
        {
            return null;
        }

        SetGlyph(i, g);
        return i + 1;
    }

    // TRACE R-TS-0038
    private int? MultipleSubst(MultipleSubstitution st, int i)
    {
        if (!st.TryGetSequence(_run[i].Glyph, out UInt16List seq))
        {
            return null;
        }

        if (seq.Count == 0)
        {
            _run.RemoveAt(i);
            return i;
        }

        GlyphInfo template = _run[i];
        _run.Insert(i + 1, seq.Count - 1);
        for (int k = 0; k < seq.Count; k++)
        {
            _run[i + k] = template;
            SetGlyph(i + k, seq[k]);
        }

        return i + seq.Count;
    }

    // TRACE R-TS-0038
    private int? AlternateSubst(AlternateSubstitution st, int i, uint value)
    {
        if (!st.TryGetAlternates(_run[i].Glyph, out UInt16List alts) || alts.Count == 0)
        {
            return null;
        }

        // Value 1 is the first alternate; a value past the set leaves the glyph unchanged (D-363).
        if (value == 0 || value > (uint)alts.Count)
        {
            return null;
        }

        SetGlyph(i, alts[(int)value - 1]);
        return i + 1;
    }

    // TRACE R-TS-0022
    private int? LigatureSubst(LigatureSubstitution st, int i, GlyphFilter filter)
    {
        int first = _run[i].Glyph;
        int count = st.GetLigatureCount(first);
        Span<int> matched = stackalloc int[64];
        for (int l = 0; l < count; l++)
        {
            Ligature lig = st.GetLigature(first, l);
            int comps = lig.ComponentCount;
            if (comps < 1 || comps > 64)
            {
                continue;
            }

            matched[0] = i;
            int at = i;
            bool ok = true;
            for (int c = 1; c < comps && ok; c++)
            {
                at = Next(at, filter);
                ok = at >= 0 && _run[at].Glyph == lig.FollowingComponents[c - 1];
                if (ok)
                {
                    matched[c] = at;
                }
            }

            if (!ok)
            {
                continue;
            }

            Ligate(matched[..comps], lig.LigatureGlyph);
            return i + 1;
        }

        return null;
    }

    private void Ligate(Span<int> matched, int ligatureGlyph)
    {
        int first = matched[0];
        int last = matched[^1];
        int cluster = int.MaxValue;
        for (int k = first; k <= last; k++)
        {
            cluster = Math.Min(cluster, _run[k].Cluster);
        }

        int id = ++_ligatureIds;
        // Marks between components keep their place and learn which component they belong to (for mark-to-ligature).
        int component = 0;
        int m = 1;
        for (int k = first + 1; k <= last; k++)
        {
            if (m < matched.Length && k == matched[m])
            {
                component++;
                m++;
                continue;
            }

            _run[k].LigatureId = id;
            _run[k].Component = component;
        }

        for (int k = matched.Length - 1; k >= 1; k--)
        {
            _run.RemoveAt(matched[k]);
        }

        int end = last - (matched.Length - 1);
        for (int k = first; k <= end; k++)
        {
            _run[k].Cluster = cluster;
        }

        SetGlyph(first, ligatureGlyph);
        _run[first].LigatureId = id;
        _run[first].Component = 0;
        _run[first].Ignorable = false;
    }

    private int _ligatureIds;

    private int? ReverseSubst(ReverseChainSingleSubstitution st, int i, GlyphFilter filter)
    {
        int index = st.Coverage.GetIndex(_run[i].Glyph);
        if (index < 0 || index >= st.Substitutes.Count)
        {
            return null;
        }

        int at = i;
        for (int b = 0; b < st.BacktrackCount; b++)
        {
            at = Previous(at, filter);
            if (at < 0 || st.GetBacktrack(b).GetIndex(_run[at].Glyph) < 0)
            {
                return null;
            }
        }

        at = i;
        for (int a = 0; a < st.LookaheadCount; a++)
        {
            at = Next(at, filter);
            if (at < 0 || st.GetLookahead(a).GetIndex(_run[at].Glyph) < 0)
            {
                return null;
            }
        }

        SetGlyph(i, st.Substitutes[index]);
        return i - 1;
    }

    // ---- Positioning ----

    private int? Position(LayoutTable table, Lookup lookup, int i, int depth)
    {
        GlyphFilter filter = new GlyphFilter(lookup, _gdef, positioning: true);
        if (!Spend() || filter.Skip(_run[i]))
        {
            return null;
        }

        for (int s = 0; s < lookup.SubtableCount; s++)
        {
            LookupSubtable st = lookup.GetSubtable(s);
            int? next = st.LookupType switch
            {
                1 => SinglePos(st.AsSinglePositioning(), i),
                2 => PairPos(st.AsPairPositioning(), i, filter),
                3 => CursivePos(st.AsCursivePositioning(), i, filter),
                4 or 5 or 6 => MarkPos(st.AsMarkAttachment(), i, filter),
                7 => Context(table, st.AsContext(), i, filter, depth, gsub: false),
                8 => Context(table, st.AsChainedContext(), i, filter, depth, gsub: false),
                _ => null,
            };
            if (next is not null)
            {
                return next;
            }
        }

        return null;
    }

    // TRACE R-TS-0024
    private int? SinglePos(SinglePositioning st, int i)
    {
        if (!st.TryGetValue(_run[i].Glyph, out ValueRecord v))
        {
            return null;
        }

        AddValue(i, v);
        return i + 1;
    }

    // TRACE R-TS-0039
    private int? PairPos(PairPositioning st, int i, GlyphFilter filter)
    {
        int j = Next(i, filter);
        if (j < 0 || !st.TryGetPair(_run[i].Glyph, _run[j].Glyph, out ValueRecord v1, out ValueRecord v2))
        {
            return null;
        }

        AddValue(i, v1);
        AddValue(j, v2);
        // When the second glyph's value record is empty, it starts the next pair (GPOS, Lookup Type 2).
        return st.ValueFormat2 == ValueFormat.None ? j : j + 1;
    }

    // TRACE R-TS-0025
    private int? CursivePos(CursivePositioning st, int i, GlyphFilter filter)
    {
        int j = Next(i, filter);
        if (j < 0 || !st.TryGetAnchors(_run[i].Glyph, out Anchor _, out Anchor exit) || !exit.Exists
            || !st.TryGetAnchors(_run[j].Glyph, out Anchor entry, out Anchor _) || !entry.Exists)
        {
            return null;
        }

        // Left to right: the pen moves from glyph i's origin to its exit point, then back by j's entry point.
        _run[i].XAdvance = exit.X - entry.X;
        _run[j].AttachKind = GlyphRun.AttachCursive;
        _run[j].AttachTo = i - j;
        _run[j].AttachDx = 0;
        _run[j].AttachDy = exit.Y - entry.Y;
        return j;
    }

    // TRACE R-TS-0026
    private int? MarkPos(MarkAttachment st, int i, GlyphFilter filter)
    {
        int markIndex = st.MarkCoverage.GetIndex(_run[i].Glyph);
        if (markIndex < 0 || !st.TryGetMark(markIndex, out int markClass, out Anchor markAnchor) || !markAnchor.Exists)
        {
            return null;
        }

        int j;
        Anchor target;
        if (st.LookupType == 6)
        {
            // Mark to mark: the preceding mark, after the lookup's own skipping.
            j = Previous(i, filter);
            if (j < 0 || _run[j].Class != (byte)GlyphClass.Mark || _run[j].LigatureId != _run[i].LigatureId || _run[j].Component != _run[i].Component)
            {
                return null;
            }

            int t = st.TargetCoverage.GetIndex(_run[j].Glyph);
            target = t < 0 ? default : st.GetTargetAnchor(t, markClass);
        }
        else
        {
            // Mark to base or ligature: the preceding glyph that is not a mark (and not an ignorable).
            j = i - 1;
            while (j >= 0 && (_run[j].Class == (byte)GlyphClass.Mark || _run[j].Ignorable))
            {
                j--;
            }

            int t = j < 0 ? -1 : st.TargetCoverage.GetIndex(_run[j].Glyph);
            if (t < 0)
            {
                return null;
            }

            if (st.LookupType == 5)
            {
                int components = st.GetComponentCount(t);
                int component = _run[i].LigatureId != 0 && _run[i].LigatureId == _run[j].LigatureId ? _run[i].Component : components - 1;
                target = st.GetLigatureAnchor(t, Math.Clamp(component, 0, Math.Max(0, components - 1)), markClass);
            }
            else
            {
                target = st.GetTargetAnchor(t, markClass);
            }
        }

        if (!target.Exists)
        {
            return null;
        }

        // TRACE R-TS-0027
        // E9 b: a mark positioned by a mark-attachment lookup does not move the pen.
        _run[i].XAdvance = 0;
        _run[i].AttachKind = GlyphRun.AttachMark;
        _run[i].AttachTo = j - i;
        _run[i].AttachDx = target.X - markAnchor.X;
        _run[i].AttachDy = target.Y - markAnchor.Y;
        return i + 1;
    }

    // ---- Context and chained context (both tables) ----

    // TRACE R-TS-0028
    private int? Context(LayoutTable table, ContextualSubtable st, int i, GlyphFilter filter, int depth, bool gsub)
    {
        if (!st.Exists)
        {
            return null;
        }

        Span<int> positions = stackalloc int[64];
        int glyph = _run[i].Glyph;
        if (st.Format == 3)
        {
            ContextRule rule = st.GetFormat3Rule();
            return TryRule(table, st, rule, i, filter, depth, gsub, positions);
        }

        if (st.Coverage.GetIndex(glyph) < 0)
        {
            return null;
        }

        int set = st.Format == 1 ? st.Coverage.GetIndex(glyph) : st.InputClassDefinition.GetClass(glyph);
        int rules = st.GetRuleCount(set);
        for (int r = 0; r < rules; r++)
        {
            int? next = TryRule(table, st, st.GetRule(set, r), i, filter, depth, gsub, positions);
            if (next is not null)
            {
                return next;
            }
        }

        return null;
    }

    private int? TryRule(LayoutTable table, ContextualSubtable st, ContextRule rule, int i, GlyphFilter filter, int depth, bool gsub, Span<int> positions)
    {
        int inputs = rule.InputCount;
        if (inputs < 1 || inputs > positions.Length)
        {
            return null;
        }

        if (!Matches(st, rule, 1, 0, _run[i].Glyph))
        {
            return null;
        }

        positions[0] = i;
        int at = i;
        for (int k = 1; k < inputs; k++)
        {
            at = Next(at, filter);
            if (at < 0 || !Matches(st, rule, 1, k, _run[at].Glyph))
            {
                return null;
            }

            positions[k] = at;
        }

        int lastInput = at;
        for (int k = 0; k < rule.LookaheadCount; k++)
        {
            at = Next(at, filter);
            if (at < 0 || !Matches(st, rule, 2, k, _run[at].Glyph))
            {
                return null;
            }
        }

        at = i;
        for (int k = 0; k < rule.BacktrackCount; k++)
        {
            at = Previous(at, filter);
            if (at < 0 || !Matches(st, rule, 0, k, _run[at].Glyph))
            {
                return null;
            }
        }

        // The input matched: apply the lookup records at their sequence positions.
        int end = lastInput + 1;
        for (int r = 0; r < rule.LookupRecordCount; r++)
        {
            (int seq, int lookupIndex) = rule.GetLookupRecord(r);
            if ((uint)seq >= (uint)inputs || (uint)lookupIndex >= (uint)table.Lookups.Count)
            {
                continue;
            }

            if (depth + 1 >= _maxDepth)
            {
                LimitHit ??= "nesting depth (" + _maxDepth + ")";
                break;
            }

            int before = _run.Count;
            int position = positions[seq];
            if (position >= _run.Count)
            {
                continue;
            }

            Lookup nested = table.Lookups.GetLookup(lookupIndex);
            if (gsub)
            {
                Substitute(table, nested, position, 1, depth + 1);
            }
            else
            {
                Position(table, nested, position, depth + 1);
            }

            int delta = _run.Count - before;
            if (delta != 0)
            {
                for (int k = seq + 1; k < inputs; k++)
                {
                    positions[k] = Math.Max(position, positions[k] + delta);
                }

                end += delta;
            }
        }

        return Math.Max(i + 1, Math.Min(end, _run.Count));
    }

    /// <summary>Whether a glyph matches item <paramref name="index"/> of a rule's sequence (0 backtrack, 1 input, 2 lookahead).</summary>
    private static bool Matches(ContextualSubtable st, ContextRule rule, int sequence, int index, int glyph)
    {
        if (st.Format == 3)
        {
            Coverage c = sequence switch
            {
                0 => rule.GetBacktrackCoverage(index),
                1 => rule.GetInputCoverage(index),
                _ => rule.GetLookaheadCoverage(index),
            };
            return c.GetIndex(glyph) >= 0;
        }

        if (sequence == 1 && index == 0)
        {
            return true; // the first input glyph was matched by the coverage and the rule set
        }

        int value = sequence switch
        {
            0 => rule.GetBacktrack(index),
            1 => rule.GetInput(index),
            _ => rule.GetLookahead(index),
        };
        if (st.Format == 1)
        {
            return glyph == value;
        }

        ClassDefinition cd = !st.IsChained ? st.InputClassDefinition : sequence switch
        {
            0 => st.BacktrackClassDefinition,
            1 => st.InputClassDefinition,
            _ => st.LookaheadClassDefinition,
        };
        return cd.GetClass(glyph) == value;
    }

    // ---- Helpers ----

    private int Next(int i, GlyphFilter filter)
    {
        for (int k = i + 1; k < _run.Count; k++)
        {
            if (!filter.Skip(_run[k]))
            {
                return k;
            }
        }

        return -1;
    }

    private int Previous(int i, GlyphFilter filter)
    {
        for (int k = i - 1; k >= 0; k--)
        {
            if (!filter.Skip(_run[k]))
            {
                return k;
            }
        }

        return -1;
    }

    // TRACE R-TS-0040
    private bool Spend()
    {
        if (--_budget >= 0)
        {
            return true;
        }

        LimitHit ??= "operation budget";
        return false;
    }

    private void SetGlyph(int i, int glyph)
    {
        _run[i].Glyph = glyph;
        _run[i].Class = _gdef is null ? (byte)0 : (byte)_gdef.GetGlyphClass(glyph);
    }

    private void AddValue(int i, ValueRecord v)
    {
        _run[i].XOffset += v.XPlacement;
        _run[i].YOffset += v.YPlacement;
        _run[i].XAdvance += v.XAdvance;
    }
}
