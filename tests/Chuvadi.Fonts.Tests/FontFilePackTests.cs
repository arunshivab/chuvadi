using Chuvadi.Fonts.ManualTests;

namespace Chuvadi.Fonts.Tests;

/// <summary>Chuvadi.Fonts 1.1.0 (D-195, D-197): the pack as an <see cref="IFontFilePack"/>.</summary>
public class FontFilePackTests
{
    [Fact]
    public void Instance_lists_the_same_22_files_as_FontPack()
    {
        IFontFilePack pack = FontPack.Instance;
        Assert.Equal("Chuvadi.Fonts", pack.Name);
        Assert.Same(pack, FontPack.Instance);
        Assert.Equal(FontPack.Files.Select(f => f.FileName), pack.Files.Select(f => f.FileName));
        for (int i = 0; i < pack.Files.Count; i++)
        {
            FontFileInfo f = pack.Files[i];
            FontPackFile p = FontPack.Files[i];
            Assert.Equal((p.FamilyName, p.StyleName, p.FullName, p.PostScriptName, p.Weight, p.Italic, p.Length, p.Sha256),
                (f.FamilyName, f.StyleName, f.FullName, f.PostScriptName, f.Weight, f.Italic, f.Length, f.Sha256));
        }
    }

    [Fact]
    public void Typographic_family_and_width_are_those_of_the_files()
    {
        foreach (FontFileInfo f in FontPack.Instance.Files)
        {
            SfntInfo info = SfntInfo.Read(FontPack.ReadAllBytes(f.FileName));
            Assert.Equal(info.Name(16) ?? info.Name(1), f.TypographicFamilyName);
            Assert.Equal(info.WidthClass, f.WidthClass);
            Assert.Equal(5, f.WidthClass);
        }
    }

    [Fact]
    public void Open_and_licence_follow_the_static_pack()
    {
        IFontFilePack pack = FontPack.Instance;
        using Stream s = pack.Open("lipi-sans-tamil-regular.ttf");
        Assert.False(s.CanWrite);
        Assert.Equal(FontPack.Find("LiPi-Sans-Tamil-Regular.ttf")!.Length, s.Length);
        Assert.Same(FontPack.LicenseText, pack.GetLicenseText("LiPi-Sans-Tamil-Regular.ttf"));
        Assert.Throws<ArgumentException>(() => pack.Open("Carlito-Regular.ttf"));
        Assert.Throws<ArgumentException>(() => pack.GetLicenseText("Carlito-Regular.ttf"));
        Assert.Throws<ArgumentNullException>(() => pack.Open(null!));
        Assert.Throws<ArgumentNullException>(() => pack.GetLicenseText(null!));
    }
}
