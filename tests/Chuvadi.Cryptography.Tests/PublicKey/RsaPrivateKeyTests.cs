// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.2.0 — RSA private key parsing

using System;
using System.Numerics;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.PublicKey;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.PublicKey;

public sealed class RsaPrivateKeyTests
{
    private const string Pkcs8Hex =
        "308204bc020100300d06092a864886f70d0101010500048204a6308204a2020100028201010087f9" +
        "dc8c618bfa19cca7f1426294bc3036f71e3233bc11e215ca44261e75be1b6fba5bf8dd2861d460bf" +
        "366dfbc52188aac4533886e636ce6b6d73827a8acdb18579f5d9d2c4dbce4506d211adbe0676e1a4" +
        "7a553181852c5b9588e6513c861041fecd797324eba100ba8d4ca865d040032c8d496c2d5675acb4" +
        "ea44397364ceab7c242a43442f212edf63607bd83b21b1bbd8c7d8cb567454186e0430117cc821ee" +
        "441f1e65a13cff4b34ef1beeb6e273fdf2aa11fd01b50ffe3c0aba2d0b49b0aa087f1f192fa6fdb6" +
        "0914640c79eaef870cfb31f82a424134562ea86dde17ef90433b645423b16ab77f03a65c51edaef3" +
        "6d6cf92ec67da1e7bf0d1f69b403020301000102820100014ec7942bb2d24a2efed09c6bf3d29965" +
        "493a50f0eec976e3d2e0779e174281eb0b1b60a167e104c02b187ef93be7f16c037dd385e09c5762" +
        "d2a3a7d07a034502af8fa6645b119f0d95d6715ed929a8485544cd563ed859afa08202d8581b6f61" +
        "6aa2939d171ddeab4c3e2078e7c8d3b5b7de9e94364ab51bd758693426d54f8687231834475df16f" +
        "1d75cd01b94ad43d9d1165945bd97c050100bd2284e1a541e25466d4fd1c24f5dd0119a6d8251236" +
        "a712bfe6d22f52d9c9837989cf55293222dfcddaf6c0d1489ceff11ba6022306db8be477d544d465" +
        "9d1de1e295ae40c942420f1dc602385cbc6368156166101f2932ef8bd228b0548d6fcc181a433102" +
        "818100baf4b4b0bda832be805c298c09524e9bc4843733938212fd0339040ee7c101299df02596f3" +
        "ed3dc507590a3815596139e52ee856a01110e38d40680f64b7f57f66eadc0c1fc202ed66da8d1c09" +
        "349ab5fda2ed36a60f27449b41ebb2796b1f5e0c35291f02b958f59cdd509743acca688377ae00c8" +
        "d12fbbf7f6e96f40bf474b02818100ba31632cd854570a35f9554253d1fc56896a659c598a12e649" +
        "2ba331d0a251218fcbafeb4d2928429d7b76afe1776ebe19d6797b0451a92711df503edb108960f8" +
        "d842a29085bba1611b5627aefd9090988ac662024e175ce4497175f30716690baacb91eccb828067" +
        "3816f610846a7296326eb969097f7a76c9a1203d6b3b29028180240494f31ff6d19fe5f6db824121" +
        "7c47823abeafcf11563b2c6dc59c6185cb858b0a0313ebf69ed9e0aa84cf7d3d74ffc4699f15614d" +
        "2865ce86c405832ff5ba0fc7db90f2655c4f046bc297ce2636502d8740103139e624cf8c89ae1eba" +
        "4696c5df5006cb6d8df7f3baa7961cd1a345335ed145116b4bb8f8f2b6d25a34ffb50281802d8c27" +
        "56b114f0b5eebe2fbd2a041456970710144f53178c7e19ceb240f7742161abd23d1d8313f637d01c" +
        "18aa6f2d08140a036db480c580693ed7c288946306f5d8f1741326a3dfb6819971dbbcebc073907f" +
        "373a991fddf97a7de6fcac9f2ed34bd4c0bdcc8b001e3ffa5df76c6d1ddae03b75dfdc12f3425bd0" +
        "aeb257a9010281800cead20ba7ee76f07cbaf51de3f5dd59a9cbcb73e3ba8416677527dd674367af" +
        "c8a4ca3774090083fd50dd045e88f6eca7be75a7c9695eab1584685b84126f59b7d0baa21e78e672" +
        "29cdf1c5d4699846f1211484e2e516ac6741133f60c515cc91c59c25f8d6b37844b706000ff9d962" +
        "6548fb4b0e9967cc454a2808e2cf6068";

    [Fact]
    public void FromPkcs8_DecodesA2048BitKey()
    {
        RsaPrivateKey key = RsaPrivateKey.FromPkcs8(Convert.FromHexString(Pkcs8Hex));
        key.Modulus.GetBitLength().Should().Be(2048);
        key.ModulusSizeBytes.Should().Be(256);
    }

    [Fact]
    public void FromPkcs8_PublicExponentIs65537()
    {
        RsaPrivateKey key = RsaPrivateKey.FromPkcs8(Convert.FromHexString(Pkcs8Hex));
        key.PublicExponent.Should().Be(new BigInteger(65537));
    }

    [Fact]
    public void FromPkcs8_PrivateExponentIsNonZero()
    {
        RsaPrivateKey key = RsaPrivateKey.FromPkcs8(Convert.FromHexString(Pkcs8Hex));
        key.PrivateExponent.Sign.Should().Be(1);
        key.PrivateExponent.GetBitLength().Should().BeGreaterThan(2000);
    }

    [Fact]
    public void FromPkcs8_PublicKeyMatches()
    {
        RsaPrivateKey priv = RsaPrivateKey.FromPkcs8(Convert.FromHexString(Pkcs8Hex));
        RsaPublicKey pub = priv.PublicKey;
        pub.Modulus.Should().Be(priv.Modulus);
        pub.PublicExponent.Should().Be(priv.PublicExponent);
    }

    [Fact]
    public void FromPkcs8_NullInput_Throws()
    {
        Action act = () => RsaPrivateKey.FromPkcs8(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FromPkcs8_NotAnRsaKey_Throws()
    {
        // ECDSA P-256 in PKCS#8 — should refuse with a clear error
        byte[] ecPkcs8 = Convert.FromHexString(
            "308187020100301306072A8648CE3D020106082A8648CE3D030107046D306B0201010420"
            + "C9AFA9D845BA75166B5C215767B1D6934E50C3DB36E89B127B8A622B120F6721A14403420004"
            + "60FED4BA255A9D31C9612BE91B935166C1F4F4C3DA2B6F0D9F69D3D72E7D7AE3C9F4D4A2D"
            + "B2A1F8AA75C13ED2F5E5A85A6F4D3D3C3F6E7F3A6C5C7B8E4F1A2B3C4D5E6F7A8");
        Action act = () => RsaPrivateKey.FromPkcs8(ecPkcs8);
        act.Should().Throw<Asn1Exception>();
    }
}
