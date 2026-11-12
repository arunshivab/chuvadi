using System;
using System.Collections.Generic;
using System.Linq;
using Chuvadi.OpenType;
using Chuvadi.OpenType.Tests;
using Xunit;

namespace Chuvadi.Text.Shaping.Tests;

/// <summary>
/// The default engine (phase 1) on small built fonts: every GSUB and GPOS lookup type, lookup flags, features and
/// ranges, fractions, ignorables and clusters (D-345), normalisation, limits, the plan cache and the D-354 fallbacks.
/// </summary>
public class EngineTests
{
    // Glyphs of the layout font (1000 units per em, base advances 500).
    private const int A = 1, B = 2, C = 3, F = 4, I = 5, Fi = 6, Acute = 7, Space = 8, X = 9, XAlt1 = 10, XAlt2 = 11;
    private const int One = 12, Two = 13, Slash = 14, OneNumr = 15, TwoDnom = 16, DotBelow = 17, D = 18, E = 19, G = 20, H = 22, EAcute = 23;

    private static readonly ShapingFont Layout = ShapingFont.Get(OpenTypeFont.Load(BuildLayoutFont()));

    // Glyphs of the fallback font (no GSUB, no GPOS; boxes as in the D-354 statement's font).
    private const int FbA = 1, FbSpace = 2, FbZero = 3, FbX = 4, FbF = 5, FbI = 6, FbB = 7;

    private static readonly ShapingFont NoGpos = ShapingFont.Get(OpenTypeFont.Load(BuildFallbackFont()));

    private static ShapedRun Shape(ShapingFont font, string text, params Feature[] features) =>
        TextShaper.Shape(font, text, new ShapeOptions { Features = features });

    private static int[] Glyphs(ShapedRun run) => run.Select(g => g.GlyphId).ToArray();

    private static int[] Clusters(ShapedRun run) => run.Select(g => g.Cluster).ToArray();

    // ---- GSUB ----

    [Fact]
    public void LigatureSubstitutionAndSwitchingItOff()
    {
        ShapedRun run = Shape(Layout, "fi");
        Assert.Equal(new[] { Fi }, Glyphs(run));
        Assert.Equal(new[] { 0 }, Clusters(run));
        Assert.Equal(550, run[0].XAdvance);
        Assert.Equal(new[] { F, I }, Glyphs(Shape(Layout, "fi", Feature.Create("liga", 0))));
    }

    [Fact]
    public void MultipleSubstitutionKeepsTheCluster()
    {
        ShapedRun run = Shape(Layout, "dc");
        Assert.Equal(new[] { A, B, C }, Glyphs(run));
        Assert.Equal(new[] { 0, 0, 1 }, Clusters(run));
    }

    [Fact]
    public void AlternateSubstitutionPicksValueMinusOneAndLeavesTheGlyphPastTheSet()
    {
        Assert.Equal(new[] { X }, Glyphs(Shape(Layout, "x")));
        Assert.Equal(new[] { XAlt1 }, Glyphs(Shape(Layout, "x", Feature.Create("salt"))));
        Assert.Equal(new[] { XAlt2 }, Glyphs(Shape(Layout, "x", Feature.Create("salt", 2))));
        Assert.Equal(new[] { X }, Glyphs(Shape(Layout, "x", Feature.Create("salt", 3))));
        Assert.Equal(new[] { X }, Glyphs(Shape(Layout, "x", Feature.Create("salt", 5))));
    }

    [Fact]
    public void ChainedContextAppliesItsNestedSingleSubstitution()
    {
        Assert.Equal(new[] { G, A }, Glyphs(Shape(Layout, "ca")));
        Assert.Equal(new[] { C, B }, Glyphs(Shape(Layout, "cb")));
        Assert.Equal(new[] { C, A }, Glyphs(Shape(Layout, "ca", Feature.Create("calt", 0))));
    }

