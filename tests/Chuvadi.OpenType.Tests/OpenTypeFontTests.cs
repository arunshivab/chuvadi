using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chuvadi.TestSupport;
using Xunit;

namespace Chuvadi.OpenType.Tests;

public class TagTests
{
    [Theory]
    [InlineData("GSUB", 0x47535542u)]
    [InlineData("cvt", 0x63767420u)]
    [InlineData("OS/2", 0x4F532F32u)]
    public void ParseAndFormat(string text, uint value)
    {
        Tag t = Tag.Parse(text);
        Assert.Equal(value, t.Value);
        Assert.Equal(text.PadRight(4), t.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("toolong")]
    [InlineData("aé")]
    public void ParseRejectsInvalid(string text) => Assert.Throws<ArgumentException>(() => Tag.Parse(text));

    [Fact]
    public void OrderIsNumeric() => Assert.True(Tag.Parse("GPOS").CompareTo(Tag.Parse("GSUB")) < 0);
}

public class LoadingTests
{
    [Fact]
    public void LoadsMinimalFont()
    {
        byte[] data = FontBuilder.Basic(3, unitsPerEm: 2048).Build();
        OpenTypeFont f = OpenTypeFont.Load(data);
        Assert.Equal(2048, f.UnitsPerEm);
        Assert.Equal(3, f.GlyphCount);
        Assert.Equal(OutlineFormat.None, f.OutlineFormat);
        Assert.False(f.IsCff);
        Assert.Equal(0x00010000u, f.SfntVersion);
        Assert.Equal(4, f.Tables.Count);
        Assert.True(f.HasTable(Tag.Parse("hmtx")));
        Assert.Equal(0, f.CharacterMap.GetGlyph('A'));
        Assert.Null(f.Os2);
        Assert.Null(f.Names);
    }

    [Fact]
    public void NullDataThrows() => Assert.Throws<ArgumentNullException>(() => OpenTypeFont.Load((byte[])null!));

    [Fact]
    public void RejectsShortOrUnknownData()
    {
        Assert.Throws<OpenTypeFormatException>(() => OpenTypeFont.Load(new byte[4]));
        Assert.Throws<OpenTypeFormatException>(() => OpenTypeFont.Load(Encoding.ASCII.GetBytes("this is not a font at all")));
        Assert.Throws<OpenTypeFormatException>(() => OpenTypeFont.Load(Encoding.ASCII.GetBytes("wOF2-------------")));
    }

    [Fact]
    public void MissingHeadOrMaxpThrows()
    {
        Assert.Throws<OpenTypeFormatException>(() => OpenTypeFont.Load(FontBuilder.Basic(2).Remove("head").Build()));
        Assert.Throws<OpenTypeFormatException>(() => OpenTypeFont.Load(FontBuilder.Basic(2).Remove("maxp").Build()));
        Assert.Throws<OpenTypeFormatException>(() => OpenTypeFont.Load(FontBuilder.Basic(2).Table("head", FontBuilder.Head(0)).Build()));
    }

    [Fact]
    public void TableOutsideDataIsTreatedAsAbsent()
    {
        byte[] data = FontBuilder.Basic(2).Table("zzzz", new byte[8]).Build();
        // Find the 'zzzz' record and point it past the end of the data.
        for (int i = 0; i < 6; i++)
        {
            int r = 12 + (16 * i);
            if (data[r] == (byte)'z')
            {
                FontBuilder.W32(data, r + 8, (uint)data.Length);
            }
        }

        OpenTypeFont f = OpenTypeFont.Load(data);
        Assert.False(f.HasTable(Tag.Parse("zzzz")));
        Assert.True(f.GetTableData(Tag.Parse("zzzz")).IsEmpty);
    }

    [Fact]
    public void BadFaceIndexThrows()
    {
        byte[] data = FontBuilder.Basic(2).Build();
        Assert.Throws<ArgumentOutOfRangeException>(() => OpenTypeFont.Load(data, 1));
        Assert.False(OpenTypeFont.TryLoad(data, 1, out _));
        Assert.True(OpenTypeFont.TryLoad(data, 0, out OpenTypeFont? f));
        Assert.NotNull(f);
    }

