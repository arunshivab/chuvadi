// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Tests for CertificateList (CRL parsing)
//
// Fixture: real CRL produced by Python cryptography library (OpenSSL) with three
// revoked serials (100=KeyCompromise, 200=Superseded+InvalidityDate, 300=no reason).

using System;
using System.Numerics;
using Chuvadi.Cryptography.Revocation;
using Chuvadi.Cryptography.X509;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.Revocation;

public sealed class CertificateListTests
{
    private const string CrlDerHex =
        "308201ed3081d6020101300d06092a864886f70d01010b0500301e311c301a06035504030c134368" +
        "75766164692043524c2054657374204341170d3235303530313030303030305a170d323530383031" +
        "3030303030305a30743020020164170d3235303431353030303030305a300c300a0603551d150403" +
        "0a0101303b020200c8170d3235303432303030303030305a3026300a0603551d1504030a01043018" +
        "0603551d180411180f32303235303431303030303030305a30130202012c170d3235303432353030" +
        "303030305aa00e300c300a0603551d14040302012a300d06092a864886f70d01010b050003820101" +
        "008ff800f413032c388d60f3bc790a4e15eee173b46dc74b362a3984d73cdc855b3dfc7f0c99acc2" +
        "ebfec51f4a94909c1c25d2ea4588b0b6a8ba17de0571890f90724dc6acd6716d0a68b75e90b87b1a" +
        "5bb45de2b214016c87d6306b6b89fcb6392c2630871ac6720d5a25671f35b481d0e01554a914ebcd" +
        "486afca99e81f6d083bf9ac4469f84796bfee422925d07a092bfa1a135a0e6e2461a93d0636bced8" +
        "f3d5de082df0168872474f630730254be2fb50ab85ca459943b4cc3ff834f5493fec5673e96f4d83" +
        "e316e4f7472b80cbe85a33b98ee59fec24929e5b97afbebd3c14702a4c680fdff838c562bc2d4760" +
        "96b435a9d4322c923aa0ec2a2fd4815e6f";

    private const string CaCertDerHex =
        "308202da308201c2a003020102020101300d06092a864886f70d01010b0500301e311c301a060355" +
        "04030c13436875766164692043524c2054657374204341301e170d3230303130313030303030305a" +
        "170d3430303130313030303030305a301e311c301a06035504030c13436875766164692043524c20" +
        "5465737420434130820122300d06092a864886f70d01010105000382010f003082010a0282010100" +
        "9f6ed2974ad6059f04ff334ff3752c9e1749e9fd58d26b91a2ff528e74a1baf628190a0516b959e0" +
        "2602f25a98139623ee0599521bb3de8a689ec850ab26262dc0131a5d4376f35d67c07fc79fac59ff" +
        "008ec718ab0bac601613b80e48a915bbd8104ae40dbadc671fd20c05747a07969b8501924fad079d" +
        "61c196e9f06709742eb88f305fb586953d527f575c093ba1326acca6edb9af0ee3857521521fd444" +
        "44ff8219399530e2a5d7ac0bd1654df11fb198b6e1ba687eec83a95925e9af0986cfa7d2e601b21a" +
        "b9ca763fa28a4a116dbfcee12cf473949f3e073ffd1af63610972f41465acf6365ab83740320899f" +
        "75eb16ab97e57480839387602e249b970203010001a3233021300f0603551d130101ff0405300301" +
        "01ff300e0603551d0f0101ff040403020106300d06092a864886f70d01010b050003820101003a56" +
        "816674d08f1cb0d117376a01ed56267cd69459f40bd5e4a24094a128cfc353493c869c7e38dd129f" +
        "fe399b3192dd2e79e6fefb05f5939aa1ad36e4aa29c2753216b1e738593c86397f77a134f8b7b5ca" +
        "483608154fc354ac8ff701534959ea3607d8518ba0e52d8ee1a4cc55b2749b2a122cbc5f5c4e4b46" +
        "207dfab2e86d17f796a7002a40a34968405b030a78a812dcbb3920f0947415aea83e95c725f3f24a" +
        "3e174a6b5b58a3d9d47c626673d07a0b5a20163f22f1846c8804696cb61089caf97b9a1360976148" +
        "625314a7c1cb7348fc66cb03d9fe39baf0cb8d7ceff28a99f673b36cc0d418b770fbf9a7ef84141f" +
        "2bcf9ed2d5050cd1681d4a80314f";