    [Fact]
    public void ExtensionAndSequenceContextFormats1And2()
    {
        Assert.Equal(new[] { C }, Glyphs(Shape(Layout, "g", Feature.Create("ss05"))));
        Assert.Equal(new[] { D, A }, Glyphs(Shape(Layout, "ea", Feature.Create("ss06"))));
        Assert.Equal(new[] { E, B }, Glyphs(Shape(Layout, "eb", Feature.Create("ss06"))));
        Assert.Equal(new[] { X, A }, Glyphs(Shape(Layout, "ba", Feature.Create("ss07"))));
        Assert.Equal(new[] { B, B }, Glyphs(Shape(Layout, "bb", Feature.Create("ss07"))));
    }

    [Fact]
    public void NestingDepthLimitStopsNestedLookups()
    {
        ShapedRun run = TextShaper.Shape(Layout, "ca", new ShapeOptions { Limits = new ShapingLimits { MaxNestingDepth = 1 } });
        Assert.Equal(new[] { C, A }, Glyphs(run));
        Assert.Contains(run.Diagnostics, d => d.Kind == ShapingDiagnosticKind.LimitReached);
    }

    [Fact]
    public void ReverseChainingSubstitutionAndRangedSettings()
    {
        Assert.Equal(new[] { B, A }, Glyphs(Shape(Layout, "ba")));
        Assert.Equal(new[] { E, A, E, A }, Glyphs(Shape(Layout, "baba", Feature.Create("ss01"))));
        Assert.Equal(new[] { B, A, E, A }, Glyphs(Shape(Layout, "baba", new Feature(Tag.Parse("ss01"), 1, 2, 3))));
    }

    [Fact]
    public void LaterSettingForTheSameTagAndRangeReplacesTheEarlier()
    {
        Assert.Equal(new[] { F, I }, Glyphs(Shape(Layout, "fi", Feature.Create("liga", 0))));
        Assert.Equal(new[] { Fi }, Glyphs(Shape(Layout, "fi", Feature.Create("liga", 0), Feature.Create("liga", 1))));
    }

    [Fact]
    public void UseDefaultFeaturesFalseLeavesOnlyAlwaysOnAndRequested()
    {
        ShapeOptions off = new ShapeOptions { UseDefaultFeatures = false };
        Assert.Equal(new[] { F, I }, Glyphs(TextShaper.Shape(Layout, "fi", off)));
        Assert.Equal(new[] { Fi }, Glyphs(TextShaper.Shape(Layout, "fi", new ShapeOptions { UseDefaultFeatures = false, Features = new[] { Feature.Create("liga") } })));
    }

    [Fact]
    public void IgnoreMarksFlagLetsALigatureFormAcrossAMark()
    {
        ShapedRun run = Shape(Layout, "a\u0301b", Feature.Create("ss02"));
        Assert.Equal(new[] { Fi, Acute }, Glyphs(run));
        Assert.Equal(new[] { 0, 0 }, Clusters(run));
        Assert.Equal(new[] { A, Acute, B }, Glyphs(Shape(Layout, "a\u0301b", Feature.Create("ss03"))));
    }

    [Fact]
    public void MarkAttachmentTypeAndMarkFilteringSetFlags()
    {
        // Attachment type 1 skips marks of any other class: the dot below (class 2) is skipped, the acute (class 1) is not.
        Assert.Equal(new[] { Fi, DotBelow }, Glyphs(Shape(Layout, "a\u0323b", Feature.Create("ss08"))));
        Assert.Equal(new[] { A, Acute, B }, Glyphs(Shape(Layout, "a\u0301b", Feature.Create("ss08"))));

        // Mark filtering set 0 = {dot below}: marks outside it are skipped.
        Assert.Equal(new[] { Fi, Acute }, Glyphs(Shape(Layout, "a\u0301b", Feature.Create("ss09"))));
        Assert.Equal(new[] { A, DotBelow, B }, Glyphs(Shape(Layout, "a\u0323b", Feature.Create("ss09"))));
    }

