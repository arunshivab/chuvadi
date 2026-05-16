// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Tests for IssuerAndSerialNumber and SignerIdentifier

using System.Numerics;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Cms;
using Chuvadi.Cryptography.Oids;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.Cms;

public sealed class IssuerAndSerialNumberTests
{
    private static byte[] BuildIssuerAndSerial(string issuerCn, BigInteger serial)
    {
        Asn1Writer w = new();
        w.PushSequence();
        // issuer Name (single CN)
        w.PushSequence();
        w.PushSet();
        w.PushSequence();
        w.WriteObjectIdentifier(KnownOids.CommonName);
        w.WriteUtf8String(issuerCn);
        w.PopSequence();
        w.PopSet();
        w.PopSequence();
        // serial
        w.WriteInteger(serial);
        w.PopSequence();
        return w.ToArray();
    }

    [Fact]
    public void RoundTrip_RecoversFields()
    {
        byte[] der = BuildIssuerAndSerial("Test CA", BigInteger.Parse("999999999999"));
        Asn1Reader r = new(der);
        IssuerAndSerialNumber ias = IssuerAndSerialNumber.Read(r);

        ias.Issuer.CommonName.Should().Be("Test CA");
        ias.SerialNumber.Should().Be(BigInteger.Parse("999999999999"));
    }
}

public sealed class SignerIdentifierTests
{
    [Fact]
    public void Read_IssuerAndSerialVariant()
    {
        // Wrap an IssuerAndSerial.
        Asn1Writer w = new();
        w.PushSequence();
        // Issuer
        w.PushSequence();
        w.PushSet();
        w.PushSequence();
        w.WriteObjectIdentifier(KnownOids.CommonName);
        w.WriteUtf8String("CA");
        w.PopSequence();
        w.PopSet();
        w.PopSequence();
        // Serial
        w.WriteInteger(1);
        w.PopSequence();

        Asn1Reader r = new(w.ToArray());
        SignerIdentifier sid = SignerIdentifier.Read(r);

        sid.Kind.Should().Be(SignerIdentifierKind.IssuerAndSerial);
        sid.IssuerAndSerial.Should().NotBeNull();
        sid.IssuerAndSerial!.SerialNumber.Should().Be(BigInteger.One);
    }

    [Fact]
    public void Read_SkiVariant()
    {
        // [0] IMPLICIT OCTET STRING content
        byte[] skiBytes = { 0xDE, 0xAD, 0xBE, 0xEF };
        Asn1Writer w = new();
        // Implicit tag 0x80, length 4, content
        w.WriteEncoded(new byte[] { 0x80, 0x04 });
        w.WriteEncoded(skiBytes);

        Asn1Reader r = new(w.ToArray());
        SignerIdentifier sid = SignerIdentifier.Read(r);

        sid.Kind.Should().Be(SignerIdentifierKind.SubjectKeyIdentifier);
        sid.SubjectKeyIdentifier.Should().Equal(skiBytes);
    }
}
