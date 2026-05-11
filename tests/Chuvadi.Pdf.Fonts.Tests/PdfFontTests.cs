// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.6, §9.10.3
// PHASE: Phase 1 — Chuvadi.Pdf.Fonts tests

using System;
using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Tests;

// ── FontException ─────────────────────────────────────────────────────────

public sealed class FontExceptionTests
{
    [Fact]
    public void DefaultConstructor_HasMessage()
    {
        FontException ex = new FontException();
        ex.Message.Should().NotBeEmpty();
    }

    [Fact]
    public void MessageConstructor_PreservesMessage()
    {
        FontException ex = new FontException("test");
        ex.Message.Should().Be("test");
    }

    [Fact]
    public void InnerExceptionConstructor_PreservesInner()
    {
        InvalidOperationException inner = new InvalidOperationException("inner");
        FontException ex = new FontException("outer", inner);
        ex.InnerException.Should().BeSameAs(inner);
    }
}

// ── PdfFontEncoding ───────────────────────────────────────────────────────

public sealed class PdfFontEncodingTests
{
    [Fact]
    public void WinAnsi_AsciiRange_MapsCorrectly()
    {
        PdfFontEncoding enc = PdfFontEncoding.FromNamedEncoding("WinAnsiEncoding");
        enc.GetCharacter(0x41).Should().Be('A');
        enc.GetCharacter(0x61).Should().Be('a');
        enc.GetCharacter(0x20).Should().Be(' ');
    }

    [Fact]
    public void WinAnsi_EuroSign_MapsCorrectly()
    {
        PdfFontEncoding enc = PdfFontEncoding.FromNamedEncoding("WinAnsiEncoding");
        enc.GetCharacter(0x80).Should().Be('\u20AC');
    }

    [Fact]
    public void WinAnsi_EmDash_MapsCorrectly()
    {
        PdfFontEncoding enc = PdfFontEncoding.FromNamedEncoding("WinAnsiEncoding");
        enc.GetCharacter(0x97).Should().Be('\u2014');
    }

    [Fact]
    public void MacRoman_HighBytes_MapsCorrectly()
    {
        PdfFontEncoding enc = PdfFontEncoding.FromNamedEncoding("MacRomanEncoding");
        enc.GetCharacter(0x80).Should().Be('\u00C4'); // A-umlaut
    }

    [Fact]
    public void UnknownEncoding_FallsBackToWinAnsi()
    {
        PdfFontEncoding enc = PdfFontEncoding.FromNamedEncoding("UnknownEncoding");
        enc.GetCharacter(0x41).Should().Be('A');
    }

    [Fact]
    public void NullEncoding_FallsBackToWinAnsi()
    {
        PdfFontEncoding enc = PdfFontEncoding.Build(null);
        enc.GetCharacter(0x41).Should().Be('A');
    }

    [Fact]
    public void UnmappedCode_ReturnsNul()
    {
        PdfFontEncoding enc = PdfFontEncoding.FromNamedEncoding("WinAnsiEncoding");
        enc.GetCharacter(0x00).Should().Be('\0');
    }

    [Fact]
    public void IsMapped_MappedCode_ReturnsTrue()
    {
        PdfFontEncoding enc = PdfFontEncoding.FromNamedEncoding("WinAnsiEncoding");
        enc.IsMapped(0x41).Should().BeTrue();
    }

    [Fact]
    public void IsMapped_UnmappedCode_ReturnsFalse()
    {
        PdfFontEncoding enc = PdfFontEncoding.FromNamedEncoding("WinAnsiEncoding");
        enc.IsMapped(0x00).Should().BeFalse();
    }
}

// ── GlyphName lookup ──────────────────────────────────────────────────────

public sealed class GlyphNameTests
{
    [Fact]
    public void KnownGlyphName_ReturnsCorrectChar()
    {
        PdfFontEncoding.GlyphNameToUnicode("bullet").Should().Be('\u2022');
        PdfFontEncoding.GlyphNameToUnicode("emdash").Should().Be('\u2014');
        PdfFontEncoding.GlyphNameToUnicode("space").Should().Be(' ');
    }

    [Fact]
    public void UniNotation_ParsesCorrectly()
    {
        PdfFontEncoding.GlyphNameToUnicode("uni0041").Should().Be('A');
        PdfFontEncoding.GlyphNameToUnicode("uni20AC").Should().Be('\u20AC');
    }

