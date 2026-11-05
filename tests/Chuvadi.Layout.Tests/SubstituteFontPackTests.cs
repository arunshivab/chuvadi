using System.Security.Cryptography;
using Chuvadi.Fonts;
using Chuvadi.Layout.Fonts;

namespace Chuvadi.Layout.Tests;

/// <summary>D-195, D-197: Chuvadi.Layout.Fonts as a pure data pack (<see cref="SubstituteFontPack"/>).</summary>
public class SubstituteFontPackTests
{
    [Fact]
    public void Pack_has_the_29_files_unchanged()
    {
        Assert.Equal(29, SubstituteFontPack.Files.Count);
        Assert.Equal(8_264_644, SubstituteFontPack.Files.Sum(f => f.Length));
        Assert.Equal(SubstituteFontPack.Files.Select(f => f.FileName).OrderBy(n => n, StringComparer.Ordinal), SubstituteFontPack.Files.Select(f => f.FileName));
        // The same files, byte for byte, as Chuvadi.Layout.Fonts carried before it became a data pack.
        using IncrementalHash all = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (FontFileInfo f in SubstituteFontPack.Files)
        {
            all.AppendData(SubstituteFontPack.ReadAllBytes(f.FileName));
        }
        Assert.Equal("aead71db5d0e71d4f071cf7086f4a6a53b847a891ffdfc3879043abf62e3ece2", Convert.ToHexStringLower(all.GetHashAndReset()));
    }

    [Fact]
    public void Every_row_matches_its_embedded_file()
    {
        foreach (FontFileInfo f in SubstituteFontPack.Files)
        {
            byte[] data = SubstituteFontPack.ReadAllBytes(f.FileName);
            Assert.Equal(f.Length, data.LongLength);
            Assert.Equal(f.Sha256, Convert.ToHexStringLower(SHA256.HashData(data)));
            FontFace face = FontFace.Load(data, 0, FontOrigin.Bundled, f.FileName);
            Assert.Equal(f.FamilyName, face.FamilyName);
            Assert.Equal(f.TypographicFamilyName, face.TypographicFamilyName);
            Assert.Equal(f.StyleName, face.SubfamilyName);
            Assert.Equal(f.FullName, face.FullName);
            Assert.Equal(f.PostScriptName, face.PostScriptName);
            Assert.Equal(f.Weight, face.Weight);
            Assert.Equal(f.Italic, face.IsItalic);
            Assert.Equal(f.WidthClass, face.WidthClass);
        }
    }

    [Fact]
    public void Lookups_ignore_case_and_unknown_names_are_reported()
    {
        Assert.Equal("Carlito-Regular.ttf", SubstituteFontPack.Find("carlito-regular.TTF")!.FileName);
        Assert.Null(SubstituteFontPack.Find("Nope.ttf"));
        Assert.Null(SubstituteFontPack.Find("LiPi-Sans-Tamil-Regular.ttf"));
        Assert.False(SubstituteFontPack.TryOpen("Nope.ttf", out Stream? none));
        Assert.Null(none);
        Assert.Throws<ArgumentException>(() => SubstituteFontPack.Open("Nope.ttf"));
        Assert.Throws<ArgumentException>(() => SubstituteFontPack.ReadAllBytes("Nope.ttf"));
        Assert.Throws<ArgumentException>(() => SubstituteFontPack.GetLicenseText("Nope.ttf"));
        Assert.Throws<ArgumentNullException>(() => SubstituteFontPack.Find(null!));
        Assert.Throws<ArgumentNullException>(() => SubstituteFontPack.FindFamily(null!));
        Assert.Throws<ArgumentNullException>(() => SubstituteFontPack.Open(null!));
        Assert.Throws<ArgumentNullException>(() => SubstituteFontPack.GetLicenseText(null!));
    }

    [Fact]
    public void FindFamily_matches_the_family_and_the_typographic_family()
    {
        Assert.Equal(4, SubstituteFontPack.FindFamily("liberation sans").Count);
        Assert.Equal(5, SubstituteFontPack.FindFamily("Selawik").Count);
        Assert.Equal("selawkl.ttf", Assert.Single(SubstituteFontPack.FindFamily("Selawik Light")).FileName);
        Assert.Empty(SubstituteFontPack.FindFamily("LiPi Sans"));
    }

    [Fact]
    public void Streams_are_read_only_and_bytes_are_fresh_copies()
    {
        using Stream s = SubstituteFontPack.Open("Carlito-Regular.ttf");
        Assert.False(s.CanWrite);
        Assert.Equal(0, s.Position);
        byte[] a = SubstituteFontPack.ReadAllBytes("Carlito-Regular.ttf");
        byte[] b = SubstituteFontPack.ReadAllBytes("Carlito-Regular.ttf");
        Assert.NotSame(a, b);
        Assert.Equal(a, b);
    }

    [Theory]
    [InlineData("Caladea-Regular.ttf", "The Caladea Project Authors")]
    [InlineData("Carlito-BoldItalic.ttf", "The Carlito Project Authors")]
    [InlineData("Gelasio-Italic.ttf", "The Gelasio Project Authors")]
    [InlineData("LiberationMono-Bold.ttf", "Reserved Font Name Liberation")]
    [InlineData("selawksb.ttf", "Reserved Font Name Selawik")]
    public void Each_file_has_its_family_licence(string file, string copyright)
    {
        string text = SubstituteFontPack.GetLicenseText(file);
        Assert.Contains("SIL OPEN FONT LICENSE Version 1.1", text, StringComparison.Ordinal);
        Assert.Contains(copyright, text, StringComparison.Ordinal);
        Assert.Same(text, SubstituteFontPack.GetLicenseText(file));
    }

