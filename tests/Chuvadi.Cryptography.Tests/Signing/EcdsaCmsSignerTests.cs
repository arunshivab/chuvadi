// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.2.2 — ISigner for ECDSA

using System;
using Chuvadi.Cryptography.Cms;
using Chuvadi.Cryptography.Hashing;
using Chuvadi.Cryptography.PublicKey;
using Chuvadi.Cryptography.Signing;
using Chuvadi.Cryptography.X509;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.Signing;

public sealed class EcdsaCmsSignerTests
{
    private const string Pkcs8HexP256 =
        "308187020100301306072a8648ce3d020106082a8648ce3d030107046d306b0201010420b6c4f05a" +
        "0020b445aa8a4020363abd2444435dbe310d7337f53387396aa6562ea14403420004ac08444b31e8" +
        "7ac1ada5d319383ee07950bfb05ae5eeb4dd9668f30913a3b8d500d1c6adec1786cf9a36186b898d" +
        "bc5f86696502c2614ee991c17bcd54b05e71";

    private const string CertHexP256 =
        "308201493081f0a003020102020101300a06082a8648ce3d040302301c311a301806035504030c11" +
        "4368757661646920546573742050323536301e170d3234303130313030303030305a170d33303031" +
        "30313030303030305a301c311a301806035504030c11436875766164692054657374205032353630" +
        "59301306072a8648ce3d020106082a8648ce3d03010703420004ac08444b31e87ac1ada5d319383e" +
        "e07950bfb05ae5eeb4dd9668f30913a3b8d500d1c6adec1786cf9a36186b898dbc5f86696502c261" +
        "4ee991c17bcd54b05e71a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101" +
        "ff0404030202c4300a06082a8648ce3d04030203480030450221008a7009174967c43635ca9b17bf" +
        "488abcbb73840f07d942511cf8c3bac25b4322022019ba1bf6db330414ae9d1a79cfa1826af28181" +
        "196d216bbf95a49545688a6e8f";

    private const string Pkcs8HexP384 =
        "3081b6020100301006072a8648ce3d020106052b8104002204819e30819b0201010430be6df9ea7d" +
        "1ff3d9e95dc321bc7b4a46dedd921640710ae1f0229c9f01d1b6401cc4b69ea216489d66f290e3c6" +
        "8bf082a1640362000484f14ecb44b9c7986785df4825d1c88b3689c9902792fecb5edf1f82fafd60" +
        "34db3e882ead0f9a25481ab9acf694f42698b9cd44bdb42540bea7908a089d3d046526ca7d0398b4" +
        "9a448dfdf19eb3254fe87c930252f9b9bb2721fb2a14dfc79e";

    private const string CertHexP384 =
        "308201863082010da003020102020101300a06082a8648ce3d040303301c311a301806035504030c" +
        "114368757661646920546573742050333834301e170d3234303130313030303030305a170d333030" +
        "3130313030303030305a301c311a301806035504030c114368757661646920546573742050333834" +
        "3076301006072a8648ce3d020106052b810400220362000484f14ecb44b9c7986785df4825d1c88b" +
        "3689c9902792fecb5edf1f82fafd6034db3e882ead0f9a25481ab9acf694f42698b9cd44bdb42540" +
        "bea7908a089d3d046526ca7d0398b49a448dfdf19eb3254fe87c930252f9b9bb2721fb2a14dfc79e" +
        "a3233021300f0603551d130101ff040530030101ff300e0603551d0f0101ff0404030202c4300a06" +
        "082a8648ce3d040303036700306402303e7e1591bcaf8bc697b9d2582b14cc58f7bc6cfafda7844f" +
        "c4bf391ac4aa7dee1e6c0f2a4ac9055b1126cbdfeaa7bd9e02307b600cf3af05fbd764054177407f" +
        "0b8db92c92bd9249beed2a5f279c7bb92471f69ed948e0144900e330462c6a1466cc";

    private const string Pkcs8HexP521 =
        "3081ee020100301006072a8648ce3d020106052b810400230481d63081d302010104420197c52201" +
        "f2859725482fa8a129eebd26bf4f34d5200d4b4f140b2d03513b180839ca8625a636cef38f56d337" +
        "dd23921f93c4d1be0ba68a578d9f233183c120910ea18189038186000400ef462594cfb952e263c8" +
        "e518bb6d1faefe665e9b012ac179b86d630bb00e3ae1e10a65dd193f826bf5621de126b227745c68" +
        "9344a5109e34c928899fc2bfc6974801377ef6e857044ff4e4b9ee2131a8a3a2009dcc7a590c8a94" +
        "12511664dd56d9d264ede65e4cc48c87b4f440e24718e59ed1bd2a42fbc3192c7b806654e81601a2" +
        "12";

