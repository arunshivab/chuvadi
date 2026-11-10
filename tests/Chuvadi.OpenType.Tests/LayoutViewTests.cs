using System.Linq;
using Chuvadi.OpenType.Layout;
using Xunit;

namespace Chuvadi.OpenType.Tests;

public class LayoutViewTests
{
    private static OpenTypeFont Font(string tag, Node table, int axes = -1, Node? gdef = null)
    {
        FontBuilder b = FontBuilder.Basic(200).Table(tag, table.Build());
        if (axes >= 0)
        {
            b.Table("fvar", L.Fvar(axes));
        }

        if (gdef is not null)
        {
            b.Table("GDEF", gdef.Build());
        }

        return OpenTypeFont.Load(b.Build());
    }

    private static Node Scripts(Node latnDefault, Node trk)
    {
        Node dflt = L.N().O16(L.LangSys(0xFFFF, 0)).U16(0);
        Node latn = L.N().O16(latnDefault).U16(1).TagField("TRK ").O16(trk);
        return L.N().U16(2).TagField("DFLT").O16(dflt).TagField("latn").O16(latn);
    }

    private static Node Features(params (string Tag, Node Feature)[] features)
    {
        Node n = L.N().U16(features.Length);
        foreach ((string t, Node f) in features)
        {
            n.TagField(t).O16(f);
        }

        return n;
    }

    internal static Node Gsub()
    {
        Node l0 = L.Lookup(1, 0, 0, L.N().U16(1).O16(L.Cov1(10, 11)).U16(0xFFFD));
        Node l1 = L.Lookup(1, 0x0218, 1, L.N().U16(2).O16(L.Cov2((20, 22))).U16(3, 30, 31, 32));
        Node l2 = L.Lookup(2, 0, 0, L.N().U16(1).O16(L.Cov1(5)).U16(1).O16(L.N().U16(3, 1, 2, 3)));
        Node l3 = L.Lookup(3, 0, 0, L.N().U16(1).O16(L.Cov1(6)).U16(1).O16(L.N().U16(2, 7, 8)));
        Node set1 = L.List(L.N().U16(50, 3, 2, 3), L.N().U16(51, 2, 2));
        Node set4 = L.List(L.N().U16(52, 2, 5));
        Node l4 = L.Lookup(4, 0, 0, L.N().U16(1).O16(L.Cov1(1, 4)).U16(2).O16(set1).O16(set4));
        Node ctx3 = L.N().U16(3, 2, 1).O16(L.Cov1(1, 2)).O16(L.Cov1(3)).U16(1, 0);
        Node l5 = L.Lookup(5, 0, 0, ctx3);
        Node chainRule = L.N().U16(1, 9).U16(2, 3).U16(1, 4).U16(1, 0, 2);
        Node chain1 = L.N().U16(1).O16(L.Cov1(2)).U16(1).O16(L.List(chainRule));
        Node l6 = L.Lookup(6, 0, 0, chain1);
        Node extTarget = L.N().U16(2).O16(L.Cov1(40)).U16(1, 41);
        Node l7 = L.Lookup(7, 0, 0, L.N().U16(1, 1).O32(extTarget));
        Node rev = L.N().U16(1).O16(L.Cov1(60, 61)).U16(1).O16(L.Cov1(70)).U16(2).O16(L.Cov1(71)).O16(L.Cov1(72)).U16(2, 62, 63);
        Node l8 = L.Lookup(8, 0, 0, rev);
        Node classRule = L.N().U16(3, 1).U16(2, 1).U16(1, 3);
        Node ctx2 = L.N().U16(2).O16(L.Cov1(1)).O16(L.Class1(1, 1, 2)).U16(2).O16(null).O16(L.List(classRule));
        Node l9 = L.Lookup(5, 0, 0, ctx2);
        Node lookups = L.List(l0, l1, l2, l3, l4, l5, l6, l7, l8, l9);

        Node features = Features(("liga", L.Feature(0, 4)), ("rclt", L.Feature()));

        // FeatureVariations: only record 4 applies at the default instance (see the test).
        Node cond(int format, int axis, int min, int max) => L.N().U16(format, axis, min, max);
        Node set(params Node[] conditions)
        {
            Node n = L.N().U16(conditions.Length);
            foreach (Node c in conditions)
            {
                n.O32(c);
            }

            return n;
        }

        Node subst(int major, int feature, Node alternate) => L.N().U16(major, 0, 1, feature).O32(alternate);
        Node fv = L.N().U16(1, 0).U32(5)
            .O32(set(cond(1, 0, 0x2000, 0x4000))).O32(subst(1, 1, L.Feature(9)))
            .O32(set(cond(1, 5, unchecked((short)0xC000), 0x4000))).O32(subst(1, 1, L.Feature(9)))
            .O32(set(cond(2, 0, 0, 0))).O32(subst(1, 1, L.Feature(9)))
            .O32(set(cond(1, 0, unchecked((short)0xC000), 0))).O32(subst(2, 1, L.Feature(9)))
            .O32(null).O32(subst(1, 1, L.Feature(0)));

        return L.LayoutTable(Scripts(L.LangSys(0xFFFF, 0, 1), L.LangSys(1, 0)), features, lookups, fv);
    }

