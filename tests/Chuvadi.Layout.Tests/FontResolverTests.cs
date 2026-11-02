using Chuvadi.Layout.Engine;
using Chuvadi.Layout.Drawing;
using Chuvadi.Layout.Model;
using Chuvadi.Layout.Fonts;
using Chuvadi.Layout.Text;

namespace Chuvadi.Layout.Tests;

public class FontResolverTests
{
    [Fact]
    public void Calibri_resolves_to_Carlito_as_metric_compatible_and_is_reported()
    {
        FontResolver r = TestFonts.BundledResolver();
        FontResolution res = r.Resolve(new FontRequest("Calibri"));
        Assert.Equal("Carlito", res.Face.FamilyName);
        Assert.Equal(FontMatchKind.MetricCompatibleSubstitute, res.Kind);
        Assert.True(res.IsMetricFaithful);
        FontSubstitution s = Assert.Single(r.ReportedSubstitutions);
        Assert.Equal("Calibri", s.RequestedFamily);
        Assert.Equal("Carlito", s.UsedFamily);
        Assert.True(s.MetricCompatible);
        Assert.Equal(FontOrigin.Bundled, s.UsedOrigin);
    }

    [Theory]
    [InlineData("Arial", "Liberation Sans")]
    [InlineData("Times New Roman", "Liberation Serif")]
    [InlineData("Courier New", "Liberation Mono")]
    [InlineData("Cambria", "Caladea")]
    public void Office_core_fonts_map_to_metric_compatible_substitutes(string requested, string expected)
    {
        FontResolution res = TestFonts.BundledResolver().Resolve(new FontRequest(requested, 700, true));
        Assert.Equal(expected, res.Face.FamilyName);
        Assert.True(res.Face.IsBold);
        Assert.True(res.Face.IsItalic);
        Assert.False(res.SyntheticBold);
        Assert.False(res.SyntheticItalic);
        Assert.Equal(FontMatchKind.MetricCompatibleSubstitute, res.Kind);
    }

    [Fact]
    public void Exact_family_is_not_reported_as_a_substitution()
    {
        FontResolver r = TestFonts.BundledResolver();
        FontResolution res = r.Resolve(new FontRequest("carlito"));
        Assert.Equal(FontMatchKind.Exact, res.Kind);
        Assert.Empty(r.ReportedSubstitutions);
    }

    [Fact]
    public void Non_metric_substitute_is_flagged()
    {
        FontResolution res = TestFonts.BundledResolver().Resolve(new FontRequest("Segoe UI"));
        Assert.Equal("Liberation Sans", res.Face.FamilyName);
        Assert.Equal(FontMatchKind.Substitute, res.Kind);
        Assert.False(res.IsMetricFaithful);
    }

    [Fact]
    public void Unknown_family_uses_the_generic_class()
    {
        FontResolution res = TestFonts.BundledResolver().Resolve(new FontRequest("Some Unheard Of Font", Generic: GenericFontFamily.Serif));
        Assert.Equal("Liberation Serif", res.Face.FamilyName);
        Assert.Equal(FontMatchKind.GenericFallback, res.Kind);
    }

    [Fact]
    public void Missing_style_is_synthesised()
    {
        FontResolution res = TestFonts.BundledResolver().Resolve(new FontRequest("LiPi Sans Tamil", 700, true));
        Assert.True(res.Face.IsBold);
        Assert.False(res.Face.IsItalic);
        Assert.True(res.SyntheticItalic);
        Assert.False(res.SyntheticBold);
    }

    [Fact]
    public void Codepoint_without_glyph_falls_back_by_script()
    {
        FontResolver r = TestFonts.BundledResolver();
        FontResolution primary = r.Resolve(new FontRequest("Calibri"));
        FontResolution tamil = r.ResolveForCodepoint(primary, 0x0BA4);
        Assert.Equal("LiPi Sans Tamil", tamil.Face.FamilyName);
        Assert.Equal(FontMatchKind.ScriptFallback, tamil.Kind);
        Assert.Same(primary, r.ResolveForCodepoint(primary, 'A'));
        Assert.Contains(r.ReportedSubstitutions, s => s.Script == UnicodeScript.Tamil && s.UsedFamily == "LiPi Sans Tamil");
        // Reported once, however often resolved.
        r.ResolveForCodepoint(primary, 0x0BA4);
        Assert.Single(r.ReportedSubstitutions, s => s.Script == UnicodeScript.Tamil);
    }

