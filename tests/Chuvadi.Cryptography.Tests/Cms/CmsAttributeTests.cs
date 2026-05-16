// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Tests for CmsAttribute and CmsAttributeTable

using System;
using System.Linq;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Cms;
using Chuvadi.Cryptography.Oids;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.Cms;

public sealed class CmsAttributeTests
{
    [Fact]
    public void Read_ContentTypeAttribute_SingleValue()
    {
        // SEQUENCE { OID id-contentType, SET { OID id-data } }
        Asn1Writer w = new();
        w.PushSequence();
        w.WriteObjectIdentifier(KnownOids.ContentType);
        w.PushSet();
        w.WriteObjectIdentifier(KnownOids.CmsData);
        w.PopSet();
        w.PopSequence();

        Asn1Reader r = new(w.ToArray());
        CmsAttribute attr = CmsAttribute.Read(r);

        attr.Type.Should().Be(KnownOids.ContentType);
        attr.Values.Should().HaveCount(1);
        attr.IsSingleValued.Should().BeTrue();
        // The single value should be the encoded OID id-data
        Asn1Reader vr = new(attr.SingleValue);
        vr.ReadObjectIdentifier().Should().Be(KnownOids.CmsData);
    }

    [Fact]
    public void Read_EmptySet_Rejected()
    {
        Asn1Writer w = new();
        w.PushSequence();
        w.WriteObjectIdentifier(KnownOids.MessageDigest);
        w.PushSet();
        w.PopSet();
        w.PopSequence();

        Asn1Reader r = new(w.ToArray());
        Action act = () => CmsAttribute.Read(r);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SingleValue_OnMultiValued_Throws()
    {
        CmsAttribute attr = new(KnownOids.MessageDigest, new[]
        {
            new byte[] { 0x04, 0x02, 0xAA, 0xBB },
            new byte[] { 0x04, 0x02, 0xCC, 0xDD },
        });
        Action act = () => _ = attr.SingleValue;
        act.Should().Throw<InvalidOperationException>();
    }
}

public sealed class CmsAttributeTableTests
{
    [Fact]
    public void Find_ReturnsMatchingAttribute()
    {
        CmsAttribute a1 = new(KnownOids.ContentType, new[] { new byte[] { 0x05, 0x00 } });
        CmsAttribute a2 = new(KnownOids.MessageDigest, new[] { new byte[] { 0x04, 0x02, 0xAA, 0xBB } });
        CmsAttributeTable table = new(new[] { a1, a2 }, derEncodedForVerification: new byte[] { 0x31 });

        table.Find(KnownOids.MessageDigest).Should().BeSameAs(a2);
        table.Find(KnownOids.SigningTime).Should().BeNull();
    }

    [Fact]
    public void FindAll_ReturnsMultiple()
    {
        CmsAttribute a1 = new(KnownOids.MessageDigest, new[] { new byte[] { 0x04, 0x01, 0x01 } });
        CmsAttribute a2 = new(KnownOids.MessageDigest, new[] { new byte[] { 0x04, 0x01, 0x02 } });
        CmsAttributeTable table = new(new[] { a1, a2 }, derEncodedForVerification: new byte[] { 0x31 });

        table.FindAll(KnownOids.MessageDigest).ToList().Should().HaveCount(2);
    }
}
