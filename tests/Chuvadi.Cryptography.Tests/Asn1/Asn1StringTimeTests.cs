// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Tests for string and time value codecs

using System;
using System.IO;
using System.Text;
using Chuvadi.Cryptography.Asn1;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Cryptography.Tests.Asn1;

public sealed class Asn1StringTests
{
    // ── UTF8String ────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_Utf8_Ascii()
    {
        using MemoryStream ms = new();
        Asn1String.WriteUtf8(ms, "Hello");
        Asn1String.ReadUtf8(ms.ToArray(), 0, out string value);
        value.Should().Be("Hello");
    }

    [Fact]
    public void RoundTrip_Utf8_MultibyteUnicode()
    {
        using MemoryStream ms = new();
        Asn1String.WriteUtf8(ms, "Café — façade — café au lait — ★");
        Asn1String.ReadUtf8(ms.ToArray(), 0, out string value);
        value.Should().Be("Café — façade — café au lait — ★");
    }

    [Fact]
    public void Read_Utf8_InvalidEncoding_Rejected()
    {
        // Tag 0x0C UTF8String, length 2, invalid continuation
        byte[] bytes = [0x0C, 0x02, 0xC3, 0x28];
        Action act = () => Asn1String.ReadUtf8(bytes, 0, out _);
        act.Should().Throw<Asn1Exception>().WithMessage("*UTF-8*");
    }

    // ── PrintableString ───────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_Printable_AllowedChars()
    {
        using MemoryStream ms = new();
        Asn1String.WritePrintable(ms, "Hello World 123 (+,-./:=?)");
        Asn1String.ReadPrintable(ms.ToArray(), 0, out string value);
        value.Should().Be("Hello World 123 (+,-./:=?)");
    }

    [Fact]
    public void Write_Printable_DisallowedChar_Rejected()
    {
        using MemoryStream ms = new();
        Action act = () => Asn1String.WritePrintable(ms, "no@allowed");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Read_Printable_IllegalChar_Rejected()
    {
        // 0x40 '@' is not legal in PrintableString
        byte[] bytes = [0x13, 0x01, 0x40];
        Action act = () => Asn1String.ReadPrintable(bytes, 0, out _);
        act.Should().Throw<Asn1Exception>().WithMessage("*illegal byte*");
    }

    // ── IA5String ─────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_IA5_AllAscii()
    {
        using MemoryStream ms = new();
        Asn1String.WriteIA5(ms, "user@example.com");
        Asn1String.ReadIA5(ms.ToArray(), 0, out string value);
        value.Should().Be("user@example.com");
    }

    [Fact]
    public void Write_IA5_NonAscii_Rejected()
    {
        using MemoryStream ms = new();
        Action act = () => Asn1String.WriteIA5(ms, "café");
        act.Should().Throw<ArgumentException>();
    }

    // ── BMPString ─────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_Bmp_BasicLatin()
    {
        using MemoryStream ms = new();
        Asn1String.WriteBmp(ms, "Hello");
        Asn1String.ReadBmp(ms.ToArray(), 0, out string value);
        value.Should().Be("Hello");
    }

    [Fact]
    public void RoundTrip_Bmp_Cyrillic()
    {
        using MemoryStream ms = new();
        Asn1String.WriteBmp(ms, "Привет");
        Asn1String.ReadBmp(ms.ToArray(), 0, out string value);
        value.Should().Be("Привет");
    }

    [Fact]
    public void Read_Bmp_OddLength_Rejected()
    {
        // Length must be even for UTF-16
        byte[] bytes = [0x1E, 0x03, 0x00, 0x41, 0x00];
        Action act = () => Asn1String.ReadBmp(bytes, 0, out _);
        act.Should().Throw<Asn1Exception>().WithMessage("*multiple of 2*");
    }
}

public sealed class Asn1TimeTests
{
    // ── UTCTime ───────────────────────────────────────────────────────────