    [Theory]
    [InlineData(400, new[] { 300, 700 }, 300)] // 400: up to 500 first, then below, then above 500.
    [InlineData(400, new[] { 500, 300 }, 500)]
    [InlineData(500, new[] { 400, 700 }, 400)]
    [InlineData(700, new[] { 400, 900 }, 900)]
    [InlineData(300, new[] { 200, 400 }, 200)]
    public void Weight_matching_follows_css(int desired, int[] available, int expected)
    {
        List<FontFaceDescriptor> faces = available.Select(w => new FontFaceDescriptor("F", "F", "s", "F", "F", w, false, 5, FontOrigin.Supplied, "k" + w, 0)).ToList();
        Assert.Equal(expected, FontResolver.PickStyle(faces, desired, false).Weight);
    }

    [Fact]
    public void Earlier_sources_win_over_later_ones()
    {
        MemoryFontSource supplied = new MemoryFontSource();
        supplied.Add(TestFonts.Bytes("Caladea-Regular.ttf"), "my-caladea.ttf");
        FontResolver r = new FontResolver(new IFontSource[] { supplied, BundledFontSource.Instance });
        FontResolution res = r.Resolve(new FontRequest("Caladea"));
        Assert.Equal(FontOrigin.Supplied, res.Face.Origin);
        Assert.Equal("my-caladea.ttf", res.Face.SourceName);
    }

    [Fact]
    public void No_sources_throws_a_clear_exception()
    {
        FontResolver r = new FontResolver(Array.Empty<IFontSource>());
        LayoutException ex = Assert.Throws<LayoutException>(() => r.Resolve(new FontRequest("Arial")));
        Assert.Contains("No fonts", ex.Message);
    }

