// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9.4 — Text objects and operators
// PHASE: Phase 1 — Chuvadi.Pdf.Content tests

using System;
using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Content.Tests;

// ── ContentException ──────────────────────────────────────────────────────

public sealed class ContentExceptionTests
{
    [Fact]
    public void DefaultConstructor_HasMessage()
    {
        ContentException ex = new ContentException();
        ex.Message.Should().NotBeEmpty();
    }

    [Fact]
    public void MessageConstructor_PreservesMessage()
    {
        ContentException ex = new ContentException("test");
        ex.Message.Should().Be("test");
    }

    [Fact]
    public void InnerExceptionConstructor_PreservesInner()
    {
        InvalidOperationException inner = new InvalidOperationException("inner");
        ContentException ex = new ContentException("outer", inner);
        ex.InnerException.Should().BeSameAs(inner);
    }
}

// ── TextFragment ──────────────────────────────────────────────────────────

public sealed class TextFragmentTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        TextFragment fragment = new TextFragment("Hello", 10.5, 20.3, 12.0);
        fragment.Text.Should().Be("Hello");
        fragment.X.Should().BeApproximately(10.5, 0.001);
        fragment.Y.Should().BeApproximately(20.3, 0.001);
        fragment.FontSize.Should().BeApproximately(12.0, 0.001);
    }

    [Fact]
    public void Constructor_NullText_DefaultsToEmpty()
    {
        TextFragment fragment = new TextFragment(null!, 0, 0, 12);
        fragment.Text.Should().BeEmpty();
    }

    [Fact]
    public void ToString_ContainsText()
    {
        TextFragment fragment = new TextFragment("Test", 100, 200, 14);
        fragment.ToString().Should().Contain("Test");
    }
}

// ── Matrix3x3 ────────────────────────────────────────────────────────────

public sealed class Matrix3x3Tests
{
    [Fact]
    public void Identity_HasCorrectValues()
    {
        Matrix3x3 m = Matrix3x3.Identity;
        m.A.Should().Be(1);
        m.D.Should().Be(1);
        m.E.Should().Be(0);
        m.F.Should().Be(0);
    }

    [Fact]
    public void Translate_UpdatesEF()
    {
        Matrix3x3 m = Matrix3x3.Identity.Translate(10, 20);
        m.TranslationX.Should().BeApproximately(10, 0.001);
        m.TranslationY.Should().BeApproximately(20, 0.001);
    }

    [Fact]
    public void Multiply_Identity_ReturnsSame()
    {
        Matrix3x3 m = new Matrix3x3(2, 0, 0, 3, 5, 7);
        Matrix3x3 result = m.Multiply(Matrix3x3.Identity);
        result.A.Should().BeApproximately(m.A, 0.001);
        result.E.Should().BeApproximately(m.E, 0.001);
        result.F.Should().BeApproximately(m.F, 0.001);
    }

    [Fact]
    public void Multiply_Translation_Accumulates()
    {
        Matrix3x3 a = Matrix3x3.Identity.Translate(10, 0);
        Matrix3x3 b = Matrix3x3.Identity.Translate(5, 0);
        Matrix3x3 result = a.Multiply(b);
        result.TranslationX.Should().BeApproximately(15, 0.001);
    }
}

// ── GraphicsState ─────────────────────────────────────────────────────────

public sealed class GraphicsStateTests
{
    [Fact]
    public void DefaultState_HasIdentityMatrices()
    {
        GraphicsState state = new GraphicsState();
        state.TextMatrix.A.Should().Be(1);
        state.TextMatrix.E.Should().Be(0);
        state.CurrentTransformationMatrix.A.Should().Be(1);
    }

    [Fact]
    public void DefaultState_HasDefaultFontSize()
    {
        GraphicsState state = new GraphicsState();
        state.FontSize.Should().Be(12.0);
    }

    [Fact]
    public void Clone_ProducesIndependentCopy()
    {
        GraphicsState original = new GraphicsState();
        original.FontSize = 24.0;

        GraphicsState clone = original.Clone();
        clone.FontSize = 36.0;

        original.FontSize.Should().Be(24.0);
    }

    [Fact]
    public void DefaultState_CharacterSpacingIsZero()
    {
        GraphicsState state = new GraphicsState();
        state.CharacterSpacing.Should().Be(0.0);
        state.WordSpacing.Should().Be(0.0);
    }

    [Fact]
    public void DefaultState_HorizontalScalingIs100()
    {
        GraphicsState state = new GraphicsState();
        state.HorizontalScaling.Should().Be(100.0);
    }
}