    [Fact]
    public void ReadsCollections()
    {
        byte[] data = FontBuilder.Collection(FontBuilder.Basic(2, unitsPerEm: 1000), FontBuilder.Basic(5, unitsPerEm: 2048));
        FontCollection c = FontCollection.Load(data);
        Assert.Equal(FontContainerKind.Collection, c.Kind);
        Assert.Equal(2, c.Count);
        Assert.Equal(1000, c.GetFont(0).UnitsPerEm);
        OpenTypeFont second = OpenTypeFont.Load(data, 1);
        Assert.Equal(2048, second.UnitsPerEm);
        Assert.Equal(5, second.GlyphCount);
        Assert.Equal(1, second.FaceIndex);
        Assert.Throws<ArgumentOutOfRangeException>(() => c.GetFont(2));
    }
}

public class MetricsTests
{
    [Fact]
    public void AdvancesAndSideBearingsFollowHmtxRules()
    {
        // 4 glyphs, numberOfHMetrics = 2: glyphs 2 and 3 repeat the last advance and take lsb from leftSideBearings[].
        byte[] data = FontBuilder.Basic(4, advances: new[] { 600, 700 }, lsbs: new short[] { 10, 20, 30, -40 }).Build();
        OpenTypeFont f = OpenTypeFont.Load(data);
        Assert.Equal(new[] { 600, 700, 700, 700 }, Enumerable.Range(0, 4).Select(f.GetAdvanceWidth));
        Assert.Equal(new[] { 10, 20, 30, -40 }, Enumerable.Range(0, 4).Select(f.GetLeftSideBearing));
        Assert.Equal(0, f.GetAdvanceWidth(4));
        Assert.Equal(0, f.GetAdvanceWidth(-1));
        Assert.Equal(800, f.Hhea!.Ascender);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void GlyphBoundsFromGlyf(int locaFormat)
    {
        (byte[] glyf, byte[] loca) = FontBuilder.GlyfLoca(new (short, short, short, short)?[] { (1, 2, 3, 4), null, (-50, -60, 70, 80) }, locaFormat);
        byte[] data = FontBuilder.Basic(3, indexToLocFormat: locaFormat).Table("glyf", glyf).Table("loca", loca).Build();
        OpenTypeFont f = OpenTypeFont.Load(data);
        Assert.Equal(OutlineFormat.TrueType, f.OutlineFormat);
        Assert.True(f.TryGetGlyphBounds(0, out GlyphBounds b0));
        Assert.Equal(new GlyphBounds(1, 2, 3, 4), b0);
        Assert.False(f.TryGetGlyphBounds(1, out _));
        Assert.Equal(new GlyphBounds(-50, -60, 70, 80), f.GetGlyphBounds(2));
        Assert.False(f.TryGetGlyphBounds(3, out _));
    }

    [Fact]
    public void KernFormat0Pairs()
    {
        byte[] data = FontBuilder.Basic(4).Table("kern", FontBuilder.KernFormat0((1, 2, -50), (2, 3, 25))).Build();
        KernTable k = OpenTypeFont.Load(data).Kern!;
        Assert.Equal(0, k.Version);
        KernSubtable s = Assert.Single(k.Subtables);
        Assert.True(s.IsHorizontal);
        Assert.Equal(0, s.Format);
        Assert.True(s.TryGetValue(1, 2, out int v1));
        Assert.Equal(-50, v1);
        Assert.True(s.TryGetValue(2, 3, out int v2));
        Assert.Equal(25, v2);
        Assert.False(s.TryGetValue(3, 2, out _));
    }

    [Fact]
    public void NameTableDecodesWindowsAndMacRoman()
    {
        byte[] name = FontBuilder.Name(
            (1, 0, 0, 1, new byte[] { (byte)'C', 0x8E, (byte)'t' }),
            (3, 1, 0x0409, 1, Encoding.BigEndianUnicode.GetBytes("Chuvadi Test")),
            (3, 1, 0x0409, 4, Encoding.BigEndianUnicode.GetBytes("Chuvadi Test Regular")));
        NameTable n = OpenTypeFont.Load(FontBuilder.Basic(1).Table("name", name).Build()).Names!;
        Assert.Equal(3, n.Records.Count);
        Assert.Equal("Cét", n.Records[0].Value);
        Assert.Equal("Chuvadi Test", n.GetName(1));
        Assert.Equal("Chuvadi Test Regular", n.GetName(4));
        Assert.Null(n.GetName(6));
    }
}

public class CmapTests
{
    private static readonly Dictionary<int, int> Map = new Dictionary<int, int> { [0x41] = 1, [0x42] = 2, [0x43] = 3, [0x61] = 4, [0x3B1] = 5 };