    [Fact]
    public void Write_UtcTime_RoundTrip2020()
    {
        DateTimeOffset original = new(2020, 6, 15, 12, 30, 45, TimeSpan.Zero);
        using MemoryStream ms = new();
        Asn1Time.WriteUtcTime(ms, original);
        Asn1Time.ReadUtcTime(ms.ToArray(), 0, out DateTimeOffset decoded);
        decoded.Should().Be(original);
    }

    [Fact]
    public void Write_UtcTime_2049_Boundary()
    {
        DateTimeOffset original = new(2049, 12, 31, 23, 59, 59, TimeSpan.Zero);
        using MemoryStream ms = new();
        Asn1Time.WriteUtcTime(ms, original);
        // YY = 49
        Encoding.ASCII.GetString(ms.ToArray(), 2, 12).Should().Be("491231235959");
        Asn1Time.ReadUtcTime(ms.ToArray(), 0, out DateTimeOffset decoded);
        decoded.Year.Should().Be(2049);
    }

    [Fact]
    public void Write_UtcTime_OutOfRange_Throws()
    {
        DateTimeOffset old = new(1949, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using MemoryStream ms = new();
        Action act = () => Asn1Time.WriteUtcTime(ms, old);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Read_UtcTime_1999_YearMap()
    {
        // YY 99 → 1999, with seconds.
        // Format: "991231235959Z" — length 13
        string text = "991231235959Z";
        byte[] content = Encoding.ASCII.GetBytes(text);
        byte[] full = new byte[2 + content.Length];
        full[0] = 0x17; // UTCTime tag
        full[1] = (byte)content.Length;
        Array.Copy(content, 0, full, 2, content.Length);

        Asn1Time.ReadUtcTime(full, 0, out DateTimeOffset decoded);
        decoded.Year.Should().Be(1999);
    }

    [Fact]
    public void Read_UtcTime_2000_YearMap()
    {
        string text = "000101000000Z";
        byte[] content = Encoding.ASCII.GetBytes(text);
        byte[] full = new byte[2 + content.Length];
        full[0] = 0x17;
        full[1] = (byte)content.Length;
        Array.Copy(content, 0, full, 2, content.Length);

        Asn1Time.ReadUtcTime(full, 0, out DateTimeOffset decoded);
        decoded.Year.Should().Be(2000);
    }

    // ── GeneralizedTime ───────────────────────────────────────────────────

    [Fact]
    public void Write_GeneralizedTime_RoundTrip()
    {
        DateTimeOffset original = new(2050, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using MemoryStream ms = new();
        Asn1Time.WriteGeneralizedTime(ms, original);
        Asn1Time.ReadGeneralizedTime(ms.ToArray(), 0, out DateTimeOffset decoded);
        decoded.Should().Be(original);
    }

    [Fact]
    public void Write_GeneralizedTime_FullCentury()
    {
        DateTimeOffset original = new(2100, 12, 31, 23, 59, 59, TimeSpan.Zero);
        using MemoryStream ms = new();
        Asn1Time.WriteGeneralizedTime(ms, original);
        Asn1Time.ReadGeneralizedTime(ms.ToArray(), 0, out DateTimeOffset decoded);
        decoded.Should().Be(original);
    }

    [Fact]
    public void Read_GeneralizedTime_WithFractionalSeconds()
    {
        // CAdES timestamps include fractional seconds — we accept these on read
        string text = "20200615123045.500Z";
        byte[] content = Encoding.ASCII.GetBytes(text);
        byte[] full = new byte[2 + content.Length];
        full[0] = 0x18;  // GeneralizedTime tag
        full[1] = (byte)content.Length;
        Array.Copy(content, 0, full, 2, content.Length);

        Asn1Time.ReadGeneralizedTime(full, 0, out DateTimeOffset decoded);
        decoded.Year.Should().Be(2020);
        decoded.Millisecond.Should().Be(500);
    }
}
