// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Tests for NULL and BOOLEAN value codecs

using System;
using System.IO;
using Chuvadi.Cryptography.Asn1;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.Asn1;

public sealed class Asn1NullTests
{
    [Fact]
    public void Write_Null_EmitsTwoBytes()
    {
        using MemoryStream ms = new();
        Asn1Null.Write(ms);
        ms.ToArray().Should().Equal(0x05, 0x00);
    }

    [Fact]
    public void EncodedBytes_MatchesSpec()
    {
        Asn1Null.EncodedBytes.Should().Equal(0x05, 0x00);
    }

    [Fact]
    public void Read_Null_ReturnsAfterOffset()
    {
        byte[] bytes = [0x05, 0x00];
        int after = Asn1Null.Read(bytes, 0);
        after.Should().Be(2);
    }

    [Fact]
    public void Read_NullWithContent_Rejected()
    {
        byte[] bytes = [0x05, 0x01, 0x00];
        Action act = () => Asn1Null.Read(bytes, 0);
        act.Should().Throw<Asn1Exception>().WithMessage("*content*");
    }

    [Fact]
    public void Read_WrongTag_Rejected()
    {
        byte[] bytes = [0x02, 0x00];  // INTEGER with empty content (legal? no, but tag is wrong)
        Action act = () => Asn1Null.Read(bytes, 0);
        act.Should().Throw<Asn1Exception>().WithMessage("*NULL tag*");
    }
}

public sealed class Asn1BooleanTests
{
    [Fact]
    public void Write_True_EmitsFF()
    {
        using MemoryStream ms = new();
        Asn1Boolean.Write(ms, true);
        ms.ToArray().Should().Equal(0x01, 0x01, 0xFF);
    }

    [Fact]
    public void Write_False_Emits00()
    {
        using MemoryStream ms = new();
        Asn1Boolean.Write(ms, false);
        ms.ToArray().Should().Equal(0x01, 0x01, 0x00);
    }

    [Fact]
    public void Read_TrueFF_ReturnsTrue()
    {
        byte[] bytes = [0x01, 0x01, 0xFF];
        Asn1Boolean.Read(bytes, 0, out bool value);
        value.Should().BeTrue();
    }

    [Fact]
    public void Read_False00_ReturnsFalse()
    {
        byte[] bytes = [0x01, 0x01, 0x00];
        Asn1Boolean.Read(bytes, 0, out bool value);
        value.Should().BeFalse();
    }

    [Fact]
    public void Read_BerNonZero_AcceptedAsTrue()
    {
        // BER allows any non-zero — confirm we accept this on read (we don't strict-DER on read).
        byte[] bytes = [0x01, 0x01, 0x42];
        Asn1Boolean.Read(bytes, 0, out bool value);
        value.Should().BeTrue();
    }

    [Fact]
    public void Read_ContentLengthZero_Rejected()
    {
        byte[] bytes = [0x01, 0x00];
        Action act = () => Asn1Boolean.Read(bytes, 0, out _);
        act.Should().Throw<Asn1Exception>().WithMessage("*1 byte*");
    }

    [Fact]
    public void Read_ContentLengthTwo_Rejected()
    {
        byte[] bytes = [0x01, 0x02, 0xFF, 0xFF];
        Action act = () => Asn1Boolean.Read(bytes, 0, out _);
        act.Should().Throw<Asn1Exception>().WithMessage("*1 byte*");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoundTrip_PreservesValue(bool value)
    {
        using MemoryStream ms = new();
        Asn1Boolean.Write(ms, value);
        byte[] bytes = ms.ToArray();
        Asn1Boolean.Read(bytes, 0, out bool decoded);
        decoded.Should().Be(value);
    }
}
