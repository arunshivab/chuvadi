// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — End-to-end tests for the X.509 certificate decoder

using System;
using System.Numerics;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Oids;
using Chuvadi.Cryptography.X509;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.X509;

public sealed class X509CertificateEndToEndTests
{
    /// <summary>
    /// Builds a minimal but well-formed X.509 v3 certificate using only Chuvadi's
    /// ASN.1 writer, decodes it, and verifies every field round-trips.
    /// </summary>
    private static byte[] BuildSyntheticCertificate(
        BigInteger serial,
        ObjectIdentifier sigAlg,
        string issuerCn,
        string subjectCn,
        DateTimeOffset notBefore,
        DateTimeOffset notAfter,
        byte[] subjectPublicKeyBytes,
        bool isCa,
        KeyUsageFlags keyUsage)
    {
        Asn1Writer w = new();

        w.PushSequence();  // Certificate

        // ── TBSCertificate ──
        w.PushSequence();

        // version [0] EXPLICIT 2 (v3)
        w.PushExplicit(0);
        w.WriteInteger(2);
        w.PopExplicit(0);

        // serialNumber
        w.WriteInteger(serial);

        // signature AlgorithmIdentifier
        w.PushSequence();
        w.WriteObjectIdentifier(sigAlg);
        w.WriteNull();
        w.PopSequence();

        // issuer Name (single CN)
        WriteSimpleDn(w, issuerCn);

        // validity
        w.PushSequence();
        w.WriteUtcTime(notBefore);
        w.WriteUtcTime(notAfter);
        w.PopSequence();

        // subject Name
        WriteSimpleDn(w, subjectCn);

        // subjectPublicKeyInfo
        w.PushSequence();
        w.PushSequence();  // algorithm
        w.WriteObjectIdentifier(KnownOids.RsaEncryption);
        w.WriteNull();
        w.PopSequence();
        w.WriteBitString(subjectPublicKeyBytes);
        w.PopSequence();

        // extensions [3] EXPLICIT
        w.PushExplicit(3);
        w.PushSequence();  // Extensions SEQUENCE

        // BasicConstraints
        {
            Asn1Writer inner = new();
            inner.PushSequence();
            if (isCa) { inner.WriteBoolean(true); }
            inner.PopSequence();
            w.PushSequence();
            w.WriteObjectIdentifier(KnownOids.BasicConstraints);
            w.WriteBoolean(true);  // critical
            w.WriteOctetString(inner.ToArray());
            w.PopSequence();
        }

        // KeyUsage
        {
            byte[] kuBytes = EncodeKeyUsage(keyUsage);
            int unusedBits = CountTrailingUnusedBits(keyUsage);
            BitStringValue bs = new(kuBytes, unusedBits);
            Asn1Writer inner = new();
            inner.WriteBitString(bs);
            w.PushSequence();
            w.WriteObjectIdentifier(KnownOids.KeyUsage);
            w.WriteBoolean(true);  // critical
            w.WriteOctetString(inner.ToArray());
            w.PopSequence();
        }

        w.PopSequence();   // close Extensions SEQUENCE
        w.PopExplicit(3);

        w.PopSequence();   // close TBSCertificate

        // ── signatureAlgorithm ──
        w.PushSequence();
        w.WriteObjectIdentifier(sigAlg);
        w.WriteNull();
        w.PopSequence();

        // ── signatureValue (dummy) ──
        w.WriteBitString(new byte[256]);

        w.PopSequence();  // close Certificate

        return w.ToArray();
    }

    private static void WriteSimpleDn(Asn1Writer w, string commonName)
    {
        w.PushSequence();
        w.PushSet();
        w.PushSequence();
        w.WriteObjectIdentifier(KnownOids.CommonName);
        w.WriteUtf8String(commonName);
        w.PopSequence();
        w.PopSet();
        w.PopSequence();
    }

    private static byte[] EncodeKeyUsage(KeyUsageFlags flags)
    {
        // Bits 0..8 in named-bit-list form. Find highest set bit.
        int highest = -1;
        for (int i = 0; i <= 8; i++)
        {
            if (((int)flags & (1 << i)) != 0) { highest = i; }
        }
        if (highest < 0) { return new byte[] { 0x00 }; }
        int byteCount = (highest / 8) + 1;
        byte[] bytes = new byte[byteCount];
        for (int i = 0; i <= highest; i++)
        {
            if (((int)flags & (1 << i)) != 0)
            {
                int byteIdx = i / 8;
                int bitInByte = 7 - (i % 8);
                bytes[byteIdx] |= (byte)(1 << bitInByte);
            }
        }
        return bytes;
    }

