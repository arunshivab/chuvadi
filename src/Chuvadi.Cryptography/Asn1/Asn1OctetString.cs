// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ITU-T X.690 §8.7 — OCTET STRING encoding
// PHASE: Phase 1.1.4 — Chuvadi.Cryptography ASN.1 values

using System;
using System.IO;

namespace Chuvadi.Cryptography.Asn1;

/// <summary>
/// Encode and decode ASN.1 OCTET STRING values.
/// </summary>
/// <remarks>
/// X.690 §8.7 permits both primitive and constructed encodings of OCTET STRING.
/// Strict DER (§10.2) requires primitive form. Chuvadi always emits primitive
/// and accepts primitive only on the read side. Constructed OCTET STRING with
/// indefinite length (which BER allows) is the source of several historical
/// signature-validation CVEs; rejecting it eliminates that attack surface.
/// </remarks>
public static class Asn1OctetString
{
    /// <summary>Writes <paramref name="value"/> as a primitive OCTET STRING.</summary>
    public static void Write(Stream output, ReadOnlySpan<byte> value)
    {
        ArgumentNullException.ThrowIfNull(output);
        Asn1TagLength.Write(output, Asn1Tag.Primitive(Asn1UniversalTag.OctetString), value.Length);
        foreach (byte b in value)
        {
            output.WriteByte(b);
        }
    }

    /// <summary>
    /// Reads an OCTET STRING and returns its content bytes (a fresh copy).
    /// </summary>
    public static int Read(byte[] source, int offset, out byte[] value)
    {
        ArgumentNullException.ThrowIfNull(source);
        int after = Asn1TagLength.Read(source, offset, out Asn1Tag tag, out int contentOffset, out int len);
        if (tag.TagClass != Asn1TagClass.Universal ||
            tag.TagNumber != (int)Asn1UniversalTag.OctetString)
        {
            throw new Asn1Exception($"Expected OCTET STRING tag, got {tag}", offset);
        }
        if (tag.IsConstructed)
        {
            throw new Asn1Exception(
                "Constructed OCTET STRING is forbidden by DER and rejected", offset);
        }
        value = new byte[len];
        Array.Copy(source, contentOffset, value, 0, len);
        return after;
    }
}
