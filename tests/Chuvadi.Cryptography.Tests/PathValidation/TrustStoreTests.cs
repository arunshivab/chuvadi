// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Tests for TrustStore

using System.Collections.Generic;
using System.Linq;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.PathValidation;
using Chuvadi.Cryptography.X509;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.PathValidation;

public sealed class TrustStoreTests
{
    [Fact]
    public void Add_ByAnchor_Works()
    {
        TrustStore store = new();
        store.Add(MakeAnchor("CN=Root"));
        store.Anchors.Should().HaveCount(1);
    }

    [Fact]
    public void FindBySubject_ReturnsMatchingAnchors()
    {
        TrustStore store = new();
        store.Add(MakeAnchor("CN=Root"));
        store.Add(MakeAnchor("CN=Other"));

        X509Name target = SimpleName("CN=Root");
        List<TrustAnchor> matches = store.FindBySubject(target).ToList();
        matches.Should().HaveCount(1);
        matches[0].Subject.RawEncoding.Should().Equal(target.RawEncoding);
    }

    [Fact]
    public void NameEquals_SameBytes_Returns_True()
    {
        X509Name a = SimpleName("CN=Root");
        X509Name b = SimpleName("CN=Root");
        TrustStore.NameEquals(a, b).Should().BeTrue();
    }

    [Fact]
    public void NameEquals_DifferentBytes_Returns_False()
    {
        X509Name a = SimpleName("CN=Root");
        X509Name b = SimpleName("CN=Other");
        TrustStore.NameEquals(a, b).Should().BeFalse();
    }

    // --- helpers ---

    private static TrustAnchor MakeAnchor(string dn)
    {
        X509Name name = SimpleName(dn);
        SubjectPublicKeyInfo spki = new(
            new AlgorithmIdentifier(new ObjectIdentifier("1.2.3"), null),
            new BitStringValue(new byte[] { 0x01 }, 0));
        return new TrustAnchor(name, spki);
    }

    private static X509Name SimpleName(string commonName)
    {
        // Build a minimal name: SEQUENCE { SET { SEQUENCE { OID 2.5.4.3, UTF8String commonName } } }
        // The raw bytes uniquely identify "this name", and that's all NameEquals cares about.
        byte[] cnBytes = System.Text.Encoding.UTF8.GetBytes(commonName);
        byte[] der = BuildDistinguishedNameDer(cnBytes);
        // Use Asn1Reader to materialise an X509Name from the DER we just built.
        Asn1Reader r = new(der);
        return X509Name.Read(r);
    }

    private static byte[] BuildDistinguishedNameDer(byte[] cn)
    {
        Asn1Writer w = new();
        w.PushSequence();           // Name
        w.PushSet();                // RDN
        w.PushSequence();           // AttributeTypeAndValue
        w.WriteObjectIdentifier(new ObjectIdentifier("2.5.4.3"));
        w.WriteUtf8String(System.Text.Encoding.UTF8.GetString(cn));
        w.PopSequence();
        w.PopSet();
        w.PopSequence();
        return w.ToArray();
    }
}
