using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace Chuvadi.OpenType.Tests;

public class FaceMetricsTests
{
    private static byte[] Os2(int version, int fsSelection, int weight = 700, int familyClass = 0x0802)
    {
        byte[] b = new byte[version >= 2 ? 96 : 78];
        FontBuilder.W16(b, 0, version);
        FontBuilder.W16(b, 4, weight);
        FontBuilder.W16(b, 26, 50);   // yStrikeoutSize
        FontBuilder.W16(b, 28, 300);  // yStrikeoutPosition
        FontBuilder.W16(b, 30, familyClass);
        FontBuilder.W16(b, 62, fsSelection);
        FontBuilder.W16(b, 68, 750);  // sTypoAscender
        FontBuilder.W16(b, 70, -250); // sTypoDescender
        FontBuilder.W16(b, 72, 90);   // sTypoLineGap
        FontBuilder.W16(b, 74, 950);  // usWinAscent
        FontBuilder.W16(b, 76, 260);  // usWinDescent
        if (version >= 2)
        {
            FontBuilder.W16(b, 86, 480); // sxHeight
            FontBuilder.W16(b, 88, 690); // sCapHeight
        }

        return b;
    }

    private static byte[] Post(int italicAngleFixed, bool fixedPitch)
    {
        byte[] b = new byte[32];
        FontBuilder.W32(b, 0, 0x00030000u);
        FontBuilder.W32(b, 4, unchecked((uint)italicAngleFixed));
        FontBuilder.W16(b, 8, -120);
        FontBuilder.W16(b, 10, 40);
        FontBuilder.W32(b, 12, fixedPitch ? 1u : 0u);
        return b;
    }

    private static byte[] Names(bool typographic)
    {
        List<(int, int, int, int, byte[])> r = new List<(int, int, int, int, byte[])>
        {
            (3, 1, 0x409, 1, Encoding.BigEndianUnicode.GetBytes("Test Sans")),
            (3, 1, 0x409, 2, Encoding.BigEndianUnicode.GetBytes("Bold")),
            (3, 1, 0x409, 4, Encoding.BigEndianUnicode.GetBytes("Test Sans Bold")),
            (3, 1, 0x409, 6, Encoding.BigEndianUnicode.GetBytes("TestSans-Bold")),
        };
        if (typographic)
        {
            r.Add((3, 1, 0x409, 16, Encoding.BigEndianUnicode.GetBytes("Test")));
            r.Add((3, 1, 0x409, 17, Encoding.BigEndianUnicode.GetBytes("Sans Bold")));
        }

        return FontBuilder.Name(r.ToArray());
    }

    /// <summary>Glyph 1 'H' (top 700), glyph 2 'x' (top 500).</summary>
    private static FontBuilder WithGlyphs()
    {
        (byte[] glyf, byte[] loca) = FontBuilder.GlyfLoca(new (short, short, short, short)?[] { null, (10, 0, 600, 700), (5, 0, 400, 500) }, 0);
        return FontBuilder.Basic(3).Table("glyf", glyf).Table("loca", loca)
            .Table("cmap", FontBuilder.Cmap((3, 1, FontBuilder.CmapFormat4(new Dictionary<int, int> { ['H'] = 1, ['x'] = 2 }, false))));
    }

    [Fact]
    public void FullTables()
    {
        OpenTypeFont f = OpenTypeFont.Load(WithGlyphs().Table("OS/2", Os2(4, 0x00A1)).Table("post", Post(-12 << 16 | 0x8000, true)).Table("name", Names(true)).Build());
        FaceMetrics m = f.Metrics;
        Assert.Same(m, f.Metrics);
        Assert.Equal("Test", m.FamilyName);
        Assert.Equal("Sans Bold", m.SubfamilyName);
        Assert.Equal("Test Sans Bold", m.FullName);
        Assert.Equal("TestSans-Bold", m.PostScriptName);
        Assert.Equal(700, m.WeightClass);
        Assert.True(m.IsBold);      // fsSelection bit 5
        Assert.True(m.IsItalic);    // bit 0
        Assert.True(m.UseTypoMetrics); // bit 7
        Assert.True(m.IsFixedPitch);
        Assert.True(m.HasOs2Table);
        Assert.Equal((750, -250, 90), (m.Ascender, m.Descender, m.LineGap));
        Assert.Equal((800, -200, 0), (m.HheaAscender, m.HheaDescender, m.HheaLineGap));
        Assert.Equal((950, 260), (m.WinAscent, m.WinDescent));
        Assert.Equal((690, 480), (m.CapHeight, m.XHeight));
        Assert.Equal((-120, 40), (m.UnderlinePosition, m.UnderlineThickness));
        Assert.Equal((300, 50), (m.StrikeoutPosition, m.StrikeoutSize));
        Assert.Equal(-11.5, m.ItalicAngle);
        Assert.Equal(new GlyphBounds(-100, 0, 1000, 900), m.FontBounds);
        Assert.Equal(8, f.Os2!.FamilyClassId);
        Assert.Equal(2, f.Os2.FamilySubclassId);
    }

