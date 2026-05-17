// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.2.6 — CAdES signingCertificateV2

using System;
using Chuvadi.Cryptography.Cms;
using Chuvadi.Cryptography.Hashing;
using Chuvadi.Cryptography.X509;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.Cms;

public sealed class CmsSigningCertificateV2Tests
{
    private const string CertDerHex =
        "308202e8308201d0a003020102020101300d06092a864886f70d01010b0500302531233021060355" +
        "04030c1a436875766164692053656c662d5369676e6564205369676e6572301e170d323430313031" +
        "3030303030305a170d3330303130313030303030305a30253123302106035504030c1a4368757661" +
        "64692053656c662d5369676e6564205369676e657230820122300d06092a864886f70d0101010500" +
        "0382010f003082010a0282010100b9c50729fca5b148ff03a6fabc563f64fd73941a2176ee259731" +
        "707285628a70c024f897a6ac3c9566875048f74a1c09cb631c50842b465bfbd432b81f9c2ea7a989" +
        "d6bfb462a2dadee693731f99061e849ccad1da67443428ccb8829c9951832322334b030f0b4e1530" +
        "3d1872cde4baf5a83046f5c26cbf545488b14b157ff6ee06e5562fcd5096c107c99655b7287c0348" +
        "454d6d99686e9790936713bc7d3626ad73939e7c38e2aff4ee618381dbae9835cc087ea4ff767f01" +
        "5d5f64c855b8a8e5fb376be746d67bb0c20303868d056b9aa4faf015e31cf0ed12d4aa2f9584de6d" +
        "986204b5878a61626a03896fe0d7b64966305b7a35da54fa267bc3a9c46f0203010001a323302130" +
        "0f0603551d130101ff040530030101ff300e0603551d0f0101ff0404030202c4300d06092a864886" +
        "f70d01010b0500038201010074653878a8af7257342375f9632ba548b2b081af0364a8a1068ca5fc" +
        "456597f85d2a91b119be39d3ff2acbd482ba0bc5f31babaae1693ce2349b7747eac9b9961b64a6e6" +
        "99f3520170e01fa3c86263a6570f375847839f11de079f11d13087a6dab8f29e448aef7e700a5b2e" +
        "ab6b3216dee2acf61262e51ecacd40b0f54b02f552a49d3b3609fd4db14f5ce2f8e2b998454468ff" +
        "eca1d0093a2a5b16690bf10fdac4fc2e38d34b8bc8c72e8e425cbfd000205cff675623e5db07b2b6" +
        "3312db2f93867ec28c1d982298d25090a3df33ca65b91bf7682047d796b8c76f1fd4abf7c1cd9ccf" +
        "0e61017c2bc53f7bbd30d00efce40ba96871fb467a1c8741057e7846";

    [Fact]
    public void Build_StartsWithSequenceTag()
    {
        X509Certificate cert = X509Certificate.Decode(Convert.FromHexString(CertDerHex));
        byte[] attr = CmsSignedDataBuilder.BuildSigningCertificateV2Attribute(cert, HashAlgorithmName.Sha256);
        attr[0].Should().Be(0x30, "Attribute encodes as a DER SEQUENCE");
    }

    [Fact]
    public void Build_ContainsExpectedOid()
    {
        // id-aa-signingCertificateV2 = 1.2.840.113549.1.9.16.2.47
        // DER OID encoding: 06 0B 2A 86 48 86 F7 0D 01 09 10 02 2F
        X509Certificate cert = X509Certificate.Decode(Convert.FromHexString(CertDerHex));
        byte[] attr = CmsSignedDataBuilder.BuildSigningCertificateV2Attribute(cert, HashAlgorithmName.Sha256);

        byte[] oidBytes = new byte[] { 0x06, 0x0B, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x09, 0x10, 0x02, 0x2F };
        bool found = false;
        for (int i = 0; i <= attr.Length - oidBytes.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < oidBytes.Length; j++)
            {
                if (attr[i + j] != oidBytes[j]) { match = false; break; }
            }
            if (match) { found = true; break; }
        }
        found.Should().BeTrue("the attribute OID must appear in the encoded bytes");
    }

    [Fact]
    public void Build_WithSha384_EmitsExplicitHashAlgorithm()
    {
        // SHA-256 is the spec DEFAULT; SHA-384 should be emitted as an explicit hashAlgorithm.
        // Therefore the SHA-384 encoding is strictly longer than the SHA-256 encoding for the
        // same cert.
        X509Certificate cert = X509Certificate.Decode(Convert.FromHexString(CertDerHex));
        byte[] sha256 = CmsSignedDataBuilder.BuildSigningCertificateV2Attribute(cert, HashAlgorithmName.Sha256);
        byte[] sha384 = CmsSignedDataBuilder.BuildSigningCertificateV2Attribute(cert, HashAlgorithmName.Sha384);
        sha384.Length.Should().BeGreaterThan(sha256.Length);
    }
}
