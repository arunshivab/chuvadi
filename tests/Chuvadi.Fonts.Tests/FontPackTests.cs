using System.Security.Cryptography;
using Chuvadi.Fonts.ManualTests;

namespace Chuvadi.Fonts.Tests;

public class FontPackTests
{
    [Fact]
    public void Pack_has_the_22_lipi_files_and_noto_emoji()
    {
        Assert.Equal(23, FontPack.Files.Count);
        Assert.Equal(4_970_604, FontPack.Files.Sum(f => f.Length));
        Assert.Equal(22, FontPack.Files.Count(f => f.Script != FontScript.Emoji));
        Assert.Equal(4_083_976, FontPack.Files.Where(f => f.Script != FontScript.Emoji).Sum(f => f.Length));
        Assert.Equal("NotoEmoji-Regular.ttf", FontPack.Files[^1].FileName);
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
        // D-113: moved byte for byte. One combined hash over the 22 LiPi Sans files in file-name order.
        using IncrementalHash all = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (FontPackFile f in FontPack.Files.Where(f => f.Script != FontScript.Emoji))
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
    [InlineData(FontScript.Emoji, "Noto Emoji", 1)]
    public void Each_script_has_its_family_and_styles(FontScript script, string family, int styles)
    {
        IReadOnlyList<FontPackFile> faces = FontPack.FindFamily(family);
        Assert.Equal(styles, faces.Count);
        Assert.All(faces, f => Assert.Equal(script, f.Script));
        Assert.Contains(faces, f => f.Weight == 400 && !f.Italic);
        if (script != FontScript.Emoji)
        {
            Assert.Contains(faces, f => f.Weight == 700 && !f.Italic);
        }
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
    public void Each_file_has_its_own_licence_text()
    {
        // D-420 F3: the LiPi Sans files keep LicenseText; Noto Emoji has Google's OFL text, byte for byte as delivered.
        foreach (FontPackFile f in FontPack.Files.Where(f => f.Script != FontScript.Emoji))
        {
            Assert.Same(FontPack.LicenseText, FontPack.GetLicenseText(f.FileName));
        }
        string emoji = FontPack.GetLicenseText("notoemoji-regular.TTF");
        Assert.Same(emoji, FontPack.GetLicenseText("NotoEmoji-Regular.ttf"));
        Assert.NotEqual(FontPack.LicenseText, emoji);
        Assert.StartsWith("Copyright 2013 Google LLC", emoji, StringComparison.Ordinal);
        Assert.Contains("SIL OPEN FONT LICENSE Version 1.1", emoji, StringComparison.Ordinal);
        Assert.DoesNotContain("with Reserved Font Name", emoji, StringComparison.Ordinal);
        string lf = emoji.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(ManualTests.Expected.EmojiLicenseSha256, Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(lf))));
        Assert.Throws<ArgumentException>(() => FontPack.GetLicenseText("Nope.ttf"));
        Assert.Throws<ArgumentNullException>(() => FontPack.GetLicenseText(null!));
    }

    [Fact]
    public void Noto_emoji_is_the_delivered_file()
    {
        // D-418, D-420: added unchanged; the SHA-256 is the one in the Pdf chat's PROVENANCE.md (v2).
        FontPackFile e = FontPack.Find("NotoEmoji-Regular.ttf")!;
        Assert.Equal(("Noto Emoji", "Regular", "Noto Emoji Regular", "NotoEmoji-Regular", 400, false, FontScript.Emoji, 886_628L),
            (e.FamilyName, e.StyleName, e.FullName, e.PostScriptName, e.Weight, e.Italic, e.Script, e.Length));
        Assert.Equal(ManualTests.Expected.EmojiSha256, e.Sha256);
        SfntInfo info = SfntInfo.Read(FontPack.ReadAllBytes(e.FileName));
        Assert.Equal("Copyright 2013 Google LLC", info.Name(0));
        Assert.Equal("Version 3.002", info.Name(5));
        Assert.Null(info.Name(16));
        Assert.Equal(5, info.WidthClass);
    }

    [Fact]
    public void Script_values_keep_their_numbers()
    {
        // F2: Emoji is added at the end, so the ten values of 1.0.0 and 1.1.0 keep their numbers.
        Assert.Equal(0, (int)FontScript.Latin);
        Assert.Equal(9, (int)FontScript.Telugu);
        Assert.Equal(10, (int)FontScript.Emoji);
        Assert.Equal(11, Enum.GetValues<FontScript>().Length);
    }

    [Fact]
    public void Verification_suite_passes()
    {
        Assert.Equal(0, Program.Run(TextWriter.Null));
    }
}
