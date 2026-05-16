// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ITU-T X.690 §8.1.2 — Identifier octets, bits 8-7
// PHASE: Phase 1.1.4 — Chuvadi.Cryptography ASN.1 foundation

namespace Chuvadi.Cryptography.Asn1;

/// <summary>
/// ASN.1 tag class. Encoded in bits 8 and 7 of the first identifier byte
/// per ITU-T X.690 §8.1.2.
/// </summary>
/// <remarks>
/// The four classes serve distinct roles in ASN.1: <see cref="Universal"/>
/// tags are reserved for the built-in types defined by X.680 (INTEGER, OCTET
/// STRING, SEQUENCE, etc.). <see cref="Application"/>, <see cref="ContextSpecific"/>,
/// and <see cref="Private"/> tags are used by individual ASN.1 specifications
/// to disambiguate fields, with <see cref="ContextSpecific"/> being by far the
/// most common in the standards Chuvadi cares about (X.509, CMS, OCSP).
/// </remarks>
public enum Asn1TagClass : byte
{
    /// <summary>Built-in ASN.1 types defined by X.680. Bits 8-7 = 00.</summary>
    Universal = 0,

    /// <summary>Application-specific tags. Bits 8-7 = 01. Rare in modern specs.</summary>
    Application = 1,

    /// <summary>Context-specific tags, used inside SEQUENCE / CHOICE / etc. Bits 8-7 = 10.</summary>
    ContextSpecific = 2,

    /// <summary>Private-use tags. Bits 8-7 = 11. Unused in standard specs.</summary>
    Private = 3,
}
