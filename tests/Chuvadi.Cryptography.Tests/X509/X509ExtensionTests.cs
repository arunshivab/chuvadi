// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Tests for X509Extension parsers

using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Oids;
using Chuvadi.Cryptography.X509;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.X509;

public sealed class BasicConstraintsExtensionTests
{
    [Fact]
    public void Parse_EndEntityCertificate_IsCaFalse()
    {
        // BasicConstraints with cA absent (default FALSE), no pathLen
        Asn1Writer w = new();
        w.PushSequence();
        w.PopSequence();

        BasicConstraintsExtension ext = BasicConstraintsExtension.Parse(w.ToArray());
        ext.IsCa.Should().BeFalse();
        ext.PathLenConstraint.Should().BeNull();
    }

    [Fact]
    public void Parse_CaWithoutPathLen()
    {
        Asn1Writer w = new();
        w.PushSequence();
        w.WriteBoolean(true);
        w.PopSequence();

        BasicConstraintsExtension ext = BasicConstraintsExtension.Parse(w.ToArray());
        ext.IsCa.Should().BeTrue();
        ext.PathLenConstraint.Should().BeNull();
    }

    [Fact]
    public void Parse_CaWithPathLen3()
    {
        Asn1Writer w = new();
        w.PushSequence();
        w.WriteBoolean(true);
        w.WriteInteger(3);
        w.PopSequence();

        BasicConstraintsExtension ext = BasicConstraintsExtension.Parse(w.ToArray());
        ext.IsCa.Should().BeTrue();
        ext.PathLenConstraint.Should().Be(3);
    }
}

public sealed class KeyUsageExtensionTests
{
    [Fact]
    public void Parse_DigitalSignatureAndNonRepudiation()
    {
        // Bits 0 and 1: 0b11000000 = 0xC0, 6 unused bits in the final octet
        BitStringValue bs = new(new byte[] { 0xC0 }, unusedBitsInFinalOctet: 6);
        Asn1Writer w = new();
        w.WriteBitString(bs);

        KeyUsageExtension ext = KeyUsageExtension.Parse(w.ToArray());
        ext.Has(KeyUsageFlags.DigitalSignature).Should().BeTrue();
        ext.Has(KeyUsageFlags.NonRepudiation).Should().BeTrue();
        ext.Has(KeyUsageFlags.KeyCertSign).Should().BeFalse();
    }

    [Fact]
    public void Parse_KeyCertSignAndCrlSign_CaCertificate()
    {
        // Bits 5 and 6: 0b00000110 in bit 5/6 of an 8-bit slot.
        // Byte: 00000110 reversed: bit 5 → position 7-5=2, bit 6 → position 7-6=1
        // So: bit 5 set means byte value 0x04; bit 6 set means 0x02; together 0x06.
        BitStringValue bs = new(new byte[] { 0x06 }, unusedBitsInFinalOctet: 1);
        Asn1Writer w = new();
        w.WriteBitString(bs);

        KeyUsageExtension ext = KeyUsageExtension.Parse(w.ToArray());
        ext.Has(KeyUsageFlags.KeyCertSign).Should().BeTrue();
        ext.Has(KeyUsageFlags.CrlSign).Should().BeTrue();
        ext.Has(KeyUsageFlags.DigitalSignature).Should().BeFalse();
    }
}

public sealed class ExtendedKeyUsageExtensionTests
{
    [Fact]
    public void Parse_TwoPurposes()
    {
        Asn1Writer w = new();
        w.PushSequence();
        w.WriteObjectIdentifier(KnownOids.ServerAuth);
        w.WriteObjectIdentifier(KnownOids.ClientAuth);
        w.PopSequence();

        ExtendedKeyUsageExtension ext = ExtendedKeyUsageExtension.Parse(w.ToArray());
        ext.Purposes.Should().HaveCount(2);
        ext.Allows(KnownOids.ServerAuth).Should().BeTrue();
        ext.Allows(KnownOids.ClientAuth).Should().BeTrue();
        ext.Allows(KnownOids.CodeSigning).Should().BeFalse();
    }

    [Fact]
    public void Parse_DocumentSigning_RecognisedByPdfSigning()
    {
        Asn1Writer w = new();
        w.PushSequence();
        w.WriteObjectIdentifier(KnownOids.DocumentSigning);
        w.PopSequence();

        ExtendedKeyUsageExtension ext = ExtendedKeyUsageExtension.Parse(w.ToArray());
        ext.Allows(KnownOids.DocumentSigning).Should().BeTrue();
    }
}

public sealed class SubjectKeyIdentifierExtensionTests
{
    [Fact]
    public void Parse_TwentyByteIdentifier()
    {
        byte[] keyId = new byte[20];
        for (int i = 0; i < 20; i++) { keyId[i] = (byte)i; }

        Asn1Writer w = new();
        w.WriteOctetString(keyId);

        SubjectKeyIdentifierExtension ext = SubjectKeyIdentifierExtension.Parse(w.ToArray());
        ext.KeyIdentifier.Should().Equal(keyId);
    }
}

public sealed class AuthorityKeyIdentifierExtensionTests
{
    [Fact]
    public void Parse_OnlyKeyIdentifier()
    {
        // SEQUENCE { [0] IMPLICIT keyIdentifier }
        byte[] keyId = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };
        Asn1Writer w = new();
        w.PushSequence();
        // [0] IMPLICIT OCTET STRING — write tag and content directly.
        w.WriteEncoded(new byte[] { 0x80, (byte)keyId.Length });
        w.WriteEncoded(keyId);
        w.PopSequence();

        AuthorityKeyIdentifierExtension ext = AuthorityKeyIdentifierExtension.Parse(w.ToArray());
        ext.KeyIdentifier.Should().Equal(keyId);
        ext.AuthorityCertIssuerRaw.Should().BeNull();
        ext.AuthorityCertSerialNumber.Should().BeNull();
    }
}

public sealed class X509ExtensionTests
{
    [Fact]
    public void Read_ExtensionWithCriticalTrue()
    {
        Asn1Writer w = new();
        w.PushSequence();
        w.WriteObjectIdentifier(KnownOids.KeyUsage);
        w.WriteBoolean(true);
        w.WriteOctetString(new byte[] { 0x03, 0x02, 0x06, 0x06 });
        w.PopSequence();

        Asn1Reader r = new(w.ToArray());
        X509Extension ext = X509Extension.Read(r);
        ext.Oid.Should().Be(KnownOids.KeyUsage);
        ext.Critical.Should().BeTrue();
        ext.Value.Should().HaveCount(4);
    }

    [Fact]
    public void Read_ExtensionDefaultCriticalFalse()
    {
        Asn1Writer w = new();
        w.PushSequence();
        w.WriteObjectIdentifier(KnownOids.SubjectKeyIdentifier);
        w.WriteOctetString(new byte[] { 0x04, 0x02, 0xAA, 0xBB });
        w.PopSequence();

        Asn1Reader r = new(w.ToArray());
        X509Extension ext = X509Extension.Read(r);
        ext.Critical.Should().BeFalse();
    }
}