    private static CertificateList LoadCrl()
        => CertificateList.Decode(Convert.FromHexString(CrlDerHex));

    private static X509Certificate LoadCaCert()
        => X509Certificate.Decode(Convert.FromHexString(CaCertDerHex));

    [Fact]
    public void Decode_RealCrl_Succeeds()
    {
        CertificateList crl = LoadCrl();
        crl.Should().NotBeNull();
    }

    [Fact]
    public void Decode_ExposesCrlNumber()
    {
        CertificateList crl = LoadCrl();
        crl.CrlNumber.Should().Be(new BigInteger(42));
    }

    [Fact]
    public void Decode_ExposesIssuerCommonName()
    {
        CertificateList crl = LoadCrl();
        crl.Issuer.CommonName.Should().Be("Chuvadi CRL Test CA");
    }

    [Fact]
    public void Decode_RevokedListHasThreeEntries()
    {
        CertificateList crl = LoadCrl();
        crl.RevokedCertificates.Should().HaveCount(3);
    }

    [Fact]
    public void IsRevoked_KnownRevokedSerials_ReturnsTrue()
    {
        CertificateList crl = LoadCrl();
        crl.IsRevoked(new BigInteger(100)).Should().BeTrue();
        crl.IsRevoked(new BigInteger(200)).Should().BeTrue();
        crl.IsRevoked(new BigInteger(300)).Should().BeTrue();
    }

    [Fact]
    public void IsRevoked_UnknownSerial_ReturnsFalse()
    {
        CertificateList crl = LoadCrl();
        crl.IsRevoked(new BigInteger(999)).Should().BeFalse();
    }

    [Fact]
    public void FindRevocation_KeyCompromise_HasCorrectReason()
    {
        CertificateList crl = LoadCrl();
        RevokedCertificate? r = crl.FindRevocation(new BigInteger(100));
        r.Should().NotBeNull();
        r!.Reason.Should().Be(CrlReason.KeyCompromise);
    }

    [Fact]
    public void FindRevocation_SupersededWithInvalidityDate_PopulatesBoth()
    {
        CertificateList crl = LoadCrl();
        RevokedCertificate? r = crl.FindRevocation(new BigInteger(200));
        r.Should().NotBeNull();
        r!.Reason.Should().Be(CrlReason.Superseded);
        r.InvalidityDate.Should().NotBeNull();
    }

    [Fact]
    public void FindRevocation_NoReasonExtension_DefaultsToUnspecified()
    {
        CertificateList crl = LoadCrl();
        RevokedCertificate? r = crl.FindRevocation(new BigInteger(300));
        r.Should().NotBeNull();
        r!.Reason.Should().Be(CrlReason.Unspecified);
        r.InvalidityDate.Should().BeNull();
    }

    [Fact]
    public void SignatureVerifier_AgainstCorrectCa_ReturnsTrue()
    {
        CertificateList crl = LoadCrl();
        X509Certificate ca = LoadCaCert();
        bool ok = CertificateListSignatureVerifier.Verify(crl, ca.Tbs.SubjectPublicKeyInfo);
        ok.Should().BeTrue();
    }

    [Fact]
    public void Version_OfV2Crl_Returns2()
    {
        CertificateList crl = LoadCrl();
        crl.Version.Should().Be(2);
    }

    [Fact]
    public void ThisUpdate_BeforeNextUpdate()
    {
        CertificateList crl = LoadCrl();
        crl.NextUpdate.Should().NotBeNull();
        crl.ThisUpdate.Should().BeBefore(crl.NextUpdate!.Value);
    }
}
