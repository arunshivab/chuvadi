// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ITU-T X.690 §8.3 — INTEGER encoding; §10.1 — DER minimum-octet rule
// PHASE: Phase 1.1.4 — Chuvadi.Cryptography ASN.1 values

using System;
using System.IO;
using System.Numerics;

namespace Chuvadi.Cryptography.Asn1;

/// <summary>
/// Encode and decode ASN.1 INTEGER values.
/// </summary>
/// <remarks>
/// X.690 §8.3 encodes INTEGER as a two's-complement big-endian byte sequence.
/// X.690 §8.3.2 requires the encoding to use the fewest possible octets: the
/// first two bytes must not both be 0x00, and must not both be 0xFF. We enforce
/// this on encode (always emit minimum length) and on decode (reject
/// non-minimal encodings — strict DER).
/// <para>
/// Backed by <see cref="BigInteger"/> so the full range of public-key moduli,
/// signature values, and certificate serial numbers (commonly 128 bits or more)
/// round-trips losslessly. Convenience overloads for <see cref="int"/> and
/// <see cref="long"/> are provided for tag numbers and small constants.
/// </para>
/// </remarks>
public static class Asn1Integer
{
    // ── Write ─────────────────────────────────────────────────────────────

    /// <summary>Writes a BigInteger as ASN.1 INTEGER (DER, minimal octets).</summary>
    public static void Write(Stream output, BigInteger value)
    {
        ArgumentNullException.ThrowIfNull(output);
        byte[] content = EncodeContent(value);
        Asn1TagLength.Write(output, Asn1Tag.Primitive(Asn1UniversalTag.Integer), content.Length);
        output.Write(content, 0, content.Length);
    }

    /// <summary>Writes a 32-bit integer as ASN.1 INTEGER (DER).</summary>
    public static void Write(Stream output, int value) => Write(output, new BigInteger(value));

    /// <summary>Writes a 64-bit integer as ASN.1 INTEGER (DER).</summary>
    public static void Write(Stream output, long value) => Write(output, new BigInteger(value));

    /// <summary>
    /// Returns the DER-encoded content octets for the given value (without tag/length).
    /// </summary>
    public static byte[] EncodeContent(BigInteger value)
    {
        // BigInteger.ToByteArray returns little-endian two's-complement bytes
        // with the minimum required length. ASN.1 needs big-endian.
        byte[] le = value.ToByteArray();
        byte[] be = new byte[le.Length];
        for (int i = 0; i < le.Length; i++)
        {
            be[i] = le[le.Length - 1 - i];
        }
        return be;
    }

    // ── Read ──────────────────────────────────────────────────────────────

    /// <summary>Reads an INTEGER. Returns the offset just past it.</summary>
    public static int Read(byte[] source, int offset, out BigInteger value)
    {
        ArgumentNullException.ThrowIfNull(source);
        int after = Asn1TagLength.Read(source, offset, out Asn1Tag tag, out int contentOffset, out int len);
        if (tag != Asn1Tag.Primitive(Asn1UniversalTag.Integer))
        {
            throw new Asn1Exception($"Expected INTEGER tag, got {tag}", offset);
        }
        value = DecodeContent(source, contentOffset, len, offset);
        return after;
    }

    /// <summary>
    /// Decodes INTEGER content octets without the tag/length wrapper. Enforces
    /// the DER minimum-octets rule.
    /// </summary>
    /// <exception cref="Asn1Exception">If the encoding is non-minimal or empty.</exception>
    public static BigInteger DecodeContent(byte[] source, int contentOffset, int length, long errorOffset)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (length == 0)
        {
            throw new Asn1Exception("INTEGER content must contain at least one octet", errorOffset);
        }

        // DER §8.3.2: with length > 1, the first 9 bits of the encoding must not
        // be all zero or all one. That is, if length >= 2:
        //   - first byte == 0x00 AND high bit of second byte == 0  → non-minimal positive
        //   - first byte == 0xFF AND high bit of second byte == 1  → non-minimal negative
        if (length >= 2)
        {
            byte b0 = source[contentOffset];
            byte b1 = source[contentOffset + 1];
            if (b0 == 0x00 && (b1 & 0x80) == 0)
            {
                throw new Asn1Exception(
                    "INTEGER not encoded in minimum octets (leading 00)", errorOffset);
            }
            if (b0 == 0xFF && (b1 & 0x80) != 0)
            {
                throw new Asn1Exception(
                    "INTEGER not encoded in minimum octets (leading FF)", errorOffset);
            }
        }

        // ASN.1 is big-endian two's complement. BigInteger wants little-endian
        // two's complement.
        byte[] le = new byte[length];
        for (int i = 0; i < length; i++)
        {
            le[i] = source[contentOffset + length - 1 - i];
        }
        return new BigInteger(le);
    }

    // ── Convenience helpers ───────────────────────────────────────────────

    /// <summary>Reads an INTEGER and converts to int, rejecting overflow.</summary>
    public static int ReadInt32(byte[] source, int offset, out int after)
    {
        after = Read(source, offset, out BigInteger value);
        if (value < int.MinValue || value > int.MaxValue)
        {
            throw new Asn1Exception("INTEGER value does not fit in Int32", offset);
        }
        return (int)value;
    }

    /// <summary>Reads an INTEGER and converts to long, rejecting overflow.</summary>
    public static long ReadInt64(byte[] source, int offset, out int after)
    {
        after = Read(source, offset, out BigInteger value);
        if (value < long.MinValue || value > long.MaxValue)
        {
            throw new Asn1Exception("INTEGER value does not fit in Int64", offset);
        }
        return (long)value;
    }
}
