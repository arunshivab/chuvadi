// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Chuvadi.Cryptography ASN.1 foundation
//
// Exhaustive tests for the ASN.1 tag and length codec. Every clause of
// X.690 §8.1.2 (tag) and §8.1.3 (length) that we enforce gets a positive
// test (legal encoding accepted) and a negative test (illegal encoding
// rejected with Asn1Exception, not some other exception type).

using System;
using System.IO;
using Chuvadi.Cryptography.Asn1;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.Asn1;

// ────────────────────────────────────────────────────────────────────────
// READ — tag encoding
// ────────────────────────────────────────────────────────────────────────

public sealed class Asn1TagLengthReadTagTests
{
    [Fact]
    public void Read_PrimitiveUniversalInteger_TagNumberZero()
    {
        // Tag 0 doesn't exist in practice but verifies the lowest possible encoding.
        byte[] input = [0x00, 0x00];
        Asn1TagLength.Read(input, 0, out Asn1Tag tag, out int contentOffset, out int contentLength);
        tag.TagClass.Should().Be(Asn1TagClass.Universal);
        tag.IsConstructed.Should().BeFalse();
        tag.TagNumber.Should().Be(0);
        contentOffset.Should().Be(2);
        contentLength.Should().Be(0);
    }

    [Fact]
    public void Read_PrimitiveUniversalInteger_ShortFormTag()
    {
        // 0x02 = universal class, primitive, tag number 2 (INTEGER)
        byte[] input = [0x02, 0x01, 0x42];
        Asn1TagLength.Read(input, 0, out Asn1Tag tag, out int contentOffset, out int contentLength);
        tag.Should().Be(Asn1Tag.Primitive(Asn1UniversalTag.Integer));
        contentOffset.Should().Be(2);
        contentLength.Should().Be(1);
    }

    [Fact]
    public void Read_ConstructedSequence_SetsIsConstructed()
    {
        // 0x30 = universal class, constructed, tag number 16 (SEQUENCE)
        byte[] input = [0x30, 0x00];
        Asn1TagLength.Read(input, 0, out Asn1Tag tag, out _, out _);
        tag.IsConstructed.Should().BeTrue();
        tag.TagNumber.Should().Be(16);
    }

    [Fact]
    public void Read_ConstructedSet_HasTagNumber17()
    {
        byte[] input = [0x31, 0x00];
        Asn1TagLength.Read(input, 0, out Asn1Tag tag, out _, out _);
        tag.TagNumber.Should().Be(17);
        tag.IsConstructed.Should().BeTrue();
    }

    [Fact]
    public void Read_ContextSpecificTagZero_DecodesClass()
    {
        // 0x80 = context-specific, primitive, tag number 0
        byte[] input = [0x80, 0x00];
        Asn1TagLength.Read(input, 0, out Asn1Tag tag, out _, out _);
        tag.TagClass.Should().Be(Asn1TagClass.ContextSpecific);
        tag.TagNumber.Should().Be(0);
    }

    [Fact]
    public void Read_ContextSpecificConstructedTag3_DecodesClassAndShape()
    {
        // 0xA3 = context-specific, constructed, tag number 3
        byte[] input = [0xA3, 0x00];
        Asn1TagLength.Read(input, 0, out Asn1Tag tag, out _, out _);
        tag.TagClass.Should().Be(Asn1TagClass.ContextSpecific);
        tag.IsConstructed.Should().BeTrue();
        tag.TagNumber.Should().Be(3);
    }

    [Fact]
    public void Read_ApplicationClass_Decoded()
    {
        // 0x40 = application, primitive, tag 0
        byte[] input = [0x40, 0x00];
        Asn1TagLength.Read(input, 0, out Asn1Tag tag, out _, out _);
        tag.TagClass.Should().Be(Asn1TagClass.Application);
    }

    [Fact]
    public void Read_PrivateClass_Decoded()
    {
        // 0xC0 = private, primitive, tag 0
        byte[] input = [0xC0, 0x00];
        Asn1TagLength.Read(input, 0, out Asn1Tag tag, out _, out _);
        tag.TagClass.Should().Be(Asn1TagClass.Private);
    }

