using System;
using Chuvadi.OpenType;
using Chuvadi.OpenType.Tests;
using Xunit;

namespace Chuvadi.Text.Shaping.Tests;

/// <summary>Typed diagnostics on the shaping result (D-246, R-TS-0014, R-TS-0015).</summary>
public class DiagnosticsTests
{
    private static readonly ShapeOptions Latin = new ShapeOptions { Script = Script.Latin, Language = Language.Default };

    private static byte[] Cmap() => FontBuilder.Cmap((3, 10, FontBuilder.CmapFormat12Or13(12, (0x41, 0x43, 1))));

    /// <summary>A GSUB whose script list holds the given script tags (no features or lookups).</summary>
    private static byte[] Gsub(params string[] scripts)
    {
        Node list = L.N().U16(scripts.Length);
        foreach (string s in scripts)
        {
            list.TagField(s).O16(L.N().O16(L.LangSys(0xFFFF)).U16(0));
        }

        return L.LayoutTable(list, L.N().U16(0), L.N().U16(0)).Build();
    }

    private static ShapingFont Font(byte[]? gsub = null)
    {
        FontBuilder b = FontBuilder.Basic(5).Table("cmap", Cmap());
        if (gsub is not null)
        {
            b.Table("GSUB", gsub);
        }

        return ShapingFont.Get(OpenTypeFont.Load(b.Build()));
    }

    [Fact]
    public void NoneWhenEverythingIsFine()
    {
        ShapedRun run = TextShaper.Shape(Font(Gsub("latn")), "ABC", Latin);
        Assert.Empty(run.Diagnostics);
        GlyphBuffer buffer = new GlyphBuffer();
        TextShaper.Shape(Font(), "AB", Latin, buffer);
        Assert.Same(Array.Empty<ShapingDiagnostic>(), buffer.Diagnostics);
    }

    [Fact]
    public void MissingGlyphIsReportedOncePerCall()
    {
        ShapedRun run = TextShaper.Shape(Font(), "AxBy", Latin);
        ShapingDiagnostic d = Assert.Single(run.Diagnostics);
        Assert.Equal(ShapingDiagnosticKind.MissingGlyph, d.Kind);
        Assert.Equal("U+0078 at index 1 has no glyph in the font; glyph 0 (.notdef) was used.", d.Message);
        Assert.Equal(0, run[1].GlyphId);
    }

    [Fact]
    public void ScriptNotInFont()
    {
        ShapeOptions tamil = new ShapeOptions { Script = Script.Tamil, Language = Language.Default };
        ShapingDiagnostic d = Assert.Single(TextShaper.Shape(Font(Gsub("latn")), "AB", tamil).Diagnostics);
        Assert.Equal(ShapingDiagnosticKind.ScriptNotInFont, d.Kind);

        // Found under either the version 2 tag or the original one.
        Assert.Empty(TextShaper.Shape(Font(Gsub("taml")), "AB", tamil).Diagnostics);
        Assert.Empty(TextShaper.Shape(Font(Gsub("DFLT", "tml2")), "AB", tamil).Diagnostics);

        // Not reported without layout tables, nor for Common; other scripts use the lower-case ISO code.
        Assert.Empty(TextShaper.Shape(Font(), "AB", tamil).Diagnostics);
        Assert.Empty(TextShaper.Shape(Font(Gsub("latn")), "AB", new ShapeOptions { Script = Script.Common, Language = Language.Default }).Diagnostics);
        ShapeOptions cyrillic = new ShapeOptions { Script = Script.Parse("Cyrl"), Language = Language.Default };
        Assert.Empty(TextShaper.Shape(Font(Gsub("cyrl")), "AB", cyrillic).Diagnostics);
        Assert.Single(TextShaper.Shape(Font(Gsub("latn")), "AB", cyrillic).Diagnostics);
    }

    [Fact]
    public void BothKindsInOrderAndBufferReuseClearsThem()
    {
        ShapeOptions tamil = new ShapeOptions { Script = Script.Tamil, Language = Language.Default };
        GlyphBuffer buffer = new GlyphBuffer();
        TextShaper.Shape(Font(Gsub("latn")), "xA", tamil, buffer);
        Assert.Equal(new[] { ShapingDiagnosticKind.ScriptNotInFont, ShapingDiagnosticKind.MissingGlyph }, new[] { buffer.Diagnostics[0].Kind, buffer.Diagnostics[1].Kind });
        TextShaper.Shape(Font(Gsub("latn")), "AB", Latin, buffer);
        Assert.Empty(buffer.Diagnostics);
    }

    [Fact]
    public void ShapingWithoutDiagnosticsDoesNotAllocateForThem()
    {
        ShapingFont font = Font(Gsub("latn"));
        ShapeOptions cyrillic = new ShapeOptions { Script = Script.Parse("Cyrl"), Language = Language.Default };
        GlyphBuffer buffer = new GlyphBuffer();
        TextShaper.Shape(font, "ABCABC", Latin, buffer);
        TextShaper.Shape(Font(Gsub("cyrl")), "AB", cyrillic, buffer);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            TextShaper.Shape(font, "ABCABC", Latin, buffer);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
