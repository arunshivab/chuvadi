// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.6, §9.10.3
// PHASE: Phase 1 — Chuvadi.Pdf.Fonts tests
//        v2.1.4 — added CodespaceRangeTests and PdfFontMultiByteTests
//                 covering codespacerange parsing and longest-match-first
//                 byte consumption for multi-byte ToUnicode entries
//                 (Word Wingdings UTF-8 case).

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

// ── CodespaceRange parsing (v2.1.4) ───────────────────────────────────────

public sealed class CodespaceRangeTests
{
    [Fact]
    public void Parse_CodespaceRange_SingleByteWidth_Recognised()
    {
        string cmap = @"
1 begincodespacerange
<00> <FF>
endcodespacerange";
        CMapParser parser = new CMapParser(cmap);
        CMapParseResult result = parser.ParseFull();

        result.CodespaceRanges.Should().ContainSingle();
        result.CodespaceRanges[0].Lo.Should().Be(0);
        result.CodespaceRanges[0].Hi.Should().Be(0xFF);
        result.CodespaceRanges[0].ByteCount.Should().Be(1);
    }

    [Fact]
    public void Parse_CodespaceRange_TwoByteWidth_Recognised()
    {
        string cmap = @"
1 begincodespacerange
<0000> <FFFF>
endcodespacerange";
        CMapParser parser = new CMapParser(cmap);
        CMapParseResult result = parser.ParseFull();

        result.CodespaceRanges[0].ByteCount.Should().Be(2);
        result.CodespaceRanges[0].Hi.Should().Be(0xFFFF);
    }

    [Fact]
    public void Parse_CodespaceRange_ThreeByteWidth_Recognised()
    {
        // The Word-Wingdings shape: 3-byte UTF-8 source codes.
        string cmap = @"
1 begincodespacerange
<000000> <FFFFFF>
endcodespacerange";
        CMapParser parser = new CMapParser(cmap);
        CMapParseResult result = parser.ParseFull();

        result.CodespaceRanges[0].ByteCount.Should().Be(3);
        result.CodespaceRanges[0].Hi.Should().Be(0xFFFFFF);
    }

    [Fact]
    public void Parse_CodespaceRange_MixedWidths_BothRecognised()
    {
        string cmap = @"
2 begincodespacerange
<00> <80>
<8140> <9FFC>
endcodespacerange";
        CMapParser parser = new CMapParser(cmap);
        CMapParseResult result = parser.ParseFull();

        result.CodespaceRanges.Should().HaveCount(2);
        result.CodespaceRanges[0].ByteCount.Should().Be(1);
        result.CodespaceRanges[1].ByteCount.Should().Be(2);
        result.CodespaceRanges[1].Lo.Should().Be(0x8140);
        result.CodespaceRanges[1].Hi.Should().Be(0x9FFC);
    }

    [Fact]
    public void ParseFull_NoCodespaceSection_RangesEmpty()
    {
        string cmap = @"
beginbfchar
<41> <0041>
endbfchar";
        CMapParser parser = new CMapParser(cmap);
        CMapParseResult result = parser.ParseFull();

        result.CodespaceRanges.Should().BeEmpty();
        result.Mapping[0x41].Should().Be("A");
    }