    [Fact]
    public void ScriptsLanguagesAndFeatures()
    {
        LayoutTable gsub = Font("GSUB", Gsub(), axes: 1).Gsub!;
        Assert.Equal(LayoutTableKind.Gsub, gsub.Kind);
        Assert.Equal(1, gsub.MinorVersion);
        Assert.Equal(2, gsub.Scripts.Count);
        Assert.True(gsub.Scripts.TryFind(Tag.Parse("latn"), out Script latn));
        Assert.False(gsub.Scripts.TryFind(Tag.Parse("cyrl"), out _));
        Assert.Equal(-1, latn.DefaultLanguageSystem.RequiredFeatureIndex);
        Assert.Equal(new[] { 0, 1 }, Enumerable.Range(0, latn.DefaultLanguageSystem.FeatureIndices.Count).Select(i => latn.DefaultLanguageSystem.FeatureIndices[i]));
        Assert.True(latn.TryFindLanguageSystem(Tag.Parse("TRK "), out LanguageSystem trk));
        Assert.Equal(1, trk.RequiredFeatureIndex);
        Assert.Equal(Tag.Parse("liga"), gsub.Features.GetTag(0));
        Assert.Equal(2, gsub.Features.GetFeature(0).LookupIndices.Count);
        Assert.Equal(4, gsub.Features.GetFeature(0).LookupIndices[1]);
        Assert.Equal(0, gsub.Features.GetFeature(1).LookupIndices.Count);
    }

    [Fact]
    public void FeatureVariationsAtTheDefaultInstance()
    {
        // Record 0: range [0.5, 1] misses 0. Record 1: axis 5 is not below the axis count, ignored.
        // Record 2: condition format 2, not matched. Record 3: matches but its substitution is version 2, skipped.
        // Record 4: null condition set, universal: rclt (feature 1) gets lookup 0.
        LayoutTable gsub = Font("GSUB", Gsub(), axes: 1).Gsub!;
        Assert.Equal(4, gsub.DefaultInstanceVariation);
        Feature rclt = gsub.GetFeatureAtDefaultInstance(1);
        Assert.Equal(Tag.Parse("rclt"), rclt.Tag);
        Assert.Equal(1, rclt.LookupIndices.Count);
        Assert.Equal(0, rclt.LookupIndices[0]);
        Assert.Equal(2, gsub.GetFeatureAtDefaultInstance(0).LookupIndices.Count);
    }

    [Fact]
    public void FeatureVariationsWithoutFvarIgnoreAxisRecords()
    {
        // No 'fvar': axis 0 is not below an axis count of 0, so every axis record is ignored; record 4 still applies.
        LayoutTable gsub = Font("GSUB", Gsub()).Gsub!;
        Assert.Equal(4, gsub.DefaultInstanceVariation);
    }

