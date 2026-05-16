// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — End-to-end CMS / PKCS#7 SignedData decoder tests

using System;
using System.Numerics;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Cms;
using Chuvadi.Cryptography.Oids;
using Chuvadi.Cryptography.X509;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.Cms;

public sealed class CmsSignedDataEndToEndTests
{
    /// <summary>Builds a minimal but well-formed X.509 v3 certificate.</summary>
    private static byte[] BuildCertificate(string issuerCn, string subjectCn, BigInteger serial)
    {
        Asn1Writer w = new();
        w.PushSequence();   // Certificate

        w.PushSequence();   // TBSCertificate
        w.PushExplicit(0); w.WriteInteger(2); w.PopExplicit(0);  // version v3
        w.WriteInteger(serial);
        w.PushSequence(); w.WriteObjectIdentifier(KnownOids.Sha256WithRsa); w.WriteNull(); w.PopSequence();  // signature
        WriteSimpleDn(w, issuerCn);
        w.PushSequence();
        w.WriteUtcTime(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
        w.WriteUtcTime(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        w.PopSequence();
        WriteSimpleDn(w, subjectCn);
        w.PushSequence();   // SPKI
        w.PushSequence(); w.WriteObjectIdentifier(KnownOids.RsaEncryption); w.WriteNull(); w.PopSequence();
        w.WriteBitString(new byte[256]);
        w.PopSequence();
        w.PopSequence();    // close TBS

        w.PushSequence(); w.WriteObjectIdentifier(KnownOids.Sha256WithRsa); w.WriteNull(); w.PopSequence();
        w.WriteBitString(new byte[256]);
        w.PopSequence();    // close Certificate

        return w.ToArray();
    }

    private static void WriteSimpleDn(Asn1Writer w, string cn)
    {
        w.PushSequence();
        w.PushSet();
        w.PushSequence();
        w.WriteObjectIdentifier(KnownOids.CommonName);
        w.WriteUtf8String(cn);
        w.PopSequence();
        w.PopSet();
        w.PopSequence();
    }

    private static byte[] BuildSignedData(byte[] certificateBytes)
    {
        // The signed message-digest payload — pretend the document hash is this:
        byte[] documentHash = new byte[32];
        for (int i = 0; i < 32; i++) { documentHash[i] = (byte)i; }
        DateTimeOffset signingTime = new(2024, 6, 15, 12, 30, 0, TimeSpan.Zero);

        Asn1Writer w = new();
        // ContentInfo
        w.PushSequence();
        w.WriteObjectIdentifier(KnownOids.CmsSignedData);
        w.PushExplicit(0);

        // SignedData
        w.PushSequence();
        w.WriteInteger(1);  // version

        // digestAlgorithms
        w.PushSet();
        w.PushSequence(); w.WriteObjectIdentifier(KnownOids.Sha256); w.WriteNull(); w.PopSequence();
        w.PopSet();

        // encapContentInfo — detached
        w.PushSequence();
        w.WriteObjectIdentifier(KnownOids.CmsData);
        w.PopSequence();

        // certificates [0] IMPLICIT — write tag and length manually
        // Use the EXPLICIT helper as a constructed-tag carrier; close manually.
        w.PushExplicit(0);  // writes 0xA0 — we actually want 0xA0 since it's IMPLICIT-constructed
        w.WriteEncoded(certificateBytes);
        w.PopExplicit(0);

        // signerInfos
        w.PushSet();
        WriteSignerInfo(w, documentHash, signingTime);
        w.PopSet();

        w.PopSequence();    // close SignedData
        w.PopExplicit(0);
        w.PopSequence();    // close ContentInfo
        return w.ToArray();
    }

    private static void WriteSignerInfo(Asn1Writer w, byte[] documentHash, DateTimeOffset signingTime)
    {
        w.PushSequence();
        w.WriteInteger(1);  // version v1 (IssuerAndSerial)

        // sid: IssuerAndSerialNumber for "Test CA" / serial 42
        w.PushSequence();
        WriteSimpleDn(w, "Test CA");
        w.WriteInteger(42);
        w.PopSequence();

        // digestAlgorithm
        w.PushSequence(); w.WriteObjectIdentifier(KnownOids.Sha256); w.WriteNull(); w.PopSequence();

        // signedAttrs [0] IMPLICIT — three attributes: contentType, messageDigest, signingTime
        // Build the SET body to use with IMPLICIT tag
        Asn1Writer setBuilder = new();
        setBuilder.PushSet();
        // contentType
        setBuilder.PushSequence();
        setBuilder.WriteObjectIdentifier(KnownOids.ContentType);
        setBuilder.PushSet();
        setBuilder.WriteObjectIdentifier(KnownOids.CmsData);
        setBuilder.PopSet();
        setBuilder.PopSequence();
        // messageDigest
        setBuilder.PushSequence();
        setBuilder.WriteObjectIdentifier(KnownOids.MessageDigest);
        setBuilder.PushSet();
        setBuilder.WriteOctetString(documentHash);
        setBuilder.PopSet();
        setBuilder.PopSequence();
        // signingTime
        setBuilder.PushSequence();
        setBuilder.WriteObjectIdentifier(KnownOids.SigningTime);
        setBuilder.PushSet();
        setBuilder.WriteUtcTime(signingTime);
        setBuilder.PopSet();
        setBuilder.PopSequence();
        setBuilder.PopSet();
        byte[] setEncoded = setBuilder.ToArray();
        // setEncoded begins with 0x31 (SET tag). Strip the tag and re-emit under IMPLICIT [0] (0xA0).
        // setEncoded[0] == 0x31; replace with 0xA0.
        setEncoded[0] = 0xA0;
        w.WriteEncoded(setEncoded);

        // signatureAlgorithm
        w.PushSequence(); w.WriteObjectIdentifier(KnownOids.RsaEncryption); w.WriteNull(); w.PopSequence();

        // signature — placeholder 256-byte RSA-2048 sized blob
        w.WriteOctetString(new byte[256]);

        w.PopSequence();
    }

    [Fact]
    public void Decode_FullSyntheticPdfSignature_RoundTripsAllFields()
    {
        byte[] cert = BuildCertificate("Test CA", "Signer", BigInteger.Parse("42"));
        byte[] cms = BuildSignedData(cert);

        ContentInfo ci = CmsDecoder.DecodeContentInfo(cms);
        ci.IsSignedData.Should().BeTrue();
        ci.ContentType.Should().Be(KnownOids.CmsSignedData);

        SignedData sd = ci.GetSignedData();
        sd.Version.Should().Be(1);
        sd.DigestAlgorithms.Should().HaveCount(1);
        sd.DigestAlgorithms[0].Algorithm.Should().Be(KnownOids.Sha256);

        sd.EncapContentInfo.IsDetached.Should().BeTrue();
        sd.EncapContentInfo.ContentType.Should().Be(KnownOids.CmsData);

        sd.Certificates.Should().HaveCount(1);
        sd.Certificates[0].Subject.CommonName.Should().Be("Signer");

        sd.SignerInfos.Should().HaveCount(1);
        SignerInfo si = sd.SignerInfos[0];
        si.Version.Should().Be(1);
        si.SignerId.Kind.Should().Be(SignerIdentifierKind.IssuerAndSerial);
        si.SignerId.IssuerAndSerial!.SerialNumber.Should().Be(BigInteger.Parse("42"));
        si.SignerId.IssuerAndSerial.Issuer.CommonName.Should().Be("Test CA");

        si.DigestAlgorithm.Algorithm.Should().Be(KnownOids.Sha256);
        si.SignatureAlgorithm.Algorithm.Should().Be(KnownOids.RsaEncryption);
        si.Signature.Length.Should().Be(256);

        si.HasSignedAttributes.Should().BeTrue();
        si.SignedAttributes!.Count.Should().Be(3);

        si.AssertedContentType.Should().Be(KnownOids.CmsData);
        si.MessageDigest!.Length.Should().Be(32);
        for (int i = 0; i < 32; i++) { si.MessageDigest[i].Should().Be((byte)i); }
        si.SigningTime.Should().Be(new DateTimeOffset(2024, 6, 15, 12, 30, 0, TimeSpan.Zero));

        si.UnsignedAttributes.Should().BeNull();
    }

    [Fact]
    public void Decode_SignedAttributesDerForVerification_StartsWithSetTag()
    {
        byte[] cert = BuildCertificate("Test CA", "Signer", BigInteger.Parse("42"));
        byte[] cms = BuildSignedData(cert);

        SignedData sd = CmsDecoder.DecodeSignedData(cms);
        SignerInfo si = sd.SignerInfos[0];

        // The bytes used for signature verification must use the universal SET tag (0x31),
        // not the IMPLICIT [0] tag (0xA0) that appeared on the wire.
        si.SignedAttributes!.DerEncodedForVerification[0].Should().Be(0x31);
    }

    [Fact]
    public void SignerInfo_FindSignerCertificate_ResolvesByIssuerAndSerial()
    {
        // Cert issuer is "Test CA", but the SignedData certificate set has subject "Signer".
        // The SignerInfo's sid is IssuerAndSerial(Test CA, 42).
        // For the match to succeed, the *signer's* certificate (subject "Signer") must have
        // issuer "Test CA" and serial 42.
        byte[] cert = BuildCertificate("Test CA", "Signer", BigInteger.Parse("42"));
        byte[] cms = BuildSignedData(cert);

        SignedData sd = CmsDecoder.DecodeSignedData(cms);
        SignerInfo si = sd.SignerInfos[0];

        X509Certificate? found = si.FindSignerCertificate(sd.Certificates);
        found.Should().NotBeNull();
        found!.Subject.CommonName.Should().Be("Signer");
    }
}