    private static OpenTypeFont Font(params (int Platform, int Encoding, byte[] Subtable)[] records) =>
        OpenTypeFont.Load(FontBuilder.Basic(10).Table("cmap", FontBuilder.Cmap(records)).Build());

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Format4(bool useArray)
    {
        CharacterMap m = Font((3, 1, FontBuilder.CmapFormat4(Map, useArray))).CharacterMap;
        Assert.Equal(4, m.Selected!.Format);
        foreach (KeyValuePair<int, int> kv in Map)
        {
            Assert.Equal(kv.Value, m.GetGlyph(kv.Key));
        }

        Assert.Equal(0, m.GetGlyph(0x44));
        Assert.Equal(0, m.GetGlyph(0x1F600));
        Assert.Equal(Map.OrderBy(kv => kv.Key), m.EnumerateMappings());
    }

    [Fact]
    public void Format0And6And10()
    {
        Assert.Equal(2, Font((1, 0, FontBuilder.CmapFormat0(new Dictionary<int, int> { [0x42] = 2 }))).CharacterMap.GetGlyph(0x42));
        CharacterMap six = Font((0, 3, FontBuilder.CmapFormat6(0x30, new[] { 7, 8, 9 }))).CharacterMap;
        Assert.Equal(new[] { 0, 7, 8, 9, 0 }, new[] { 0x2F, 0x30, 0x31, 0x32, 0x33 }.Select(c => six.GetGlyph(c)));
        CharacterMap ten = Font((0, 4, FontBuilder.CmapFormat10(0x10000, new[] { 5, 6 }))).CharacterMap;
        Assert.Equal(new[] { 5, 6, 0 }, new[] { 0x10000, 0x10001, 0x10002 }.Select(c => ten.GetGlyph(c)));
    }

    [Fact]
    public void Format12And13()
    {
        CharacterMap twelve = Font((3, 10, FontBuilder.CmapFormat12Or13(12, (0x41, 0x43, 1), (0x1F600, 0x1F601, 8)))).CharacterMap;
        Assert.Equal(new[] { 1, 2, 3, 8, 9, 0 }, new[] { 0x41, 0x42, 0x43, 0x1F600, 0x1F601, 0x44 }.Select(c => twelve.GetGlyph(c)));
        CharacterMap thirteen = Font((0, 6, FontBuilder.CmapFormat12Or13(13, (0x0, 0x10FFFF, 7)))).CharacterMap;
        Assert.Equal(7, thirteen.GetGlyph(0x1234));
        Assert.Equal(7, thirteen.GetGlyph(0x10FFFF));
    }

    [Fact]
    public void Format2SingleAndDoubleByte()
    {
        CmapSubtable s = Font((3, 2, FontBuilder.CmapFormat2(new[] { 0, 1, 2, 3 }, new[] { 7, 8, 9 }))).CharacterMap.Subtables[0];
        Assert.Equal(2, s.Format);
        Assert.Equal(3, s.GetGlyph(0x03));
        Assert.Equal(8, s.GetGlyph(0x8141));
        Assert.Equal(0, s.GetGlyph(0x8143));
        Assert.Equal(0, s.GetGlyph(0x81));
    }

    [Fact]
    public void SelectionPrefers32BitThenWindows()
    {
        byte[] bmp = FontBuilder.CmapFormat4(new Dictionary<int, int> { [0x41] = 1 }, false);
        byte[] full = FontBuilder.CmapFormat12Or13(12, (0x41, 0x41, 2), (0x1F600, 0x1F600, 3));
        CharacterMap m = Font((0, 3, bmp), (0, 4, full), (3, 1, bmp)).CharacterMap;
        Assert.Equal(12, m.Selected!.Format);
        Assert.Equal(2, m.GetGlyph(0x41));
        CharacterMap w = Font((0, 3, FontBuilder.CmapFormat4(new Dictionary<int, int> { [0x41] = 4 }, false)), (3, 1, bmp)).CharacterMap;
        Assert.Equal(3, w.Selected!.PlatformId);
        Assert.Equal(1, w.GetGlyph(0x41));
    }

    [Fact]
    public void MacRomanFallbackConvertsCodePoints()
    {
        CharacterMap m = Font((1, 0, FontBuilder.CmapFormat0(new Dictionary<int, int> { [0x8E] = 6, [0x41] = 1 }))).CharacterMap;
        Assert.Equal(6, m.GetGlyph(0xE9));
        Assert.Equal(1, m.GetGlyph(0x41));
        Assert.Equal(0, m.GetGlyph(0x8E));
        Assert.Equal(new[] { new KeyValuePair<int, int>(0x41, 1), new KeyValuePair<int, int>(0xE9, 6) }, m.EnumerateMappings());
    }

    [Fact]
    public void GlyphsBeyondGlyphCountReadAsZero()
    {
        CharacterMap m = Font((3, 1, FontBuilder.CmapFormat4(new Dictionary<int, int> { [0x41] = 50 }, true))).CharacterMap;
        Assert.Equal(0, m.GetGlyph(0x41));
    }

