// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Tests for high-level Asn1Reader / Asn1Writer

using System;
using System.Collections.Generic;
using System.Numerics;
using Chuvadi.Cryptography.Asn1;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.Asn1;

public sealed class Asn1WriterTests
{
    [Fact]
    public void Empty_Writer_ReturnsEmptyArray()
    {
        Asn1Writer w = new();
        w.ToArray().Should().BeEmpty();
    }

    [Fact]
    public void WriteInteger_SingleElement()
    {
        Asn1Writer w = new();
        w.WriteInteger(42);
        w.ToArray().Should().Equal(0x02, 0x01, 0x2A);
    }

    [Fact]
    public void Sequence_WithThreeIntegers()
    {
        Asn1Writer w = new();
        w.PushSequence();
        w.WriteInteger(1);
        w.WriteInteger(2);
        w.WriteInteger(3);
        w.PopSequence();
        w.ToArray().Should().Equal(
            0x30, 0x09,           // SEQUENCE length 9
            0x02, 0x01, 0x01,
            0x02, 0x01, 0x02,
            0x02, 0x01, 0x03);
    }

    [Fact]
    public void NestedSequences()
    {
        Asn1Writer w = new();
        w.PushSequence();
        w.PushSequence();
        w.WriteInteger(7);
        w.PopSequence();
        w.PopSequence();

        w.ToArray().Should().Equal(
            0x30, 0x05,           // outer SEQ length 5
            0x30, 0x03,           // inner SEQ length 3
            0x02, 0x01, 0x07);
    }

    [Fact]
    public void ExplicitTag_WrapsInner()
    {
        Asn1Writer w = new();
        w.PushExplicit(0);
        w.WriteInteger(42);
        w.PopExplicit(0);

        w.ToArray().Should().Equal(
            0xA0, 0x03,           // [0] EXPLICIT (context, constructed, tag 0)
            0x02, 0x01, 0x2A);
    }

