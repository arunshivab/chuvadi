// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.2.6 — RFC 6979 deterministic ECDSA

using System;
using Chuvadi.Cryptography.Hashing;
using Chuvadi.Cryptography.PublicKey;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.Signing;

public sealed class Rfc6979Tests
{
    [Fact]
    public void SignDeterministic_ProducesIdenticalSignaturesForSameInputs()
    {
        EcdsaPrivateKey priv = MakeP256Key();
        byte[] digest = new byte[32];
        for (int i = 0; i < 32; i++) { digest[i] = (byte)i; }

        byte[] sig1 = EcdsaSigner.SignDeterministic(priv, digest, HashAlgorithmName.Sha256);
        byte[] sig2 = EcdsaSigner.SignDeterministic(priv, digest, HashAlgorithmName.Sha256);
        byte[] sig3 = EcdsaSigner.SignDeterministic(priv, digest, HashAlgorithmName.Sha256);

        Convert.ToHexString(sig1).Should().Be(Convert.ToHexString(sig2));
        Convert.ToHexString(sig1).Should().Be(Convert.ToHexString(sig3));
    }

    [Fact]
    public void SignDeterministic_DifferentDigests_ProduceDifferentSignatures()
    {
        EcdsaPrivateKey priv = MakeP256Key();
        byte[] d1 = new byte[32];
        byte[] d2 = new byte[32];
        d2[0] = 1;

        byte[] s1 = EcdsaSigner.SignDeterministic(priv, d1, HashAlgorithmName.Sha256);
        byte[] s2 = EcdsaSigner.SignDeterministic(priv, d2, HashAlgorithmName.Sha256);

        Convert.ToHexString(s1).Should().NotBe(Convert.ToHexString(s2));
    }

    [Fact]
    public void SignDeterministic_VerifiesAgainstSameKey()
    {
        EcdsaPrivateKey priv = MakeP256Key();
        byte[] digest = new byte[32];
        for (int i = 0; i < 32; i++) { digest[i] = (byte)(i + 1); }

        byte[] sig = EcdsaSigner.SignDeterministic(priv, digest, HashAlgorithmName.Sha256);

        EcdsaPublicKey pub = new(priv.PublicKey.PublicPoint);
        EcdsaVerifier.Verify(pub, digest, sig).Should().BeTrue();
    }

    private static EcdsaPrivateKey MakeP256Key()
    {
        // Use a fixed P-256 key. The bytes are 32 octets representing d.
        byte[] d = new byte[32];
        for (int i = 0; i < 32; i++) { d[i] = (byte)(i + 1); }
        System.Numerics.BigInteger dInt = new(d, isUnsigned: true, isBigEndian: true);
        return new EcdsaPrivateKey(EcCurve.P256, dInt);
    }
}
