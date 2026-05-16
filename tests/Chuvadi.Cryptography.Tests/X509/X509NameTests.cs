// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Tests for X509Name and RDN

using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Oids;
using Chuvadi.Cryptography.X509;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.X509;

public sealed class X509NameTests
{
    private static byte[] BuildDn()
    {
        // CN=John Doe, O=Acme Corp, C=US
        // encoded as: SEQUENCE { SET { SEQ{C,US} }, SET { SEQ{O,Acme Corp} }, SET { SEQ{CN,John Doe} } }
        Asn1Writer w = new();
        w.PushSequence();

        // C=US
        w.PushSet();
        w.PushSequence();
        w.WriteObjectIdentifier(KnownOids.CountryName);
        w.WritePrintableString("US");
        w.PopSequence();
        w.PopSet();

        // O=Acme Corp
        w.PushSet();
        w.PushSequence();
        w.WriteObjectIdentifier(KnownOids.OrganizationName);
        w.WriteUtf8String("Acme Corp");
        w.PopSequence();
        w.PopSet();

        // CN=John Doe
        w.PushSet();
        w.PushSequence();
        w.WriteObjectIdentifier(KnownOids.CommonName);
        w.WriteUtf8String("John Doe");
        w.PopSequence();
        w.PopSet();

        w.PopSequence();
        return w.ToArray();
    }

    [Fact]
    public void Decode_ThreeAttributeDn()
    {
        byte[] der = BuildDn();
        Asn1Reader r = new(der);
        X509Name name = X509Name.Read(r);

        name.Rdns.Should().HaveCount(3);
        name.CommonName.Should().Be("John Doe");
        name.FindFirst(KnownOids.OrganizationName).Should().Be("Acme Corp");
        name.FindFirst(KnownOids.CountryName).Should().Be("US");
    }

    [Fact]
    public void RawEncoding_Preserved()
    {
        byte[] der = BuildDn();
        Asn1Reader r = new(der);
        X509Name name = X509Name.Read(r);
        name.RawEncoding.Should().Equal(der);
    }

    [Fact]
    public void ToString_RfcOrder_MostSpecificFirst()
    {
        byte[] der = BuildDn();
        Asn1Reader r = new(der);
        X509Name name = X509Name.Read(r);
        name.ToString().Should().Be("CN=John Doe,O=Acme Corp,C=US");
    }

    [Fact]
    public void FindAll_ReturnsMultipleMatches()
    {
        // Build a DN with two OUs
        Asn1Writer w = new();
        w.PushSequence();
        foreach (string ou in new[] { "Engineering", "Cryptography" })
        {
            w.PushSet();
            w.PushSequence();
            w.WriteObjectIdentifier(KnownOids.OrganizationalUnitName);
            w.WriteUtf8String(ou);
            w.PopSequence();
            w.PopSet();
        }
        w.PopSequence();

        Asn1Reader r = new(w.ToArray());
        X509Name name = X509Name.Read(r);
        System.Collections.Generic.List<string> all = new(name.FindAll(KnownOids.OrganizationalUnitName));
        all.Should().Equal("Engineering", "Cryptography");
    }
}
