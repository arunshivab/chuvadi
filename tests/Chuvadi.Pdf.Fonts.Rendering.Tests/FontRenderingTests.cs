// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  OpenType specification — glyph metrics, cmap, glyf
// PHASE: Phase 2 — Chuvadi.Pdf.Fonts.Rendering tests

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Graphics;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Rendering.Tests;

// ── FontRenderingException ─────────────────────────────────────────────────

public sealed class FontRenderingExceptionTests
{
    [Fact]
    public void DefaultConstructor_HasMessage()
    {
        FontRenderingException ex = new FontRenderingException();
        ex.Message.Should().NotBeEmpty();
    }

    [Fact]
    public void MessageConstructor_PreservesMessage()
    {
        FontRenderingException ex = new FontRenderingException("bad font");
        ex.Message.Should().Be("bad font");
    }

    [Fact]
    public void InnerExceptionConstructor_PreservesInner()
    {
        InvalidOperationException inner = new InvalidOperationException("inner");
        FontRenderingException ex = new FontRenderingException("outer", inner);
        ex.InnerException.Should().BeSameAs(inner);
    }
}

// ── GlyphMetrics ──────────────────────────────────────────────────────────

public sealed class GlyphMetricsTests
{
    [Fact]
    public void AdvanceWidthAt_ScalesCorrectly()
    {
        GlyphMetrics m = new GlyphMetrics(1000, 0, 1000, RectangleF.Zero);
        // 1000 units / 1000 unitsPerEm × 12pt = 12pt
        m.AdvanceWidthAt(12).Should().BeApproximately(12.0, 1e-10);
    }

    [Fact]
    public void AdvanceWidthAt_HalfWidth_IsHalfAdvance()
    {
        GlyphMetrics m = new GlyphMetrics(500, 0, 1000, RectangleF.Zero);
        m.AdvanceWidthAt(10).Should().BeApproximately(5.0, 1e-10);
    }

    [Fact]
    public void AdvanceWidthAt_ZeroUnitsPerEm_ReturnsZero()
    {
        GlyphMetrics m = new GlyphMetrics(500, 0, 0, RectangleF.Zero);
        m.AdvanceWidthAt(12).Should().Be(0);
    }
}

// ── GlyphOutline ──────────────────────────────────────────────────────────

