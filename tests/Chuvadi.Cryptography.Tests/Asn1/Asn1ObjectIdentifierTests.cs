// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Tests for OBJECT IDENTIFIER

using System;
using System.IO;
using Chuvadi.Cryptography.Asn1;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.Asn1;

public sealed class ObjectIdentifierTests
{
    [Fact]
    public void Construct_FromArcs_DottedReflectsArcs()
    {
        ObjectIdentifier oid = new(1, 2, 840, 113549);
        oid.Dotted.Should().Be("1.2.840.113549");
        oid.Arcs.Should().Equal(1L, 2L, 840L, 113549L);
    }

    [Fact]
    public void Construct_FromDotted_ProducesArcs()
    {
        ObjectIdentifier oid = new("2.5.4.3");
        oid.Arcs.Should().Equal(2L, 5L, 4L, 3L);
    }

    [Fact]
    public void Construct_TooFewArcs_Throws()
    {
        Action act = () => new ObjectIdentifier(1);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Construct_FirstArcOutOfRange_Throws()
    {
        Action act = () => new ObjectIdentifier(3, 0);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Construct_SecondArcOutOfRangeForArc0_Throws()
    {
        Action act = () => new ObjectIdentifier(1, 40);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Construct_SecondArc_AllowedLargeForArc2()
    {
        ObjectIdentifier oid = new(2, 999);
        oid.Dotted.Should().Be("2.999");
    }

    [Fact]
    public void Equals_SameArcs_AreEqual()
    {
        ObjectIdentifier a = new("1.2.3.4");
        ObjectIdentifier b = new("1.2.3.4");
        a.Should().Be(b);
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Equals_DifferentArcs_NotEqual()
    {
        ObjectIdentifier a = new("1.2.3.4");
        ObjectIdentifier b = new("1.2.3.5");
        (a != b).Should().BeTrue();
    }
}

public sealed class Asn1ObjectIdentifierCodecTests
{
    // ── Spec-defined known encodings ──────────────────────────────────────

    [Fact]
    public void Encode_RsaEncryption_MatchesSpec()
    {
        // 1.2.840.113549.1.1.1 — RSA encryption OID, very well-known encoding
        ObjectIdentifier oid = new("1.2.840.113549.1.1.1");
        using MemoryStream ms = new();
        Asn1ObjectIdentifier.Write(ms, oid);
        // Encoding: 06 09 2A 86 48 86 F7 0D 01 01 01
        ms.ToArray().Should().Equal(
            0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x01, 0x01);
    }

    [Fact]
    public void Encode_Sha256_MatchesSpec()
    {
        // 2.16.840.1.101.3.4.2.1
        ObjectIdentifier oid = new("2.16.840.1.101.3.4.2.1");
        using MemoryStream ms = new();
        Asn1ObjectIdentifier.Write(ms, oid);
        // 06 09 60 86 48 01 65 03 04 02 01
        ms.ToArray().Should().Equal(
            0x06, 0x09, 0x60, 0x86, 0x48, 0x01, 0x65, 0x03, 0x04, 0x02, 0x01);
    }

    [Fact]
    public void Encode_Ed25519_MatchesSpec()
    {
        // 1.3.101.112
        ObjectIdentifier oid = new("1.3.101.112");
        using MemoryStream ms = new();
        Asn1ObjectIdentifier.Write(ms, oid);
        // 06 03 2B 65 70
        ms.ToArray().Should().Equal(0x06, 0x03, 0x2B, 0x65, 0x70);
    }

    [Fact]
    public void Encode_FirstTwoArcs_PackedCorrectly()
    {
        // 1.2 → first byte = 40 * 1 + 2 = 42 = 0x2A
        ObjectIdentifier oid = new(1, 2);
        using MemoryStream ms = new();
        Asn1ObjectIdentifier.Write(ms, oid);
        ms.ToArray().Should().Equal(0x06, 0x01, 0x2A);
    }

    [Fact]
    public void Decode_Sha256_MatchesSpec()
    {
        byte[] bytes = [0x06, 0x09, 0x60, 0x86, 0x48, 0x01, 0x65, 0x03, 0x04, 0x02, 0x01];
        Asn1ObjectIdentifier.Read(bytes, 0, out ObjectIdentifier oid);
        oid.Dotted.Should().Be("2.16.840.1.101.3.4.2.1");
    }

    [Fact]
    public void RoundTrip_KnownOids()
    {
        string[] knownOids =
        {
            "0.0",
            "0.39",
            "1.0",
            "1.39",
            "2.0",
            "2.100",
            "1.2.840.113549.1.1.1",
            "2.16.840.1.101.3.4.2.1",
            "1.3.6.1.5.5.7.3.36",
            "2.5.4.3",
        };

        foreach (string dotted in knownOids)
        {
            ObjectIdentifier oid = new(dotted);
            using MemoryStream ms = new();
            Asn1ObjectIdentifier.Write(ms, oid);
            Asn1ObjectIdentifier.Read(ms.ToArray(), 0, out ObjectIdentifier decoded);
            decoded.Dotted.Should().Be(dotted);
        }
    }

    [Fact]
    public void Decode_LeadingZeroInSubId_Rejected()
    {
        // 0x80 alone in a SubIdentifier means leading zeros — forbidden.
        // 06 03 2A 80 03 — middle subId starts with 0x80 with continuation
        byte[] bytes = [0x06, 0x03, 0x2A, 0x80, 0x03];
        Action act = () => Asn1ObjectIdentifier.Read(bytes, 0, out _);
        act.Should().Throw<Asn1Exception>().WithMessage("*leading zero*");
    }

    [Fact]
    public void Decode_TruncatedSubId_Rejected()
    {
        // Continuation bit set with no following byte
        byte[] bytes = [0x06, 0x02, 0x2A, 0x81];
        Action act = () => Asn1ObjectIdentifier.Read(bytes, 0, out _);
        act.Should().Throw<Asn1Exception>().WithMessage("*Truncated*");
    }

    [Fact]
    public void Decode_EmptyContent_Rejected()
    {
        byte[] bytes = [0x06, 0x00];
        Action act = () => Asn1ObjectIdentifier.Read(bytes, 0, out _);
        act.Should().Throw<Asn1Exception>().WithMessage("*at least 1 byte*");
    }
}