    [Fact]
    public void Read_LongFormTag_SingleContinuationByte()
    {
        // 0x5F = application primitive, tag-number-low = 31 → long form
        // Next byte: 0x1F = 31 with continuation bit clear → tag number 31
        byte[] input = [0x5F, 0x1F, 0x00];
        Asn1TagLength.Read(input, 0, out Asn1Tag tag, out _, out _);
        tag.TagClass.Should().Be(Asn1TagClass.Application);
        tag.TagNumber.Should().Be(31);
    }

    [Fact]
    public void Read_LongFormTag_TwoContinuationBytes()
    {
        // tag number 128 = 0b10000000 = 7-bit base-128 → bytes 0x81, 0x00
        byte[] input = [0x5F, 0x81, 0x00, 0x00];
        Asn1TagLength.Read(input, 0, out Asn1Tag tag, out _, out _);
        tag.TagNumber.Should().Be(128);
    }

    [Fact]
    public void Read_LongFormTag_BoundaryValue16384()
    {
        // tag number 16384 = 0x4000 = 0b1000000_0000000 → three bytes 0x81, 0x80, 0x00
        byte[] input = [0x5F, 0x81, 0x80, 0x00, 0x00];
        Asn1TagLength.Read(input, 0, out Asn1Tag tag, out _, out _);
        tag.TagNumber.Should().Be(16384);
    }

    [Fact]
    public void Read_LongFormTag_LeadingZeroByte_Rejected()
    {
        // 0x80 as first long-form byte is forbidden — it'd mean leading zeros.
        byte[] input = [0x5F, 0x80, 0x00];
        Action act = () => Asn1TagLength.Read(input, 0, out _, out _, out _);
        act.Should().Throw<Asn1Exception>().WithMessage("*leading zero*");
    }

    [Fact]
    public void Read_LongFormTag_NumberBelow31_Rejected()
    {
        // Long form must encode a value >= 31. Encoding 30 in long form is illegal.
        byte[] input = [0x5F, 0x1E, 0x00];
        Action act = () => Asn1TagLength.Read(input, 0, out _, out _, out _);
        act.Should().Throw<Asn1Exception>().WithMessage("*Long-form*");
    }

    [Fact]
    public void Read_LongFormTag_OverflowsInt32_Rejected()
    {
        // Five continuation bytes is too many; reject before potential overflow.
        byte[] input = [0x5F, 0x81, 0x81, 0x81, 0x81, 0x01, 0x00];
        Action act = () => Asn1TagLength.Read(input, 0, out _, out _, out _);
        act.Should().Throw<Asn1Exception>().WithMessage("*too large*");
    }

    [Fact]
    public void Read_LongFormTag_TruncatedContinuation_Rejected()
    {
        // 0x81 has continuation bit set but no further bytes follow.
        byte[] input = [0x5F, 0x81];
        Action act = () => Asn1TagLength.Read(input, 0, out _, out _, out _);
        act.Should().Throw<Asn1Exception>().WithMessage("*Unexpected end of input*");
    }
}

// ────────────────────────────────────────────────────────────────────────
// READ — length encoding
// ────────────────────────────────────────────────────────────────────────

public sealed class Asn1TagLengthReadLengthTests
{
    [Fact]
    public void Read_ShortFormLength_Zero()
    {
        byte[] input = [0x05, 0x00];
        Asn1TagLength.Read(input, 0, out _, out int contentOffset, out int contentLength);
        contentOffset.Should().Be(2);
        contentLength.Should().Be(0);
    }

    [Fact]
    public void Read_ShortFormLength_Max127()
    {
        byte[] input = new byte[2 + 127];
        input[0] = 0x04;
        input[1] = 0x7F;
        Asn1TagLength.Read(input, 0, out _, out int contentOffset, out int contentLength);
        contentOffset.Should().Be(2);
        contentLength.Should().Be(127);
    }

