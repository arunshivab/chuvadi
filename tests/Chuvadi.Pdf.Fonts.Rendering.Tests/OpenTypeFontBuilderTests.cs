// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Tests for OpenTypeFontBuilder: verifies the OTTO envelope and synthesised
// tables wrapping a minimal embedded CFF program. The CFF fixture is a small,
// valid 3-glyph CFF (.notdef, A, checkmark) generated offline; hand-building a
// CFF in code (cf. BuildMinimalTtf) is impractical, so it is embedded as base64.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Rendering.Tests;

public sealed class OpenTypeFontBuilderTests
{
    // Minimal valid CFF: glyphs [.notdef, A(gid1), checkmark(gid2)], 1000 upm.
    private const string MinimalCffBase64 =
        "AQAEAQABAQEMTWluaW1hbFRlc3QAAQEBE/gcArNZ+Tz5UAXTD4v3BBLYEQACAQEKFmNoZWNrbWFya01pbmltYWwgVGVzdAAAAAAiAYcAAwEBBBAd+IgO+Oy9FviI+VD8iAYO+WSzWRX5FPky/RQGDg==";

    private static readonly byte[] MinimalCff = Convert.FromBase64String(MinimalCffBase64);

    private const int GidA = 1;
    private const int GidCheckmark = 2;

    private static Dictionary<int, int> BuildUnicodeMap()
    {
        return new Dictionary<int, int>
        {
            [0x0041] = GidA,
            [0x2713] = GidCheckmark,
        };
    }

    private static byte[] BuildFont()
    {
        CffLoader loader = new(MinimalCff);
        return OpenTypeFontBuilder.Build(MinimalCff, loader, BuildUnicodeMap(), "MinimalTest", false, false);
    }

    private static int ReadU16(byte[] data, int offset)
    {
        return (data[offset] << 8) | data[offset + 1];
    }

    private static long ReadU32(byte[] data, int offset)
    {
        return ((long)data[offset] << 24) | ((long)data[offset + 1] << 16)
             | ((long)data[offset + 2] << 8) | data[offset + 3];
    }

    private static Dictionary<string, byte[]> ParseTables(byte[] font)
    {
        Dictionary<string, byte[]> tables = new();
        int numTables = ReadU16(font, 4);
        for (int i = 0; i < numTables; i++)
        {
            int entry = 12 + i * 16;
            char[] tagChars =
            {
                (char)font[entry],
                (char)font[entry + 1],
                (char)font[entry + 2],
                (char)font[entry + 3],
            };
            string tag = new string(tagChars);
            int offset = (int)ReadU32(font, entry + 8);
            int length = (int)ReadU32(font, entry + 12);
            byte[] data = new byte[length];
            Array.Copy(font, offset, data, 0, length);
            tables[tag] = data;
        }

        return tables;
    }

    // Resolves a code point through a format-4 cmap subtable (idRangeOffset == 0 here).
    private static int LookupFormat4(byte[] cmapTable, int codePoint)
    {
        int subtableOffset = (int)ReadU32(cmapTable, 8); // record[0] offset field at +4 of the 4-byte header
        int format = ReadU16(cmapTable, subtableOffset);
        if (format != 4)
        {
            return -1;
        }

        int segCountX2 = ReadU16(cmapTable, subtableOffset + 6);
        int segCount = segCountX2 / 2;
        int endBase = subtableOffset + 14;
        int startBase = endBase + segCountX2 + 2; // + reservedPad
        int deltaBase = startBase + segCountX2;
        int rangeBase = deltaBase + segCountX2;

        for (int i = 0; i < segCount; i++)
        {
            int end = ReadU16(cmapTable, endBase + i * 2);
            int start = ReadU16(cmapTable, startBase + i * 2);
            if (codePoint < start || codePoint > end)
            {
                continue;
            }

            int rangeOffset = ReadU16(cmapTable, rangeBase + i * 2);
            if (rangeOffset != 0)
            {
                return -1;
            }

            int delta = ReadU16(cmapTable, deltaBase + i * 2);
            return (codePoint + delta) & 0xFFFF;
        }

        return 0;
    }

