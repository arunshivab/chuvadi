// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Tests for GeneralName and SubjectAlternativeName

using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.X509;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.X509;

public sealed class GeneralNameTests
{
    [Fact]
    public void Read_DnsName()
    {
        // [2] IMPLICIT IA5String "example.com"
        Asn1Writer w = new();
        // Tag 0x82 = [2] IMPLICIT, length 11
        byte[] text = System.Text.Encoding.ASCII.GetBytes("example.com");
        w.WriteEncoded(new byte[] { 0x82, (byte)text.Length });
        w.WriteEncoded(text);

        Asn1Reader r = new(w.ToArray());
        GeneralName gn = GeneralName.Read(r);
        gn.Kind.Should().Be(GeneralNameKind.DnsName);
        gn.StringValue.Should().Be("example.com");
    }

    [Fact]
    public void Read_Rfc822Name()
    {
        Asn1Writer w = new();
        byte[] text = System.Text.Encoding.ASCII.GetBytes("user@example.com");
        w.WriteEncoded(new byte[] { 0x81, (byte)text.Length });
        w.WriteEncoded(text);

        Asn1Reader r = new(w.ToArray());
        GeneralName gn = GeneralName.Read(r);
        gn.Kind.Should().Be(GeneralNameKind.Rfc822Name);
        gn.StringValue.Should().Be("user@example.com");
    }

    [Fact]
    public void Read_Uri()
    {
        Asn1Writer w = new();
        byte[] text = System.Text.Encoding.ASCII.GetBytes("http://crl.example.com/ca.crl");
        w.WriteEncoded(new byte[] { 0x86, (byte)text.Length });
        w.WriteEncoded(text);

        Asn1Reader r = new(w.ToArray());
        GeneralName gn = GeneralName.Read(r);
        gn.Kind.Should().Be(GeneralNameKind.UniformResourceIdentifier);
        gn.StringValue.Should().Be("http://crl.example.com/ca.crl");
    }

    [Fact]
    public void Read_IPv4Address()
    {
        Asn1Writer w = new();
        w.WriteEncoded(new byte[] { 0x87, 0x04, 192, 168, 1, 1 });

        Asn1Reader r = new(w.ToArray());
        GeneralName gn = GeneralName.Read(r);
        gn.Kind.Should().Be(GeneralNameKind.IpAddress);
        gn.RawValue.Should().Equal(192, 168, 1, 1);
        gn.ToString().Should().Be("IP:192.168.1.1");
    }
}

public sealed class SubjectAlternativeNameTests
{
    [Fact]
    public void Parse_TwoDnsNames()
    {
        // SEQUENCE { [2] "a.example.com", [2] "b.example.com" }
        Asn1Writer w = new();
        w.PushSequence();
        byte[] a = System.Text.Encoding.ASCII.GetBytes("a.example.com");
        byte[] b = System.Text.Encoding.ASCII.GetBytes("b.example.com");
        w.WriteEncoded(new byte[] { 0x82, (byte)a.Length });
        w.WriteEncoded(a);
        w.WriteEncoded(new byte[] { 0x82, (byte)b.Length });
        w.WriteEncoded(b);
        w.PopSequence();

        SubjectAlternativeNameExtension ext = SubjectAlternativeNameExtension.Parse(w.ToArray());
        ext.Names.Should().HaveCount(2);
        ext.Names[0].StringValue.Should().Be("a.example.com");
        ext.Names[1].StringValue.Should().Be("b.example.com");
    }
}

public sealed class AuthorityInformationAccessTests
{
    [Fact]
    public void Parse_OcspAndCaIssuers()
    {
        // SEQUENCE {
        //   SEQUENCE { OID id-ad-ocsp, [6] URI "http://ocsp.example.com" },
        //   SEQUENCE { OID id-ad-caIssuers, [6] URI "http://ca.example.com/ca.cer" }
        // }
        Asn1Writer w = new();
        w.PushSequence();

        w.PushSequence();
        w.WriteObjectIdentifier(Chuvadi.Cryptography.Oids.KnownOids.OcspAccess);
        byte[] ocspUri = System.Text.Encoding.ASCII.GetBytes("http://ocsp.example.com");
        w.WriteEncoded(new byte[] { 0x86, (byte)ocspUri.Length });
        w.WriteEncoded(ocspUri);
        w.PopSequence();

        w.PushSequence();
        w.WriteObjectIdentifier(Chuvadi.Cryptography.Oids.KnownOids.CaIssuers);
        byte[] caUri = System.Text.Encoding.ASCII.GetBytes("http://ca.example.com/ca.cer");
        w.WriteEncoded(new byte[] { 0x86, (byte)caUri.Length });
        w.WriteEncoded(caUri);
        w.PopSequence();

        w.PopSequence();

        AuthorityInformationAccessExtension ext = AuthorityInformationAccessExtension.Parse(w.ToArray());
        ext.Descriptions.Should().HaveCount(2);
        ext.OcspUri.Should().Be("http://ocsp.example.com");
        ext.CaIssuersUri.Should().Be("http://ca.example.com/ca.cer");
    }
}