    [Fact]
    public void Unclosed_Sequence_ToArrayThrows()
    {
        Asn1Writer w = new();
        w.PushSequence();
        w.WriteInteger(1);
        Action act = () => w.ToArray();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Pop_WithoutPush_Throws()
    {
        Asn1Writer w = new();
        Action act = () => w.PopSequence();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Pop_WrongType_Throws()
    {
        Asn1Writer w = new();
        w.PushSequence();
        Action act = () => w.PopSet();
        act.Should().Throw<InvalidOperationException>();
    }
}

public sealed class Asn1ReaderTests
{
    [Fact]
    public void EmptyBuffer_IsAtEnd()
    {
        Asn1Reader r = new(Array.Empty<byte>());
        r.IsAtEnd.Should().BeTrue();
    }

    [Fact]
    public void ReadInteger_BasicValue()
    {
        byte[] bytes = [0x02, 0x01, 0x2A];
        Asn1Reader r = new(bytes);
        r.ReadInteger().Should().Be(new BigInteger(42));
        r.IsAtEnd.Should().BeTrue();
    }

    [Fact]
    public void ReadSequence_DescendsIntoChildren()
    {
        Asn1Writer w = new();
        w.PushSequence();
        w.WriteInteger(1);
        w.WriteBoolean(true);
        w.PopSequence();

        Asn1Reader top = new(w.ToArray());
        Asn1Reader seq = top.ReadSequence();
        seq.ReadInt32().Should().Be(1);
        seq.ReadBoolean().Should().BeTrue();
        seq.ExpectEnd();
        top.ExpectEnd();
    }

    [Fact]
    public void ReadSequence_WhenTagIsNotSequence_Throws()
    {
        // INTEGER, not SEQUENCE
        byte[] bytes = [0x02, 0x01, 0x01];
        Asn1Reader r = new(bytes);
        Action act = () => r.ReadSequence();
        act.Should().Throw<Asn1Exception>();
    }

    [Fact]
    public void ExpectEnd_WhenTrailingBytes_Throws()
    {
        byte[] bytes = [0x02, 0x01, 0x01, 0xFF];  // trailing 0xFF
        Asn1Reader r = new(bytes);
        r.ReadInt32().Should().Be(1);
        Action act = () => r.ExpectEnd();
        act.Should().Throw<Asn1Exception>();
    }

    [Fact]
    public void PeekTag_DoesNotAdvance()
    {
        byte[] bytes = [0x02, 0x01, 0x05];
        Asn1Reader r = new(bytes);
        r.PeekTag().Should().Be(Asn1Tag.Primitive(Asn1UniversalTag.Integer));
        r.PeekTag().Should().Be(Asn1Tag.Primitive(Asn1UniversalTag.Integer));
        r.ReadInt32().Should().Be(5);
    }

    [Fact]
    public void HasContextSpecific_ReturnsTrueWhenPresent()
    {
        Asn1Writer w = new();
        w.PushExplicit(2);
        w.WriteInteger(99);
        w.PopExplicit(2);
        Asn1Reader r = new(w.ToArray());
        r.HasContextSpecific(2).Should().BeTrue();
        r.HasContextSpecific(5).Should().BeFalse();
    }

    [Fact]
    public void ReadExplicit_DescendsAndReadsInner()
    {
        Asn1Writer w = new();
        w.PushExplicit(0);
        w.WriteInteger(123);
        w.PopExplicit(0);

        Asn1Reader r = new(w.ToArray());
        Asn1Reader inner = r.ReadExplicit(0);
        inner.ReadInt32().Should().Be(123);
        inner.ExpectEnd();
        r.ExpectEnd();
    }

    [Fact]
    public void ReadEncoded_ReturnsCompleteElementBytes()
    {
        Asn1Writer w = new();
        w.PushSequence();
        w.WriteInteger(42);
        w.PopSequence();
        w.WriteBoolean(true);
        byte[] all = w.ToArray();

        Asn1Reader r = new(all);
        byte[] seqBytes = r.ReadEncoded();
        // The SEQUENCE encoded bytes: tag + length + content
        seqBytes.Should().Equal(0x30, 0x03, 0x02, 0x01, 0x2A);
        // Boolean still available after
        r.ReadBoolean().Should().BeTrue();
    }

    [Fact]
    public void Skip_AdvancesPastUnreadElement()
    {
        Asn1Writer w = new();
        w.WriteInteger(1);
        w.WriteBoolean(false);
        Asn1Reader r = new(w.ToArray());
        r.Skip();
        r.ReadBoolean().Should().BeFalse();
    }
}

public sealed class Asn1RoundTripIntegrationTests
{
    [Fact]
    public void RoundTrip_X509LikeStructure()
    {
        // A miniature X.509-style structure to exercise the full reader/writer pair:
        //   SEQUENCE {
        //     [0] EXPLICIT INTEGER version (2 = v3)
        //     INTEGER serialNumber
        //     SEQUENCE signature algorithm {
        //       OID
        //       NULL
        //     }
        //     ... etc
        //   }

        ObjectIdentifier sha256RsaOid = new("1.2.840.113549.1.1.11");
        BigInteger serial = BigInteger.Parse("0123456789ABCDEF0123456789ABCDEF",
            System.Globalization.NumberStyles.HexNumber);

        Asn1Writer w = new();
        w.PushSequence();  // outer

        w.PushExplicit(0);
        w.WriteInteger(2);
        w.PopExplicit(0);

        w.WriteInteger(serial);

        w.PushSequence();  // signature algorithm
        w.WriteObjectIdentifier(sha256RsaOid);
        w.WriteNull();
        w.PopSequence();

        w.PopSequence();
        byte[] encoded = w.ToArray();

        Asn1Reader r = new(encoded);
        Asn1Reader outer = r.ReadSequence();

        Asn1Reader versionExplicit = outer.ReadExplicit(0);
        versionExplicit.ReadInt32().Should().Be(2);
        versionExplicit.ExpectEnd();

        outer.ReadInteger().Should().Be(serial);

        Asn1Reader sigAlg = outer.ReadSequence();
        sigAlg.ReadObjectIdentifier().Should().Be(sha256RsaOid);
        sigAlg.ReadNull();
        sigAlg.ExpectEnd();

        outer.ExpectEnd();
        r.ExpectEnd();
    }

    [Fact]
    public void RoundTrip_NestedSets()
    {
        // SEQUENCE { SET { INTEGER 1, INTEGER 2 } }
        Asn1Writer w = new();
        w.PushSequence();
        w.PushSet();
        w.WriteInteger(1);
        w.WriteInteger(2);
        w.PopSet();
        w.PopSequence();

        Asn1Reader r = new(w.ToArray());
        Asn1Reader seq = r.ReadSequence();
        Asn1Reader set = seq.ReadSet();
        List<int> values = new();
        while (!set.IsAtEnd)
        {
            values.Add(set.ReadInt32());
        }
        values.Should().Equal(1, 2);
        seq.ExpectEnd();
        r.ExpectEnd();
    }
}
