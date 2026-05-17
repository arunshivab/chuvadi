// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.2.2 — ECDSA private key parsing

using System;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.PublicKey;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.PublicKey;

public sealed class EcdsaPrivateKeyTests
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

    [Theory]
    [InlineData(Pkcs8HexP256, "P-256", 256)]
    [InlineData(Pkcs8HexP384, "P-384", 384)]
    [InlineData(Pkcs8HexP521, "P-521", 521)]
    public void FromPkcs8_RecognisesCurve(string pkcs8Hex, string curveName, int expectedScalarBits)
    {
        EcdsaPrivateKey key = EcdsaPrivateKey.FromPkcs8(Convert.FromHexString(pkcs8Hex));
        key.Curve.Name.Should().Be(curveName);
        // The scalar should fit within (and usually fill) the curve's bit length.
        key.D.GetBitLength().Should().BeLessOrEqualTo(expectedScalarBits);
        key.D.GetBitLength().Should().BeGreaterThan(expectedScalarBits - 8);
    }

    [Theory]
    [InlineData(Pkcs8HexP256)]
    [InlineData(Pkcs8HexP384)]
    [InlineData(Pkcs8HexP521)]
    public void FromPkcs8_PublicKeyDerivationLandsOnTheCurve(string pkcs8Hex)
    {
        EcdsaPrivateKey priv = EcdsaPrivateKey.FromPkcs8(Convert.FromHexString(pkcs8Hex));
        EcdsaPublicKey pub = priv.PublicKey;
        priv.Curve.IsOnCurve(pub.PublicPoint.X, pub.PublicPoint.Y).Should().BeTrue();
    }

    [Fact]
    public void FromPkcs8_NullInput_Throws()
    {
        Action act = () => EcdsaPrivateKey.FromPkcs8(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ScalarOutOfRange_Throws()
    {
        EcdsaPrivateKey priv = EcdsaPrivateKey.FromPkcs8(Convert.FromHexString(Pkcs8HexP256));
        Action negative = () => new EcdsaPrivateKey(priv.Curve, System.Numerics.BigInteger.MinusOne);
        negative.Should().Throw<ArgumentException>();
        Action zero = () => new EcdsaPrivateKey(priv.Curve, System.Numerics.BigInteger.Zero);
        zero.Should().Throw<ArgumentException>();
    }
}
