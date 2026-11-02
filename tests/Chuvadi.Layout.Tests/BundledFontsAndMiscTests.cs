using Chuvadi.Layout.Fonts;
using Chuvadi.Layout.Text;

namespace Chuvadi.Layout.Tests;

public class BundledFontsTests
{
    [Fact]
    public void Every_catalog_entry_loads_and_matches_its_parsed_identity()
    {
        IReadOnlyList<FontFaceDescriptor> faces = BundledFontSource.Instance.GetFaces();
        Assert.Equal(42, faces.Count);
        foreach (FontFaceDescriptor d in faces)
        {
            FontFace f = BundledFontSource.Instance.LoadFace(d);
            Assert.Equal(d.FamilyName, f.FamilyName);
            Assert.Equal(d.SubfamilyName, f.SubfamilyName);
            Assert.Equal(d.Weight, f.Weight);
            Assert.Equal(d.IsItalic, f.IsItalic);
            Assert.Equal(FontOrigin.Bundled, f.Origin);
            Assert.Equal(FontOutlineFormat.TrueType, f.OutlineFormat);
            Assert.True(f.EmbeddingRights == FontEmbeddingRights.Installable, d.Key + " must be installable-embeddable");
        }
    }

    [Fact]
    public void Pack_has_four_styles_of_each_metric_compatible_family()
    {
        foreach (string family in new[] { "Liberation Sans", "Liberation Serif", "Liberation Mono", "Carlito", "Caladea", "LiPi Sans" })
        {
            List<FontFaceDescriptor> styles = BundledFontSource.Instance.GetFaces().Where(f => f.FamilyName == family).ToList();
            Assert.Equal(4, styles.Count);
            Assert.Contains(styles, s => s.Weight == 700 && s.IsItalic);
        }
        Assert.Contains("LiPi Sans Malayalam", BundledFontSource.Families);
    }

    [Fact]
    public void Each_indic_fallback_face_covers_its_script()
    {
        (string Family, int Sample)[] checks =
        {
            ("LiPi Sans Devanagari", 0x0915), ("LiPi Sans Bengali", 0x0995), ("LiPi Sans Gurmukhi", 0x0A15),
            ("LiPi Sans Gujarati", 0x0A95), ("LiPi Sans Odia", 0x0B15), ("LiPi Sans Tamil", 0x0B95),
            ("LiPi Sans Telugu", 0x0C15), ("LiPi Sans Kannada", 0x0C95), ("LiPi Sans Malayalam", 0x0D15),
        };
        FontResolver r = TestFonts.BundledResolver();
        foreach ((string family, int sample) in checks)
        {
            FontResolution res = r.Resolve(new FontRequest(family));
            Assert.True(res.Face.HasGlyph(sample), family);
            FontResolution viaFallback = r.ResolveForCodepoint(r.Resolve(new FontRequest("Arial")), sample);
            Assert.Equal(family, viaFallback.Face.FamilyName);
        }
    }

    [Fact]
    public void Unknown_resource_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => BundledFontSource.ReadResource("Nope.ttf"));
    }
}

public class EmbeddedFontObfuscationTests
{
    [Fact]
    public void Key_is_the_guid_bytes_in_reverse_string_order()
    {
        byte[] key = EmbeddedFontObfuscation.DeriveKey("{00112233-4455-6677-8899-AABBCCDDEEFF}");
        Assert.Equal(new byte[] { 0xFF, 0xEE, 0xDD, 0xCC, 0xBB, 0xAA, 0x99, 0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11, 0x00 }, key);
    }