    [Fact]
    public void AutomaticFractionsAndTheirSwitch()
    {
        Assert.Equal(new[] { OneNumr, Slash, TwoDnom }, Glyphs(Shape(Layout, "1\u20442")));
        Assert.Equal(new[] { One, Slash, Two }, Glyphs(Shape(Layout, "1\u20442", Feature.Create("frac", 0))));
        Assert.Equal(new[] { Slash, Two }, Glyphs(Shape(Layout, "\u20442")));
    }

    [Fact]
    public void NormalisationComposesOnlyIntoGlyphsTheFontHas()
    {
        Assert.Equal(new[] { EAcute }, Glyphs(Shape(Layout, "e\u0301")));
        Assert.Equal(new[] { EAcute }, Glyphs(Shape(Layout, "\u00E9")));
        Assert.Equal(new[] { A, Acute }, Glyphs(Shape(Layout, "\u00E1")));
        Assert.Equal(new[] { 0, 0 }, Clusters(Shape(Layout, "\u00E1")));
    }

    [Fact]
    public void MarksAreCanonicallyOrderedEvenWhenTheClusterIsKeptAsGiven()
    {
        // U+0302 has no glyph, so neither form has fewer missing characters; the kept text is still reordered.
        Assert.Equal(new[] { A, DotBelow, Acute, 0 }, Glyphs(Shape(Layout, "a\u0301\u0302\u0323")));
    }

    // ---- GPOS ----

    [Fact]
    public void PairAdjustmentAndLegacyKernSwitch()
    {
        ShapedRun run = Shape(Layout, "ab");
        Assert.Equal(450, run[0].XAdvance);
        Assert.Equal(500, Shape(Layout, "ab", Feature.Create("kern", 0))[0].XAdvance);
    }

    [Fact]
    public void SingleAdjustment()
    {
        PositionedGlyph g = Shape(Layout, "b", Feature.Create("ss04"))[0];
        Assert.Equal((530, 10, 20), (g.XAdvance, g.XOffset, g.YOffset));
    }

    [Fact]
    public void ChainedContextPositioning()
    {
        Assert.Equal(507, Shape(Layout, "ac", Feature.Create("ss10"))[0].XAdvance);
        Assert.Equal(500, Shape(Layout, "ac")[0].XAdvance);
        Assert.Equal(450, Shape(Layout, "ab", Feature.Create("ss10"))[0].XAdvance);
    }

    [Fact]
    public void CursiveAttachmentJoinsExitToEntry()
    {
        ShapedRun run = Shape(Layout, "hh");
        Assert.Equal(400, run[0].XAdvance);
        Assert.Equal(100, run[1].YOffset - run[0].YOffset);
    }

    [Fact]
    public void MarkToBaseZeroesTheAdvanceAndPlacesOnTheAnchor()
    {
        ShapedRun run = Shape(Layout, "a\u0301");
        Assert.Equal(new[] { A, Acute }, Glyphs(run));
        Assert.Equal((0, -300, 600), (run[1].XAdvance, run[1].XOffset, run[1].YOffset));
        ShapedRun below = Shape(Layout, "a\u0323");
        Assert.Equal((-300, -100), (below[1].XOffset, below[1].YOffset));
    }

    [Fact]
    public void MarkToMarkStacksOnThePreviousMark()
    {
        ShapedRun run = Shape(Layout, "a\u0301\u0301");
        Assert.Equal((-300, 600), (run[1].XOffset, run[1].YOffset));
        Assert.Equal((-350, 800), (run[2].XOffset, run[2].YOffset));
        ShapedRun off = Shape(Layout, "a\u0301\u0301", Feature.Create("mkmk", 0));
        Assert.Equal((0, -300, 600), (off[2].XAdvance, off[2].XOffset, off[2].YOffset));
    }

    [Fact]
    public void MarkToLigatureUsesTheComponentTheMarkFollowed()
    {
        // ss02 forms the ligature across the acute, which followed the first component (a).
        ShapedRun run = Shape(Layout, "a\u0301b", Feature.Create("ss02"));
        Assert.Equal(new[] { Fi, Acute }, Glyphs(run));
        Assert.Equal((0, -500, 700), (run[1].XAdvance, run[1].XOffset, run[1].YOffset));
    }