    [Fact]
    public void SingleSubstitution()
    {
        LookupList lookups = Font("GSUB", Gsub(), axes: 1).Gsub!.Lookups;
        SingleSubstitution f1 = lookups.GetLookup(0).GetSubtable(0).AsSingleSubstitution();
        Assert.True(f1.TryGetSubstitute(10, out int s));
        Assert.Equal(7, s); // 10 + 0xFFFD modulo 65536
        Assert.True(f1.TryGetSubstitute(11, out s));
        Assert.Equal(8, s);
        Assert.False(f1.TryGetSubstitute(12, out _));

        Lookup l1 = lookups.GetLookup(1);
        Assert.Equal(1, l1.MarkFilteringSet);
        Assert.Equal(2, l1.MarkAttachmentClass);
        Assert.True(l1.Flags.HasFlag(LookupFlags.UseMarkFilteringSet));
        Assert.True(l1.Flags.HasFlag(LookupFlags.IgnoreMarks));
        SingleSubstitution f2 = l1.GetSubtable(0).AsSingleSubstitution();
        Assert.True(f2.TryGetSubstitute(21, out s));
        Assert.Equal(31, s);
        Assert.Equal(-1, lookups.GetLookup(0).MarkFilteringSet);
    }

    [Fact]
    public void MultipleAlternateAndLigature()
    {
        LookupList lookups = Font("GSUB", Gsub(), axes: 1).Gsub!.Lookups;
        Assert.True(lookups.GetLookup(2).GetSubtable(0).AsMultipleSubstitution().TryGetSequence(5, out UInt16List seq));
        Assert.Equal(3, seq.Count);
        Assert.Equal(3, seq[2]);
        Assert.True(lookups.GetLookup(3).GetSubtable(0).AsAlternateSubstitution().TryGetAlternates(6, out UInt16List alts));
        Assert.Equal(8, alts[1]);
        LigatureSubstitution lig = lookups.GetLookup(4).GetSubtable(0).AsLigatureSubstitution();
        Assert.Equal(2, lig.GetLigatureCount(1));
        Ligature first = lig.GetLigature(1, 0);
        Assert.Equal(50, first.LigatureGlyph);
        Assert.Equal(3, first.ComponentCount);
        Assert.Equal(3, first.FollowingComponents[1]);
        Assert.Equal(52, lig.GetLigature(4, 0).LigatureGlyph);
        Assert.Equal(0, lig.GetLigatureCount(2));
    }

    [Fact]
    public void ContextualFormats()
    {
        LookupList lookups = Font("GSUB", Gsub(), axes: 1).Gsub!.Lookups;
        ContextualSubtable c3 = lookups.GetLookup(5).GetSubtable(0).AsContext();
        Assert.Equal(3, c3.Format);
        ContextRule r3 = c3.GetFormat3Rule();
        Assert.Equal(2, r3.InputCount);
        Assert.True(r3.UsesCoverages);
        Assert.Equal(0, r3.GetInputCoverage(1).GetIndex(3));
        Assert.Equal((1, 0), r3.GetLookupRecord(0));

        ContextualSubtable c6 = lookups.GetLookup(6).GetSubtable(0).AsChainedContext();
        Assert.True(c6.IsChained);
        ContextRule r6 = c6.GetRule(0, 0);
        Assert.Equal(1, r6.BacktrackCount);
        Assert.Equal(9, r6.GetBacktrack(0));
        Assert.Equal(2, r6.InputCount);
        Assert.Equal(3, r6.GetInput(1));
        Assert.Equal(1, r6.LookaheadCount);
        Assert.Equal(4, r6.GetLookahead(0));
        Assert.Equal((0, 2), r6.GetLookupRecord(0));

        ContextualSubtable c2 = lookups.GetLookup(9).GetSubtable(0).AsContext();
        Assert.Equal(2, c2.Format);
        Assert.Equal(1, c2.InputClassDefinition.GetClass(1));
        Assert.Equal(0, c2.GetRuleCount(0));
        ContextRule r2 = c2.GetRule(1, 0);
        Assert.Equal(3, r2.InputCount);
        Assert.Equal(2, r2.GetInput(1));
        Assert.Equal((1, 3), r2.GetLookupRecord(0));
    }

