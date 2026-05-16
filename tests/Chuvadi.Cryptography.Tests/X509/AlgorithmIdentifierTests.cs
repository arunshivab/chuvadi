// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Tests for AlgorithmIdentifier

using System;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Oids;
using Chuvadi.Cryptography.X509;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.X509;

public sealed class AlgorithmIdentifierTests
{
    [Fact]
    public void Read_RsaWithNullParameters()
    {
        // SEQUENCE { OID 1.2.840.113549.1.1.11 (sha256WithRSAEncryption), NULL }
        Asn1Writer w = new();
        w.PushSequence();
        w.WriteObjectIdentifier(KnownOids.Sha256WithRsa);
        w.WriteNull();
        w.PopSequence();

        Asn1Reader r = new(w.ToArray());
        AlgorithmIdentifier alg = AlgorithmIdentifier.Read(r);

        alg.Algorithm.Should().Be(KnownOids.Sha256WithRsa);
        alg.ParametersAreNull.Should().BeTrue();
        alg.ParametersAreAbsent.Should().BeFalse();
    }

    [Fact]
    public void Read_EcdsaWithAbsentParameters()
    {
        Asn1Writer w = new();
        w.PushSequence();
        w.WriteObjectIdentifier(KnownOids.Sha256WithEcdsa);
        w.PopSequence();

        Asn1Reader r = new(w.ToArray());
        AlgorithmIdentifier alg = AlgorithmIdentifier.Read(r);

        alg.Algorithm.Should().Be(KnownOids.Sha256WithEcdsa);
        alg.ParametersAreAbsent.Should().BeTrue();
        alg.ParametersAreNull.Should().BeFalse();
    }

    [Fact]
    public void Equals_SameAlgorithmAndParameters_AreEqual()
    {
        AlgorithmIdentifier a = new(KnownOids.Sha256WithRsa, new byte[] { 0x05, 0x00 });
        AlgorithmIdentifier b = new(KnownOids.Sha256WithRsa, new byte[] { 0x05, 0x00 });
        a.Should().Be(b);
    }

    [Fact]
    public void Equals_DifferentParameters_AreNotEqual()
    {
        AlgorithmIdentifier a = new(KnownOids.Sha256WithRsa, new byte[] { 0x05, 0x00 });
        AlgorithmIdentifier b = new(KnownOids.Sha256WithRsa, Array.Empty<byte>());
        a.Should().NotBe(b);
    }
}
