// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.2.3 — TSA request encoding

using System;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Hashing;
using Chuvadi.Cryptography.Timestamps;
using Chuvadi.Cryptography.X509;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.Timestamps;

public sealed class TimeStampRequestTests
{
    [Fact]
    public void ForData_HashesAndProducesAValidStructure()
    {
        byte[] data = "hello world"u8.ToArray();
        TimeStampRequest req = TimeStampRequest.ForData(data, HashAlgorithmName.Sha256);

        // The encoded request must parse as a SEQUENCE starting with INTEGER v1
        byte[] encoded = req.Encode();
        encoded[0].Should().Be(0x30, "encoded request is a SEQUENCE");

        Asn1Reader root = new(encoded);
        Asn1Reader seq = root.ReadSequence();
        seq.ReadInteger().Should().Be(1, "version is v1");
    }

    [Fact]
    public void ForData_GeneratesANonZeroNonce()
    {
        TimeStampRequest req = TimeStampRequest.ForData(
            "x"u8.ToArray(), HashAlgorithmName.Sha256);
        req.Nonce.Should().NotBeNull();
        req.Nonce!.Value.Sign.Should().Be(1);
    }

    [Fact]
    public void ForData_TwoCallsProduceDifferentNonces()
    {
        TimeStampRequest a = TimeStampRequest.ForData("x"u8.ToArray(), HashAlgorithmName.Sha256);
        TimeStampRequest b = TimeStampRequest.ForData("x"u8.ToArray(), HashAlgorithmName.Sha256);
        a.Nonce.Should().NotBe(b.Nonce);
    }

    [Fact]
    public void CertReq_TrueEmitsBooleanInEncoding()
    {
        // Build a request with certReq=true; the BOOLEAN should appear in the DER.
        byte[] digest = new byte[32];
        TimeStampRequest req = new(
            new MessageImprint(new AlgorithmIdentifier(
                Chuvadi.Cryptography.Oids.KnownOids.Sha256, null), digest),
            nonce: System.Numerics.BigInteger.One,
            certReq: true);
        byte[] encoded = req.Encode();
        // The BOOLEAN TRUE encoding is 01 01 FF — scan for it.
        bool found = false;
        for (int i = 0; i < encoded.Length - 2; i++)
        {
            if (encoded[i] == 0x01 && encoded[i + 1] == 0x01 && encoded[i + 2] == 0xFF)
            {
                found = true; break;
            }
        }
        found.Should().BeTrue("certReq=true should appear as BOOLEAN TRUE in the DER");
    }

    [Fact]
    public void CertReq_FalseOmitsBooleanInEncoding()
    {
        byte[] digest = new byte[32];
        TimeStampRequest req = new(
            new MessageImprint(new AlgorithmIdentifier(
                Chuvadi.Cryptography.Oids.KnownOids.Sha256, null), digest),
            nonce: System.Numerics.BigInteger.One,
            certReq: false);
        byte[] encoded = req.Encode();
        // No BOOLEAN TRUE encoding (DER omits DEFAULT values).
        bool found = false;
        for (int i = 0; i < encoded.Length - 2; i++)
        {
            if (encoded[i] == 0x01 && encoded[i + 1] == 0x01 && encoded[i + 2] == 0xFF)
            {
                found = true; break;
            }
        }
        found.Should().BeFalse();
    }

    [Fact]
    public void Constructor_NullMessageImprint_Throws()
    {
        Action act = () => new TimeStampRequest(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ForData_UnsupportedHashAlgorithm_Throws()
    {
        Action act = () => TimeStampRequest.ForData(
            "x"u8.ToArray(), (HashAlgorithmName)9999);
        act.Should().Throw<ArgumentException>();
    }
}
