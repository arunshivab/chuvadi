using System.Reflection;
using System.Security.Cryptography;
using Chuvadi.Fonts.ManualTests;

namespace Chuvadi.Fonts.Liberation.Tests;

/// <summary>D-302, D-307, D-311: Chuvadi.Fonts.Liberation as a pure data pack (<see cref="LiberationPack"/>).</summary>
public class LiberationPackTests
{
    [Fact]
    public void Pack_has_the_12_files_unchanged()
    {
        Assert.Equal(12, LiberationPack.Files.Count);
        Assert.Equal(4_359_164, LiberationPack.Files.Sum(f => f.Length));
        Assert.Equal(LiberationPack.Files.Select(f => f.FileName).OrderBy(n => n, StringComparer.Ordinal), LiberationPack.Files.Select(f => f.FileName));
        // The same files, byte for byte, that Chuvadi.Pdf.PdfA 3.32.1 embedded and Chuvadi.Layout.Fonts 0.1.0 carries.
        using IncrementalHash all = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (FontFileInfo f in LiberationPack.Files)
        {
            all.AppendData(LiberationPack.ReadAllBytes(f.FileName));
        }
        Assert.Equal("3e72b28294f5b176a00f27f9bcdac583d112146b8d5dcda4938118107511f1cb", Convert.ToHexStringLower(all.GetHashAndReset()));
    }

    [Fact]
    public void Every_row_matches_its_embedded_file()
    {
        foreach (FontFileInfo f in LiberationPack.Files)
        {
            byte[] data = LiberationPack.ReadAllBytes(f.FileName);
            Assert.Equal(f.Length, data.LongLength);
            Assert.Equal(f.Sha256, Convert.ToHexStringLower(SHA256.HashData(data)));
            SfntInfo info = SfntInfo.Read(data);
            Assert.Equal(f.FamilyName, info.Name(1));
            Assert.Equal(f.TypographicFamilyName, info.Name(16) ?? info.Name(1));
            Assert.Equal(f.StyleName, info.Name(2));
            Assert.Equal(f.FullName, info.Name(4));
            Assert.Equal(f.PostScriptName, info.Name(6));
            Assert.Equal(f.Weight, info.Weight);
            Assert.Equal(f.Italic, info.Italic);
            Assert.Equal(f.WidthClass, info.WidthClass);
        }
    }

