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
    public void Cambria_maps_to_Caladea_which_is_not_metric_compatible()
    {
        // F1: Cambria is 6.5% wider than Caladea and its lines are shorter (A7), so breaks may move.
        FontResolution res = TestFonts.BundledResolver().Resolve(new FontRequest("Cambria", 700, true));
        Assert.Equal("Caladea", res.Face.FamilyName);
        Assert.True(res.Face.IsBold);
        Assert.True(res.Face.IsItalic);
        Assert.Equal(FontMatchKind.Substitute, res.Kind);
        Assert.False(res.IsMetricFaithful);
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
        // D-116 A5: Segoe UI → Selawik (closer design; non-metric until measured), Liberation Sans after it.
        FontResolution res = TestFonts.BundledResolver().Resolve(new FontRequest("Segoe UI"));
        Assert.Equal("Selawik", res.Face.FamilyName);
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
        FontResolver r = new FontResolver(new IFontSource[] { supplied, FontPackSource.For(SubstituteFontPack.Instance) });
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

    [Fact]
    public void Directory_source_parses_a_face_once_and_Refresh_rescans()
    {
        string dir = Path.Combine(Path.GetTempPath(), "chuvadi-layout-fonts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "a.ttf"), TestFonts.Bytes("Carlito-Bold.ttf"));
            DirectoryFontSource src = new DirectoryFontSource(dir);
            FontFaceDescriptor bold = Assert.Single(src.GetFaces());
            FontFace first = src.LoadFace(bold);
            Assert.Same(first, src.LoadFace(bold));

            File.WriteAllBytes(Path.Combine(dir, "b.ttf"), TestFonts.Bytes("Caladea-Regular.ttf"));
            Assert.Single(src.GetFaces()); // the scan is kept until Refresh
            src.Refresh();
            Assert.Equal(2, src.GetFaces().Count);
            FontFace again = src.LoadFace(src.GetFaces().Single(f => f.FamilyName == "Carlito"));
            Assert.NotSame(first, again); // loaded faces were released
            Assert.Equal("Carlito", first.FamilyName); // a face already handed out stays usable
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Layout_options_use_one_shared_system_font_source()
    {
        Assert.Same(SystemFontSource.Shared, SystemFontSource.Shared);
        LayoutOptions o = new LayoutOptions { AllowSystemFonts = true };
        IFontSource a = o.CreateFontResolver().Sources.Single(s => s.Name == "System fonts");
        IFontSource b = new LayoutOptions { AllowSystemFonts = true }.CreateFontResolver().Sources.Single(s => s.Name == "System fonts");
        Assert.Same(SystemFontSource.Shared, a);
        Assert.Same(a, b);
        o.PreferBundledFonts = true;
        Assert.Same(SystemFontSource.Shared, o.CreateFontResolver().Sources.Single(s => s.Name == "System fonts"));
    }

    [Fact]
    public void System_font_folders_per_os_include_Android()
    {
        Assert.Equal(new[] { "/system/fonts", "/product/fonts" }, SystemFontSource.DirectoriesFor("android"));
        Assert.Contains("/System/Library/Fonts", SystemFontSource.DirectoriesFor("macos"));
        Assert.Contains("/usr/share/fonts", SystemFontSource.DirectoriesFor("other"));
        Assert.DoesNotContain("/system/fonts", SystemFontSource.DirectoriesFor("other"));
        Assert.All(SystemFontSource.DirectoriesFor("windows"), d => Assert.EndsWith("Fonts", d));
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
        o.FontSources.Add(FontPackSource.For(SubstituteFontPack.Instance));
        Assert.Equal(new[] { "Embedded fonts", "System fonts", "Chuvadi.Layout.Fonts", "Chuvadi.Fonts" }, Names(o));
        o.PreferBundledFonts = true;
        Assert.Equal(new[] { "Embedded fonts", "Chuvadi.Layout.Fonts", "Chuvadi.Fonts", "System fonts" }, Names(o));
        // Without installed fonts the option changes nothing.
        LayoutOptions off = new LayoutOptions();
        off.FontSources.Add(FontPackSource.For(SubstituteFontPack.Instance));
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
        FontResolver r = new FontResolver(new IFontSource[] { installed, FontPackSource.For(SubstituteFontPack.Instance) });
        Assert.Equal(FontOrigin.System, r.Resolve(new FontRequest("Carlito")).Face.Origin);
        FontResolver packFirst = new FontResolver(new IFontSource[] { FontPackSource.For(SubstituteFontPack.Instance), installed });
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

/// <summary>D-116 A5 / Q6: every metric-compatible substitution is announced, at Info, once per family.</summary>
public class MetricSubstitutionWarningTests
{
    [Fact]
    public void Metric_compatible_substitutions_raise_an_info_warning_once_per_family()
    {
        CharacterFormat calibri = new CharacterFormat { FontFamily = "Calibri", FontSize = 11 };
        CharacterFormat tamil = new CharacterFormat { FontFamily = "Noto Sans Tamil", FontSize = 11 };
        FlowDocument doc = Flow.Doc(
            FlowParagraph.Of("One", calibri, new ParagraphFormat()),
            FlowParagraph.Of("Two", calibri, new ParagraphFormat()),
            FlowParagraph.Of("தமிழ்", tamil, new ParagraphFormat()));
        using LaidOutDocument laid = DocumentLayout.Open(doc, Flow.Options());
        laid.GetPage(0);
        List<LayoutWarning> metric = laid.Warnings.Where(w => w.Code == LayoutWarningCodes.MetricFontSubstitution).ToList();
        Assert.Equal(2, metric.Count);
        Assert.All(metric, w => Assert.Equal(LayoutWarningSeverity.Info, w.Severity));
        Assert.Contains(metric, w => w.Location == "Calibri" && w.Message.Contains("'Carlito'", StringComparison.Ordinal));
        Assert.Contains(metric, w => w.Location == "Noto Sans Tamil" && w.Message.Contains("'LiPi Sans Tamil'", StringComparison.Ordinal));
        Assert.DoesNotContain(laid.Warnings, w => w.Code == LayoutWarningCodes.NonMetricFontSubstitution);
    }

    [Fact]
    public void An_exact_family_raises_no_substitution_warning()
    {
        FlowDocument doc = Flow.Doc(FlowParagraph.Of("Carlito itself", new CharacterFormat { FontFamily = "Carlito", FontSize = 11 }, new ParagraphFormat()));
        using LaidOutDocument laid = DocumentLayout.Open(doc, Flow.Options());
        laid.GetPage(0);
        Assert.DoesNotContain(laid.Warnings, w => w.Code is LayoutWarningCodes.MetricFontSubstitution or LayoutWarningCodes.NonMetricFontSubstitution);
    }
}