    [Fact]
    public void ExtensionAndReverseChain()
    {
        LookupList lookups = Font("GSUB", Gsub(), axes: 1).Gsub!.Lookups;
        LookupSubtable ext = lookups.GetLookup(7).GetSubtable(0);
        Assert.Equal(1, ext.LookupType);
        Assert.True(ext.AsSingleSubstitution().TryGetSubstitute(40, out int s));
        Assert.Equal(41, s);

        ReverseChainSingleSubstitution rev = lookups.GetLookup(8).GetSubtable(0).AsReverseChainSingleSubstitution();
        Assert.True(rev.Exists);
        Assert.Equal(1, rev.BacktrackCount);
        Assert.Equal(0, rev.GetBacktrack(0).GetIndex(70));
        Assert.Equal(2, rev.LookaheadCount);
        Assert.Equal(0, rev.GetLookahead(1).GetIndex(72));
        Assert.Equal(63, rev.Substitutes[rev.Coverage.GetIndex(61)]);
    }

    [Fact]
    public void CoverageAndClassFormats()
    {
        LookupList lookups = Font("GSUB", Gsub(), axes: 1).Gsub!.Lookups;
        Coverage c2 = lookups.GetLookup(1).GetSubtable(0).AsSingleSubstitution().Coverage;
        Assert.Equal(2, c2.Format);
        Assert.Equal(2, c2.GetIndex(22));
        Assert.Equal(-1, c2.GetIndex(23));
        Assert.Equal(-1, c2.GetIndex(-1));
        Assert.Equal(new[] { (20, 0), (21, 1), (22, 2) }, c2.GetGlyphs().ToArray());

        Node classes = L.N().U16(2).O16(L.Cov1(1)).O16(L.Class2((10, 12, 3), (20, 20, 5))).U16(0);
        ContextualSubtable sub = Font("GSUB", L.LayoutTable(Scripts(L.LangSys(0xFFFF), L.LangSys(0xFFFF)), Features(), L.List(L.Lookup(5, 0, 0, classes)))).Gsub!
            .Lookups.GetLookup(0).GetSubtable(0).AsContext();
        Assert.Equal(3, sub.InputClassDefinition.GetClass(11));
        Assert.Equal(5, sub.InputClassDefinition.GetClass(20));
        Assert.Equal(0, sub.InputClassDefinition.GetClass(13));
    }