    [Fact]
    public void Read_LongFormLength_128_OneByte()
    {
        // 0x81 = "one length byte follows", then 0x80 = 128
        byte[] input = new byte[3 + 128];
        input[0] = 0x04;
        input[1] = 0x81;
        input[2] = 0x80;
        Asn1TagLength.Read(input, 0, out _, out int contentOffset, out int contentLength);
        contentOffset.Should().Be(3);
        contentLength.Should().Be(128);
    }

    [Fact]
    public void Read_LongFormLength_256_TwoBytes()
    {
        byte[] input = new byte[4 + 256];
        input[0] = 0x04;
        input[1] = 0x82;
        input[2] = 0x01;
        input[3] = 0x00;
        Asn1TagLength.Read(input, 0, out _, out int contentOffset, out int contentLength);
        contentOffset.Should().Be(4);
        contentLength.Should().Be(256);
    }

    [Fact]
    public void Read_IndefiniteLength_Rejected()
    {
        // 0x80 alone is the indefinite-length sentinel — forbidden in DER.
        byte[] input = [0x30, 0x80, 0x00, 0x00];
        Action act = () => Asn1TagLength.Read(input, 0, out _, out _, out _);
        act.Should().Throw<Asn1Exception>().WithMessage("*Indefinite*");
    }

    [Fact]
    public void Read_ReservedLengthOfLength_Rejected()
    {
        // 0xFF as the length byte is reserved per X.690 §8.1.3.5(c).
        byte[] input = [0x04, 0xFF, 0x01];
        Action act = () => Asn1TagLength.Read(input, 0, out _, out _, out _);
        act.Should().Throw<Asn1Exception>().WithMessage("*Reserved*");
    }

    [Fact]
    public void Read_LengthOfLengthExceedsFour_Rejected()
    {
        // 0x85 = 5 length octets, more than Int32 can hold.
        byte[] input = [0x04, 0x85, 0x00, 0x00, 0x00, 0x00, 0x00];
        Action act = () => Asn1TagLength.Read(input, 0, out _, out _, out _);
        act.Should().Throw<Asn1Exception>().WithMessage("*exceeds 4*");
    }

    [Fact]
    public void Read_LengthOctets_Truncated_Rejected()
    {
        // 0x82 promises 2 length octets but only 1 follows.
        byte[] input = [0x04, 0x82, 0x01];
        Action act = () => Asn1TagLength.Read(input, 0, out _, out _, out _);
        act.Should().Throw<Asn1Exception>().WithMessage("*Unexpected end of input*");
    }

    [Fact]
    public void Read_ContentExtendsPastBuffer_Rejected()
    {
        // Length says 10 but only 3 content bytes are present.
        byte[] input = [0x04, 0x0A, 0xAA, 0xBB, 0xCC];
        Action act = () => Asn1TagLength.Read(input, 0, out _, out _, out _);
        act.Should().Throw<Asn1Exception>().WithMessage("*past end*");
    }

    [Fact]
    public void Read_LengthOverflowsInt32_Rejected()
    {
        // Top bit set on first of four length bytes → negative when shifted in.
        byte[] input = [0x04, 0x84, 0x80, 0x00, 0x00, 0x00];
        Action act = () => Asn1TagLength.Read(input, 0, out _, out _, out _);
        act.Should().Throw<Asn1Exception>().WithMessage("*overflowed*");
    }

