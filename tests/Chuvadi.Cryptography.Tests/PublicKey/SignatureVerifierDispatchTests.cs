// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Tests for SignatureVerifier dispatcher

using System;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Oids;
using Chuvadi.Cryptography.PublicKey;
using Chuvadi.Cryptography.X509;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.PublicKey;

public sealed class SignatureVerifierDispatchTests
{
    [Fact]
    public void Verify_UnknownAlgorithm_ThrowsNotSupported()
    {
        var alg = new AlgorithmIdentifier(new ObjectIdentifier("1.2.3.4.5"), null);
        var key = new RsaPublicKey(123, 65537);
        Action act = () => SignatureVerifier.Verify(alg, key, new byte[32], new byte[32]);
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Verify_RsaAlgorithmWithEcdsaKey_Throws()
    {
        // Sha256WithRsa expects an RsaPublicKey
        var alg = new AlgorithmIdentifier(KnownOids.Sha256WithRsa, null);
        var ecKey = new EcdsaPublicKey(EcPoint.Generator(EcCurve.P256));
        Action act = () => SignatureVerifier.Verify(alg, ecKey, new byte[32], new byte[256]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Verify_PssWithoutParameters_ThrowsNotSupported()
    {
        // PSS without parameters would default to SHA-1, which we refuse
        var alg = new AlgorithmIdentifier(KnownOids.RsaSsaPss, null);
        var key = new RsaPublicKey(new System.Numerics.BigInteger(123), 65537);
        Action act = () => SignatureVerifier.Verify(alg, key, new byte[20], new byte[256]);
        act.Should().Throw<NotSupportedException>();
    }
}
