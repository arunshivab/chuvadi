// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ITU-T X.690 §8.2 — BOOLEAN; §11.1 — DER restriction
// PHASE: Phase 1.1.4 — Chuvadi.Cryptography ASN.1 values

using System;
using System.IO;

namespace Chuvadi.Cryptography.Asn1;

/// <summary>
/// Encode and decode ASN.1 BOOLEAN values.
/// </summary>
/// <remarks>
/// X.690 §8.2.2 requires the contents octet to be a single byte. BER allows
/// any non-zero value to represent TRUE; DER (§11.1) restricts TRUE to exactly
/// 0xFF. Chuvadi emits DER (always 0xFF for TRUE) and accepts both BER and DER
/// on the read side, treating any non-zero content byte as TRUE.
/// </remarks>
public static class Asn1Boolean
{
    /// <summary>Writes a BOOLEAN value in DER form.</summary>
    public static void Write(Stream output, bool value)
    {
        ArgumentNullException.ThrowIfNull(output);
        Asn1TagLength.Write(output, Asn1Tag.Primitive(Asn1UniversalTag.Boolean), 1);
        output.WriteByte(value ? (byte)0xFF : (byte)0x00);
    }

    /// <summary>Reads a BOOLEAN value. Returns the offset just past the encoded value.</summary>
    public static int Read(byte[] source, int offset, out bool value)
    {
        ArgumentNullException.ThrowIfNull(source);
        int after = Asn1TagLength.Read(source, offset, out Asn1Tag tag, out int contentOffset, out int len);
        if (tag != Asn1Tag.Primitive(Asn1UniversalTag.Boolean))
        {
            throw new Asn1Exception($"Expected BOOLEAN tag, got {tag}", offset);
        }
        if (len != 1)
        {
            throw new Asn1Exception($"BOOLEAN content must be exactly 1 byte; got {len}", offset);
        }
        value = source[contentOffset] != 0;
        return after;
    }
}