    [Fact]
    public void VariationSequences()
    {
        byte[] bmp = FontBuilder.CmapFormat4(new Dictionary<int, int> { [0x8FBB] = 1, [0x8FBC] = 2 }, false);
        byte[] uvs = FontBuilder.CmapFormat14(0xE0100, new[] { (0x8FBB, 0) }, new[] { (0x8FBC, 9) });
        CharacterMap m = Font((0, 3, bmp), (0, 5, uvs)).CharacterMap;
        Assert.NotNull(m.VariationSequences);
        Assert.Equal(VariationSequenceKind.DefaultGlyph, m.GetVariationKind(0x8FBB, 0xE0100));
        Assert.Equal(VariationSequenceKind.OwnGlyph, m.GetVariationKind(0x8FBC, 0xE0100));
        Assert.Equal(VariationSequenceKind.NotListed, m.GetVariationKind(0x8FBC, 0xE0101));
        Assert.Equal(1, m.GetGlyph(0x8FBB, 0xE0100));
        Assert.Equal(9, m.GetGlyph(0x8FBC, 0xE0100));
        Assert.Equal(2, m.GetGlyph(0x8FBC, 0xE0101));
    }
}

public class RobustnessTests
{
    [Fact]
    public void RandomCorruptionNeverThrowsAfterLoad()
    {
        (byte[] glyf, byte[] loca) = FontBuilder.GlyfLoca(new (short, short, short, short)?[] { (1, 2, 3, 4), null, (5, 6, 7, 8) }, 0);
        byte[] original = FontBuilder.Basic(3)
            .Table("glyf", glyf).Table("loca", loca)
            .Table("cmap", FontBuilder.Cmap((3, 1, FontBuilder.CmapFormat4(new Dictionary<int, int> { [0x41] = 1, [0x42] = 2 }, true)), (3, 10, FontBuilder.CmapFormat12Or13(12, (0x41, 0x42, 1)))))
            .Table("kern", FontBuilder.KernFormat0((1, 2, -5)))
            .Table("name", FontBuilder.Name((3, 1, 0x409, 1, Encoding.BigEndianUnicode.GetBytes("X"))))
            .Build();
        Random random = new Random(12345);
        int loaded = 0;
        for (int round = 0; round < 3000; round++)
        {
            byte[] data = (byte[])original.Clone();
            int flips = random.Next(1, 12);
            for (int k = 0; k < flips; k++)
            {
                data[random.Next(data.Length)] = (byte)random.Next(256);
            }

            if (round % 7 == 0)
            {
                Array.Resize(ref data, random.Next(12, data.Length));
            }

            OpenTypeFont? f;
            try
            {
                f = OpenTypeFont.Load(data);
            }
            catch (OpenTypeFormatException)
            {
                continue;
            }

            loaded++;
            for (int g = -1; g < f.GlyphCount + 2; g++)
            {
                _ = f.GetAdvanceWidth(g);
                _ = f.GetLeftSideBearing(g);
                _ = f.TryGetGlyphBounds(g, out _);
            }

            for (int cp = 0; cp < 0x50; cp++)
            {
                _ = f.CharacterMap.GetGlyph(cp);
            }

            _ = f.CharacterMap.EnumerateMappings().Take(1000).Count();
            _ = f.Names?.GetName(1);
            _ = f.Kern?.Subtables.Select(s => s.TryGetValue(1, 2, out _)).ToArray();
            _ = f.Os2;
            _ = f.Post;
        }

        Assert.True(loaded > 100);
    }

    [Fact]
    public void ConcurrentFirstUseIsSafe()
    {
        byte[] data = FontBuilder.Basic(3).Table("cmap", FontBuilder.Cmap((3, 1, FontBuilder.CmapFormat4(new Dictionary<int, int> { [0x41] = 1 }, false)))).Build();
        OpenTypeFont f = OpenTypeFont.Load(data);
        int[] results = new int[64];
        Parallel.For(0, results.Length, i => results[i] = f.CharacterMap.GetGlyph(0x41));
        Assert.All(results, r => Assert.Equal(1, r));
    }
}

public class TraceLogTests
{
    [Fact]
    public void EveryTraceTagHasAnEntryAndEveryEntryPointsAtItsCode()
    {
        string root = TraceLogCheck.FindRepositoryRoot();
        IReadOnlyList<string> problems = TraceLogCheck.Check(root, "docs/Chuvadi.OpenType/trace/rules.md", "R-OT-", "src/Chuvadi.OpenType", "tools/Chuvadi.Fonts.TestSupport");
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }
}
