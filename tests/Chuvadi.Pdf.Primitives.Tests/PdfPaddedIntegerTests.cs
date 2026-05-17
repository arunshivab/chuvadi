// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.2.1 — Fixed-width PDF integer primitive

using System;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Primitives.Tests;

public sealed class PdfPaddedIntegerTests
{
    [Fact]
    public void ToString_PadsValueWithLeadingZeros()
    {
        new PdfPaddedInteger(42, 10).ToString().Should().Be("0000000042");
    }

    [Fact]
    public void ToString_FullWidthValueHasNoExtraPadding()
    {
        new PdfPaddedInteger(1234567890, 10).ToString().Should().Be("1234567890");
    }

    [Fact]
    public void ToString_ZeroValuePadsCompletely()
    {
        new PdfPaddedInteger(0, 10).ToString().Should().Be("0000000000");
    }

    [Fact]
    public void Value_AndPaddedWidth_AreExposed()
    {
        PdfPaddedInteger i = new(42, 10);
        i.Value.Should().Be(42);
        i.PaddedWidth.Should().Be(10);
    }

    [Fact]
    public void PrimitiveType_IsInteger()
    {
        new PdfPaddedInteger(0, 1).PrimitiveType.Should().Be(PdfPrimitiveType.Integer);
    }

    [Fact]
    public void ZeroWidth_Throws()
    {
        Action act = () => new PdfPaddedInteger(0, 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void NegativeWidth_Throws()
    {
        Action act = () => new PdfPaddedInteger(0, -1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ValueTooLargeForWidth_Throws()
    {
        Action act = () => new PdfPaddedInteger(100, 2);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
