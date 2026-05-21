// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: v2.0.0 R1 D3b — RenderableFont tests

using System;
using System.Text;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Rendering.Tests;

public sealed class RenderableFontTests
{
    private static IPdfObjectResolver MakeResolver()
    {
        return new PdfObjectStore(_ => null);
    }

    private static PdfDictionary MakeFontDict(string baseFontName)
    {
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Intern("Type"), PdfName.Intern("Font"));
        dict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Type1"));
        dict.Set(PdfName.Intern("BaseFont"), PdfName.Intern(baseFontName));
        return dict;
    }

    // ── Identity ──────────────────────────────────────────────────────────

    [Fact]
    public void Default_IsStandard14()
    {
        RenderableFont font = RenderableFont.Default();
        font.IsStandard14.Should().BeTrue();
        font.FontName.Should().Be("Helvetica");
    }

    [Fact]
    public void Default_UnitsPerEm_Is1000()
    {
        RenderableFont.Default().UnitsPerEm.Should().Be(1000);
    }

    [Fact]
    public void FromDictionary_Std14BaseFont_RecognizedAsStandard14()
    {
        PdfDictionary dict = MakeFontDict("Helvetica");
        RenderableFont font = RenderableFont.FromDictionary(dict, MakeResolver());
        font.IsStandard14.Should().BeTrue();
        font.FontName.Should().Be("Helvetica");
    }

    [Fact]
    public void FromDictionary_StripsSubsetPrefix()
    {
        PdfDictionary dict = MakeFontDict("ABCDEF+Helvetica");
        RenderableFont font = RenderableFont.FromDictionary(dict, MakeResolver());
        font.FontName.Should().Be("Helvetica");
        font.IsStandard14.Should().BeTrue();
    }

    [Fact]
    public void FromDictionary_NonStandard14_HasIsStandard14False()
    {
        PdfDictionary dict = MakeFontDict("CustomFont-Regular");
        RenderableFont font = RenderableFont.FromDictionary(dict, MakeResolver());
        font.IsStandard14.Should().BeFalse();
        font.FontName.Should().Be("CustomFont-Regular");
    }

    [Fact]
    public void FromDictionary_MissingBaseFont_UsesUnknown()
    {
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Intern("Type"), PdfName.Intern("Font"));
        dict.Set(PdfName.Intern("Subtype"), PdfName.Intern("Type1"));
        RenderableFont font = RenderableFont.FromDictionary(dict, MakeResolver());
        font.FontName.Should().Be("Unknown");
        font.IsStandard14.Should().BeFalse();
    }

    [Fact]
    public void FromDictionary_NullDict_Throws()
    {
        Action act = () => RenderableFont.FromDictionary(null!, MakeResolver());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FromDictionary_NullResolver_Throws()
    {
        PdfDictionary dict = MakeFontDict("Helvetica");
        Action act = () => RenderableFont.FromDictionary(dict, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("Helvetica")]
    [InlineData("Helvetica-Bold")]
    [InlineData("Times-Roman")]
    [InlineData("Courier")]
    [InlineData("Symbol")]
    [InlineData("ZapfDingbats")]
    public void IsStandard14Name_RecognizesAllFamilies(string name)
    {
        RenderableFont.IsStandard14Name(name).Should().BeTrue();
    }

    [Fact]
    public void IsStandard14Name_RejectsArial()
    {
        RenderableFont.IsStandard14Name("Arial").Should().BeFalse();
    }

    [Fact]
    public void IsStandard14Name_NullName_Throws()
    {
        Action act = () => RenderableFont.IsStandard14Name(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── Advance widths ────────────────────────────────────────────────────

    [Fact]
    public void GetAdvanceWidth_Courier_AnyChar_Returns600ScaledByPointSize()
    {
        RenderableFont font = RenderableFont.FromDictionary(
            MakeFontDict("Courier"),
            MakeResolver());
        // 600 / 1000 * 12 = 7.2
        font.GetAdvanceWidth('A', 12).Should().BeApproximately(7.2, 0.001);
        font.GetAdvanceWidth('i', 12).Should().BeApproximately(7.2, 0.001);
        font.GetAdvanceWidth(' ', 12).Should().BeApproximately(7.2, 0.001);
    }

    [Fact]
    public void GetAdvanceWidth_HelveticaSpace_Returns278Scaled()
    {
        RenderableFont font = RenderableFont.FromDictionary(
            MakeFontDict("Helvetica"),
            MakeResolver());
        // 278 / 1000 * 10 = 2.78
        font.GetAdvanceWidth(' ', 10).Should().BeApproximately(2.78, 0.001);
    }

    [Fact]
    public void GetAdvanceWidth_NonStandard14_ApproximateHalfEm()
    {
        RenderableFont font = RenderableFont.FromDictionary(
            MakeFontDict("CustomFont"),
            MakeResolver());
        // 0.5 * 12 = 6.0
        font.GetAdvanceWidth('A', 12).Should().BeApproximately(6.0, 0.001);
    }

    [Fact]
    public void GetAdvanceWidth_NegativePointSize_Throws()
    {
        RenderableFont font = RenderableFont.Default();
        Action act = () => font.GetAdvanceWidth('A', -1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GetAdvanceWidth_ZeroPointSize_Throws()
    {
        RenderableFont font = RenderableFont.Default();
        Action act = () => font.GetAdvanceWidth('A', 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── Glyph paths ───────────────────────────────────────────────────────

    [Fact]
    public void GetGlyphPath_Std14_FromEmptyBundle_ReturnsEmptyPath()
    {
        // Until Standard14.bin is populated by build_standard14_bundle.py with
        // real font data, paths are empty. This test asserts the placeholder
        // behavior; once the bundle is built, the path will contain segments
        // and this test will need to be paired with a positive case.
        RenderableFont font = RenderableFont.FromDictionary(
            MakeFontDict("Helvetica"),
            MakeResolver());

        if (!Standard14Outlines.BundleAvailable)
        {
            font.GetGlyphPath('A').IsEmpty.Should().BeTrue();
        }
        else
        {
            // Bundle is available - assert sanity instead
            font.GetGlyphPath('A').Should().NotBeNull();
        }
    }

    [Fact]
    public void GetGlyphPath_NonStandard14_ReturnsEmptyPath()
    {
        // D3b: embedded font support unsupported in RenderableFont. D3c fixes this.
        RenderableFont font = RenderableFont.FromDictionary(
            MakeFontDict("CustomFont"),
            MakeResolver());
        font.GetGlyphPath('A').IsEmpty.Should().BeTrue();
        font.GetGlyphPath('A', 12).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void GetGlyphPath_ScaledOverload_NegativePointSize_Throws()
    {
        RenderableFont font = RenderableFont.Default();
        Action act = () => font.GetGlyphPath('A', -1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GetGlyphPath_ScaledOverload_ZeroPointSize_Throws()
    {
        RenderableFont font = RenderableFont.Default();
        Action act = () => font.GetGlyphPath('A', 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── Text decoding ─────────────────────────────────────────────────────

    [Fact]
    public void DecodeText_DefaultFont_AsciiRoundTrips()
    {
        // PdfFont.Default() uses WinAnsiEncoding; ASCII letters are identity-mapped.
        RenderableFont font = RenderableFont.Default();
        string decoded = font.DecodeText(Encoding.ASCII.GetBytes("Hello"));
        decoded.Should().Be("Hello");
    }

    [Fact]
    public void DecodeText_EmptyBytes_ReturnsEmptyString()
    {
        RenderableFont font = RenderableFont.Default();
        font.DecodeText([]).Should().Be(string.Empty);
    }

    [Fact]
    public void DecodeText_NullBytes_Throws()
    {
        RenderableFont font = RenderableFont.Default();
        Action act = () => font.DecodeText(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