    [Fact]
    public void LegacyKernTableOnlyWithoutGpos()
    {
        ShapingFont font = ShapingFont.Get(OpenTypeFont.Load(FontBuilder.Basic(3, unitsPerEm: 1000, advances: new[] { 500, 500, 500 })
            .Table("cmap", FontBuilder.Cmap((3, 10, FontBuilder.CmapFormat12Or13(12, (0x41, 0x42, 1)))))
            .Table("kern", FontBuilder.KernFormat0((1, 2, -80))).Build()));
        Assert.Equal(420, Shape(font, "AB")[0].XAdvance);
        Assert.Equal(500, Shape(font, "AB", Feature.Create("kern", 0))[0].XAdvance);
        Assert.Equal(500, Shape(font, "BA")[0].XAdvance);
    }

    // ---- Ignorables and clusters (E8, D-345) ----

    [Fact]
    public void JoinersBecomeTheSpaceGlyphWithZeroAdvanceAndPositioningSkipsThem()
    {
        ShapedRun run = Shape(Layout, "a\u200Db");
        Assert.Equal(new[] { A, Space, B }, Glyphs(run));
        Assert.Equal(new[] { 450, 0, 500 }, run.Select(g => g.XAdvance));
    }

    [Theory]
    [InlineData("a\u200Db", new[] { 0, 0, 2 })]
    [InlineData("a\u200Cb", new[] { 0, 1, 2 })]
    [InlineData("a\u200C\u200Db", new[] { 0, 1, 1, 3 })]
    [InlineData("a\u200D\u200Db", new[] { 0, 0, 0, 3 })]
    [InlineData("a\u200D\u200Cb", new[] { 0, 0, 2, 3 })]
    [InlineData("\u200Da", new[] { 0, 1 })]
    public void JoinerClusters(string text, int[] clusters)
    {
        Assert.Equal(clusters, Clusters(Shape(Layout, text)));
    }

    // ---- Limits and the plan cache ----

    [Fact]
    public void OperationBudgetStopsShapingAndReportsLimitReached()
    {
        ShapeOptions tight = new ShapeOptions { Limits = new ShapingLimits { MinOperations = 1, MaxOperationsPerGlyph = 1 } };
        // Every 'a' is covered by four requested lookups and 'kern', so the work is about five operations per glyph.
        Feature[] four = { Feature.Create("ss02"), Feature.Create("ss03"), Feature.Create("ss08"), Feature.Create("ss09") };
        string text = new string('a', 20);
        ShapedRun run = TextShaper.Shape(Layout, text, new ShapeOptions { Features = four, Limits = tight.Limits });
        Assert.Contains(run.Diagnostics, d => d.Kind == ShapingDiagnosticKind.LimitReached);
        Assert.DoesNotContain(TextShaper.Shape(Layout, text, new ShapeOptions { Features = four }).Diagnostics, d => d.Kind == ShapingDiagnosticKind.LimitReached);
    }

    [Fact]
    public void LimitsValidateTheirValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShapingLimits { MaxNestingDepth = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShapingLimits { MaxOperationsPerGlyph = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShapingLimits { MinOperations = 0 });
        Assert.Throws<ArgumentNullException>(() => new ShapeOptions { Limits = null! });
        Assert.Equal(16, ShapingLimits.Default.MaxNestingDepth);
        Assert.Equal(64, ShapingLimits.Default.MaxOperationsPerGlyph);
        Assert.Equal(16384, ShapingLimits.Default.MinOperations);
    }