    [Fact]
    public void Build_ProducesOttoEnvelopeWithAllTables()
    {
        byte[] font = BuildFont();
        font.Should().NotBeEmpty();
        ReadU32(font, 0).Should().Be(0x4F54544F); // 'OTTO'

        Dictionary<string, byte[]> tables = ParseTables(font);
        tables.Keys.Should().Contain(new[] { "CFF ", "cmap", "head", "hhea", "hmtx", "maxp", "name", "OS/2", "post" });
    }

    [Fact]
    public void Build_PassesCffProgramThroughUnchanged()
    {
        byte[] font = BuildFont();
        Dictionary<string, byte[]> tables = ParseTables(font);
        tables["CFF "].Should().Equal(MinimalCff);
    }

    [Fact]
    public void Build_HeadHasMagicNumberAndUnitsPerEm()
    {
        CffLoader loader = new(MinimalCff);
        byte[] font = OpenTypeFontBuilder.Build(MinimalCff, loader, BuildUnicodeMap(), "MinimalTest", false, false);
        Dictionary<string, byte[]> tables = ParseTables(font);
        byte[] head = tables["head"];
        ReadU32(head, 12).Should().Be(0x5F0F3CF5);          // magicNumber
        ReadU16(head, 18).Should().Be(loader.UnitsPerEm);    // unitsPerEm
    }

    [Fact]
    public void Build_MaxpNumGlyphsMatchesLoader()
    {
        CffLoader loader = new(MinimalCff);
        byte[] font = OpenTypeFontBuilder.Build(MinimalCff, loader, BuildUnicodeMap(), "MinimalTest", false, false);
        Dictionary<string, byte[]> tables = ParseTables(font);
        ReadU16(tables["maxp"], 4).Should().Be(loader.NumGlyphs);
    }

    [Fact]
    public void Build_HmtxLengthMatchesGlyphCount()
    {
        CffLoader loader = new(MinimalCff);
        byte[] font = OpenTypeFontBuilder.Build(MinimalCff, loader, BuildUnicodeMap(), "MinimalTest", false, false);
        Dictionary<string, byte[]> tables = ParseTables(font);
        tables["hmtx"].Length.Should().Be(loader.NumGlyphs * 4);
    }

    [Fact]
    public void Build_CmapResolvesRequestedCodePoints()
    {
        byte[] font = BuildFont();
        Dictionary<string, byte[]> tables = ParseTables(font);
        byte[] cmap = tables["cmap"];
        LookupFormat4(cmap, 0x0041).Should().Be(GidA);
        LookupFormat4(cmap, 0x2713).Should().Be(GidCheckmark);
    }

    [Fact]
    public void Build_NameTablePostScriptNameIsSanitised()
    {
        CffLoader loader = new(MinimalCff);
        byte[] font = OpenTypeFontBuilder.Build(MinimalCff, loader, BuildUnicodeMap(), "ABCDEF+Times New Roman", false, false);
        Dictionary<string, byte[]> tables = ParseTables(font);
        tables.Should().ContainKey("name");
        tables["name"].Length.Should().BeGreaterThan(6);
    }

    [Fact]
    public void Build_NullArguments_Throw()
    {
        CffLoader loader = new(MinimalCff);
        Dictionary<int, int> map = BuildUnicodeMap();
        Action nullCff = () => OpenTypeFontBuilder.Build(null!, loader, map, "X", false, false);
        Action nullLoader = () => OpenTypeFontBuilder.Build(MinimalCff, null!, map, "X", false, false);
        Action nullMap = () => OpenTypeFontBuilder.Build(MinimalCff, loader, null!, "X", false, false);
        Action nullName = () => OpenTypeFontBuilder.Build(MinimalCff, loader, map, null!, false, false);
        nullCff.Should().Throw<ArgumentNullException>();
        nullLoader.Should().Throw<ArgumentNullException>();
        nullMap.Should().Throw<ArgumentNullException>();
        nullName.Should().Throw<ArgumentNullException>();
    }
}
