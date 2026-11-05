using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chuvadi.OpenType;
using Chuvadi.OpenType.Tests;
using Chuvadi.TestSupport;
using Xunit;

namespace Chuvadi.Text.Shaping.Tests;

public class TextShaperTests
{
    private static readonly ShapingFont Font = ShapingFont.Get(OpenTypeFont.Load(
        FontBuilder.Basic(6, unitsPerEm: 2048, advances: new[] { 0, 100, 200, 300, 400, 500 })
            .Table("cmap", FontBuilder.Cmap((3, 10, FontBuilder.CmapFormat12Or13(12, (0x41, 0x43, 1), (0xFFFD, 0xFFFD, 4), (0x1F600, 0x1F600, 5)))))
            .Build()));

    [Fact]
    public void MapsCharactersWithAdvancesAndClusters()
    {
        ShapedRun run = TextShaper.Shape(Font, "AB\U0001F600C");
        Assert.Equal(2048, run.UnitsPerEm);
        Assert.Equal(new[] { 1, 2, 5, 3 }, run.Select(g => g.GlyphId));
        Assert.Equal(new[] { 0, 1, 2, 4 }, run.Select(g => g.Cluster));
        Assert.Equal(new[] { 100, 200, 500, 300 }, run.Select(g => g.XAdvance));
        Assert.All(run, g => Assert.Equal(0, g.XOffset + g.YOffset));
    }

    [Fact]
    public void UnpairedSurrogatesBecomeReplacementCharacter()
    {
        ShapedRun run = TextShaper.Shape(Font, "A\uD800B\uDC00");
        Assert.Equal(new[] { 1, 4, 2, 4 }, run.Select(g => g.GlyphId));
        Assert.Equal(new[] { 0, 1, 2, 3 }, run.Select(g => g.Cluster));
    }

    [Fact]
    public void RunInsideTextKeepsClustersRelativeToWholeText()
    {
        GlyphBuffer buffer = new GlyphBuffer(1);
        TextShaper.Shape(Font, "xxABx".AsSpan(), 2, 2, null, buffer);
        Assert.Equal(2, buffer.Count);
        Assert.Equal(new[] { 2, 3 }, buffer.Clusters.ToArray());
        TextShaper.Shape(Font, "C".AsSpan(), null, buffer);
        Assert.Equal(1, buffer.Count);
        Assert.Equal(3, buffer.GlyphIds[0]);
    }

    [Fact]
    public void RunEndingInsideASurrogatePairSeesAnUnpairedSurrogate()
    {
        GlyphBuffer buffer = new GlyphBuffer();
        TextShaper.Shape(Font, "\U0001F600".AsSpan(), 0, 1, null, buffer);
        Assert.Equal(4, buffer.GlyphIds[0]);
    }

    [Fact]
    public void EmptyRunGivesNoGlyphs()
    {
        GlyphBuffer buffer = new GlyphBuffer();
        TextShaper.Shape(Font, ReadOnlySpan<char>.Empty, null, buffer);
        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public void ArgumentChecks()
    {
        GlyphBuffer buffer = new GlyphBuffer();
        Assert.Throws<ArgumentNullException>(() => TextShaper.Shape(null!, "A".AsSpan(), null, buffer));
        Assert.Throws<ArgumentNullException>(() => TextShaper.Shape(Font, "A".AsSpan(), null, null!));
        Assert.Throws<ArgumentNullException>(() => TextShaper.Shape(Font, (string)null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextShaper.Shape(Font, "AB".AsSpan(), 1, 2, null, buffer));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextShaper.Shape(Font, "AB".AsSpan(), -1, 1, null, buffer));
        Assert.Throws<NotSupportedException>(() => TextShaper.Shape(Font, "A", new ShapeOptions { Direction = TextDirection.RightToLeft }));
        Assert.Throws<ArgumentNullException>(() => ShapingFont.Get(null!));
    }

    [Fact]
    public void SameFontGivesSameShapingFont()
    {
        Assert.Same(Font, ShapingFont.Get(Font.Font));
    }

    [Fact]
    public void ConcurrentShapingIsDeterministic()
    {
        string text = "ABC\U0001F600ABC";
        int[] reference = TextShaper.Shape(Font, text).Select(g => g.GlyphId).ToArray();
        bool[] same = new bool[200];
        Parallel.For(0, same.Length, i => same[i] = TextShaper.Shape(Font, text).Select(g => g.GlyphId).SequenceEqual(reference));
        Assert.All(same, Assert.True);
    }

    [Fact]
    public void ScriptAndOptionTypes()
    {
        Assert.Equal("Deva", Script.Parse("dEVA").ToString());
        Assert.Throws<ArgumentException>(() => Script.Parse("De1a"));
        Assert.Equal("dflt", Language.Default.ToString());
        Feature f = Feature.Create("cpsp", 0);
        Assert.Equal(0u, f.Value);
        Assert.Equal(int.MaxValue, f.End);
        Assert.True(ShapeOptions.Default.UseDefaultFeatures);
        Assert.Equal(Script.Latin, ShapeOptions.Default.Script);
    }
}

public class TraceLogTests
{
    [Fact]
    public void EveryTraceTagHasAnEntryAndEveryEntryPointsAtItsCode()
    {
        string root = TraceLogCheck.FindRepositoryRoot();
        IReadOnlyList<string> problems = TraceLogCheck.Check(root, "docs/Chuvadi.Text.Shaping/trace/rules.md", "R-TS-",
            "src/Chuvadi.Text.Shaping", "tools/Chuvadi.Text.Shaping.UcdGen", "tools/Chuvadi.Text.Shaping.Gate");
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }
}
