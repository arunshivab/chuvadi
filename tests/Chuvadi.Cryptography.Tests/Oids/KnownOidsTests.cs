// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Tests for the OID registry

using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Oids;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.Oids;

public sealed class KnownOidsTests
{
    [Fact]
    public void HashOids_HaveExpectedDotted()
    {
        KnownOids.Sha1.Dotted.Should().Be("1.3.14.3.2.26");
        KnownOids.Sha256.Dotted.Should().Be("2.16.840.1.101.3.4.2.1");
        KnownOids.Sha384.Dotted.Should().Be("2.16.840.1.101.3.4.2.2");
        KnownOids.Sha512.Dotted.Should().Be("2.16.840.1.101.3.4.2.3");
    }

    [Fact]
    public void PublicKeyOids_HaveExpectedDotted()
    {
        KnownOids.RsaEncryption.Dotted.Should().Be("1.2.840.113549.1.1.1");
        KnownOids.EcPublicKey.Dotted.Should().Be("1.2.840.10045.2.1");
        KnownOids.Ed25519.Dotted.Should().Be("1.3.101.112");
        KnownOids.Ed448.Dotted.Should().Be("1.3.101.113");
    }

    [Fact]
    public void SignatureOids_HaveExpectedDotted()
    {
        KnownOids.Sha256WithRsa.Dotted.Should().Be("1.2.840.113549.1.1.11");
        KnownOids.Sha384WithEcdsa.Dotted.Should().Be("1.2.840.10045.4.3.3");
    }

    [Fact]
    public void DnAttributeOids_HaveExpectedDotted()
    {
        KnownOids.CommonName.Dotted.Should().Be("2.5.4.3");
        KnownOids.CountryName.Dotted.Should().Be("2.5.4.6");
        KnownOids.OrganizationName.Dotted.Should().Be("2.5.4.10");
    }

    [Fact]
    public void ExtensionOids_HaveExpectedDotted()
    {
        KnownOids.KeyUsage.Dotted.Should().Be("2.5.29.15");
        KnownOids.SubjectAltName.Dotted.Should().Be("2.5.29.17");
        KnownOids.BasicConstraints.Dotted.Should().Be("2.5.29.19");
        KnownOids.AuthorityKeyIdentifier.Dotted.Should().Be("2.5.29.35");
        KnownOids.ExtKeyUsage.Dotted.Should().Be("2.5.29.37");
    }

    [Fact]
    public void CmsContentTypeOids_HaveExpectedDotted()
    {
        KnownOids.CmsData.Dotted.Should().Be("1.2.840.113549.1.7.1");
        KnownOids.CmsSignedData.Dotted.Should().Be("1.2.840.113549.1.7.2");
    }

    [Fact]
    public void CmsSignedAttrOids_HaveExpectedDotted()
    {
        KnownOids.ContentType.Dotted.Should().Be("1.2.840.113549.1.9.3");
        KnownOids.MessageDigest.Dotted.Should().Be("1.2.840.113549.1.9.4");
        KnownOids.SigningTime.Dotted.Should().Be("1.2.840.113549.1.9.5");
        KnownOids.SigningCertificateV2.Dotted.Should().Be("1.2.840.113549.1.9.16.2.47");
    }

    [Fact]
    public void Rfc3161AndOcspOids_HaveExpectedDotted()
    {
        KnownOids.TstInfo.Dotted.Should().Be("1.2.840.113549.1.9.16.1.4");
        KnownOids.OcspBasicResponse.Dotted.Should().Be("1.3.6.1.5.5.7.48.1.1");
        KnownOids.OcspNonce.Dotted.Should().Be("1.3.6.1.5.5.7.48.1.2");
    }

    [Fact]
    public void PdfSubFilterConstants_AreCanonical()
    {
        KnownOids.AdbePkcs7Detached.Should().Be("adbe.pkcs7.detached");
        KnownOids.EtsiCAdESDetached.Should().Be("ETSI.CAdES.detached");
        KnownOids.EtsiRfc3161.Should().Be("ETSI.RFC3161");
    }
}

public sealed class OidNameLookupTests
{
    [Fact]
    public void GetName_ReturnsFriendlyForKnownOid()
    {
        OidNameLookup.GetName(KnownOids.Sha256).Should().Be("Sha256");
        OidNameLookup.GetName(KnownOids.CommonName).Should().Be("CommonName");
        OidNameLookup.GetName(KnownOids.Sha256WithRsa).Should().Be("Sha256WithRsa");
    }

    [Fact]
    public void GetName_ReturnsDottedForUnknownOid()
    {
        ObjectIdentifier unknown = new("1.2.3.4.5.6.7.8.9");
        OidNameLookup.GetName(unknown).Should().Be("1.2.3.4.5.6.7.8.9");
    }

    [Fact]
    public void IsKnown_TrueForRegisteredOid()
    {
        OidNameLookup.IsKnown(KnownOids.Sha256).Should().BeTrue();
    }

    [Fact]
    public void IsKnown_FalseForUnregisteredOid()
    {
        OidNameLookup.IsKnown(new ObjectIdentifier("1.2.3.99")).Should().BeFalse();
    }
}