    [Fact]
    public void ParseFull_WordWingdingsShape_MultiByteBfCharStored()
    {
        // The actual shape Word emits for the Wingdings checkmark: glyph
        // byte 0xFC re-encoded as UTF-8 bytes E2 9C 93, paired with a
        // ToUnicode mapping back to the semantic check-mark codepoint
        // U+2713.
        string cmap = @"
1 begincodespacerange
<000000> <FFFFFF>
endcodespacerange
beginbfchar
<E29C93> <2713>
endbfchar";
        CMapParser parser = new CMapParser(cmap);
        CMapParseResult result = parser.ParseFull();

        result.CodespaceRanges[0].ByteCount.Should().Be(3);
        result.Mapping.Should().ContainKey(0xE29C93);
        result.Mapping[0xE29C93].Should().Be("\u2713");
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

// ── PdfFont longest-match decoding (v2.1.4) ───────────────────────────────

public sealed class PdfFontMultiByteTests
{
    private static readonly IReadOnlyList<CodespaceRange> ThreeByteRange =
        new CodespaceRange[] { new CodespaceRange(0, 0xFFFFFF, 3) };

    [Fact]
    public void Decode_ThreeByteCode_LongestMatchWins()
    {
        // Word Wingdings shape: bytes E2 9C 93 map to U+2713 ✓.
        Dictionary<int, string> map = new Dictionary<int, string>
        {
            { 0xE29C93, "\u2713" },
        };
        PdfFont font = PdfFont.FromMappings(
            map,
            ThreeByteRange,
            PdfFontEncoding.FromNamedEncoding("WinAnsiEncoding"));

        font.Decode([0xE2, 0x9C, 0x93]).Should().Be("\u2713");
    }

    [Fact]
    public void Decode_ThreeByteCode_FollowedByAscii_BothDecoded()
    {
        Dictionary<int, string> map = new Dictionary<int, string>
        {
            { 0xE29C93, "\u2713" },
        };
        PdfFont font = PdfFont.FromMappings(
            map,
            ThreeByteRange,
            PdfFontEncoding.FromNamedEncoding("WinAnsiEncoding"));

        font.Decode([0xE2, 0x9C, 0x93, 0x41, 0x42]).Should().Be("\u2713AB");
    }

    [Fact]
    public void Decode_NoMultiByteMatch_FallsBackToWinAnsiPerByte()
    {
        // Map has only the 3-byte entry; the 0xE2 0x41 sequence doesn't
        // match at any width, so each byte falls through to WinAnsi:
        // 0xE2 → U+00E2 'â', 0x41 → 'A'.
        Dictionary<int, string> map = new Dictionary<int, string>
        {
            { 0xE29C93, "\u2713" },
        };
        PdfFont font = PdfFont.FromMappings(
            map,
            ThreeByteRange,
            PdfFontEncoding.FromNamedEncoding("WinAnsiEncoding"));

        font.Decode([0xE2, 0x41]).Should().Be("\u00E2A");
    }

    [Fact]
    public void Decode_OneByteMapWithOneByteFont_StillWorks()
    {
        Dictionary<int, string> map = new Dictionary<int, string>
        {
            { 0x41, "X" },
        };
        PdfFont font = PdfFont.FromMappings(
            map,
            codespaceRanges: null,
            PdfFontEncoding.FromNamedEncoding("WinAnsiEncoding"));

        // 0x41 hits the map, 0x42 falls through to WinAnsi.
        font.Decode([0x41, 0x42]).Should().Be("XB");
    }

    [Fact]
    public void Decode_TwoByteCompositeMap_StillWorks()
    {
        Dictionary<int, string> map = new Dictionary<int, string>
        {
            { 0x0041, "X" },
        };
        PdfFont font = PdfFont.FromMappings(
            map,
            codespaceRanges: null,
            encoding: null,
            isComposite: true);

        font.Decode([0x00, 0x41]).Should().Be("X");
    }

    [Fact]
    public void FromMappings_NullMap_StillDecodesViaEncoding()
    {
        PdfFont font = PdfFont.FromMappings(
            toUnicodeMap: null,
            codespaceRanges: null,
            PdfFontEncoding.FromNamedEncoding("WinAnsiEncoding"));

        font.Decode([0x41]).Should().Be("A");
    }

    [Fact]
    public void FromMappings_LongerCodeOverShorter_LongestWins()
    {
        // Both 1-byte and 3-byte entries cover the leading byte; the
        // longest match takes precedence.
        Dictionary<int, string> map = new Dictionary<int, string>
        {
            { 0xE2, "shouldNotBeUsed" },
            { 0xE29C93, "\u2713" },
        };
        PdfFont font = PdfFont.FromMappings(map, ThreeByteRange);

        font.Decode([0xE2, 0x9C, 0x93]).Should().Be("\u2713");
    }

    [Fact]
    public void FromMappings_TruncatedTrailingBytes_FallsBackPerByte()
    {
        // Only 2 trailing bytes after a leading 0xE2; the 3-byte entry
        // can't match because there aren't enough bytes. The first byte
        // falls through (no shorter match in map either) to WinAnsi.
        Dictionary<int, string> map = new Dictionary<int, string>
        {
            { 0xE29C93, "\u2713" },
        };
        PdfFont font = PdfFont.FromMappings(
            map,
            ThreeByteRange,
            PdfFontEncoding.FromNamedEncoding("WinAnsiEncoding"));

        font.Decode([0xE2, 0x9C]).Should().Be("\u00E2\u0153");
    }
}
