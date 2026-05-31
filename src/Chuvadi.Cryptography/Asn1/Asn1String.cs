// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ITU-T X.680 §41 — Character string types
// PHASE: Phase 1.1.4 — Chuvadi.Cryptography ASN.1 values
//
// PDF signing workflows use several ASN.1 string types: UTF8String for X.500
// names, PrintableString for legacy DN components, IA5String for email
// addresses inside certificates, BMPString for some Microsoft-issued
// certificates. T61String / TeletexString appears in older legacy chains.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Chuvadi.Cryptography.Asn1;

/// <summary>
/// Encode and decode ASN.1 character string types.
/// </summary>
/// <remarks>
/// All these types share encoding shape (tag, length, content octets); they
/// differ in character set:
/// <list type="bullet">
///   <item>UTF8String — UTF-8 encoded Unicode.</item>
///   <item>PrintableString — ASCII subset: A-Z a-z 0-9 space ' ( ) + , - . / : = ?</item>
///   <item>IA5String — full 7-bit ASCII (0-127).</item>
///   <item>T61String / TeletexString — legacy; Chuvadi reads as Latin-1 best-effort.</item>
///   <item>BMPString — UTF-16 Big-Endian Basic Multilingual Plane.</item>
/// </list>
/// </remarks>
public static class Asn1String
{
    // Encoding.UTF8 uses replacement fallback and never throws on malformed
    // input. A strict decoder is required so that invalid UTF-8 in a UTF8String
    // is rejected (the DecoderFallbackException is converted to Asn1Exception).
    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly HashSet<char> PrintableChars = BuildPrintableChars();

    private static HashSet<char> BuildPrintableChars()
    {
        HashSet<char> s = new();
        for (char c = 'A'; c <= 'Z'; c++) { s.Add(c); }
        for (char c = 'a'; c <= 'z'; c++) { s.Add(c); }
        for (char c = '0'; c <= '9'; c++) { s.Add(c); }
        foreach (char c in " '()+,-./:=?")
        {
            s.Add(c);
        }
        return s;
    }

    // ── UTF8String ────────────────────────────────────────────────────────

    /// <summary>Writes a UTF8String value.</summary>
    public static void WriteUtf8(Stream output, string value)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(value);
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Asn1TagLength.Write(output, Asn1Tag.Primitive(Asn1UniversalTag.Utf8String), bytes.Length);
        output.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Reads a UTF8String. Returns the offset just past the encoded value.</summary>
    public static int ReadUtf8(byte[] source, int offset, out string value)
    {
        ArgumentNullException.ThrowIfNull(source);
        int after = Asn1TagLength.Read(source, offset, out Asn1Tag tag, out int contentOffset, out int len);
        if (tag != Asn1Tag.Primitive(Asn1UniversalTag.Utf8String))
        {
            throw new Asn1Exception($"Expected UTF8String tag, got {tag}", offset);
        }
        try
        {
            value = StrictUtf8.GetString(source, contentOffset, len);
        }
        catch (DecoderFallbackException ex)
        {
            throw new Asn1Exception("Invalid UTF-8 in UTF8String", ex);
        }
        return after;
    }

    // ── PrintableString ───────────────────────────────────────────────────

    /// <summary>Writes a PrintableString value. Throws if any character is outside the allowed subset.</summary>
    public static void WritePrintable(Stream output, string value)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(value);
        foreach (char c in value)
        {
            if (!PrintableChars.Contains(c))
            {
                throw new ArgumentException(
                    $"Character '{c}' (U+{(int)c:X4}) is not permitted in PrintableString.",
                    nameof(value));
            }
        }
        byte[] bytes = Encoding.ASCII.GetBytes(value);
        Asn1TagLength.Write(output, Asn1Tag.Primitive(Asn1UniversalTag.PrintableString), bytes.Length);
        output.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Reads a PrintableString.</summary>
    public static int ReadPrintable(byte[] source, int offset, out string value)
    {
        ArgumentNullException.ThrowIfNull(source);
        int after = Asn1TagLength.Read(source, offset, out Asn1Tag tag, out int contentOffset, out int len);
        if (tag != Asn1Tag.Primitive(Asn1UniversalTag.PrintableString))
        {
            throw new Asn1Exception($"Expected PrintableString tag, got {tag}", offset);
        }
        for (int i = 0; i < len; i++)
        {
            byte b = source[contentOffset + i];
            if (b >= 128 || !PrintableChars.Contains((char)b))
            {
                throw new Asn1Exception(
                    $"PrintableString contains illegal byte 0x{b:X2}", offset);
            }
        }
        value = Encoding.ASCII.GetString(source, contentOffset, len);
        return after;
    }

