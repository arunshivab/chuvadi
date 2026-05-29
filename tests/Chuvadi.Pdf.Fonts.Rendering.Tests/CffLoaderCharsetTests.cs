// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// Tests for CffLoader charset parsing (v2.1.6): glyph-name -> GID for simple
// (Type1C) fonts and the CID-keyed detection flag. The Type1C fixture is a
// real 4-glyph CFF (.notdef, A, checkmark, ffi) generated offline; "ffi" has a
// non-standard SID so it exercises the String INDEX resolution path, while
// "A"/"checkmark" exercise the Standard Strings path.

using System;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Rendering.Tests;

public sealed class CffLoaderCharsetTests
{
    // Type1C CFF: glyph order [.notdef, A, checkmark, ffi], 1000 upm, not CID-keyed.
    private const string NameCffBase64 =
        "AQAEAQABAQEJTmFtZVRlc3QAAQEBD4uL+R75UAW/D4v2EsYRAAEBAQpjaGVja21hcmsAAAAAIgGHAQsABAEBCxUfKfiIixb4iPlQBg74uosW+Lr5UAYO+OyLFvjs+VAGDvkeixb5HvlQBg4=";

    private static readonly byte[] NameCff = Convert.FromBase64String(NameCffBase64);

    [Fact]
    public void SimpleFont_IsNotCidKeyed()
    {
        CffLoader loader = new(NameCff);
        loader.IsCidFont.Should().BeFalse();
    }

    [Fact]
    public void SimpleFont_HasEmptyCidToGid()
    {
        CffLoader loader = new(NameCff);
        loader.CidToGid.Should().BeEmpty();
    }

    [Fact]
    public void SimpleFont_ResolvesStandardStringGlyphNames()
    {
        CffLoader loader = new(NameCff);
        // "A" (SID 34) and "checkmark" both reachable; .notdef is GID 0.
        loader.GlyphNameToGid.Should().ContainKey("A");
        loader.GlyphNameToGid["A"].Should().Be(1);
        loader.GlyphNameToGid.Should().ContainKey(".notdef");
        loader.GlyphNameToGid[".notdef"].Should().Be(0);
    }

    [Fact]
    public void SimpleFont_ResolvesNonStandardStringViaStringIndex()
    {
        CffLoader loader = new(NameCff);
        // "ffi" has a non-standard SID (>= 391), resolved through the String INDEX.
        loader.GlyphNameToGid.Should().ContainKey("ffi");
        loader.GlyphNameToGid["ffi"].Should().Be(3);
    }

    [Fact]
    public void SimpleFont_GlyphNameToGidCountMatchesGlyphCount()
    {
        CffLoader loader = new(NameCff);
        loader.GlyphNameToGid.Count.Should().Be(loader.NumGlyphs);
    }

    [Fact]
    public void NullFontData_Throws()
    {
        Action act = () => _ = new CffLoader(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