    internal static Node Gpos()
    {
        Node device = L.N().U16(10, 12, 1, 0x7800);
        Node p0 = L.Lookup(1, 0, 0, L.N().U16(1).O16(L.Cov1(3)).U16(0x0045, unchecked((short)-5), 100).O16(device));
        Node p1 = L.Lookup(1, 0, 0, L.N().U16(2).O16(L.Cov1(3, 4)).U16(0x0008, 2, 7, 8));
        Node pairDevice = L.N().U16(8, 8, 2, 0x3000);
        Node pairSet = L.N().U16(2).U16(2, unchecked((short)-50)).O16(pairDevice).U16(0).U16(5, unchecked((short)-60)).O16(null).U16(3);
        Node p2 = L.Lookup(2, 0, 0, L.N().U16(1).O16(L.Cov1(1)).U16(0x0044, 0x0001, 1).O16(pairSet));
        Node p3 = L.Lookup(2, 0, 0, L.N().U16(2).O16(L.Cov1(1, 2)).U16(0x0004, 0).O16(L.Class1(2, 1)).O16(L.Class1(9, 1)).U16(2, 2)
            .U16(0, unchecked((short)-10), 0, unchecked((short)-20)));
        Node p4 = L.Lookup(3, 0, 0, L.N().U16(1).O16(L.Cov1(11, 12)).U16(2).O16(L.Anchor1(10, 20)).O16(null).O16(null).O16(L.Anchor2(30, 40, 5)));
        Node markArray = L.N().U16(1).U16(1).O16(L.Anchor1(5, 6));
        Node baseArray = L.N().U16(1).O16(L.Anchor1(1, 2)).O16(L.Anchor3(3, 4, L.N().U16(9, 9, 3, 0x7000), null));
        Node p5 = L.Lookup(4, 0, 0, L.N().U16(1).O16(L.Cov1(100)).O16(L.Cov1(1)).U16(2).O16(markArray).O16(baseArray));
        Node ligAttach = L.N().U16(2).O16(L.Anchor1(100, 0)).O16(null);
        Node p6 = L.Lookup(5, 0, 0, L.N().U16(1).O16(L.Cov1(101)).O16(L.Cov1(60)).U16(1).O16(L.N().U16(1).U16(0).O16(L.Anchor1(7, 8)))
            .O16(L.N().U16(1).O16(ligAttach)));
        Node p7 = L.Lookup(6, 0, 0, L.N().U16(1).O16(L.Cov1(102)).O16(L.Cov1(100)).U16(1).O16(L.N().U16(1).U16(0).O16(L.Anchor1(1, 1)))
            .O16(L.N().U16(1).O16(L.Anchor1(0, 50))));
        Node rule = L.N().U16(2, 1).U16(4).U16(1, 0);
        Node p8 = L.Lookup(7, 0, 0, L.N().U16(1).O16(L.Cov1(3)).U16(1).O16(L.List(rule)));
        Node p9 = L.Lookup(8, 0, 0, L.N().U16(3).U16(1).O16(L.Cov1(1)).U16(1).O16(L.Cov1(2)).U16(0).U16(1, 0, 2));
        Node p10 = L.Lookup(9, 0, 0, L.N().U16(1, 1).O32(L.N().U16(1).O16(L.Cov1(7)).U16(0x0004, 30)));
        Node p11 = L.Lookup(9, 0, 0, L.N().U16(1, 9).O32(L.N().U16(1).O16(L.Cov1(7)).U16(0x0004, 30)));
        return L.LayoutTable(Scripts(L.LangSys(0xFFFF, 0), L.LangSys(0xFFFF, 0)), Features(("kern", L.Feature(2, 3))),
            L.List(p0, p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11));
    }

    [Fact]
    public void SingleAndPairPositioning()
    {
        LookupList lookups = Font("GPOS", Gpos()).Gpos!.Lookups;
        SinglePositioning s1 = lookups.GetLookup(0).GetSubtable(0).AsSinglePositioning();
        Assert.True(s1.TryGetValue(3, out ValueRecord v));
        Assert.Equal(-5, v.XPlacement);
        Assert.Equal(100, v.XAdvance);
        Assert.Equal(0, v.YAdvance);
        Assert.True(v.XAdvanceDevice.Exists);
        Assert.Equal(1, v.XAdvanceDevice.GetDelta(10));
        Assert.Equal(-1, v.XAdvanceDevice.GetDelta(11));
        Assert.Equal(-2, v.XAdvanceDevice.GetDelta(12));
        Assert.Equal(0, v.XAdvanceDevice.GetDelta(13));
        Assert.Equal(0, v.XAdvanceDevice.DefaultInstanceDelta);
        Assert.False(s1.TryGetValue(4, out _));

        Assert.True(lookups.GetLookup(1).GetSubtable(0).AsSinglePositioning().TryGetValue(4, out v));
        Assert.Equal(8, v.YAdvance);

        PairPositioning p1 = lookups.GetLookup(2).GetSubtable(0).AsPairPositioning();
        Assert.True(p1.TryGetPair(1, 2, out ValueRecord a, out ValueRecord b));
        Assert.Equal(-50, a.XAdvance);
        Assert.Equal(3, a.XAdvanceDevice.GetDelta(8)); // 4-bit deltas, first nibble of 0x3000
        Assert.True(p1.TryGetPair(1, 5, out a, out b));
        Assert.Equal(-60, a.XAdvance);
        Assert.Equal(3, b.XPlacement);
        Assert.False(a.XAdvanceDevice.Exists);
        Assert.False(p1.TryGetPair(1, 3, out _, out _));

        PairPositioning p2 = lookups.GetLookup(3).GetSubtable(0).AsPairPositioning();
        Assert.True(p2.TryGetPair(1, 9, out a, out _));
        Assert.Equal(-10, a.XAdvance);
        Assert.True(p2.TryGetPair(2, 9, out a, out _));
        Assert.Equal(-20, a.XAdvance);
        Assert.True(p2.TryGetPair(2, 8, out a, out _));
        Assert.Equal(0, a.XAdvance);
        Assert.False(p2.TryGetPair(3, 9, out _, out _));
    }

