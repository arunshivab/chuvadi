// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Tests for ByteRange

using System;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Signatures.Tests;

public sealed class ByteRangeTests
{
    [Fact]
    public void Construct_ValidValues()
    {
        ByteRange br = new(0, 100, 200, 50);
        br.FirstOffset.Should().Be(0);
        br.FirstLength.Should().Be(100);
        br.SecondOffset.Should().Be(200);
        br.SecondLength.Should().Be(50);
        br.TotalLength.Should().Be(150);
        br.GapOffset.Should().Be(100);
        br.GapLength.Should().Be(100);
    }

    [Fact]
    public void Construct_NegativeFirstOffset_Throws()
    {
        Action act = () => new ByteRange(-1, 100, 200, 50);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Construct_NegativeFirstLength_Throws()
    {
        Action act = () => new ByteRange(0, -1, 200, 50);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Construct_OverlappingRanges_Throws()
    {
        Action act = () => new ByteRange(0, 100, 50, 50);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Construct_AdjacentRanges_AreAllowed()
    {
        ByteRange br = new(0, 100, 100, 50);
        br.GapLength.Should().Be(0);
    }

    [Fact]
    public void ToString_FormatsAsPdfArray()
    {
        ByteRange br = new(0, 100, 200, 50);
        br.ToString().Should().Be("[0 100 200 50]");
    }
}