    [Fact]
    public void PlanCacheReusesEqualOptionsAndDropsTheLeastRecentlyUsed()
    {
        ShapingFont font = ShapingFont.Get(OpenTypeFont.Load(BuildLayoutFont()));
        ShapeOptions first = new ShapeOptions { Features = new[] { Feature.Create("salt", 2) } };
        Engine.ShapePlan plan = font.GetPlan(first);
        Assert.Same(plan, font.GetPlan(new ShapeOptions { Features = new[] { Feature.Create("salt", 2) } }));
        for (uint v = 3; v < 3 + 16; v++)
        {
            _ = font.GetPlan(new ShapeOptions { Features = new[] { Feature.Create("salt", v) } });
        }

        Assert.NotSame(plan, font.GetPlan(first));
    }

    // ---- D-354 fallbacks ----

    [Theory]
    [InlineData("A\u0301", -550, 762)]
    [InlineData("x\u0301", -500, 562)]
    [InlineData("A\u0323", -550, -762)]
    [InlineData("A\u0327", -550, -700)]
    [InlineData("A\u0334", -550, 0)]
    [InlineData("A\u0361A", -250, 762)]
    [InlineData("C\u0301", -500, 762)]
    [InlineData("f\u0308", -410, 762)]
    [InlineData("f\u0301", -410, 762)]
    [InlineData("i\u0301", -370, 752)]
    [InlineData("B\u0301", -540, 762)]
    [InlineData("A\u031B", -450, 700)]
    [InlineData("f\u031B", -450, 700)]
    [InlineData("i\u031B", -450, 690)]
    public void MissingMarkIsCentredOnTheBaseAdvanceAndPlacedByClass(string text, int x, int y)
    {
        ShapedRun run = Shape(NoGpos, text);
        PositionedGlyph mark = run[1];
        Assert.Equal(0, mark.GlyphId);
        Assert.Equal(run[0].Cluster, mark.Cluster);
        Assert.Equal((0, x, y), (mark.XAdvance, mark.XOffset, mark.YOffset));
    }

    [Fact]
    public void MissingMarkOnASpaceIsCentredOnTheAdvance()
    {
        Assert.Equal(-375, Shape(NoGpos, " \u0301")[1].XOffset);
    }

    [Fact]
    public void MissingMarksStackAndAreCanonicallyOrdered()
    {
        ShapedRun two = Shape(NoGpos, "A\u0301\u0301");
        Assert.Equal(new[] { 762, 1524 }, new[] { two[1].YOffset, two[2].YOffset });
        ShapedRun a = Shape(NoGpos, "A\u0301\u0323");
        ShapedRun b = Shape(NoGpos, "A\u0323\u0301");
        Assert.Equal(a.ToArray(), b.ToArray());
        Assert.Equal(new[] { -762, 762 }, new[] { a[1].YOffset, a[2].YOffset });
        ShapedRun dots = Shape(NoGpos, "A\u0323\u0323");
        Assert.Equal(-1524, dots[2].YOffset);
    }

    [Fact]
    public void MissingMarkWithNoBase()
    {
        PositionedGlyph g = Shape(NoGpos, "\u0301A")[0];
        Assert.Equal((0, 0, -500, 0), (g.GlyphId, g.XAdvance, g.XOffset, g.YOffset));
    }

    [Fact]
    public void MarkFallbackIsNotUsedInAFontWithGpos()
    {
        ShapedRun run = Shape(Layout, "a\u0302");
        Assert.Equal((0, 500, 0, 0), (run[1].GlyphId, run[1].XAdvance, run[1].XOffset, run[1].YOffset));
    }

    [Theory]
    [InlineData('\u2000', 500)]
    [InlineData('\u2002', 500)]
    [InlineData('\u2001', 1000)]
    [InlineData('\u2003', 1000)]
    [InlineData('\u3000', 1000)]
    [InlineData('\u2004', 333)]
    [InlineData('\u2005', 250)]
    [InlineData('\u2006', 167)]
    [InlineData('\u2009', 200)]
    [InlineData('\u200A', 63)]
    [InlineData('\u202F', 125)]
    [InlineData('\u205F', 222)]
    [InlineData('\u00A0', 250)]
    [InlineData('\u2007', 520)]
    [InlineData('\u2008', 250)]
    public void MissingSpaceTakesTheSpaceGlyphWithItsWidth(char space, int width)
    {
        ShapedRun run = Shape(NoGpos, "A" + space);
        Assert.Equal((FbSpace, width), (run[1].GlyphId, run[1].XAdvance));
        Assert.Empty(run.Diagnostics);
    }