    [Fact]
    public void Instance_is_the_pack_as_an_IFontFilePack()
    {
        IFontFilePack pack = SubstituteFontPack.Instance;
        Assert.Equal("Chuvadi.Layout.Fonts", pack.Name);
        Assert.Same(SubstituteFontPack.Files, pack.Files);
        using Stream s = pack.Open("LiberationSans-Regular.ttf");
        Assert.Equal(SubstituteFontPack.Find("LiberationSans-Regular.ttf")!.Length, s.Length);
        Assert.Same(SubstituteFontPack.GetLicenseText("LiberationSans-Regular.ttf"), pack.GetLicenseText("LiberationSans-Regular.ttf"));
    }

    [Fact]
    public void The_pack_references_no_layout_engine()
    {
        // D-195: a pure data pack. Its only reference beyond the BCL is Chuvadi.Fonts.
        string[] refs = typeof(SubstituteFontPack).Assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("Chuvadi", StringComparison.Ordinal)).ToArray();
        Assert.Equal(new[] { "Chuvadi.Fonts" }, refs);
    }
}

/// <summary>D-195, D-197: the engine's adapter from any <see cref="IFontFilePack"/> to an <see cref="IFontSource"/>.</summary>
public class FontPackSourceTests
{
    [Fact]
    public void For_returns_one_shared_source_per_pack()
    {
        FontPackSource a = FontPackSource.For(SubstituteFontPack.Instance);
        Assert.Same(a, FontPackSource.For(SubstituteFontPack.Instance));
        Assert.NotSame(a, FontPackSource.For(FontPack.Instance));
        Assert.NotSame(a, new FontPackSource(SubstituteFontPack.Instance));
        Assert.Same(SubstituteFontPack.Instance, a.Pack);
        Assert.Equal("Chuvadi.Layout.Fonts", a.Name);
        Assert.Equal("Chuvadi.Fonts", FontPackSource.For(FontPack.Instance).Name);
        Assert.Throws<ArgumentNullException>(() => FontPackSource.For(null!));
        Assert.Throws<ArgumentNullException>(() => new FontPackSource(null!));
    }

    [Fact]
    public void Descriptors_equal_the_identity_parsed_from_each_file()
    {
        // The same descriptors the resolver had before (BundledFontSource and the built-in source): Layout output
        // cannot change.
        foreach (IFontFilePack pack in new IFontFilePack[] { SubstituteFontPack.Instance, FontPack.Instance })
        {
            FontPackSource source = FontPackSource.For(pack);
            Assert.Equal(pack.Files.Count, source.GetFaces().Count);
            foreach (FontFaceDescriptor d in source.GetFaces())
            {
                FontFace f = source.LoadFace(d);
                Assert.Equal(FontFaceDescriptors.FromFace(f, d.Key), d);
                Assert.Equal(FontOrigin.Bundled, d.Origin);
                Assert.Equal(0, d.FaceIndex);
                Assert.Same(f, source.LoadFace(d));
            }
        }
        FontFaceDescriptor light = FontPackSource.For(SubstituteFontPack.Instance).GetFaces().Single(d => d.Key == "selawkl.ttf");
        Assert.Equal(("Selawik Light", "Selawik", 300), (light.FamilyName, light.TypographicFamilyName, light.Weight));
    }

    [Fact]
    public void The_origin_can_be_chosen_and_unknown_keys_are_rejected()
    {
        FontPackSource supplied = new FontPackSource(SubstituteFontPack.Instance, FontOrigin.Supplied);
        FontFaceDescriptor d = supplied.GetFaces()[0];
        Assert.Equal(FontOrigin.Supplied, d.Origin);
        Assert.Equal(FontOrigin.Supplied, supplied.LoadFace(d).Origin);
        Assert.Throws<ArgumentException>(() => supplied.LoadFace(d with { Key = "Nope.ttf" }));
        Assert.Throws<ArgumentNullException>(() => supplied.LoadFace(null!));
    }

    [Fact]
    public void The_built_in_fonts_are_the_shared_source_of_Chuvadi_Fonts()
    {
        FontResolver r = new LayoutOptions().CreateFontResolver();
        Assert.Contains(FontPackSource.For(FontPack.Instance), r.Sources);
        Assert.DoesNotContain(r.Sources, s => s is FontPackSource p && ReferenceEquals(p.Pack, SubstituteFontPack.Instance));
        LayoutOptions off = new LayoutOptions { UseBuiltInFonts = false };
        Assert.Empty(off.CreateFontResolver().Sources);
    }

    [Fact]
    public void An_application_can_bring_its_own_pack()
    {
        OnePack mine = new OnePack();
        LayoutOptions o = new LayoutOptions { UseBuiltInFonts = false };
        o.FontSources.Add(FontPackSource.For(mine));
        FontResolution res = o.CreateFontResolver().Resolve(new FontRequest("Carlito"));
        Assert.Equal("Carlito", res.Face.FamilyName);
        Assert.Equal("my.ttf", res.Face.SourceName);
    }

    private sealed class OnePack : IFontFilePack
    {
        private static readonly FontFileInfo Carlito = SubstituteFontPack.Find("Carlito-Regular.ttf")! with { FileName = "my.ttf" };

        public string Name => "Mine";

        public IReadOnlyList<FontFileInfo> Files { get; } = new[] { Carlito };

        public Stream Open(string fileName) => fileName == "my.ttf" ? SubstituteFontPack.Open("Carlito-Regular.ttf") : throw new ArgumentException(fileName);

        public string GetLicenseText(string fileName) => SubstituteFontPack.GetLicenseText("Carlito-Regular.ttf");
    }
}