    [Fact]
    public void CursiveAndMarkAttachment()
    {
        LookupList lookups = Font("GPOS", Gpos()).Gpos!.Lookups;
        CursivePositioning cur = lookups.GetLookup(4).GetSubtable(0).AsCursivePositioning();
        Assert.True(cur.TryGetAnchors(11, out Anchor entry, out Anchor exit));
        Assert.Equal((10, 20), (entry.X, entry.Y));
        Assert.False(exit.Exists);
        Assert.True(cur.TryGetAnchors(12, out entry, out exit));
        Assert.False(entry.Exists);
        Assert.Equal((30, 40, 5), (exit.X, exit.Y, exit.AnchorPoint));

        MarkAttachment mb = lookups.GetLookup(5).GetSubtable(0).AsMarkAttachment();
        Assert.Equal(4, mb.LookupType);
        Assert.True(mb.TryGetMark(mb.MarkCoverage.GetIndex(100), out int cls, out Anchor mark));
        Assert.Equal(1, cls);
        Assert.Equal((5, 6), (mark.X, mark.Y));
        Anchor b1 = mb.GetTargetAnchor(mb.TargetCoverage.GetIndex(1), 1);
        Assert.Equal(3, b1.Format);
        Assert.Equal(112, b1.XDevice.GetDelta(9)); // 8-bit deltas, first byte of 0x7000
        Assert.False(mb.GetTargetAnchor(0, 2).Exists);

        MarkAttachment ml = lookups.GetLookup(6).GetSubtable(0).AsMarkAttachment();
        Assert.Equal(5, ml.LookupType);
        Assert.Equal(2, ml.GetComponentCount(0));
        Assert.Equal(100, ml.GetLigatureAnchor(0, 0, 0).X);
        Assert.False(ml.GetLigatureAnchor(0, 1, 0).Exists);
        Assert.False(ml.GetLigatureAnchor(0, 2, 0).Exists);

        MarkAttachment mm = lookups.GetLookup(7).GetSubtable(0).AsMarkAttachment();
        Assert.Equal(6, mm.LookupType);
        Assert.Equal(50, mm.GetTargetAnchor(0, 0).Y);
    }

    [Fact]
    public void ContextualPositioningAndExtension()
    {
        LookupList lookups = Font("GPOS", Gpos()).Gpos!.Lookups;
        ContextualSubtable c7 = lookups.GetLookup(8).GetSubtable(0).AsContext();
        Assert.Equal(1, c7.Format);
        Assert.Equal(4, c7.GetRule(0, 0).GetInput(1));
        ContextualSubtable c8 = lookups.GetLookup(9).GetSubtable(0).AsChainedContext();
        Assert.Equal(3, c8.Format);
        ContextRule r = c8.GetFormat3Rule();
        Assert.Equal((1, 1, 0), (r.BacktrackCount, r.InputCount, r.LookaheadCount));
        Assert.Equal(0, r.GetBacktrackCoverage(0).GetIndex(1));
        Assert.Equal((0, 2), r.GetLookupRecord(0));

        LookupSubtable ext = lookups.GetLookup(10).GetSubtable(0);
        Assert.Equal(1, ext.LookupType);
        Assert.True(ext.AsSinglePositioning().TryGetValue(7, out ValueRecord v));
        Assert.Equal(30, v.XAdvance);
        Assert.False(lookups.GetLookup(11).GetSubtable(0).Exists); // an extension of an extension is invalid
    }