    [Fact]
    public void ZeroWidthSpaceAndOghamSpaceMark()
    {
        Assert.Equal((FbSpace, 0), (Shape(NoGpos, "\u200B")[0].GlyphId, Shape(NoGpos, "\u200B")[0].XAdvance));
        ShapedRun ogham = Shape(NoGpos, "\u1680");
        Assert.Equal((0, 500), (ogham[0].GlyphId, ogham[0].XAdvance));
        Assert.Contains(ogham.Diagnostics, d => d.Kind == ShapingDiagnosticKind.MissingGlyph);
    }

    [Fact]
    public void SpaceTheFontHasIsNotReplaced()
    {
        Assert.Equal((FbSpace, 250), (Shape(NoGpos, " ")[0].GlyphId, Shape(NoGpos, " ")[0].XAdvance));
        Assert.Equal(FbZero, Shape(NoGpos, "0")[0].GlyphId);
        Assert.Equal(FbX, Shape(NoGpos, "x")[0].GlyphId);
        Assert.Equal(FbA, Shape(NoGpos, "A")[0].GlyphId);
    }

    // ---- Fonts ----

    private static byte[] BuildLayoutFont()
    {
        int[] advances = new int[24];
        Array.Fill(advances, 500);
        advances[F] = 300;
        advances[I] = 250;
        advances[Fi] = 550;
        advances[Acute] = 100;
        advances[DotBelow] = 100;
        advances[Space] = 250;

        byte[] cmap = FontBuilder.Cmap((3, 10, FontBuilder.CmapFormat12Or13(12,
            (0x20, 0x20, Space), (0x31, 0x31, One), (0x32, 0x32, Two), (0x61, 0x63, A), (0x64, 0x64, D), (0x65, 0x65, E),
            (0x66, 0x66, F), (0x67, 0x67, G), (0x68, 0x68, H), (0x69, 0x69, I), (0x78, 0x78, X), (0xE9, 0xE9, EAcute),
            (0x301, 0x301, Acute), (0x323, 0x323, DotBelow), (0x2044, 0x2044, Slash))));

        // GSUB lookups, in LookupList order.
        Node liga = L.Lookup(4, 0, 0, L.N().U16(1).O16(L.Cov1(F)).U16(1).O16(L.List(L.N().U16(Fi, 2, I))));
        Node salt = L.Lookup(3, 0, 0, L.N().U16(1).O16(L.Cov1(X)).U16(1).O16(L.N().U16(2, XAlt1, XAlt2)));
        Node ccmp = L.Lookup(2, 0, 0, L.N().U16(1).O16(L.Cov1(D)).U16(1).O16(L.N().U16(2, A, B)));
        Node calt = L.Lookup(6, 0, 0, L.N().U16(3, 0, 1).O16(L.Cov1(C)).U16(1).O16(L.Cov1(A)).U16(1, 0, 4));
        Node cToG = L.Lookup(1, 0, 0, L.N().U16(2).O16(L.Cov1(C)).U16(1, G));
        Node ss01 = L.Lookup(8, 0, 0, L.N().U16(1).O16(L.Cov1(B)).U16(0, 1).O16(L.Cov1(A)).U16(1, E));
        Node numr = L.Lookup(1, 0, 0, L.N().U16(2).O16(L.Cov1(One)).U16(1, OneNumr));
        Node dnom = L.Lookup(1, 0, 0, L.N().U16(2).O16(L.Cov1(Two)).U16(1, TwoDnom));
        Node ss02 = L.Lookup(4, 0x0008, 0, L.N().U16(1).O16(L.Cov1(A)).U16(1).O16(L.List(L.N().U16(Fi, 2, B))));
        Node ss03 = L.Lookup(4, 0, 0, L.N().U16(1).O16(L.Cov1(A)).U16(1).O16(L.List(L.N().U16(Fi, 2, B))));
        Node ss05 = L.Lookup(7, 0, 0, L.N().U16(1, 1).O32(L.N().U16(2).O16(L.Cov1(G)).U16(1, C)));
        Node ss06 = L.Lookup(5, 0, 0, L.N().U16(1).O16(L.Cov1(E)).U16(1).O16(L.List(L.N().U16(2, 1, A).U16(0, 12))));
        Node eToD = L.Lookup(1, 0, 0, L.N().U16(2).O16(L.Cov1(E)).U16(1, D));
        Node ss07 = L.Lookup(5, 0, 0, L.N().U16(2).O16(L.Cov1(B)).O16(L.Class1(A, 1, 2)).U16(3).O16(null).O16(null).O16(L.List(L.N().U16(2, 1, 1).U16(0, 14))));
        Node bToX = L.Lookup(1, 0, 0, L.N().U16(2).O16(L.Cov1(B)).U16(1, X));
        Node ss08 = L.Lookup(4, 0x0100, 0, L.N().U16(1).O16(L.Cov1(A)).U16(1).O16(L.List(L.N().U16(Fi, 2, B))));
        Node ss09 = L.Lookup(4, 0x0010, 0, L.N().U16(1).O16(L.Cov1(A)).U16(1).O16(L.List(L.N().U16(Fi, 2, B))));
        Node gsubLookups = L.List(liga, salt, ccmp, calt, cToG, ss01, numr, dnom, ss02, ss03, ss05, ss06, eToD, ss07, bToX, ss08, ss09);
        Node gsubFeatures = Features(("calt", L.Feature(3)), ("ccmp", L.Feature(2)), ("dnom", L.Feature(7)), ("liga", L.Feature(0)),
            ("numr", L.Feature(6)), ("salt", L.Feature(1)), ("ss01", L.Feature(5)), ("ss02", L.Feature(8)), ("ss03", L.Feature(9)),
            ("ss05", L.Feature(10)), ("ss06", L.Feature(11)), ("ss07", L.Feature(13)),
            ("ss08", L.Feature(15)), ("ss09", L.Feature(16)));
        Node gsub = L.LayoutTable(Scripts(14), gsubFeatures, gsubLookups);

        // GPOS lookups.
        Node pairSet = L.N().U16(1).U16(B).U16(unchecked((ushort)-50));
        Node kern = L.Lookup(2, 0, 0, L.N().U16(1).O16(L.Cov1(A)).U16(0x0004, 0, 1).O16(pairSet));
        Node markArray = L.N().U16(2).U16(0).O16(L.Anchor1(50, 0)).U16(1).O16(L.Anchor1(50, 0));
        Node baseArray = L.N().U16(3)
            .O16(L.Anchor1(250, 600)).O16(L.Anchor1(250, -100))
            .O16(L.Anchor1(250, 600)).O16(L.Anchor1(250, -100))
            .O16(L.Anchor1(250, 600)).O16(L.Anchor1(250, -100));
        Node mark = L.Lookup(4, 0, 0, L.N().U16(1).O16(L.Cov1(Acute, DotBelow)).O16(L.Cov1(A, B, E)).U16(2).O16(markArray).O16(baseArray));
        Node single = L.Lookup(1, 0, 0, L.N().U16(1).O16(L.Cov1(B)).U16(0x0007, 10, 20, 30));
        Node curs = L.Lookup(3, 0, 0, L.N().U16(1).O16(L.Cov1(H)).U16(1).O16(L.Anchor1(0, 0)).O16(L.Anchor1(400, 100)));
        Node mark1Array = L.N().U16(1).U16(0).O16(L.Anchor1(50, 0));
        Node mark2Array = L.N().U16(1).O16(L.Anchor1(0, 200));
        Node mkmk = L.Lookup(6, 0, 0, L.N().U16(1).O16(L.Cov1(Acute)).O16(L.Cov1(Acute)).U16(1).O16(mark1Array).O16(mark2Array));
        Node ligAttach = L.N().U16(2).O16(L.Anchor1(100, 700)).O16(null).O16(L.Anchor1(400, 700)).O16(null);
        Node ligArray = L.N().U16(1).O16(ligAttach);
        Node markLig = L.Lookup(5, 0, 0, L.N().U16(1).O16(L.Cov1(Acute, DotBelow)).O16(L.Cov1(Fi)).U16(2).O16(L.N().U16(2).U16(0).O16(L.Anchor1(50, 0)).U16(1).O16(L.Anchor1(50, 0))).O16(ligArray));
        Node aWider = L.Lookup(1, 0, 0, L.N().U16(1).O16(L.Cov1(A)).U16(0x0004, 7));
        Node ss10 = L.Lookup(8, 0, 0, L.N().U16(3, 0, 1).O16(L.Cov1(A)).U16(1).O16(L.Cov1(C)).U16(1, 0, 6));
        Node gposLookups = L.List(kern, mark, single, curs, mkmk, markLig, aWider, ss10);
        Node gposFeatures = Features(("curs", L.Feature(3)), ("kern", L.Feature(0)), ("mark", L.Feature(1, 5)), ("mkmk", L.Feature(4)), ("ss04", L.Feature(2)),
            ("ss10", L.Feature(7)));
        Node gpos = L.LayoutTable(Scripts(6), gposFeatures, gposLookups);

        // GDEF 1.2: glyph classes, mark attachment classes (acute 1, dot below 2) and one mark glyph set {dot below}.
        Node gdef = L.N().U16(1, 2).O16(L.Class2((A, F, 1), (I, I, 1), (Fi, Fi, 2), (Acute, Acute, 3), (X, Two, 1), (DotBelow, DotBelow, 3), (D, EAcute, 1)))
            .O16(null).O16(null).O16(L.Class2((Acute, Acute, 1), (DotBelow, DotBelow, 2))).O16(L.N().U16(1, 1).O32(L.Cov1(DotBelow)));

        return FontBuilder.Basic(24, unitsPerEm: 1000, advances: advances)
            .Table("cmap", cmap).Table("GSUB", gsub.Build()).Table("GPOS", gpos.Build()).Table("GDEF", gdef.Build()).Build();
    }

