// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Tests for EncapsulatedContentInfo

using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Cms;
using Chuvadi.Cryptography.Oids;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.Cms;

public sealed class EncapsulatedContentInfoTests
{
    [Fact]
    public void Read_DetachedSignature_NoContent()
    {
        // SEQUENCE { OID id-data }
        Asn1Writer w = new();
        w.PushSequence();
        w.WriteObjectIdentifier(KnownOids.CmsData);
        w.PopSequence();

        Asn1Reader r = new(w.ToArray());
        EncapsulatedContentInfo eci = EncapsulatedContentInfo.Read(r);

        eci.ContentType.Should().Be(KnownOids.CmsData);
        eci.IsDetached.Should().BeTrue();
        eci.Content.Should().BeNull();
    }

    [Fact]
    public void Read_AttachedSignature_WithContent()
    {
        // SEQUENCE { OID id-data, [0] EXPLICIT OCTET STRING "Hello" }
        byte[] content = System.Text.Encoding.ASCII.GetBytes("Hello");
        Asn1Writer w = new();
        w.PushSequence();
        w.WriteObjectIdentifier(KnownOids.CmsData);
        w.PushExplicit(0);
        w.WriteOctetString(content);
        w.PopExplicit(0);
        w.PopSequence();

        Asn1Reader r = new(w.ToArray());
        EncapsulatedContentInfo eci = EncapsulatedContentInfo.Read(r);

        eci.IsDetached.Should().BeFalse();
        eci.Content.Should().Equal(content);
    }

    [Fact]
    public void Read_TstInfoContentType()
    {
        // RFC 3161 timestamps wrap a TSTInfo OID
        Asn1Writer w = new();
        w.PushSequence();
        w.WriteObjectIdentifier(KnownOids.TstInfo);
        w.PushExplicit(0);
        w.WriteOctetString(new byte[] { 0x30, 0x00 });  // empty SEQUENCE — placeholder
        w.PopExplicit(0);
        w.PopSequence();

        Asn1Reader r = new(w.ToArray());
        EncapsulatedContentInfo eci = EncapsulatedContentInfo.Read(r);

        eci.ContentType.Should().Be(KnownOids.TstInfo);
        eci.Content.Should().NotBeNull();
    }
}