    [Fact]
    public void Read_OffsetPastEndOfBuffer_Throws()
    {
        byte[] input = [0x04, 0x00];
        Action act = () => Asn1TagLength.Read(input, 5, out _, out _, out _);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Read_NegativeOffset_Throws()
    {
        byte[] input = [0x04, 0x00];
        Action act = () => Asn1TagLength.Read(input, -1, out _, out _, out _);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Read_NullSource_Throws()
    {
        Action act = () => Asn1TagLength.Read(null!, 0, out _, out _, out _);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Read_EmptyBuffer_Throws()
    {
        Action act = () => Asn1TagLength.Read([], 0, out _, out _, out _);
        act.Should().Throw<Asn1Exception>().WithMessage("*Unexpected end of input*");
    }
}

// ────────────────────────────────────────────────────────────────────────
// WRITE — encoding correctness
// ────────────────────────────────────────────────────────────────────────

public sealed class Asn1TagLengthWriteTests
{
    [Fact]
    public void Write_PrimitiveInteger_ShortFormTagShortFormLength()
    {
        using MemoryStream ms = new();
        Asn1TagLength.Write(ms, Asn1Tag.Primitive(Asn1UniversalTag.Integer), 1);
        ms.ToArray().Should().Equal(0x02, 0x01);
    }

    [Fact]
    public void Write_ConstructedSequence_HasConstructedBit()
    {
        using MemoryStream ms = new();
        Asn1TagLength.Write(ms, Asn1Tag.Constructed(Asn1UniversalTag.Sequence), 0);
        ms.ToArray().Should().Equal(0x30, 0x00);
    }

    [Fact]
    public void Write_ContextSpecificConstructed3_EmitsCorrectFirstByte()
    {
        using MemoryStream ms = new();
        Asn1TagLength.Write(ms, Asn1Tag.ContextSpecific(3, isConstructed: true), 0);
        ms.ToArray().Should().Equal(0xA3, 0x00);
    }

    [Fact]
    public void Write_LongFormTag_TagNumber31_TwoBytes()
    {
        using MemoryStream ms = new();
        Asn1TagLength.Write(ms, new Asn1Tag(Asn1TagClass.Universal, false, 31), 0);
        ms.ToArray().Should().Equal(0x1F, 0x1F, 0x00);
    }

    [Fact]
    public void Write_LongFormTag_TagNumber128_ThreeBytes()
    {
        using MemoryStream ms = new();
        Asn1TagLength.Write(ms, new Asn1Tag(Asn1TagClass.Universal, false, 128), 0);
        ms.ToArray().Should().Equal(0x1F, 0x81, 0x00, 0x00);
    }

    [Fact]
    public void Write_LengthAtBoundary127_StaysShortForm()
    {
        using MemoryStream ms = new();
        Asn1TagLength.Write(ms, Asn1Tag.Primitive(Asn1UniversalTag.OctetString), 127);
        ms.ToArray().Should().Equal(0x04, 0x7F);
    }

    [Fact]
    public void Write_LengthAtBoundary128_GoesLongForm()
    {
        using MemoryStream ms = new();
        Asn1TagLength.Write(ms, Asn1Tag.Primitive(Asn1UniversalTag.OctetString), 128);
        ms.ToArray().Should().Equal(0x04, 0x81, 0x80);
    }

    [Fact]
    public void Write_LengthAtBoundary255_StillOneLengthByte()
    {
        using MemoryStream ms = new();
        Asn1TagLength.Write(ms, Asn1Tag.Primitive(Asn1UniversalTag.OctetString), 255);
        ms.ToArray().Should().Equal(0x04, 0x81, 0xFF);
    }

    [Fact]
    public void Write_LengthAtBoundary256_GoesToTwoLengthBytes()
    {
        using MemoryStream ms = new();
        Asn1TagLength.Write(ms, Asn1Tag.Primitive(Asn1UniversalTag.OctetString), 256);
        ms.ToArray().Should().Equal(0x04, 0x82, 0x01, 0x00);
    }

    [Fact]
    public void Write_LargeLength_FourLengthBytes()
    {
        using MemoryStream ms = new();
        Asn1TagLength.Write(ms, Asn1Tag.Primitive(Asn1UniversalTag.OctetString), 0x01020304);
        ms.ToArray().Should().Equal(0x04, 0x84, 0x01, 0x02, 0x03, 0x04);
    }

    [Fact]
    public void Write_NegativeLength_Throws()
    {
        using MemoryStream ms = new();
        Action act = () => Asn1TagLength.Write(ms, Asn1Tag.Primitive(Asn1UniversalTag.Integer), -1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Write_NullStream_Throws()
    {
        Action act = () => Asn1TagLength.Write(null!, Asn1Tag.Primitive(Asn1UniversalTag.Null), 0);
        act.Should().Throw<ArgumentNullException>();
    }
}

// ────────────────────────────────────────────────────────────────────────
// ROUND-TRIP — write then read returns the same tag and length
// ────────────────────────────────────────────────────────────────────────

public sealed class Asn1TagLengthRoundTripTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(31)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(16383)]
    [InlineData(16384)]
    [InlineData(0x1FFFFF)]
    public void RoundTrip_TagNumber_PreservesValue(int tagNumber)
    {
        using MemoryStream ms = new();
        Asn1Tag original = new(Asn1TagClass.Universal, false, tagNumber);
        Asn1TagLength.Write(ms, original, 0);
        byte[] bytes = ms.ToArray();
        Asn1TagLength.Read(bytes, 0, out Asn1Tag decoded, out _, out _);
        decoded.Should().Be(original);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(65535)]
    [InlineData(65536)]
    [InlineData(0x00FFFFFF)]
    [InlineData(0x01000000)]
    [InlineData(0x7FFFFFFF)]
    public void RoundTrip_Length_PreservesValue(int length)
    {
        using MemoryStream ms = new();
        Asn1TagLength.Write(ms, Asn1Tag.Primitive(Asn1UniversalTag.OctetString), length);
        byte[] header = ms.ToArray();

        // Append fake content so the read succeeds. We only care about the
        // length round-trip, not validating real content.
        byte[] full = new byte[header.Length + length];
        Buffer.BlockCopy(header, 0, full, 0, header.Length);

        Asn1TagLength.Read(full, 0, out _, out _, out int decodedLength);
        decodedLength.Should().Be(length);
    }

    [Theory]
    [InlineData(Asn1TagClass.Universal, false, 5)]
    [InlineData(Asn1TagClass.Universal, true, 16)]
    [InlineData(Asn1TagClass.Application, false, 0)]
    [InlineData(Asn1TagClass.ContextSpecific, true, 3)]
    [InlineData(Asn1TagClass.Private, false, 200)]
    public void RoundTrip_AllClasses_PreserveClassAndConstructed(
        Asn1TagClass tagClass, bool isConstructed, int tagNumber)
    {
        using MemoryStream ms = new();
        Asn1Tag original = new(tagClass, isConstructed, tagNumber);
        Asn1TagLength.Write(ms, original, 0);
        byte[] bytes = ms.ToArray();
        Asn1TagLength.Read(bytes, 0, out Asn1Tag decoded, out _, out _);
        decoded.TagClass.Should().Be(tagClass);
        decoded.IsConstructed.Should().Be(isConstructed);
        decoded.TagNumber.Should().Be(tagNumber);
    }
}

// ────────────────────────────────────────────────────────────────────────
// Asn1Tag struct — equality / hashing / factories
// ────────────────────────────────────────────────────────────────────────

public sealed class Asn1TagTests
{
    [Fact]
    public void Constructor_NegativeTagNumber_Throws()
    {
        Action act = () => new Asn1Tag(Asn1TagClass.Universal, false, -1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void EqualTags_AreEqual()
    {
        Asn1Tag a = Asn1Tag.Primitive(Asn1UniversalTag.Integer);
        Asn1Tag b = new(Asn1TagClass.Universal, false, 2);
        (a == b).Should().BeTrue();
        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void DifferentClass_NotEqual()
    {
        Asn1Tag universal = new(Asn1TagClass.Universal, false, 0);
        Asn1Tag context = new(Asn1TagClass.ContextSpecific, false, 0);
        (universal == context).Should().BeFalse();
    }

    [Fact]
    public void DifferentConstructed_NotEqual()
    {
        Asn1Tag primitive = Asn1Tag.Primitive(Asn1UniversalTag.Sequence);
        Asn1Tag constructed = Asn1Tag.Constructed(Asn1UniversalTag.Sequence);
        (primitive != constructed).Should().BeTrue();
    }

    [Fact]
    public void ToString_HasReasonableFormat()
    {
        Asn1Tag tag = Asn1Tag.ContextSpecific(2, isConstructed: true);
        tag.ToString().Should().Contain("CTX").And.Contain("C").And.Contain("2");
    }
}