    private const string CertHexP521 =
        "308201d230820133a003020102020101300a06082a8648ce3d040304301c311a301806035504030c" +
        "114368757661646920546573742050353231301e170d3234303130313030303030305a170d333030" +
        "3130313030303030305a301c311a301806035504030c114368757661646920546573742050353231" +
        "30819b301006072a8648ce3d020106052b81040023038186000400ef462594cfb952e263c8e518bb" +
        "6d1faefe665e9b012ac179b86d630bb00e3ae1e10a65dd193f826bf5621de126b227745c689344a5" +
        "109e34c928899fc2bfc6974801377ef6e857044ff4e4b9ee2131a8a3a2009dcc7a590c8a94125116" +
        "64dd56d9d264ede65e4cc48c87b4f440e24718e59ed1bd2a42fbc3192c7b806654e81601a212a323" +
        "3021300f0603551d130101ff040530030101ff300e0603551d0f0101ff0404030202c4300a06082a" +
        "8648ce3d04030403818c00308188024200dfc3a81a37210e79e70a0be0a3903e8c931402ae702d21" +
        "1411cb15e31ff3a5fbf947ffc782153f485d1b9ab73740bc405bb7d965e9c6e30d272a278d475951" +
        "4f0d0242019fa17510a14f84efdf248df6ecd5cb97197fa2b02c989595107ff024dcc0cbc4b8a4fd" +
        "729982cecf32c4f3f958cd19443c940fa4543b5be26a481bcfec71f4ef3f";

    public static TheoryData<string, string, HashAlgorithmName, string> Cases =>
        new()
        {
            { Pkcs8HexP256, CertHexP256, HashAlgorithmName.Sha256, "1.2.840.10045.4.3.2" },
            { Pkcs8HexP384, CertHexP384, HashAlgorithmName.Sha384, "1.2.840.10045.4.3.3" },
            { Pkcs8HexP521, CertHexP521, HashAlgorithmName.Sha512, "1.2.840.10045.4.3.4" },
        };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Sign_BuildsACmsThatVerifies(string pkcs8Hex, string certHex,
        HashAlgorithmName hashAlg, string expectedSigOid)
    {
        EcdsaPrivateKey priv = EcdsaPrivateKey.FromPkcs8(Convert.FromHexString(pkcs8Hex));
        X509Certificate cert = X509Certificate.Decode(Convert.FromHexString(certHex));
        ISigner signer = new EcdsaCmsSigner(priv, cert, hashAlg);

        signer.SignatureAlgorithm.Algorithm.Dotted.Should().Be(expectedSigOid);

        byte[] cms = CmsSignedDataBuilder.BuildDetached(
            "data"u8.ToArray(), signer,
            signingTime: new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero));

        SignedData sd = CmsDecoder.DecodeSignedData(cms);
        sd.SignerInfos.Should().HaveCount(1);
        SignerInfo si = sd.SignerInfos[0];
        si.SignatureAlgorithm.Algorithm.Dotted.Should().Be(expectedSigOid);

        // The signed-attributes signature verifies against the cert's public key.
        EcdsaPublicKey pub = EcdsaPublicKey.FromSubjectPublicKeyInfo(cert.Tbs.SubjectPublicKeyInfo);
        byte[] toVerify = si.SignedAttributes!.DerEncodedForVerification;
        IHashAlgorithm h = HashFactory.Create(hashAlg);
        h.Update(toVerify);
        byte[] digest = new byte[h.DigestSize];
        h.Finish(digest);
        SignatureVerifier.Verify(si.SignatureAlgorithm, pub, digest, si.Signature)
            .Should().BeTrue();
    }

    [Fact]
    public void Constructor_NullPrivateKey_Throws()
    {
        X509Certificate cert = X509Certificate.Decode(Convert.FromHexString(CertHexP256));
        Action act = () => new EcdsaCmsSigner(null!, cert, HashAlgorithmName.Sha256);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullCertificate_Throws()
    {
        EcdsaPrivateKey priv = EcdsaPrivateKey.FromPkcs8(Convert.FromHexString(Pkcs8HexP256));
        Action act = () => new EcdsaCmsSigner(priv, null!, HashAlgorithmName.Sha256);
        act.Should().Throw<ArgumentNullException>();
    }
}