    [Fact]
    public void UnknownGlyphName_ReturnsNul()
    {
        PdfFontEncoding.GlyphNameToUnicode("unknownXYZ").Should().Be('\0');
    }

    [Fact]
    public void EmptyGlyphName_ReturnsNul()
    {
        PdfFontEncoding.GlyphNameToUnicode("").Should().Be('\0');
    }
}

// ── CMapParser ────────────────────────────────────────────────────────────

public sealed class CMapParserTests
{
    [Fact]
    public void Parse_BfChar_SingleEntry()
    {
        string cmap = @"
/CIDInit /ProcSet findresource begin
beginbfchar
<41> <0041>
endbfchar
end";
        CMapParser parser = new CMapParser(cmap);
        Dictionary<int, string> result = parser.Parse();

        result.Should().ContainKey(0x41);
        result[0x41].Should().Be("A");
    }

    [Fact]
    public void Parse_BfChar_MultipleEntries()
    {
        string cmap = @"
beginbfchar
<41> <0041>
<42> <0042>
<43> <0043>
endbfchar";
        CMapParser parser = new CMapParser(cmap);
        Dictionary<int, string> result = parser.Parse();

        result[0x41].Should().Be("A");
        result[0x42].Should().Be("B");
        result[0x43].Should().Be("C");
    }

    [Fact]
    public void Parse_BfRange_Sequential()
    {
        string cmap = @"
beginbfrange
<41> <43> <0041>
endbfrange";
        CMapParser parser = new CMapParser(cmap);
        Dictionary<int, string> result = parser.Parse();

        result[0x41].Should().Be("A");
        result[0x42].Should().Be("B");
        result[0x43].Should().Be("C");
    }

    [Fact]
    public void Parse_BfRange_ArrayDestination()
    {
        string cmap = @"
beginbfrange
<41> <43> [<0058> <0059> <005A>]
endbfrange";
        CMapParser parser = new CMapParser(cmap);
        Dictionary<int, string> result = parser.Parse();

        result[0x41].Should().Be("X");
        result[0x42].Should().Be("Y");
        result[0x43].Should().Be("Z");
    }

    [Fact]
    public void Parse_TwoByteCode_Works()
    {
        string cmap = @"
beginbfchar
<0041> <0041>
endbfchar";
        CMapParser parser = new CMapParser(cmap);
        Dictionary<int, string> result = parser.Parse();
        result.Should().ContainKey(0x0041);
    }

    [Fact]
    public void Parse_EmptyCMap_ReturnsEmpty()
    {
        CMapParser parser = new CMapParser("");
        Dictionary<int, string> result = parser.Parse();
        result.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_NullBytes_Throws()
    {
        Action act = () => new CMapParser((byte[])null!);
        act.Should().Throw<ArgumentNullException>();
    }
}

// ── PdfFont ───────────────────────────────────────────────────────────────

public sealed class PdfFontDefaultTests
{
    [Fact]
    public void Default_AsciiBytes_DecodesCorrectly()
    {
        PdfFont font = PdfFont.Default();
        byte[] bytes = Encoding.ASCII.GetBytes("Hello");
        font.Decode(bytes).Should().Be("Hello");
    }

    [Fact]
    public void Default_NullBytes_Throws()
    {
        PdfFont font = PdfFont.Default();
        Action act = () => font.Decode(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Default_EmptyBytes_ReturnsEmpty()
    {
        PdfFont font = PdfFont.Default();
        font.Decode([]).Should().Be(string.Empty);
    }

    [Fact]
    public void Default_EuroSign_DecodesCorrectly()
    {
        PdfFont font = PdfFont.Default();
        // WinAnsi 0x80 = Euro sign
        font.Decode([0x80]).Should().Be("\u20AC");
    }

    [Fact]
    public void DecodeCode_KnownCode_ReturnsUnicode()
    {
        PdfFont font = PdfFont.Default();
        font.DecodeCode(0x41).Should().Be("A");
    }

    [Fact]
    public void DecodeCode_UnmappedCode_ReturnsEmpty()
    {
        PdfFont font = PdfFont.Default();
        font.DecodeCode(0x01).Should().Be(string.Empty);
    }
}
