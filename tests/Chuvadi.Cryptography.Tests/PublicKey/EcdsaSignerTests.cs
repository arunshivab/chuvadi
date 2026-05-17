// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.2.2 — ECDSA signing primitive
//
// Round-trip with the existing EcdsaVerifier. Random nonces mean two
// signings of the same message produce different signatures; both must
// verify.

using System;
using Chuvadi.Cryptography.Hashing;
using Chuvadi.Cryptography.PublicKey;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.PublicKey;

public sealed class EcdsaSignerTests
{
    private const string Pkcs8HexP256 =
        "308187020100301306072a8648ce3d020106082a8648ce3d030107046d306b0201010420b6c4f05a" +
        "0020b445aa8a4020363abd2444435dbe310d7337f53387396aa6562ea14403420004ac08444b31e8" +
        "7ac1ada5d319383ee07950bfb05ae5eeb4dd9668f30913a3b8d500d1c6adec1786cf9a36186b898d" +
        "bc5f86696502c2614ee991c17bcd54b05e71";

    private const string Pkcs8HexP384 =
        "3081b6020100301006072a8648ce3d020106052b8104002204819e30819b0201010430be6df9ea7d" +
        "1ff3d9e95dc321bc7b4a46dedd921640710ae1f0229c9f01d1b6401cc4b69ea216489d66f290e3c6" +
        "8bf082a1640362000484f14ecb44b9c7986785df4825d1c88b3689c9902792fecb5edf1f82fafd60" +
        "34db3e882ead0f9a25481ab9acf694f42698b9cd44bdb42540bea7908a089d3d046526ca7d0398b4" +
        "9a448dfdf19eb3254fe87c930252f9b9bb2721fb2a14dfc79e";

    private const string Pkcs8HexP521 =
        "3081ee020100301006072a8648ce3d020106052b810400230481d63081d302010104420197c52201" +
        "f2859725482fa8a129eebd26bf4f34d5200d4b4f140b2d03513b180839ca8625a636cef38f56d337" +
        "dd23921f93c4d1be0ba68a578d9f233183c120910ea18189038186000400ef462594cfb952e263c8" +
        "e518bb6d1faefe665e9b012ac179b86d630bb00e3ae1e10a65dd193f826bf5621de126b227745c68" +
        "9344a5109e34c928899fc2bfc6974801377ef6e857044ff4e4b9ee2131a8a3a2009dcc7a590c8a94" +
        "12511664dd56d9d264ede65e4cc48c87b4f440e24718e59ed1bd2a42fbc3192c7b806654e81601a2" +
        "12";

    public static TheoryData<string, HashAlgorithmName> CurveAndHashCases =>
        new()
        {
            { Pkcs8HexP256, HashAlgorithmName.Sha256 },
            { Pkcs8HexP384, HashAlgorithmName.Sha384 },
            { Pkcs8HexP521, HashAlgorithmName.Sha512 },
        };

    [Theory]
    [MemberData(nameof(CurveAndHashCases))]
    public void Sign_RoundTripsThroughEcdsaVerifier(string pkcs8Hex, HashAlgorithmName hashAlg)
    {
        EcdsaPrivateKey priv = EcdsaPrivateKey.FromPkcs8(Convert.FromHexString(pkcs8Hex));
        byte[] message = "round trip message"u8.ToArray();

        IHashAlgorithm h = HashFactory.Create(hashAlg);
        h.Update(message);
        byte[] digest = new byte[h.DigestSize];
        h.Finish(digest);

        byte[] sig = EcdsaSigner.Sign(priv, digest);
        EcdsaVerifier.Verify(priv.PublicKey, digest, sig).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(CurveAndHashCases))]
    public void Sign_ProducesAStandardDerSequence(string pkcs8Hex, HashAlgorithmName hashAlg)
    {
        EcdsaPrivateKey priv = EcdsaPrivateKey.FromPkcs8(Convert.FromHexString(pkcs8Hex));
        IHashAlgorithm h = HashFactory.Create(hashAlg);
        h.Update("x"u8);
        byte[] digest = new byte[h.DigestSize];
        h.Finish(digest);

        byte[] sig = EcdsaSigner.Sign(priv, digest);
        // SEQUENCE tag
        sig[0].Should().Be(0x30);
        // EcdsaVerifier.DecodeSignature parses two INTEGERs out of the SEQUENCE
        (System.Numerics.BigInteger r, System.Numerics.BigInteger s) =
            EcdsaVerifier.DecodeSignature(sig);
        r.Sign.Should().Be(1);
        s.Sign.Should().Be(1);
        r.Should().BeLessThan(priv.Curve.N);
        s.Should().BeLessThan(priv.Curve.N);
    }

    [Theory]
    [MemberData(nameof(CurveAndHashCases))]
    public void Sign_TwoCallsProduceDifferentSignaturesBothVerify(string pkcs8Hex, HashAlgorithmName hashAlg)
    {
        EcdsaPrivateKey priv = EcdsaPrivateKey.FromPkcs8(Convert.FromHexString(pkcs8Hex));
        IHashAlgorithm h = HashFactory.Create(hashAlg);
        h.Update("same message"u8);
        byte[] digest = new byte[h.DigestSize];
        h.Finish(digest);

        byte[] s1 = EcdsaSigner.Sign(priv, digest);
        byte[] s2 = EcdsaSigner.Sign(priv, digest);

        // Random k → near-certain to differ.
        s1.Should().NotEqual(s2);
        EcdsaVerifier.Verify(priv.PublicKey, digest, s1).Should().BeTrue();
        EcdsaVerifier.Verify(priv.PublicKey, digest, s2).Should().BeTrue();
    }

    [Fact]
    public void Sign_NullPrivateKey_Throws()
    {
        Action act = () => EcdsaSigner.Sign(null!, Array.Empty<byte>());
        act.Should().Throw<ArgumentNullException>();
    }
}
