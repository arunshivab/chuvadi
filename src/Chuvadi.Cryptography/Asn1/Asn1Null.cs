// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ITU-T X.690 §8.8 — NULL encoding
// PHASE: Phase 1.1.4 — Chuvadi.Cryptography ASN.1 values

using System;
using System.IO;

namespace Chuvadi.Cryptography.Asn1;

/// <summary>
/// Encode and decode ASN.1 NULL values.
/// </summary>
/// <remarks>
/// A NULL has no content (X.690 §8.8.2). Its encoded form is always exactly
/// the two bytes <c>05 00</c>: tag 5 universal primitive, length 0.
/// </remarks>
public static class Asn1Null
{
    /// <summary>The full DER encoding of NULL, ready to emit verbatim.</summary>
    public static readonly byte[] EncodedBytes = [0x05, 0x00];

    /// <summary>Writes a NULL value at the current position of <paramref name="output"/>.</summary>
    public static void Write(Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        Asn1TagLength.Write(output, Asn1Tag.Primitive(Asn1UniversalTag.Null), 0);
    }

    /// <summary>
    /// Reads and validates a NULL value from <paramref name="source"/> at
    /// <paramref name="offset"/>. Returns the offset just past the encoded NULL.
    /// </summary>
    public static int Read(byte[] source, int offset)
    {
        ArgumentNullException.ThrowIfNull(source);
        int after = Asn1TagLength.Read(source, offset, out Asn1Tag tag, out _, out int len);
        if (tag != Asn1Tag.Primitive(Asn1UniversalTag.Null))
        {
            throw new Asn1Exception($"Expected NULL tag, got {tag}", offset);
        }
        if (len != 0)
        {
            throw new Asn1Exception($"NULL content must be empty; got {len} bytes", offset);
        }
        return after;
    }
}
