using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Chuvadi.OpenType.Tests;

public class CffTests
{
    // Type 2 charstring operand encoding for small integers: v + 139 (-107..107), else the 28 (int16) form.
    private static IEnumerable<byte> N(int v)
    {
        if (v >= -107 && v <= 107)
        {
            return new[] { (byte)(v + 139) };
        }

        return new byte[] { 28, (byte)(v >> 8), (byte)v };
    }

    private static byte[] Cs(params object[] parts) =>
        parts.SelectMany(p => p is int i ? N(i) : p is byte[] b ? (IEnumerable<byte>)b : new[] { (byte)p }).ToArray();

    private static byte[] Index(params byte[][] items)
    {
        List<byte> b = new List<byte>();
        FontBuilder.P16(b, items.Length);
        if (items.Length == 0)
        {
            return b.ToArray();
        }

        b.Add(4);
        uint off = 1;
        FontBuilder.P32(b, off);
        foreach (byte[] it in items)
        {
            off += (uint)it.Length;
            FontBuilder.P32(b, off);
        }

        foreach (byte[] it in items)
        {
            b.AddRange(it);
        }

        return b.ToArray();
    }

    private static byte[] DictInt(int v) => new byte[] { 29, (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };

    /// <summary>A CFF table with one font, the given charstrings and optional local subroutines.</summary>
    private static byte[] Cff(byte[][] charStrings, byte[][] localSubrs, byte[][] globalSubrs)
    {
        byte[] header = { 1, 0, 4, 4 };
        byte[] names = Index(new[] { (byte)'T' });
        byte[] strings = Index();
        byte[] globals = Index(globalSubrs);
        byte[] charIndex = Index(charStrings);
        byte[] subrIndex = Index(localSubrs);

        // Top DICT has fixed size: CharStrings (5 + 1) + Private (5 + 5 + 1) = 17 bytes.
        int topSize = 17;
        byte[] topIndexProbe = Index(new byte[topSize]);
        int charStringsOffset = header.Length + names.Length + topIndexProbe.Length + strings.Length + globals.Length;
        byte[] privateDict = localSubrs.Length > 0 ? DictInt(6).Concat(new byte[] { 19 }).ToArray() : new byte[0];
        int privateOffset = charStringsOffset + charIndex.Length;
        byte[] top = DictInt(charStringsOffset).Concat(new byte[] { 17 })
            .Concat(DictInt(privateDict.Length)).Concat(DictInt(privateOffset)).Concat(new byte[] { 18 }).ToArray();
        Assert.Equal(topSize, top.Length);
        List<byte> cff = new List<byte>();
        cff.AddRange(header);
        cff.AddRange(names);
        cff.AddRange(Index(top));
        cff.AddRange(strings);
        cff.AddRange(globals);
        cff.AddRange(charIndex);
        cff.AddRange(privateDict);
        cff.AddRange(subrIndex);
        return cff.ToArray();
    }

    private static OpenTypeFont Font(byte[] cff, int glyphs)
    {
        FontBuilder b = FontBuilder.Basic(glyphs).Table("CFF ", cff);
        b.SfntVersion = 0x4F54544Fu;
        return OpenTypeFont.Load(b.Build());
    }

    [Fact]
    public void BoundsOfLinesCurvesSubroutinesAndEmptyGlyphs()
    {
        byte[][] glyphs =
        {
            // 0: width 500, box 100..400 x -20..700 drawn with rmoveto + rlineto.
            Cs(500, 100, -20, (byte)21, 300, 0, 0, 720, -300, 0, (byte)5, (byte)14),
            // 1: a curve bulging above its end points: (0,0) -> control (0,300) (300,300) -> (300,0). Peak y = 225.
            Cs(0, 0, (byte)21, 0, 300, 300, 0, 0, -300, (byte)8, (byte)14),
            // 2: width only.
            Cs(250, (byte)14),
            // 3: hmoveto, then a local subroutine (index 0 - bias 107 = -107) drawing hlineto/vlineto.
            Cs(10, (byte)22, -107, (byte)10, (byte)14),
            // 4: hstem hints with width, then hintmask with mask bytes, then a line.
            Cs(600, 0, 50, 100, 50, (byte)1, (byte)19, (byte)0xC0, 0, 0, (byte)21, 40, 60, (byte)5, (byte)14),
        };
        byte[][] subrs = { Cs(200, (byte)6, 100, (byte)7, (byte)11) };
        OpenTypeFont f = Font(Cff(glyphs, subrs, new byte[0][]), glyphs.Length);
        Assert.Equal(OutlineFormat.Cff, f.OutlineFormat);
        Assert.True(f.IsCff);
        Assert.Equal(new GlyphBounds(100, -20, 400, 700), f.GetGlyphBounds(0));
        Assert.Equal(new GlyphBounds(0, 0, 300, 225), f.GetGlyphBounds(1));
        Assert.False(f.TryGetGlyphBounds(2, out _));
        Assert.Equal(new GlyphBounds(10, 0, 210, 100), f.GetGlyphBounds(3));
        Assert.Equal(new GlyphBounds(0, 0, 40, 60), f.GetGlyphBounds(4));
    }

    [Fact]
    public void SelfCallingSubroutineStops()
    {
        byte[][] glyphs = { Cs(0, 0, (byte)21, 10, 10, (byte)5, -107, (byte)10, (byte)14) };
        byte[][] subrs = { Cs(-107, (byte)10, (byte)11) };
        OpenTypeFont f = Font(Cff(glyphs, subrs, new byte[0][]), 1);
        Assert.Equal(new GlyphBounds(0, 0, 10, 10), f.GetGlyphBounds(0));
    }

    [Fact]
    public void GarbageCffNeverThrows()
    {
        System.Random r = new System.Random(7);
        for (int i = 0; i < 500; i++)
        {
            byte[] cff = new byte[r.Next(4, 400)];
            r.NextBytes(cff);
            cff[0] = 1;
            OpenTypeFont f = Font(cff, 4);
            for (int g = 0; g < 4; g++)
            {
                _ = f.TryGetGlyphBounds(g, out _);
            }
        }
    }
}