// ── ContentStreamParser ───────────────────────────────────────────────────

public sealed class ContentStreamParserTests
{
    private static ContentStreamParser MakeParser()
    {
        return new ContentStreamParser(new NullResolver(), resources: null);
    }

    [Fact]
    public void Parse_NullBytes_Throws()
    {
        ContentStreamParser parser = MakeParser();
        Action act = () => parser.Parse(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Parse_EmptyStream_ReturnsEmpty()
    {
        ContentStreamParser parser = MakeParser();
        List<TextFragment> fragments = parser.Parse([]);
        fragments.Should().BeEmpty();
    }

    [Fact]
    public void Parse_BT_ET_NoText_ReturnsEmpty()
    {
        ContentStreamParser parser = MakeParser();
        byte[] stream = Encoding.ASCII.GetBytes("BT ET");
        List<TextFragment> fragments = parser.Parse(stream);
        fragments.Should().BeEmpty();
    }

    [Fact]
    public void Parse_SimpleTj_ExtractsText()
    {
        ContentStreamParser parser = MakeParser();
        // BT 12 Tf (Hello) Tj ET
        byte[] stream = Encoding.ASCII.GetBytes("BT /F1 12 Tf (Hello) Tj ET");
        List<TextFragment> fragments = parser.Parse(stream);
        fragments.Should().HaveCount(1);
        fragments[0].Text.Should().Be("Hello");
        fragments[0].FontSize.Should().Be(12.0);
    }

    [Fact]
    public void Parse_Tm_SetsPosition()
    {
        ContentStreamParser parser = MakeParser();
        byte[] stream = Encoding.ASCII.GetBytes("BT /F1 12 Tf 1 0 0 1 100 200 Tm (Hi) Tj ET");
        List<TextFragment> fragments = parser.Parse(stream);
        fragments.Should().HaveCount(1);
        fragments[0].X.Should().BeApproximately(100, 1);
        fragments[0].Y.Should().BeApproximately(200, 1);
    }

    [Fact]
    public void Parse_MultipleTj_ExtractsAll()
    {
        ContentStreamParser parser = MakeParser();
        byte[] stream = Encoding.ASCII.GetBytes(
            "BT /F1 12 Tf (Hello) Tj (World) Tj ET");
        List<TextFragment> fragments = parser.Parse(stream);
        fragments.Should().HaveCount(2);
        fragments[0].Text.Should().Be("Hello");
        fragments[1].Text.Should().Be("World");
    }

    [Fact]
    public void Parse_OutsideBT_IgnoresTj()
    {
        ContentStreamParser parser = MakeParser();
        byte[] stream = Encoding.ASCII.GetBytes("(Outside) Tj");
        List<TextFragment> fragments = parser.Parse(stream);
        fragments.Should().BeEmpty();
    }

    [Fact]
    public void Parse_QqPreservesState()
    {
        ContentStreamParser parser = MakeParser();
        byte[] stream = Encoding.ASCII.GetBytes(
            "BT /F1 12 Tf q /F1 24 Tf Q (Hello) Tj ET");
        List<TextFragment> fragments = parser.Parse(stream);
        fragments.Should().HaveCount(1);
        // After Q, font size should be restored to 12
        fragments[0].FontSize.Should().Be(12.0);
    }

    [Fact]
    public void Parse_TJArray_CombinesText()
    {
        ContentStreamParser parser = MakeParser();
        byte[] stream = Encoding.ASCII.GetBytes(
            "BT /F1 12 Tf [(Hello) -50 (World)] TJ ET");
        List<TextFragment> fragments = parser.Parse(stream);
        fragments.Should().HaveCount(1);
        fragments[0].Text.Should().Contain("Hello");
        fragments[0].Text.Should().Contain("World");
    }
}

// ── NullResolver (test helper) ────────────────────────────────────────────

internal sealed class NullResolver : Chuvadi.Pdf.Objects.IPdfObjectResolver
{
    public Chuvadi.Pdf.Primitives.PdfPrimitive Resolve(
        Chuvadi.Pdf.Primitives.PdfPrimitive primitive) => primitive;

    public Chuvadi.Pdf.Primitives.PdfPrimitive ResolveById(
        Chuvadi.Pdf.Primitives.PdfObjectId id) =>
        Chuvadi.Pdf.Primitives.PdfNull.Value;

    public bool Contains(Chuvadi.Pdf.Primitives.PdfObjectId id) => false;
}
