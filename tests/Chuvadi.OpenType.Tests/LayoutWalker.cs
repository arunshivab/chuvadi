using System;
using Chuvadi.OpenType;
using Chuvadi.OpenType.Layout;

namespace Chuvadi.OpenType.Tests;

/// <summary>
/// Touches every member of the layout views of a font (GDEF, GSUB, GPOS), with caps on how far it walks so a damaged
/// table with huge counts stays quick. Used to check that damaged layout data never throws.
/// </summary>
internal static class LayoutWalker
{
    private const int Cap = 64;

    /// <summary>Walks the font's layout tables and returns a checksum of what was read.</summary>
    internal static long Walk(OpenTypeFont font)
    {
        long sum = 0;
        if (font.Gdef is GdefTable gdef)
        {
            sum += gdef.MarkGlyphSetCount + gdef.ItemVariationStoreOffset;
            for (int g = 0; g < Math.Min(font.GlyphCount, 2048); g++)
            {
                sum += (int)gdef.GetGlyphClass(g) + gdef.GetMarkAttachmentClass(g) + gdef.GetAttachmentPoints(g).Count;
                int carets = Math.Min(gdef.GetLigatureCaretCount(g), Cap);
                for (int c = 0; c <= carets; c++)
                {
                    sum += gdef.GetLigatureCaret(g, c, out bool _);
                }

                for (int s = 0; s <= Math.Min(gdef.MarkGlyphSetCount, 4); s++)
                {
                    sum += gdef.IsInMarkGlyphSet(s, g) ? 1 : 0;
                }
            }
        }

        foreach (LayoutTable? t in new[] { font.Gsub, font.Gpos })
        {
            if (t is not null)
            {
                sum += Table(t);
            }
        }

        return sum;
    }

    private static long Table(LayoutTable t)
    {
        long sum = t.DefaultInstanceVariation + t.FeatureVariationsOffset;
        for (int i = 0; i < Math.Min(t.Scripts.Count, Cap); i++)
        {
            Script s = t.Scripts.GetScript(i);
            sum += t.Scripts.GetTag(i).Value + LangSys(s.DefaultLanguageSystem);
            for (int l = 0; l < Math.Min(s.LanguageSystemCount, Cap); l++)
            {
                sum += s.GetLanguageTag(l).Value + LangSys(s.GetLanguageSystem(l));
            }

            _ = t.Scripts.TryFind(t.Scripts.GetTag(i), out _);
        }

        for (int f = 0; f <= Math.Min(t.Features.Count, 512); f++)
        {
            Feature a = t.Features.GetFeature(f);
            Feature b = t.GetFeatureAtDefaultInstance(f);
            sum += a.FeatureParamsOffset + List(a.LookupIndices) + List(b.LookupIndices) + b.Tag.Value;
        }

        for (int l = 0; l <= Math.Min(t.Lookups.Count, 2048); l++)
        {
            Lookup lookup = t.Lookups.GetLookup(l);
            sum += lookup.LookupType + (int)lookup.Flags + lookup.MarkAttachmentClass + lookup.MarkFilteringSet;
            for (int s = 0; s <= Math.Min(lookup.SubtableCount, 16); s++)
            {
                sum += Subtable(lookup.GetSubtable(s));
            }
        }

        return sum;
    }

    private static long LangSys(LanguageSystem l) => l.RequiredFeatureIndex + List(l.FeatureIndices);

    private static long List(UInt16List list)
    {
        long sum = list.Count;
        for (int i = 0; i <= Math.Min(list.Count, Cap); i++)
        {
            sum += list[i];
        }

        return sum;
    }

    private static long Coverage(Coverage c)
    {
        long sum = c.Format;
        int n = 0;
        foreach ((int glyph, int index) in c.GetGlyphs())
        {
            sum += glyph + index + c.GetIndex(glyph);
            if (++n > 256)
            {
                break;
            }
        }

        return sum;
    }

    private static long Glyphs(Coverage c, Func<int, long> each)
    {
        long sum = Coverage(c);
        int n = 0;
        foreach ((int glyph, int _) in c.GetGlyphs())
        {
            sum += each(glyph);
            if (++n > Cap)
            {
                break;
            }
        }

        return sum + each(-1) + each(70000);
    }

    private static long Value(ValueRecord v) =>
        v.XPlacement + v.YPlacement + v.XAdvance + v.YAdvance + Device(v.XPlacementDevice) + Device(v.YPlacementDevice) + Device(v.XAdvanceDevice) + Device(v.YAdvanceDevice);

    private static long Device(DeviceTable d) =>
        d.DeltaFormat + d.StartSizeOrOuterIndex + d.EndSizeOrInnerIndex + d.DefaultInstanceDelta + d.GetDelta(12) + d.GetDelta(d.StartSizeOrOuterIndex) + (d.IsVariationIndex ? 1 : 0);

    private static long Anchor(Anchor a) => a.Format + a.X + a.Y + a.AnchorPoint + Device(a.XDevice) + Device(a.YDevice);