    [Fact]
    public void Obfuscation_touches_only_the_first_32_bytes_and_round_trips()
    {
        byte[] font = TestFonts.Bytes("Carlito-Regular.ttf");
        const string Key = "{6C5B3A1F-0D2E-4F80-9A7B-3C2D1E0F4A5B}";
        byte[] obfuscated = EmbeddedFontObfuscation.Obfuscate(font, Key);
        Assert.NotEqual(font.AsSpan(0, 32).ToArray(), obfuscated.AsSpan(0, 32).ToArray());
        Assert.Equal(font.AsSpan(32).ToArray(), obfuscated.AsSpan(32).ToArray());
        Assert.Throws<FontFormatException>(() => FontFace.Load(obfuscated));
        byte[] restored = EmbeddedFontObfuscation.Deobfuscate(obfuscated, Key);
        Assert.Equal(font, restored);
        Assert.Equal("Carlito", FontFace.Load(restored).FamilyName);
        // The input buffer is never modified.
        Assert.Equal(font, TestFonts.Bytes("Carlito-Regular.ttf"));
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("{0011223344}")]
    [InlineData("{00112233-4455-6677-8899-AABBCCDDEEFF0}")]
    public void Bad_keys_are_rejected(string key)
    {
        Assert.Throws<FormatException>(() => EmbeddedFontObfuscation.DeriveKey(key));
    }
}

public class UnicodeScriptTests
{
    [Theory]
    [InlineData('A', UnicodeScript.Latin)]
    [InlineData('1', UnicodeScript.Common)]
    [InlineData(' ', UnicodeScript.Common)]
    [InlineData(0x0301, UnicodeScript.Inherited)]
    [InlineData(0x0BA4, UnicodeScript.Tamil)]
    [InlineData(0x0D15, UnicodeScript.Malayalam)]
    [InlineData(0x0915, UnicodeScript.Devanagari)]
    [InlineData(0x0627, UnicodeScript.Arabic)]
    [InlineData(0x4E00, UnicodeScript.Han)]
    [InlineData(0x3042, UnicodeScript.Hiragana)]
    [InlineData(0xAC00, UnicodeScript.Hangul)]
    [InlineData(0x0E01, UnicodeScript.Thai)]
    [InlineData(0x10000, UnicodeScript.Other)] // Linear B
    [InlineData(0x10FFFF, UnicodeScript.Unknown)]
    [InlineData(-1, UnicodeScript.Unknown)]
    public void Script_lookup(int cp, UnicodeScript expected)
    {
        Assert.Equal(expected, UnicodeScripts.GetScript(cp));
    }

    [Fact]
    public void Classification_helpers()
    {
        Assert.True(UnicodeScripts.IsIndic(UnicodeScript.Tamil));
        Assert.False(UnicodeScripts.IsIndic(UnicodeScript.Latin));
        Assert.True(UnicodeScripts.IsRightToLeft(UnicodeScript.Hebrew));
        Assert.True(UnicodeScripts.NeedsComplexShaping(UnicodeScript.Thai));
        Assert.False(UnicodeScripts.NeedsComplexShaping(UnicodeScript.Han));
    }
}

public class LayoutLimitsTests
{
    [Fact]
    public void Check_throws_with_details()
    {
        LayoutLimitExceededException ex = Assert.Throws<LayoutLimitExceededException>(() => LayoutLimits.Check(nameof(LayoutLimits.MaxPages), 10, 11));
        Assert.Equal("MaxPages", ex.LimitName);
        Assert.Equal(10, ex.Limit);
        Assert.Equal(11, ex.Actual);
        LayoutLimits.Check("MaxPages", 10, 10);
    }

    [Fact]
    public void Options_build_resolver_with_embedded_first()
    {
        LayoutOptions o = new LayoutOptions();
        o.FontSources.Add(BundledFontSource.Instance);
        MemoryFontSource embedded = new MemoryFontSource("embedded", FontOrigin.Embedded);
        embedded.Add(TestFonts.Bytes("Carlito-Regular.ttf"));
        FontResolver r = o.CreateFontResolver(embedded);
        Assert.Equal(FontOrigin.Embedded, r.Resolve(new FontRequest("Carlito")).Face.Origin);
        o.UseEmbeddedFonts = false;
        Assert.Equal(FontOrigin.Bundled, o.CreateFontResolver(embedded).Resolve(new FontRequest("Carlito")).Face.Origin);
    }
}