    private static byte[] BuildFallbackFont()
    {
        (byte[] glyf, byte[] loca) = FontBuilder.GlyfLoca(new (short, short, short, short)?[]
        {
            (50, 0, 450, 700), (50, 0, 550, 700), null, (60, 0, 460, 700), (40, 0, 460, 500),
            (60, 0, 300, 700), (70, 0, 160, 690), (60, 0, 500, 700),
        }, 0);
        byte[] cmap = FontBuilder.Cmap((3, 10, FontBuilder.CmapFormat12Or13(12, (0x20, 0x20, FbSpace), (0x30, 0x30, FbZero), (0x41, 0x41, FbA), (0x42, 0x42, FbB),
            (0x66, 0x66, FbF), (0x69, 0x69, FbI), (0x78, 0x78, FbX))));
        return FontBuilder.Basic(8, unitsPerEm: 1000, advances: new[] { 500, 600, 250, 520, 500, 320, 240, 580 })
            .Table("cmap", cmap).Table("glyf", glyf).Table("loca", loca).Build();
    }

    /// <summary>A script list with DFLT and latn, both default language systems listing features 0..count-1.</summary>
    private static Node Scripts(int featureCount)
    {
        int[] all = Enumerable.Range(0, featureCount).ToArray();
        Node dflt = L.N().O16(L.LangSys(0xFFFF, all)).U16(0);
        Node latn = L.N().O16(L.LangSys(0xFFFF, all)).U16(0);
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
}