    private static long Rule(ContextRule r)
    {
        long sum = r.BacktrackCount + r.InputCount + r.LookaheadCount + r.LookupRecordCount;
        for (int i = 0; i <= Math.Min(r.BacktrackCount, Cap); i++)
        {
            sum += r.GetBacktrack(i) + Coverage(r.GetBacktrackCoverage(i));
        }

        for (int i = 0; i <= Math.Min(r.InputCount, Cap); i++)
        {
            sum += r.GetInput(i) + Coverage(r.GetInputCoverage(i));
        }

        for (int i = 0; i <= Math.Min(r.LookaheadCount, Cap); i++)
        {
            sum += r.GetLookahead(i) + Coverage(r.GetLookaheadCoverage(i));
        }

        for (int i = 0; i <= Math.Min(r.LookupRecordCount, Cap); i++)
        {
            (int a, int b) = r.GetLookupRecord(i);
            sum += a + b;
        }

        return sum;
    }

    private static long Context(ContextualSubtable c)
    {
        long sum = c.Format + Coverage(c.Coverage) + c.InputClassDefinition.GetClass(5) + c.BacktrackClassDefinition.GetClass(5) + c.LookaheadClassDefinition.GetClass(5);
        if (c.Format == 3)
        {
            return sum + Rule(c.GetFormat3Rule());
        }

        for (int s = 0; s <= Math.Min(c.RuleSetCount, Cap); s++)
        {
            for (int r = 0; r <= Math.Min(c.GetRuleCount(s), 16); r++)
            {
                sum += Rule(c.GetRule(s, r));
            }
        }

        return sum;
    }

    private static long Subtable(LookupSubtable st)
    {
        long sum = st.LookupType + st.Format + (st.Exists ? 1 : 0);
        if (st.Kind == LayoutTableKind.Gsub)
        {
            SingleSubstitution s1 = st.AsSingleSubstitution();
            sum += Glyphs(s1.Coverage, g => s1.TryGetSubstitute(g, out int x) ? x : 0);
            MultipleSubstitution s2 = st.AsMultipleSubstitution();
            sum += Glyphs(s2.Coverage, g => s2.TryGetSequence(g, out UInt16List l) ? List(l) : 0);
            AlternateSubstitution s3 = st.AsAlternateSubstitution();
            sum += Glyphs(s3.Coverage, g => s3.TryGetAlternates(g, out UInt16List l) ? List(l) : 0);
            LigatureSubstitution s4 = st.AsLigatureSubstitution();
            sum += Glyphs(s4.Coverage, g =>
            {
                long x = 0;
                for (int i = 0; i <= Math.Min(s4.GetLigatureCount(g), 16); i++)
                {
                    Ligature lig = s4.GetLigature(g, i);
                    x += lig.LigatureGlyph + lig.ComponentCount + List(lig.FollowingComponents);
                }

                return x;
            });
            ReverseChainSingleSubstitution s8 = st.AsReverseChainSingleSubstitution();
            sum += Coverage(s8.Coverage) + List(s8.Substitutes);
            for (int i = 0; i <= Math.Min(s8.BacktrackCount, Cap); i++)
            {
                sum += Coverage(s8.GetBacktrack(i));
            }

            for (int i = 0; i <= Math.Min(s8.LookaheadCount, Cap); i++)
            {
                sum += Coverage(s8.GetLookahead(i));
            }
        }
        else
        {
            SinglePositioning p1 = st.AsSinglePositioning();
            sum += (int)p1.ValueFormat + Glyphs(p1.Coverage, g => p1.TryGetValue(g, out ValueRecord v) ? Value(v) : 0);
            PairPositioning p2 = st.AsPairPositioning();
            sum += (int)p2.ValueFormat1 + (int)p2.ValueFormat2 + p2.Class1Count + p2.Class2Count + p2.ClassDefinition1.GetClass(3) + p2.ClassDefinition2.GetClass(3);
            sum += Glyphs(p2.Coverage, g =>
            {
                long x = 0;
                for (int second = 0; second < 64; second++)
                {
                    if (p2.TryGetPair(g, second, out ValueRecord a, out ValueRecord b))
                    {
                        x += Value(a) + Value(b);
                    }
                }

                return x;
            });
            CursivePositioning p3 = st.AsCursivePositioning();
            sum += Glyphs(p3.Coverage, g => p3.TryGetAnchors(g, out Anchor a, out Anchor b) ? Anchor(a) + Anchor(b) : 0);
            MarkAttachment m = st.AsMarkAttachment();
            sum += m.LookupType + m.MarkClassCount + Coverage(m.MarkCoverage) + Coverage(m.TargetCoverage);
            for (int i = 0; i <= Cap; i++)
            {
                sum += m.TryGetMark(i, out int cls, out Anchor a) ? cls + Anchor(a) : 0;
                for (int c = 0; c <= Math.Min(m.MarkClassCount, 8); c++)
                {
                    sum += Anchor(m.GetTargetAnchor(i, c));
                    for (int k = 0; k <= Math.Min(m.GetComponentCount(i), 8); k++)
                    {
                        sum += Anchor(m.GetLigatureAnchor(i, k, c));
                    }
                }
            }
        }

        return sum + Context(st.AsContext()) + Context(st.AsChainedContext());
    }
}
