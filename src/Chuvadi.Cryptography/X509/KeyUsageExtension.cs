// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5280 §4.2.1.3 — Key Usage
// PHASE: Phase 1.1.4 — X.509 certificate decoder

using System;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Oids;

namespace Chuvadi.Cryptography.X509;

/// <summary>The named bits in a KeyUsage extension (RFC 5280 §4.2.1.3).</summary>
[Flags]
public enum KeyUsageFlags
{
    /// <summary>No usage permitted.</summary>
    None = 0,

    /// <summary>digitalSignature (bit 0).</summary>
    DigitalSignature = 1 << 0,

    /// <summary>nonRepudiation / contentCommitment (bit 1).</summary>
    NonRepudiation = 1 << 1,

    /// <summary>keyEncipherment (bit 2).</summary>
    KeyEncipherment = 1 << 2,

    /// <summary>dataEncipherment (bit 3).</summary>
    DataEncipherment = 1 << 3,

    /// <summary>keyAgreement (bit 4).</summary>
    KeyAgreement = 1 << 4,

    /// <summary>keyCertSign (bit 5).</summary>
    KeyCertSign = 1 << 5,

    /// <summary>cRLSign (bit 6).</summary>
    CrlSign = 1 << 6,

    /// <summary>encipherOnly (bit 7).</summary>
    EncipherOnly = 1 << 7,

    /// <summary>decipherOnly (bit 8).</summary>
    DecipherOnly = 1 << 8,
}

/// <summary>
/// The Key Usage extension — restricts the cryptographic operations the
/// certified key may participate in.
/// </summary>
/// <remarks>
/// Encoded as a named-bit BIT STRING; bits are numbered from the most-significant
/// bit of the first content octet. Per RFC 5280, this extension SHOULD be
/// marked critical when it appears.
/// </remarks>
public sealed class KeyUsageExtension
{
    /// <summary>Initialises a new KeyUsageExtension.</summary>
    public KeyUsageExtension(KeyUsageFlags usages) { Usages = usages; }

    /// <summary>The combined usage flags.</summary>
    public KeyUsageFlags Usages { get; }

    /// <summary>True when the given flag is set.</summary>
    public bool Has(KeyUsageFlags flag) => (Usages & flag) == flag;

    /// <summary>The OID identifying this extension.</summary>
    public static ObjectIdentifier Oid => KnownOids.KeyUsage;

    /// <summary>Parses a KeyUsage extension from the raw extnValue bytes.</summary>
    public static KeyUsageExtension Parse(byte[] extnValue)
    {
        ArgumentNullException.ThrowIfNull(extnValue);
        Asn1Reader r = new(extnValue);
        BitStringValue bs = r.ReadBitString();
        r.ExpectEnd();

        KeyUsageFlags flags = KeyUsageFlags.None;
        for (int bit = 0; bit < 9; bit++)
        {
            int byteIndex = bit / 8;
            int bitInByte = 7 - (bit % 8);
            if (byteIndex >= bs.Bytes.Length) { break; }
            if (((bs.Bytes[byteIndex] >> bitInByte) & 1) == 1)
            {
                flags |= (KeyUsageFlags)(1 << bit);
            }
        }
        return new KeyUsageExtension(flags);
    }
}