public sealed class GlyphOutlineTests
{
    [Fact]
    public void Constructor_NullOutline_Throws()
    {
        GlyphMetrics m = new GlyphMetrics(500, 0, 1000, RectangleF.Zero);
        Action act = () => new GlyphOutline(null!, m);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullMetrics_Throws()
    {
        Action act = () => new GlyphOutline(new Path(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsEmpty_EmptyPath_ReturnsTrue()
    {
        GlyphMetrics m = new GlyphMetrics(0, 0, 1000, RectangleF.Zero);
        GlyphOutline g = new GlyphOutline(new Path(), m);
        g.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Scale_NegativePointSize_Throws()
    {
        GlyphMetrics m = new GlyphMetrics(500, 0, 1000, RectangleF.Zero);
        GlyphOutline g = new GlyphOutline(new Path(), m);
        Action act = () => g.Scale(-1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Scale_EmptyPath_ReturnsEmptyPath()
    {
        GlyphMetrics m = new GlyphMetrics(1000, 0, 1000, RectangleF.Zero);
        GlyphOutline g = new GlyphOutline(new Path(), m);
        GlyphOutline scaled = g.Scale(12);
        scaled.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Scale_ScalesMetrics()
    {
        GlyphMetrics m = new GlyphMetrics(2048, 100, 2048, RectangleF.Zero);
        GlyphOutline g = new GlyphOutline(new Path(), m);
        GlyphOutline scaled = g.Scale(2048); // 1 unit = 1 pt at this size
        scaled.Metrics.AdvanceWidth.Should().Be(2048);
    }

    [Fact]
    public void Scale_ScalesPathCoordinates()
    {
        GlyphMetrics m = new GlyphMetrics(1000, 0, 1000, RectangleF.Zero);
        Path p = new Path();
        p.MoveTo(0, 0).LineTo(1000, 0).LineTo(1000, 1000).ClosePath();
        GlyphOutline g = new GlyphOutline(p, m);

        GlyphOutline scaled = g.Scale(12); // scale = 12/1000 = 0.012
        scaled.Outline.Count.Should().Be(4); // MoveTo + 2 LineTo + ClosePath

        // The LineTo(1000, 0) should become approximately LineTo(12, 0)
        PathSegment lineSeg = scaled.Outline.Segments[1];
        lineSeg.P0.X.Should().BeApproximately(12.0, 1e-6);
        lineSeg.P0.Y.Should().BeApproximately(0.0, 1e-6);
    }
}

// ── TrueTypeLoader ────────────────────────────────────────────────────────

public sealed class TrueTypeLoaderTests
{
    [Fact]
    public void Constructor_NullData_Throws()
    {
        Action act = () => new TrueTypeLoader(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_InvalidData_ThrowsFontRenderingException()
    {
        byte[] garbage = new byte[64];
        Action act = () => new TrueTypeLoader(garbage);
        act.Should().Throw<FontRenderingException>();
    }

    [Fact]
    public void Constructor_TooShort_ThrowsException()
    {
        byte[] tooShort = [0x00, 0x01, 0x00, 0x00];
        Action act = () => new TrueTypeLoader(tooShort);
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void Constructor_ValidMinimalFont_LoadsSuccessfully()
    {
        byte[] font = BuildMinimalTtf();
        TrueTypeLoader loader = new TrueTypeLoader(font);
        loader.UnitsPerEm.Should().Be(1000);
        loader.NumGlyphs.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetGlyphIndex_UnmappedChar_ReturnsZero()
    {
        byte[] font = BuildMinimalTtf();
        TrueTypeLoader loader = new TrueTypeLoader(font);
        // No cmap entries in minimal font — all chars map to .notdef
        loader.GetGlyphIndex(0x0041).Should().Be(0);
    }

    [Fact]
    public void GetGlyphOutline_GlyphZero_ReturnsEmptyOutline()
    {
        byte[] font = BuildMinimalTtf();
        TrueTypeLoader loader = new TrueTypeLoader(font);
        // .notdef glyph (0) in minimal font has no contours
        GlyphOutline outline = loader.GetGlyphOutline(0);
        outline.Should().NotBeNull();
        outline.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void GetGlyphOutline_OutOfRange_Throws()
    {
        byte[] font = BuildMinimalTtf();
        TrueTypeLoader loader = new TrueTypeLoader(font);
        Action act = () => loader.GetGlyphOutline(9999);
        act.Should().Throw<FontRenderingException>();
    }

    // ── Minimal valid TTF builder ──────────────────────────────────────────

    /// <summary>
    /// Builds a structurally valid minimal TrueType font with 1 glyph (.notdef),
    /// enough to exercise the loader without a real font file.
    /// </summary>
    private static byte[] BuildMinimalTtf()
    {
        // We need: sfVersion + numTables + searchRange/entrySelector/rangeShift
        // Then table directory entries (16 bytes each)
        // Then the table data for: head, hhea, maxp, loca, glyf, hmtx, cmap

        List<byte> data = new List<byte>();

        // --- Offset table (12 bytes) ---
        // sfVersion = 0x00010000 (TrueType)
        // numTables = 7
        data.AddRange([0x00, 0x01, 0x00, 0x00]); // sfVersion
        data.AddRange(U16(7));                     // numTables
        data.AddRange(U16(128));                   // searchRange
        data.AddRange(U16(3));                     // entrySelector
        data.AddRange(U16(112));                   // rangeShift

        // Reserve space for table directory (7 × 16 = 112 bytes)
        int dirStart = data.Count;

        for (int i = 0; i < 7 * 16; i++)
        {
            data.Add(0);
        }

        // --- Build tables ---
        // cmap (minimal — no mappings, just the header)
        int cmapOffset = data.Count;
        data.AddRange(U16(0));  // version
        data.AddRange(U16(0));  // numTables = 0
        int cmapLen = data.Count - cmapOffset;

        // head (54 bytes)
        int headOffset = data.Count;
        data.AddRange(U32(0x00010000)); // version
        data.AddRange(U32(0));          // fontRevision
        data.AddRange(U32(0));          // checkSumAdjustment
        data.AddRange(U32(0x5F0F3CF5)); // magicNumber
        data.AddRange(U16(0));          // flags
        data.AddRange(U16(1000));       // unitsPerEm
        data.AddRange(new byte[16]);    // created + modified
        data.AddRange(U16(0));          // xMin
        data.AddRange(U16(0));          // yMin
        data.AddRange(U16(0));          // xMax
        data.AddRange(U16(0));          // yMax
        data.AddRange(U16(0));          // macStyle
        data.AddRange(U16(8));          // lowestRecPPEM
        data.AddRange(U16(2));          // fontDirectionHint
        data.AddRange(new byte[2]);     // indexToLocFormat = 0 (short)
        data.AddRange(U16(0));          // glyphDataFormat
        int headLen = data.Count - headOffset;

        // hhea (36 bytes)
        int hheaOffset = data.Count;
        data.AddRange(U32(0x00010000)); // version
        data.AddRange(new byte[28]);    // ascender..caretSlopeRun
        data.AddRange(U16(1));          // numberOfHMetrics = 1
        int hheaLen = data.Count - hheaOffset;

        // maxp (6 bytes — v0.5)
        int maxpOffset = data.Count;
        data.AddRange(U32(0x00005000)); // version 0.5
        data.AddRange(U16(1));          // numGlyphs = 1
        int maxpLen = data.Count - maxpOffset;

        // hmtx (4 bytes — 1 hMetric)
        int hmtxOffset = data.Count;
        data.AddRange(U16(500));        // advanceWidth
        data.AddRange(new byte[2]);     // lsb = 0
        int hmtxLen = data.Count - hmtxOffset;

        // loca (4 bytes — 2 uint16 entries for 1 glyph, short format)
        // Both entries = 0 → empty glyph
        int locaOffset = data.Count;
        data.AddRange(U16(0));          // glyph 0 offset
        data.AddRange(U16(0));          // glyph 0 end = same → empty
        int locaLen = data.Count - locaOffset;

        // glyf (0 bytes — glyph is empty)
        int glyfOffset = data.Count;
        int glyfLen = 0;

        // --- Fill in table directory ---
        string[] tags = ["cmap", "glyf", "head", "hhea", "hmtx", "loca", "maxp"];
        int[] offsets = [cmapOffset, glyfOffset, headOffset, hheaOffset, hmtxOffset, locaOffset, maxpOffset];
        int[] lengths = [cmapLen, glyfLen, headLen, hheaLen, hmtxLen, locaLen, maxpLen];

        for (int i = 0; i < 7; i++)
        {
            int pos = dirStart + i * 16;
            byte[] tagBytes = System.Text.Encoding.ASCII.GetBytes(tags[i]);
            data[pos] = tagBytes[0];
            data[pos + 1] = tagBytes[1];
            data[pos + 2] = tagBytes[2];
            data[pos + 3] = tagBytes[3];
            // checkSum = 0 (not validated)
            byte[] off = U32((uint)offsets[i]);
            byte[] len = U32((uint)lengths[i]);
            data[pos + 8] = off[0]; data[pos + 9] = off[1];
            data[pos + 10] = off[2]; data[pos + 11] = off[3];
            data[pos + 12] = len[0]; data[pos + 13] = len[1];
            data[pos + 14] = len[2]; data[pos + 15] = len[3];
        }

        return data.ToArray();
    }

    private static byte[] U16(int v)
    {
        return [(byte)((v >> 8) & 0xFF), (byte)(v & 0xFF)];
    }

    internal static byte[] InvokeMinimalFont() => BuildMinimalTtf();

    private static byte[] U32(uint v)
    {
        return [
            (byte)((v >> 24) & 0xFF),
            (byte)((v >> 16) & 0xFF),
            (byte)((v >> 8) & 0xFF),
            (byte)(v & 0xFF),
        ];
    }
}

// ── FontRenderer ──────────────────────────────────────────────────────────

public sealed class FontRendererTests
{
    [Fact]
    public void Constructor_NullData_Throws()
    {
        Action act = () => new FontRenderer(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ValidFont_Loads()
    {
        byte[] font = TrueTypeLoaderTests.InvokeMinimalFont();
        FontRenderer renderer = new FontRenderer(font);
        renderer.UnitsPerEm.Should().Be(1000);
        renderer.NumGlyphs.Should().Be(1);
    }

    [Fact]
    public void GetGlyphOutline_GlyphZero_IsEmpty()
    {
        byte[] font = TrueTypeLoaderTests.InvokeMinimalFont();
        FontRenderer renderer = new FontRenderer(font);
        GlyphOutline outline = renderer.GetGlyphOutline(0);
        outline.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void GetScaledGlyphOutline_NegativeSize_Throws()
    {
        byte[] font = TrueTypeLoaderTests.InvokeMinimalFont();
        FontRenderer renderer = new FontRenderer(font);
        Action act = () => renderer.GetScaledGlyphOutline(0, -1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void LayoutText_NullText_Throws()
    {
        byte[] font = TrueTypeLoaderTests.InvokeMinimalFont();
        FontRenderer renderer = new FontRenderer(font);
        Action act = () => renderer.LayoutText(null!, 12);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void LayoutText_EmptyString_ReturnsEmpty()
    {
        byte[] font = TrueTypeLoaderTests.InvokeMinimalFont();
        FontRenderer renderer = new FontRenderer(font);
        List<(double X, GlyphOutline Glyph)> result = renderer.LayoutText("", 12);
        result.Should().BeEmpty();
    }

    [Fact]
    public void MeasureText_NullText_Throws()
    {
        byte[] font = TrueTypeLoaderTests.InvokeMinimalFont();
        FontRenderer renderer = new FontRenderer(font);
        Action act = () => renderer.MeasureText(null!, 12);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void MeasureText_EmptyString_ReturnsZero()
    {
        byte[] font = TrueTypeLoaderTests.InvokeMinimalFont();
        FontRenderer renderer = new FontRenderer(font);
        renderer.MeasureText("", 12).Should().Be(0);
    }
}
