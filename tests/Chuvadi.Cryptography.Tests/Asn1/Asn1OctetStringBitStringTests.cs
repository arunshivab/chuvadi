// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Tests for OCTET STRING and BIT STRING

using System;
using System.IO;
using Chuvadi.Cryptography.Asn1;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.Asn1;

public sealed class Asn1OctetStringTests
{
    [Fact]
    public void Write_EmptyOctetString()
    {
        using MemoryStream ms = new();
        Asn1OctetString.Write(ms, ReadOnlySpan<byte>.Empty);
        ms.ToArray().Should().Equal(0x04, 0x00);
    }

    [Fact]
    public void Write_ThreeBytes()
    {
        using MemoryStream ms = new();
        Asn1OctetString.Write(ms, new byte[] { 0xDE, 0xAD, 0xBE });
        ms.ToArray().Should().Equal(0x04, 0x03, 0xDE, 0xAD, 0xBE);
    }

    [Fact]
    public void Read_DecodesContent()
    {
        byte[] bytes = [0x04, 0x03, 0xDE, 0xAD, 0xBE];
        Asn1OctetString.Read(bytes, 0, out byte[] value);
        value.Should().Equal(0xDE, 0xAD, 0xBE);
    }

    [Fact]
    public void Read_ConstructedFormRejected()
    {
        // 0x24 = constructed OCTET STRING. DER forbids it.
        byte[] bytes = [0x24, 0x00];
        Action act = () => Asn1OctetString.Read(bytes, 0, out _);
        act.Should().Throw<Asn1Exception>().WithMessage("*Constructed*");
    }

    [Fact]
    public void Read_WrongTag_Rejected()
    {
        byte[] bytes = [0x05, 0x00];  // NULL
        Action act = () => Asn1OctetString.Read(bytes, 0, out _);
        act.Should().Throw<Asn1Exception>().WithMessage("*OCTET STRING*");
    }

    [Fact]
    public void RoundTrip_PreservesContent()
    {
        byte[] data = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        using MemoryStream ms = new();
        Asn1OctetString.Write(ms, data);
        Asn1OctetString.Read(ms.ToArray(), 0, out byte[] result);
        result.Should().Equal(data);
    }
}

public sealed class Asn1BitStringTests
{
    [Fact]
    public void Write_EmptyBitString()
    {
        using MemoryStream ms = new();
        Asn1BitString.Write(ms, ReadOnlySpan<byte>.Empty);
        // 03 01 00 — empty value: one byte of "unused = 0", no payload
        ms.ToArray().Should().Equal(0x03, 0x01, 0x00);
    }

    [Fact]
    public void Write_OneByte_ZeroUnused()
    {
        using MemoryStream ms = new();
        Asn1BitString.Write(ms, new byte[] { 0xAA });
        ms.ToArray().Should().Equal(0x03, 0x02, 0x00, 0xAA);
    }

    [Fact]
    public void Write_WithUnusedBits()
    {
        // 5 bits used, 3 unused. Last byte's low 3 bits must be zero.
        BitStringValue v = new(new byte[] { 0b10101000 }, unusedBitsInFinalOctet: 3);
        using MemoryStream ms = new();
        Asn1BitString.Write(ms, v);
        ms.ToArray().Should().Equal(0x03, 0x02, 0x03, 0b10101000);
    }

    [Fact]
    public void Read_PaddingBitsNonZero_Rejected()
    {
        // unused = 3 but the low 3 bits of the payload are non-zero — DER violation.
        byte[] bytes = [0x03, 0x02, 0x03, 0b10101111];
        Action act = () => Asn1BitString.Read(bytes, 0, out _);
        act.Should().Throw<Asn1Exception>().WithMessage("*padding*");
    }

    [Fact]
    public void Read_UnusedBitsOver7_Rejected()
    {
        byte[] bytes = [0x03, 0x02, 0x08, 0x00];
        Action act = () => Asn1BitString.Read(bytes, 0, out _);
        act.Should().Throw<Asn1Exception>().WithMessage("*0..7*");
    }

    [Fact]
    public void Read_ConstructedFormRejected()
    {
        byte[] bytes = [0x23, 0x00];
        Action act = () => Asn1BitString.Read(bytes, 0, out _);
        act.Should().Throw<Asn1Exception>().WithMessage("*Constructed*");
    }

    [Fact]
    public void Read_EmptyContent_Rejected()
    {
        byte[] bytes = [0x03, 0x00];
        Action act = () => Asn1BitString.Read(bytes, 0, out _);
        act.Should().Throw<Asn1Exception>().WithMessage("*at least the unused-bits byte*");
    }

    [Fact]
    public void Read_EmptyBitStringWithNonZeroUnused_Rejected()
    {
        byte[] bytes = [0x03, 0x01, 0x03];
        Action act = () => Asn1BitString.Read(bytes, 0, out _);
        act.Should().Throw<Asn1Exception>().WithMessage("*Empty*");
    }

    [Fact]
    public void RoundTrip_PreservesValue()
    {
        BitStringValue v = new(new byte[] { 0xDE, 0xAD, 0xB8 }, unusedBitsInFinalOctet: 3);
        using MemoryStream ms = new();
        Asn1BitString.Write(ms, v);
        Asn1BitString.Read(ms.ToArray(), 0, out BitStringValue decoded);
        decoded.Bytes.Should().Equal(v.Bytes);
        decoded.UnusedBitsInFinalOctet.Should().Be(3);
        decoded.BitLength.Should().Be((3 * 8) - 3);
    }

    [Fact]
    public void BitStringValue_RejectsBadUnusedCount()
    {
        Action act = () => new BitStringValue(new byte[] { 0x00 }, unusedBitsInFinalOctet: 8);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void BitStringValue_EmptyMustHaveZeroUnused()
    {
        Action act = () => new BitStringValue(Array.Empty<byte>(), unusedBitsInFinalOctet: 1);
        act.Should().Throw<ArgumentException>();
    }
}