    [Fact]
    public void Three_families_with_four_styles_each()
    {
        string[] families = { "Liberation Mono", "Liberation Sans", "Liberation Serif" };
        Assert.Equal(families, LiberationPack.Files.Select(f => f.FamilyName).Distinct().OrderBy(n => n, StringComparer.Ordinal));
        foreach (string family in families)
        {
            Assert.Equal(new[] { "Bold", "Bold Italic", "Italic", "Regular" },
                LiberationPack.FindFamily(family).Select(f => f.StyleName).OrderBy(n => n, StringComparer.Ordinal));
        }
        Assert.Equal(12, LiberationPack.Files.Select(f => f.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Same_bytes_as_the_Liberation_files_in_Layout_Fonts_0_1_0()
    {
        // Hashes of Chuvadi.Layout.Fonts 0.1.0 (its FONTS.md). When Layout.Fonts forwards to this package they are the same files.
        var expected = new Dictionary<string, string>
        {
            ["LiberationMono-Bold.ttf"] = "bd62a0672d0b9b6710b01df434c80ad54fa5f0835207eb7b17b7a761463067bb",
            ["LiberationMono-BoldItalic.ttf"] = "79451f3c09fe25116098853b7a2ca6e2436220ccc11af022979adbcf195be130",
            ["LiberationMono-Italic.ttf"] = "605c01c711b44480a7508d349dfbf3264e81fa43d69e61cfa7d10b86e764c4d1",
            ["LiberationMono-Regular.ttf"] = "f2b83c763e8afd21709333370bed4774337fae82267937e2b5aea7e2fbd922c1",
            ["LiberationSans-Bold.ttf"] = "788abee4c806d660e8aee46689dd8540cd4bb98da03dcc9d171ce3efd99a9173",
            ["LiberationSans-BoldItalic.ttf"] = "698da70fc191cc5f33ad4d6d3fe830fe4624b898ea2e3169955928b7c491f1ee",
            ["LiberationSans-Italic.ttf"] = "e5bae5c4cde31f22142753855f4f8fb86da6ff39955ed3c0a11248b0d16948b0",
            ["LiberationSans-Regular.ttf"] = "76d04c18ea243f426b7de1f3ad208e927008f961dc5945e5aad352d0dfde8ee8",
            ["LiberationSerif-Bold.ttf"] = "d754ba427cfe0bca54ae052384baa8f842da5bd6550ad4da024ac441e7a7d5ce",
            ["LiberationSerif-BoldItalic.ttf"] = "f17db8af71e24d2066b587546021d4f0b296be389512b658dec3c09affeb11a7",
            ["LiberationSerif-Italic.ttf"] = "0e3dea9f8d613e006ccfa62201f33e265d19167bd0907725c3e145368b04fc2e",
            ["LiberationSerif-Regular.ttf"] = "058ea80864aef09a23f45cbec2bb5400bc3dfbdea01c3f10538a21fcb497fb74",
        };
        Assert.Equal(expected.Keys.OrderBy(n => n, StringComparer.Ordinal), LiberationPack.Files.Select(f => f.FileName));
        foreach (FontFileInfo f in LiberationPack.Files)
        {
            Assert.Equal(expected[f.FileName], f.Sha256);
        }
    }

    [Fact]
    public void Lookups_ignore_case_and_unknown_names_are_reported()
    {
        Assert.Equal("LiberationSans-Regular.ttf", LiberationPack.Find("liberationsans-regular.TTF")!.FileName);
        Assert.Null(LiberationPack.Find("Nope.ttf"));
        Assert.Null(LiberationPack.Find("LiPi-Sans-Tamil-Regular.ttf"));
        Assert.False(LiberationPack.TryOpen("Nope.ttf", out Stream? none));
        Assert.Null(none);
        Assert.Throws<ArgumentException>(() => LiberationPack.Open("Nope.ttf"));
        Assert.Throws<ArgumentException>(() => LiberationPack.ReadAllBytes("Nope.ttf"));
        Assert.Throws<ArgumentException>(() => LiberationPack.Instance.GetLicenseText("Nope.ttf"));
        Assert.Throws<ArgumentNullException>(() => LiberationPack.Find(null!));
        Assert.Throws<ArgumentNullException>(() => LiberationPack.FindFamily(null!));
        Assert.Throws<ArgumentNullException>(() => LiberationPack.TryOpen(null!, out _));
        Assert.Throws<ArgumentNullException>(() => LiberationPack.Open(null!));
        Assert.Throws<ArgumentNullException>(() => LiberationPack.Instance.GetLicenseText(null!));
    }

    [Fact]
    public void FindFamily_matches_the_family_and_the_typographic_family()
    {
        Assert.Equal(4, LiberationPack.FindFamily("liberation sans").Count);
        Assert.Equal(4, LiberationPack.FindFamily("LIBERATION MONO").Count);
        Assert.Empty(LiberationPack.FindFamily("Carlito"));
        Assert.Empty(LiberationPack.FindFamily("LiPi Sans"));
    }

    [Fact]
    public void Streams_are_read_only_and_bytes_are_fresh_copies()
    {
        using Stream s = LiberationPack.Open("LiberationSerif-Italic.ttf");
        Assert.False(s.CanWrite);
        Assert.Equal(0, s.Position);
        byte[] a = LiberationPack.ReadAllBytes("LiberationSerif-Italic.ttf");
        byte[] b = LiberationPack.ReadAllBytes("LiberationSerif-Italic.ttf");
        Assert.NotSame(a, b);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Licence_is_the_OFL_with_the_Liberation_copyright_lines()
    {
        string text = LiberationPack.LicenseText;
        Assert.Contains("SIL OPEN FONT LICENSE Version 1.1", text, StringComparison.Ordinal);
        Assert.Contains("Reserved Font Name Liberation", text, StringComparison.Ordinal);
        Assert.Contains("Red Hat", text, StringComparison.Ordinal);
        Assert.Same(text, LiberationPack.LicenseText);
    }

    [Fact]
    public void Instance_is_the_pack_as_an_IFontFilePack()
    {
        IFontFilePack pack = LiberationPack.Instance;
        Assert.Equal("Chuvadi.Fonts.Liberation", pack.Name);
        Assert.Same(LiberationPack.Instance, pack);
        Assert.Same(LiberationPack.Files, pack.Files);
        using Stream s = pack.Open("liberationsans-regular.ttf");
        Assert.Equal(LiberationPack.Find("LiberationSans-Regular.ttf")!.Length, s.Length);
        foreach (FontFileInfo f in pack.Files)
        {
            Assert.Same(LiberationPack.LicenseText, pack.GetLicenseText(f.FileName));
        }
        // Not the LiPi Sans pack: Chuvadi.Fonts keeps its own 22 files.
        Assert.Equal(22, FontPack.Files.Count);
        Assert.Null(FontPack.Find("LiberationSans-Regular.ttf"));
    }

    [Fact]
    public void The_pack_references_no_layout_engine()
    {
        // D-195: a pure data pack. Its only reference beyond the BCL is Chuvadi.Fonts.
        string[] refs = typeof(LiberationPack).Assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("Chuvadi", StringComparison.Ordinal)).ToArray();
        Assert.Equal(new[] { "Chuvadi.Fonts" }, refs);
    }
}
