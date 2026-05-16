// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Tests for INTEGER value codec

using System;
using System.IO;
using System.Numerics;
using Chuvadi.Cryptography.Asn1;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.Asn1;

public sealed class Asn1IntegerTests
{
    // ── Spec-defined known encodings ──────────────────────────────────────

    [Fact]
    public void Encode_Zero()
    {
        // X.690 §8.3.2: value 0 encodes as one byte 00.
        using MemoryStream ms = new();
        Asn1Integer.Write(ms, 0);
        ms.ToArray().Should().Equal(0x02, 0x01, 0x00);
    }

    [Fact]
    public void Encode_PositiveSmall()
    {
        using MemoryStream ms = new();
        Asn1Integer.Write(ms, 1);
        ms.ToArray().Should().Equal(0x02, 0x01, 0x01);
    }

    [Fact]
    public void Encode_Boundary127_OneByte()
    {
        using MemoryStream ms = new();
        Asn1Integer.Write(ms, 127);
        ms.ToArray().Should().Equal(0x02, 0x01, 0x7F);
    }

    [Fact]
    public void Encode_Boundary128_LeadingZeroAddedToKeepPositive()
    {
        // Value 128 = 0x80, but 0x80 as a two's-complement byte means -128.
        // So we need a leading 0x00.
        using MemoryStream ms = new();
        Asn1Integer.Write(ms, 128);
        ms.ToArray().Should().Equal(0x02, 0x02, 0x00, 0x80);
    }

    [Fact]
    public void Encode_NegativeOne()
    {
        using MemoryStream ms = new();
        Asn1Integer.Write(ms, -1);
        ms.ToArray().Should().Equal(0x02, 0x01, 0xFF);
    }

    [Fact]
    public void Encode_NegativeBoundary_Minus128()
    {
        using MemoryStream ms = new();
        Asn1Integer.Write(ms, -128);
        ms.ToArray().Should().Equal(0x02, 0x01, 0x80);
    }

    [Fact]
    public void Encode_NegativeBoundary_Minus129()
    {
        using MemoryStream ms = new();
        Asn1Integer.Write(ms, -129);
        ms.ToArray().Should().Equal(0x02, 0x02, 0xFF, 0x7F);
    }

    // ── Decode rejects non-minimal encodings (strict DER) ─────────────────

    [Fact]
    public void Decode_NonMinimalPositive_Rejected()
    {
        // Leading 0x00 with next byte's high bit also 0 — not minimal.
        byte[] bytes = [0x02, 0x02, 0x00, 0x01];
        Action act = () => Asn1Integer.Read(bytes, 0, out _);
        act.Should().Throw<Asn1Exception>().WithMessage("*minimum*");
    }

    [Fact]
    public void Decode_NonMinimalNegative_Rejected()
    {
        // Leading 0xFF with next byte's high bit also 1 — not minimal.
        byte[] bytes = [0x02, 0x02, 0xFF, 0xFF];
        Action act = () => Asn1Integer.Read(bytes, 0, out _);
        act.Should().Throw<Asn1Exception>().WithMessage("*minimum*");
    }

    [Fact]
    public void Decode_EmptyContent_Rejected()
    {
        byte[] bytes = [0x02, 0x00];
        Action act = () => Asn1Integer.Read(bytes, 0, out _);
        act.Should().Throw<Asn1Exception>().WithMessage("*at least one octet*");
    }

    // ── Large integer (typical certificate serial number) ─────────────────

    [Fact]
    public void RoundTrip_LargeBigInteger()
    {
        // 128-bit positive integer.
        BigInteger value = BigInteger.Parse("12345678901234567890123456789012345678");
        using MemoryStream ms = new();
        Asn1Integer.Write(ms, value);
        byte[] bytes = ms.ToArray();
        Asn1Integer.Read(bytes, 0, out BigInteger decoded);
        decoded.Should().Be(value);
    }

    // ── Property-style round trips across a range ─────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(-128)]
    [InlineData(-129)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(-256)]
    [InlineData(32767)]
    [InlineData(32768)]
    [InlineData(-32768)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void RoundTrip_Int32_PreservesValue(int value)
    {
        using MemoryStream ms = new();
        Asn1Integer.Write(ms, value);
        byte[] bytes = ms.ToArray();
        Asn1Integer.Read(bytes, 0, out BigInteger decoded);
        decoded.Should().Be(new BigInteger(value));
    }

    [Theory]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    [InlineData(1_000_000_000_000L)]
    [InlineData(-1_000_000_000_000L)]
    public void RoundTrip_Int64_PreservesValue(long value)
    {
        using MemoryStream ms = new();
        Asn1Integer.Write(ms, value);
        byte[] bytes = ms.ToArray();
        Asn1Integer.Read(bytes, 0, out BigInteger decoded);
        decoded.Should().Be(new BigInteger(value));
    }

    [Fact]
    public void Write_NullStream_Throws()
    {
        Action act = () => Asn1Integer.Write(null!, 0);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Read_NullSource_Throws()
    {
        Action act = () => Asn1Integer.Read(null!, 0, out _);
        act.Should().Throw<ArgumentNullException>();
    }
}