    [Fact]
    public void FallbacksForOldOrMissingTables()
    {
        // OS/2 version 1: no cap height or x-height, so the glyph tops; fsSelection bit 7 clear, so hhea values.
        FaceMetrics m = OpenTypeFont.Load(WithGlyphs().Table("OS/2", Os2(1, 0)).Table("name", Names(false)).Build()).Metrics;
        Assert.Equal("Test Sans", m.FamilyName);
        Assert.Equal("Bold", m.SubfamilyName);
        Assert.False(m.IsBold);
        Assert.False(m.UseTypoMetrics);
        Assert.Equal((800, -200, 0), (m.Ascender, m.Descender, m.LineGap));
        Assert.Equal((700, 500), (m.CapHeight, m.XHeight));
        Assert.False(m.IsFixedPitch);
        Assert.Equal(0, m.ItalicAngle);

        // No OS/2: style from head macStyle (bold bit 0, italic bit 1), OS/2 values 0.
        byte[] head = FontBuilder.Head(1000);
        FontBuilder.W16(head, 44, 0x0002);
        FaceMetrics n = OpenTypeFont.Load(WithGlyphs().Table("head", head).Build()).Metrics;
        Assert.False(n.HasOs2Table);
        Assert.Equal(0, n.WeightClass);
        Assert.False(n.IsBold);
        Assert.True(n.IsItalic);
        Assert.Equal((0, 0, 0, 0), (n.TypoAscender, n.WinAscent, n.StrikeoutSize, n.StrikeoutPosition));
        Assert.Null(n.FamilyName);
        Assert.Equal(700, n.CapHeight);

        // No glyph for 'H' or 'x' and no OS/2 version 2: 0.
        Assert.Equal((0, 0), (OpenTypeFont.Load(FontBuilder.Basic(2).Build()).Metrics.CapHeight, OpenTypeFont.Load(FontBuilder.Basic(2).Build()).Metrics.XHeight));
    }

    [Fact]
    public void HasGlyphAndSymbolSubtable()
    {
        OpenTypeFont f = OpenTypeFont.Load(WithGlyphs().Build());
        Assert.True(f.CharacterMap.HasGlyph('H'));
        Assert.False(f.CharacterMap.HasGlyph('Q'));
        Assert.False(f.CharacterMap.HasSymbolSubtable);
        OpenTypeFont s = OpenTypeFont.Load(FontBuilder.Basic(3).Table("cmap", FontBuilder.Cmap((3, 0, FontBuilder.CmapFormat4(new Dictionary<int, int> { [0xF041] = 1 }, false)))).Build());
        Assert.True(s.CharacterMap.HasSymbolSubtable);
    }

    [Fact]
    public void VariationSequencesAreEnumerated()
    {
        byte[] format14 = FontBuilder.CmapFormat14(0xFE00, new[] { (0x41, 1), (0x1F12F, 0) }, new[] { (0x30, 7), (0x42, 9) });
        byte[] cmap = FontBuilder.Cmap(
            (0, 5, format14),
            (3, 10, FontBuilder.CmapFormat12Or13(12, (0x30, 0x30, 2), (0x41, 0x42, 3), (0x1F12F, 0x1F12F, 5))));
        CharacterMap m = OpenTypeFont.Load(FontBuilder.Basic(10).Table("cmap", cmap).Build()).CharacterMap;
        (int, int, VariationSequenceKind, int)[] all = m.EnumerateVariationSequences().ToArray();
        Assert.Equal(
            new[]
            {
                (0x41, 0xFE00, VariationSequenceKind.DefaultGlyph, 3),
                (0x42, 0xFE00, VariationSequenceKind.DefaultGlyph, 4),
                (0x1F12F, 0xFE00, VariationSequenceKind.DefaultGlyph, 5),
                (0x30, 0xFE00, VariationSequenceKind.OwnGlyph, 7),
                (0x42, 0xFE00, VariationSequenceKind.OwnGlyph, 9),
            },
            all);
        Assert.Equal(7, m.GetGlyph(0x30, 0xFE00));
        Assert.Equal(VariationSequenceKind.NotListed, m.GetVariationKind(0x43, 0xFE00));
        Assert.Empty(OpenTypeFont.Load(FontBuilder.Basic(2).Build()).CharacterMap.EnumerateVariationSequences());
    }
}