    [Fact]
    public void Directory_source_scans_without_full_parse_and_loads_faces()
    {
        string dir = Path.Combine(Path.GetTempPath(), "chuvadi-layout-fonts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "a.TTF"), TestFonts.Bytes("Carlito-Bold.ttf"));
            File.WriteAllBytes(Path.Combine(dir, "pair.ttc"), TestFonts.BuildCollection(TestFonts.Bytes("Caladea-Regular.ttf"), TestFonts.Bytes("Caladea-Italic.ttf")));
            File.WriteAllBytes(Path.Combine(dir, "junk.ttf"), new byte[] { 0, 1, 0, 0, 9, 9, 9 });
            File.WriteAllText(Path.Combine(dir, "readme.txt"), "not a font");
            DirectoryFontSource src = new DirectoryFontSource(dir);
            IReadOnlyList<FontFaceDescriptor> faces = src.GetFaces();
            Assert.Equal(3, faces.Count);
            FontFaceDescriptor italic = faces.Single(f => f.IsItalic);
            Assert.Equal(1, italic.FaceIndex);
            FontFace loaded = src.LoadFace(italic);
            Assert.Equal("Caladea", loaded.FamilyName);
            Assert.True(loaded.IsItalic);
            Assert.True(faces.Single(f => f.FamilyName == "Carlito").Weight >= 700);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

/// <summary>D-116: installed fonts before the pack, the option to prefer the pack, and embedded subset fonts.</summary>
public class FontSearchOrderTests
{
    private static List<string> Names(LayoutOptions o) => o.CreateFontResolver(new MemoryFontSource("Embedded fonts", FontOrigin.Embedded)).Sources.Select(s => s.Name).ToList();

    [Fact]
    public void Installed_fonts_come_before_the_pack_unless_the_pack_is_preferred()
    {
        LayoutOptions o = new LayoutOptions { AllowSystemFonts = true };
        o.FontSources.Add(BundledFontSource.Instance);
        Assert.Equal(new[] { "Embedded fonts", "System fonts", "Chuvadi.Layout.Fonts", "Chuvadi.Fonts" }, Names(o));
        o.PreferBundledFonts = true;
        Assert.Equal(new[] { "Embedded fonts", "Chuvadi.Layout.Fonts", "Chuvadi.Fonts", "System fonts" }, Names(o));
        // Without installed fonts the option changes nothing.
        LayoutOptions off = new LayoutOptions();
        off.FontSources.Add(BundledFontSource.Instance);
        List<string> plain = Names(off);
        off.PreferBundledFonts = true;
        Assert.Equal(plain, Names(off));
        Assert.Equal(new[] { "Embedded fonts", "Chuvadi.Layout.Fonts", "Chuvadi.Fonts" }, plain);
    }

    [Fact]
    public void A_family_found_earlier_wins_so_the_installed_copy_is_used_by_default()
    {
        // Two copies of the same family in search order: the first source's copy is used (here the "installed" one).
        MemoryFontSource installed = new MemoryFontSource("Installed", FontOrigin.System);
        installed.Add(TestFonts.Bytes("Carlito-Regular.ttf"), "carlito-installed.ttf");
        FontResolver r = new FontResolver(new IFontSource[] { installed, BundledFontSource.Instance });
        Assert.Equal(FontOrigin.System, r.Resolve(new FontRequest("Carlito")).Face.Origin);
        FontResolver packFirst = new FontResolver(new IFontSource[] { BundledFontSource.Instance, installed });
        Assert.Equal(FontOrigin.Bundled, packFirst.Resolve(new FontRequest("Carlito")).Face.Origin);
    }

    private static LaidOutDocument LayOut(string text, out FlowDocument doc)
    {
        MemoryFontSource embedded = new MemoryFontSource("Embedded fonts", FontOrigin.Embedded);
        embedded.Add(TestFonts.SubsetCmap(TestFonts.Bytes("Caladea-Regular.ttf"), "Hello wrd".Select(c => (int)c)), "Caladea (subset)");
        doc = Flow.Doc(FlowParagraph.Of(text, new CharacterFormat { FontFamily = "Caladea", FontSize = 11 }, new ParagraphFormat()));
        doc.EmbeddedFonts = embedded;
        return DocumentLayout.Open(doc, Flow.Options());
    }

    [Fact]
    public void A_character_missing_from_an_embedded_subset_uses_the_full_font_of_the_same_family_with_a_warning()
    {
        using LaidOutDocument laid = LayOut("Hello wérld", out _);   // é is not in the subset
        laid.GetPage(0);
        LayoutWarning w = Assert.Single(laid.Warnings, w => w.Code == LayoutWarningCodes.EmbeddedFontMissingGlyphs);
        Assert.Equal("Caladea", w.Location);
        Assert.Contains("U+00E9", w.Message);
        Assert.Contains("the bundled 'Caladea' is used", w.Message);
        Assert.Contains(laid.FontSubstitutions, s => s.RequestedFamily == "Caladea" && s.UsedFamily == "Caladea" && s.UsedOrigin == FontOrigin.Bundled);
        // The subset still draws the characters it has.
        Assert.Contains(laid.GetPage(0).DisplayList.OfType<GlyphRunOp>(), g => g.Font.Origin == FontOrigin.Embedded);
        Assert.Contains(laid.GetPage(0).DisplayList.OfType<GlyphRunOp>(), g => g.Font.Origin == FontOrigin.Bundled && g.Font.FamilyName == "Caladea");
    }

    [Fact]
    public void Without_the_family_elsewhere_the_script_fallback_draws_it_and_the_warning_says_so()
    {
        using LaidOutDocument laid = LayOut("Hello த", out _);   // Tamil: neither Caladea has it
        laid.GetPage(0);
        LayoutWarning w = Assert.Single(laid.Warnings, w => w.Code == LayoutWarningCodes.EmbeddedFontMissingGlyphs);
        Assert.Contains("U+0BA4", w.Message);
        Assert.Contains("'LiPi Sans Tamil' is used", w.Message);
    }

    [Fact]
    public void A_complete_embedded_font_raises_no_warning()
    {
        using LaidOutDocument laid = LayOut("Hello world", out _);
        laid.GetPage(0);
        Assert.DoesNotContain(laid.Warnings, w => w.Code == LayoutWarningCodes.EmbeddedFontMissingGlyphs);
    }
}
