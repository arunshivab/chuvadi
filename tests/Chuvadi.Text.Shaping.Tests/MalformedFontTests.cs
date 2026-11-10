using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Chuvadi.OpenType;
using Xunit;

namespace Chuvadi.Text.Shaping.Tests;

/// <summary>
/// The malformed-font fixtures of d.6 (D-190): every fixture is rebuilt in memory from a committed base font,
/// checked against the received SHA-256, and shaped; the outcome is checked against Fixtures/Malformed/expected.tsv.
/// </summary>
public class MalformedFontTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Malformed");

    private static readonly Lazy<Dictionary<string, string>> Hashes = new Lazy<Dictionary<string, string>>(() =>
        DataLines("SHA256.tsv").Select(f => (Name: f[0], Hash: f[1])).ToDictionary(x => x.Name, x => x.Hash, StringComparer.Ordinal));

    public static IEnumerable<object[]> Fixtures()
    {
        foreach (string[] f in DataLines("recipes.tsv"))
        {
            yield return new object[] { f[0] };
        }

        foreach (string table in new[] { "GSUB", "GPOS", "GDEF" })
        {
            yield return new object[] { "M3-" + table + "-round0.ttf" };
        }
    }

    [Fact]
    public void BaseFontsMatchTheirHashes()
    {
        foreach (string name in new[] { "M1-base-Carlito-Regular.ttf", "M3-base-LiPi-Sans-Tamil.ttf", "M3-corrupt-layout-patches.tsv" })
        {
            Assert.Equal(Hashes.Value[name], Sha256(File.ReadAllBytes(Path.Combine(Folder, name))));
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void RebuiltFixtureMatchesItsHash(string fixture)
    {
        Assert.Equal(Hashes.Value[fixture], Sha256(Build(fixture)));
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void FixtureHasItsExpectedOutcome(string fixture)
    {
        string[] expected = DataLines("expected.tsv").Single(f => f[0] == fixture);
        byte[] data = Build(fixture);
        if (expected[3] == "load-fails")
        {
            Assert.Throws<OpenTypeFormatException>(() => OpenTypeFont.Load(data));
            Assert.False(OpenTypeFont.TryLoad(data, 0, out _));
            return;
        }

        ShapedRun run = ShapeTimed(data, expected[1], expected[2]);
        Assert.NotEmpty(run);
        if (expected[3] != "glyphs" && !IsPendingTamilShaping(expected[1]))
        {
            Assert.Equal(expected[3], Format(run));
        }
    }

    [Fact(Skip = "Exact Tamil glyphs need the Tamil shaping of phase 3 (design note v1); load, no-exception and time are checked now.")]
    public void TamilFixturesGiveTheExactExpectedGlyphs()
    {
        foreach (string[] expected in DataLines("expected.tsv").Where(f => f[1] == "Taml" && f[3] != "glyphs" && f[3] != "load-fails"))
        {
            Assert.Equal(expected[3], Format(ShapeTimed(Build(expected[0]), expected[1], expected[2])));
        }
    }

    [Fact]
    public void EveryM3VariantLoadsAndShapes()
    {
        string[] expected = DataLines("expected.tsv").Single(f => f[0] == "M3-*");
        byte[] baseFont = File.ReadAllBytes(Path.Combine(Folder, "M3-base-LiPi-Sans-Tamil.ttf"));
        List<string[]> variants = DataLines("M3-corrupt-layout-patches.tsv").ToList();
        Assert.Equal(180, variants.Count);
        foreach (string[] v in variants)
        {
            byte[] data = ApplyPatches(baseFont, v[2].Split(' '));
            Assert.NotEmpty(ShapeTimed(data, expected[1], expected[2]));
        }
    }

    [Fact]
    public void EveryFixtureWalksItsLayoutTablesWithoutThrowing()
    {
        // The layout views over every d.6 fixture that loads and every M3 variant: no exception, no hang.
        Stopwatch time = Stopwatch.StartNew();
        foreach (object[] f in Fixtures())
        {
            if (OpenTypeFont.TryLoad(Build((string)f[0]), 0, out OpenTypeFont? font))
            {
                _ = Chuvadi.OpenType.Tests.LayoutWalker.Walk(font!);
            }
        }

        byte[] baseFont = File.ReadAllBytes(Path.Combine(Folder, "M3-base-LiPi-Sans-Tamil.ttf"));
        Assert.NotEqual(0, Chuvadi.OpenType.Tests.LayoutWalker.Walk(OpenTypeFont.Load(baseFont)));
        foreach (string[] v in DataLines("M3-corrupt-layout-patches.tsv"))
        {
            _ = Chuvadi.OpenType.Tests.LayoutWalker.Walk(OpenTypeFont.Load(ApplyPatches(baseFont, v[2].Split(' '))));
        }

        Assert.True(time.Elapsed < TimeSpan.FromSeconds(120), $"took {time.Elapsed}");
    }

    private static bool IsPendingTamilShaping(string script) => script == "Taml";

    private static ShapedRun ShapeTimed(byte[] data, string script, string codePoints)
    {
        ShapingFont font = ShapingFont.Get(OpenTypeFont.Load(data));
        ShapeOptions options = new ShapeOptions { Script = Script.Parse(script), Language = Language.Default };
        string text = string.Concat(codePoints.Split(' ').Select(h => char.ConvertFromUtf32(int.Parse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture))));
        Stopwatch watch = Stopwatch.StartNew();
        ShapedRun run = TextShaper.Shape(font, text, options);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"shaping took {watch.Elapsed.TotalMilliseconds} ms");
        return run;
    }

    private static string Format(ShapedRun run) =>
        string.Join(" ", run.Select(g => string.Create(CultureInfo.InvariantCulture, $"{g.GlyphId}:{g.Cluster}:{g.XAdvance}:{g.XOffset}:{g.YOffset}")));

    private static byte[] Build(string fixture)
    {
        if (fixture.StartsWith("M3-", StringComparison.Ordinal) && fixture.EndsWith("-round0.ttf", StringComparison.Ordinal))
        {
            string table = fixture.Substring(3, 4);
            string[] line = DataLines("M3-corrupt-layout-patches.tsv").Single(f => f[0] == table && f[1] == "0");
            return ApplyPatches(File.ReadAllBytes(Path.Combine(Folder, "M3-base-LiPi-Sans-Tamil.ttf")), line[2].Split(' '));
        }

        string[] recipe = DataLines("recipes.tsv").Single(f => f[0] == fixture);
        List<byte> bytes = File.ReadAllBytes(Path.Combine(Folder, recipe[1])).ToList();
        foreach (string op in recipe[2].Split(' '))
        {
            int colon = op.IndexOf(':', StringComparison.Ordinal);
            string kind = op.Substring(0, colon);
            string value = op.Substring(colon + 1);
            switch (kind)
            {
                case "truncate":
                    bytes.RemoveRange(int.Parse(value, CultureInfo.InvariantCulture), bytes.Count - int.Parse(value, CultureInfo.InvariantCulture));
                    break;
                case "patch":
                    foreach (string p in value.Split(','))
                    {
                        string[] pair = p.Split(':');
                        bytes[int.Parse(pair[0], CultureInfo.InvariantCulture)] = byte.Parse(pair[1], CultureInfo.InvariantCulture);
                    }

                    break;
                case "append":
                    bytes.AddRange(Convert.FromHexString(value));
                    break;
                default:
                    throw new InvalidDataException("unknown recipe operation " + kind);
            }
        }

        return bytes.ToArray();
    }

    private static byte[] ApplyPatches(byte[] baseFont, IEnumerable<string> patches)
    {
        byte[] data = (byte[])baseFont.Clone();
        foreach (string p in patches)
        {
            string[] pair = p.Split(':');
            data[int.Parse(pair[0], CultureInfo.InvariantCulture)] = byte.Parse(pair[1], CultureInfo.InvariantCulture);
        }

        return data;
    }

    private static IEnumerable<string[]> DataLines(string file) =>
        File.ReadAllLines(Path.Combine(Folder, file), Encoding.UTF8)
            .Where(l => l.Length > 0 && l[0] != '#')
            .Select(l => l.Split('\t'));

    private static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