    private static int CountTrailingUnusedBits(KeyUsageFlags flags)
    {
        int highest = -1;
        for (int i = 0; i <= 8; i++)
        {
            if (((int)flags & (1 << i)) != 0) { highest = i; }
        }
        if (highest < 0) { return 0; }
        int totalBits = ((highest / 8) + 1) * 8;
        return totalBits - (highest + 1);
    }

    [Fact]
    public void Decode_SyntheticCertificate_RoundTripsEveryField()
    {
        DateTimeOffset nb = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset na = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        byte[] pubKey = new byte[256];
        for (int i = 0; i < 256; i++) { pubKey[i] = (byte)i; }

        byte[] der = BuildSyntheticCertificate(
            serial: BigInteger.Parse("123456789012345678"),
            sigAlg: KnownOids.Sha256WithRsa,
            issuerCn: "Test CA",
            subjectCn: "End Entity",
            notBefore: nb,
            notAfter: na,
            subjectPublicKeyBytes: pubKey,
            isCa: false,
            keyUsage: KeyUsageFlags.DigitalSignature | KeyUsageFlags.NonRepudiation);

        X509Certificate cert = X509Certificate.Decode(der);

        cert.Tbs.Version.Should().Be(2);
        cert.Tbs.SerialNumber.Should().Be(BigInteger.Parse("123456789012345678"));
        cert.SignatureAlgorithm.Algorithm.Should().Be(KnownOids.Sha256WithRsa);
        cert.Subject.CommonName.Should().Be("End Entity");
        cert.Issuer.CommonName.Should().Be("Test CA");
        cert.Validity.NotBefore.Should().Be(nb);
        cert.Validity.NotAfter.Should().Be(na);
        cert.Validity.IsWithin(new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero)).Should().BeTrue();
        cert.IsSelfIssued.Should().BeFalse();
        cert.TbsAndOuterAlgorithmsMatch.Should().BeTrue();
        cert.Tbs.Extensions.Should().HaveCount(2);
    }

    [Fact]
    public void Decode_SyntheticCertificate_TbsRawEncoding_ExactlyMatches()
    {
        byte[] pubKey = new byte[64];
        byte[] der = BuildSyntheticCertificate(
            serial: BigInteger.One,
            sigAlg: KnownOids.Sha256WithRsa,
            issuerCn: "X",
            subjectCn: "X",
            notBefore: new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            notAfter: new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
            subjectPublicKeyBytes: pubKey,
            isCa: true,
            keyUsage: KeyUsageFlags.KeyCertSign | KeyUsageFlags.CrlSign);

        X509Certificate cert = X509Certificate.Decode(der);
        cert.IsSelfIssued.Should().BeTrue();

        // TBS RawEncoding must be a contiguous sub-sequence of the full certificate.
        byte[] tbs = cert.Tbs.RawEncoding;
        bool found = false;
        for (int i = 0; i <= der.Length - tbs.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < tbs.Length; j++)
            {
                if (der[i + j] != tbs[j]) { match = false; break; }
            }
            if (match) { found = true; break; }
        }
        found.Should().BeTrue();
    }

    [Fact]
    public void Decode_SyntheticCertificate_ExtensionsLookupWorks()
    {
        byte[] pubKey = new byte[64];
        byte[] der = BuildSyntheticCertificate(
            serial: BigInteger.One,
            sigAlg: KnownOids.Sha256WithRsa,
            issuerCn: "CA",
            subjectCn: "CA",
            notBefore: new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            notAfter: new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero),
            subjectPublicKeyBytes: pubKey,
            isCa: true,
            keyUsage: KeyUsageFlags.KeyCertSign);

        X509Certificate cert = X509Certificate.Decode(der);

        X509Extension? bcExt = cert.Tbs.FindExtension(KnownOids.BasicConstraints);
        bcExt.Should().NotBeNull();
        BasicConstraintsExtension bc = BasicConstraintsExtension.Parse(bcExt!.Value);
        bc.IsCa.Should().BeTrue();

        X509Extension? kuExt = cert.Tbs.FindExtension(KnownOids.KeyUsage);
        kuExt.Should().NotBeNull();
        KeyUsageExtension ku = KeyUsageExtension.Parse(kuExt!.Value);
        ku.Has(KeyUsageFlags.KeyCertSign).Should().BeTrue();

        cert.Tbs.FindExtension(KnownOids.ExtKeyUsage).Should().BeNull();
    }
}
