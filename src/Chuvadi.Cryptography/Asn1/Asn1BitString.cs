// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ITU-T X.690 §8.6 — BIT STRING encoding; §11.2 — DER restrictions
// PHASE: Phase 1.1.4 — Chuvadi.Cryptography ASN.1 values

using System;
using System.IO;

namespace Chuvadi.Cryptography.Asn1;

/// <summary>
/// A decoded ASN.1 BIT STRING — an octet sequence plus a count of unused
/// trailing bits in the final octet.
/// </summary>
public sealed class BitStringValue
{
    /// <summary>Initialises a new BitStringValue.</summary>
    /// <param name="bytes">The bit string content as packed bytes, big-endian.</param>
    /// <param name="unusedBitsInFinalOctet">0..7 — bits to ignore at the end.</param>
    public BitStringValue(byte[] bytes, int unusedBitsInFinalOctet)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (unusedBitsInFinalOctet < 0 || unusedBitsInFinalOctet > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(unusedBitsInFinalOctet),
                "Unused bits count must be 0..7.");
        }
        if (bytes.Length == 0 && unusedBitsInFinalOctet != 0)
        {
            throw new ArgumentException(
                "Empty bit string must declare zero unused bits.", nameof(bytes));
        }

        Bytes = bytes;
        UnusedBitsInFinalOctet = unusedBitsInFinalOctet;
    }

    /// <summary>The packed bytes (big-endian bit ordering).</summary>
    public byte[] Bytes { get; }

    /// <summary>Number of bits in the final octet that are not part of the value.</summary>
    public int UnusedBitsInFinalOctet { get; }

    /// <summary>Total number of bits represented.</summary>
    public int BitLength => (Bytes.Length * 8) - UnusedBitsInFinalOctet;
}

/// <summary>
/// Encode and decode ASN.1 BIT STRING values.
/// </summary>
/// <remarks>
/// X.690 §8.6.2 encodes BIT STRING with an "unused bits" leading byte indicating
/// how many trailing bits of the final octet are padding. DER (§11.2.1) requires
/// padding bits to be zero, and forbids constructed encoding.
/// </remarks>
public static class Asn1BitString
{
    /// <summary>Writes a BIT STRING value (primitive DER form).</summary>
    public static void Write(Stream output, BitStringValue value)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(value);

        int contentLength = value.Bytes.Length + 1;  // +1 for unused-bits byte
        Asn1TagLength.Write(output, Asn1Tag.Primitive(Asn1UniversalTag.BitString), contentLength);
        output.WriteByte((byte)value.UnusedBitsInFinalOctet);
        output.Write(value.Bytes, 0, value.Bytes.Length);
    }

    /// <summary>Writes a BIT STRING from raw bytes with zero unused bits.</summary>
    public static void Write(Stream output, ReadOnlySpan<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(output);
        Asn1TagLength.Write(output, Asn1Tag.Primitive(Asn1UniversalTag.BitString), bytes.Length + 1);
        output.WriteByte(0x00);
        foreach (byte b in bytes)
        {
            output.WriteByte(b);
        }
    }

    /// <summary>Reads a BIT STRING. Enforces DER (primitive only, padding bits zero).</summary>
    public static int Read(byte[] source, int offset, out BitStringValue value)
    {
        ArgumentNullException.ThrowIfNull(source);
        int after = Asn1TagLength.Read(source, offset, out Asn1Tag tag, out int contentOffset, out int len);
        if (tag.TagClass != Asn1TagClass.Universal ||
            tag.TagNumber != (int)Asn1UniversalTag.BitString)
        {
            throw new Asn1Exception($"Expected BIT STRING tag, got {tag}", offset);
        }
        if (tag.IsConstructed)
        {
            throw new Asn1Exception("Constructed BIT STRING is forbidden by DER", offset);
        }
        if (len < 1)
        {
            throw new Asn1Exception("BIT STRING must have at least the unused-bits byte", offset);
        }

        byte unused = source[contentOffset];
        if (unused > 7)
        {
            throw new Asn1Exception(
                $"Unused-bits count must be 0..7; got {unused}", offset);
        }
        if (len == 1 && unused != 0)
        {
            throw new Asn1Exception(
                "Empty BIT STRING must have zero unused bits", offset);
        }

        int payloadLength = len - 1;
        byte[] bytes = new byte[payloadLength];
        Array.Copy(source, contentOffset + 1, bytes, 0, payloadLength);

        // DER §11.2.1: padding bits must be zero.
        if (payloadLength > 0 && unused > 0)
        {
            byte mask = (byte)((1 << unused) - 1);
            byte finalOctet = bytes[payloadLength - 1];
            if ((finalOctet & mask) != 0)
            {
                throw new Asn1Exception(
                    "DER violation: BIT STRING padding bits are not zero", offset);
            }
        }

        value = new BitStringValue(bytes, unused);
        return after;
    }
}