    // ── IA5String ─────────────────────────────────────────────────────────

    /// <summary>Writes an IA5String value (7-bit ASCII).</summary>
    public static void WriteIA5(Stream output, string value)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(value);
        foreach (char c in value)
        {
            if (c > 127)
            {
                throw new ArgumentException(
                    $"Character U+{(int)c:X4} is not permitted in IA5String (must be 7-bit ASCII).",
                    nameof(value));
            }
        }
        byte[] bytes = Encoding.ASCII.GetBytes(value);
        Asn1TagLength.Write(output, Asn1Tag.Primitive(Asn1UniversalTag.IA5String), bytes.Length);
        output.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Reads an IA5String.</summary>
    public static int ReadIA5(byte[] source, int offset, out string value)
    {
        ArgumentNullException.ThrowIfNull(source);
        int after = Asn1TagLength.Read(source, offset, out Asn1Tag tag, out int contentOffset, out int len);
        if (tag != Asn1Tag.Primitive(Asn1UniversalTag.IA5String))
        {
            throw new Asn1Exception($"Expected IA5String tag, got {tag}", offset);
        }
        for (int i = 0; i < len; i++)
        {
            if (source[contentOffset + i] >= 128)
            {
                throw new Asn1Exception(
                    $"IA5String contains non-ASCII byte 0x{source[contentOffset + i]:X2}", offset);
            }
        }
        value = Encoding.ASCII.GetString(source, contentOffset, len);
        return after;
    }

    // ── BMPString (UTF-16 Big-Endian, BMP only) ───────────────────────────

    /// <summary>Writes a BMPString value (BMP only — no characters above U+FFFF).</summary>
    public static void WriteBmp(Stream output, string value)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(value);
        // Detect surrogates — BMPString cannot represent supplementary plane chars.
        foreach (char c in value)
        {
            if (char.IsSurrogate(c))
            {
                throw new ArgumentException(
                    "Surrogate characters cannot be encoded in BMPString.", nameof(value));
            }
        }
        byte[] bytes = Encoding.BigEndianUnicode.GetBytes(value);
        Asn1TagLength.Write(output, Asn1Tag.Primitive(Asn1UniversalTag.BmpString), bytes.Length);
        output.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Reads a BMPString.</summary>
    public static int ReadBmp(byte[] source, int offset, out string value)
    {
        ArgumentNullException.ThrowIfNull(source);
        int after = Asn1TagLength.Read(source, offset, out Asn1Tag tag, out int contentOffset, out int len);
        if (tag != Asn1Tag.Primitive(Asn1UniversalTag.BmpString))
        {
            throw new Asn1Exception($"Expected BMPString tag, got {tag}", offset);
        }
        if ((len % 2) != 0)
        {
            throw new Asn1Exception(
                "BMPString length must be a multiple of 2", offset);
        }
        value = Encoding.BigEndianUnicode.GetString(source, contentOffset, len);
        return after;
    }

    // ── T61String / TeletexString (legacy — read as Latin-1) ──────────────

    /// <summary>Reads a T61String / TeletexString as Latin-1 best-effort.</summary>
    public static int ReadT61(byte[] source, int offset, out string value)
    {
        ArgumentNullException.ThrowIfNull(source);
        int after = Asn1TagLength.Read(source, offset, out Asn1Tag tag, out int contentOffset, out int len);
        if (tag != Asn1Tag.Primitive(Asn1UniversalTag.T61String))
        {
            throw new Asn1Exception($"Expected T61String tag, got {tag}", offset);
        }
        value = Encoding.Latin1.GetString(source, contentOffset, len);
        return after;
    }
}
