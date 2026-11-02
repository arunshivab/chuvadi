using System.Security.Cryptography;
using Chuvadi.Fonts.ManualTests;

namespace Chuvadi.Fonts.Tests;

public class FontPackTests
{
    [Fact]
    public void Pack_has_the_22_lipi_files()
    {
        Assert.Equal(22, FontPack.Files.Count);
        Assert.Equal(4_083_976, FontPack.Files.Sum(f => f.Length));
        Assert.Equal(FontPack.Files.Select(f => f.FileName).OrderBy(n => n, StringComparer.Ordinal), FontPack.Files.Select(f => f.FileName));
    }

    [Fact]
    public void Every_catalog_row_matches_its_embedded_file()
    {
        foreach (FontPackFile f in FontPack.Files)
        {
            byte[] data = FontPack.ReadAllBytes(f.FileName);
            Assert.Equal(f.Length, data.LongLength);
            Assert.Equal(f.Sha256, Convert.ToHexStringLower(SHA256.HashData(data)));
            SfntInfo info = SfntInfo.Read(data);
            Assert.Equal(f.FamilyName, info.Name(1));
            Assert.Equal(f.StyleName, info.Name(2));
            Assert.Equal(f.FullName, info.Name(4));
            Assert.Equal(f.PostScriptName, info.Name(6));
            Assert.Equal(f.Weight, info.Weight);
            Assert.Equal(f.Italic, info.Italic);
            Assert.Equal(0, info.FsType); // installable embedding
            Assert.Equal(0x00010000u, info.Version); // TrueType outlines
        }
    }

    [Fact]
    public void Files_are_those_layout_fonts_carried_before_the_move()
    {
        // D-113: moved byte for byte. One combined hash over the 22 files in file-name order.
        using IncrementalHash all = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (FontPackFile f in FontPack.Files)
        {
            all.AppendData(FontPack.ReadAllBytes(f.FileName));
        }
        Assert.Equal(ManualTests.Expected.CombinedSha256, Convert.ToHexStringLower(all.GetHashAndReset()));
    }

    [Theory]
    [InlineData(FontScript.Latin, "LiPi Sans", 4)]
    [InlineData(FontScript.Bengali, "LiPi Sans Bengali", 2)]
    [InlineData(FontScript.Devanagari, "LiPi Sans Devanagari", 2)]
    [InlineData(FontScript.Gujarati, "LiPi Sans Gujarati", 2)]
    [InlineData(FontScript.Gurmukhi, "LiPi Sans Gurmukhi", 2)]
    [InlineData(FontScript.Kannada, "LiPi Sans Kannada", 2)]
    [InlineData(FontScript.Malayalam, "LiPi Sans Malayalam", 2)]
    [InlineData(FontScript.Odia, "LiPi Sans Odia", 2)]
    [InlineData(FontScript.Tamil, "LiPi Sans Tamil", 2)]
    [InlineData(FontScript.Telugu, "LiPi Sans Telugu", 2)]
    public void Each_script_has_its_family_and_styles(FontScript script, string family, int styles)
    {
        IReadOnlyList<FontPackFile> faces = FontPack.FindFamily(family);
        Assert.Equal(styles, faces.Count);
        Assert.All(faces, f => Assert.Equal(script, f.Script));
        Assert.Contains(faces, f => f.Weight == 400 && !f.Italic);
        Assert.Contains(faces, f => f.Weight == 700 && !f.Italic);
        Assert.Equal(styles, FontPack.Files.Count(f => f.Script == script));
    }

    [Fact]
    public void Lookups_ignore_case_and_unknown_names_are_reported()
    {
        Assert.NotNull(FontPack.Find("lipi-sans-tamil-regular.TTF"));
        Assert.Null(FontPack.Find("Nope.ttf"));
        Assert.Empty(FontPack.FindFamily("Nope"));
        Assert.Equal(2, FontPack.FindFamily("lipi sans tamil").Count);
        Assert.False(FontPack.TryOpen("Nope.ttf", out Stream? none));
        Assert.Null(none);
        ArgumentException ex = Assert.Throws<ArgumentException>(() => FontPack.Open("Nope.ttf"));
        Assert.Contains("Nope.ttf", ex.Message);
        Assert.Throws<ArgumentException>(() => FontPack.ReadAllBytes("Nope.ttf"));
        Assert.Throws<ArgumentNullException>(() => FontPack.Find(null!));
    }

    [Fact]
    public void Streams_are_read_only_and_bytes_are_fresh_copies()
    {
        using Stream s = FontPack.Open("LiPi-Sans-Latin-Regular.ttf");
        Assert.False(s.CanWrite);
        Assert.Equal(0, s.Position);
        Assert.Equal(FontPack.Find("LiPi-Sans-Latin-Regular.ttf")!.Length, s.Length);
        byte[] a = FontPack.ReadAllBytes("LiPi-Sans-Latin-Regular.ttf");
        a[0] ^= 0xFF;
        byte[] b = FontPack.ReadAllBytes("LiPi-Sans-Latin-Regular.ttf");
        Assert.NotEqual(a[0], b[0]);
    }

    [Fact]
    public void Licence_is_the_ofl()
    {
        Assert.Contains("SIL OPEN FONT LICENSE Version 1.1", FontPack.LicenseText, StringComparison.OrdinalIgnoreCase);
        Assert.Same(FontPack.LicenseText, FontPack.LicenseText);
    }

    [Fact]
    public void Verification_suite_passes()
    {
        Assert.Equal(0, Program.Run(TextWriter.Null));
    }
}