    internal static Node GdefSample()
    {
        Node device = L.N().U16(10, 10, 1, 0x4000);
        Node attach = L.N().O16(L.Cov1(5)).U16(1).O16(L.N().U16(2, 3, 7));
        Node ligGlyph = L.N().U16(3).O16(L.N().U16(1, 250)).O16(L.N().U16(2, 4)).O16(L.N().U16(3, 500).O16(device));
        Node carets = L.N().O16(L.Cov1(50)).U16(1).O16(ligGlyph);
        Node markSets = L.N().U16(1, 2).O32(L.Cov1(100, 101)).O32(L.Cov1(102));
        return L.N().U16(1, 2).O16(L.Class2((1, 4, 1), (50, 50, 2), (100, 102, 3), (120, 120, 4), (130, 130, 9)))
            .O16(attach).O16(carets).O16(L.Class1(100, 1, 2)).O16(markSets);
    }

    [Fact]
    public void Gdef()
    {
        GdefTable g = Font("GSUB", Gsub(), gdef: GdefSample()).Gdef!;
        Assert.Equal(2, g.MinorVersion);
        Assert.True(g.HasGlyphClasses);
        Assert.Equal(GlyphClass.Base, g.GetGlyphClass(2));
        Assert.Equal(GlyphClass.Ligature, g.GetGlyphClass(50));
        Assert.Equal(GlyphClass.Mark, g.GetGlyphClass(101));
        Assert.Equal(GlyphClass.Component, g.GetGlyphClass(120));
        Assert.Equal(GlyphClass.Unclassified, g.GetGlyphClass(130));
        Assert.Equal(GlyphClass.Unclassified, g.GetGlyphClass(7));
        Assert.Equal(2, g.GetMarkAttachmentClass(101));
        Assert.Equal(2, g.MarkGlyphSetCount);
        Assert.True(g.IsInMarkGlyphSet(0, 101));
        Assert.False(g.IsInMarkGlyphSet(0, 102));
        Assert.True(g.IsInMarkGlyphSet(1, 102));
        Assert.False(g.IsInMarkGlyphSet(2, 102));
        UInt16List points = g.GetAttachmentPoints(5);
        Assert.Equal(2, points.Count);
        Assert.Equal(7, points[1]);
        Assert.Equal(0, g.GetAttachmentPoints(6).Count);
        Assert.Equal(3, g.GetLigatureCaretCount(50));
        Assert.Equal(250, g.GetLigatureCaret(50, 0, out bool isPoint));
        Assert.False(isPoint);
        Assert.Equal(4, g.GetLigatureCaret(50, 1, out isPoint));
        Assert.True(isPoint);
        Assert.Equal(500, g.GetLigatureCaret(50, 2, out isPoint));
        Assert.Equal(0, g.GetLigatureCaret(50, 3, out _));
        Assert.Equal(0, g.GetLigatureCaretCount(51));
    }

    [Fact]
    public void MissingOrUnsupportedTablesAreNull()
    {
        Assert.Null(OpenTypeFont.Load(FontBuilder.Basic(3).Build()).Gsub);
        Assert.Null(OpenTypeFont.Load(FontBuilder.Basic(3).Table("GSUB", new byte[] { 0, 2, 0, 0, 0, 10, 0, 10, 0, 10 }).Build()).Gsub);
        Assert.Null(OpenTypeFont.Load(FontBuilder.Basic(3).Table("GPOS", new byte[] { 0, 1, 0, 0, 0 }).Build()).Gpos);
        Assert.Null(OpenTypeFont.Load(FontBuilder.Basic(3).Table("GDEF", new byte[] { 0, 1, 0, 0 }).Build()).Gdef);
        Assert.Equal(0, OpenTypeFont.Load(FontBuilder.Basic(3).Build()).VariationAxisCount);
        Assert.Equal(2, OpenTypeFont.Load(FontBuilder.Basic(3).Table("fvar", L.Fvar(2)).Build()).VariationAxisCount);
    }
}
