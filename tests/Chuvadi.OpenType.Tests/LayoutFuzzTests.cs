using System;
using System.Diagnostics;
using Xunit;

namespace Chuvadi.OpenType.Tests;

/// <summary>Damaged layout data reads as empty or wrong values, never as an exception or a hang (R-OT-0040).</summary>
public class LayoutFuzzTests
{
    private static byte[] FontWith(byte[] gsub, byte[] gpos, byte[] gdef) =>
        FontBuilder.Basic(200).Table("GSUB", gsub).Table("GPOS", gpos).Table("GDEF", gdef).Table("fvar", L.Fvar(1)).Build();

    private static (byte[] Gsub, byte[] Gpos, byte[] Gdef) Tables()
    {
        return (LayoutViewTests.Gsub().Build(), LayoutViewTests.Gpos().Build(), LayoutViewTests.GdefSample().Build());
    }

    [Fact]
    public void TheSampleWalksCleanly()
    {
        (byte[] gsub, byte[] gpos, byte[] gdef) = Tables();
        Assert.NotEqual(0, LayoutWalker.Walk(OpenTypeFont.Load(FontWith(gsub, gpos, gdef))));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RandomByteDamageNeverThrows(int seed)
    {
        (byte[] gsub, byte[] gpos, byte[] gdef) = Tables();
        Random r = new Random(seed);
        Stopwatch time = Stopwatch.StartNew();
        for (int round = 0; round < 400; round++)
        {
            byte[][] t = { (byte[])gsub.Clone(), (byte[])gpos.Clone(), (byte[])gdef.Clone() };
            for (int k = 0; k < 1 + r.Next(6); k++)
            {
                byte[] target = t[r.Next(3)];
                int at = r.Next(target.Length);
                target[at] = r.Next(4) switch { 0 => 0xFF, 1 => 0x00, _ => (byte)r.Next(256) };
            }

            _ = LayoutWalker.Walk(OpenTypeFont.Load(FontWith(t[0], t[1], t[2])));
        }

        Assert.True(time.Elapsed < TimeSpan.FromSeconds(60), $"took {time.Elapsed}");
    }

    [Fact]
    public void TruncatedTablesNeverThrow()
    {
        (byte[] gsub, byte[] gpos, byte[] gdef) = Tables();
        for (int n = 0; n < Math.Max(gsub.Length, Math.Max(gpos.Length, gdef.Length)); n += 3)
        {
            _ = LayoutWalker.Walk(OpenTypeFont.Load(FontWith(gsub[..Math.Min(n, gsub.Length)], gpos[..Math.Min(n, gpos.Length)], gdef[..Math.Min(n, gdef.Length)])));
        }
    }
}
