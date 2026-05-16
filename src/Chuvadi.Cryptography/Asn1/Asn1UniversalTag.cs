// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ITU-T X.680 §8 — Universal class tag numbers
// PHASE: Phase 1.1.4 — Chuvadi.Cryptography ASN.1 foundation

namespace Chuvadi.Cryptography.Asn1;

/// <summary>
/// Universal-class ASN.1 tag numbers as assigned by ITU-T X.680.
/// </summary>
/// <remarks>
/// Only the tags Chuvadi expects to encounter when parsing or producing
/// cryptographic structures are enumerated. Numeric values are the tag
/// numbers themselves, not full identifier octets.
/// </remarks>
public enum Asn1UniversalTag : byte
{
    /// <summary>BOOLEAN. X.680 §17.</summary>
    Boolean = 1,

    /// <summary>INTEGER. X.680 §18.</summary>
    Integer = 2,

    /// <summary>BIT STRING. X.680 §22.</summary>
    BitString = 3,

    /// <summary>OCTET STRING. X.680 §23.</summary>
    OctetString = 4,

    /// <summary>NULL. X.680 §24.</summary>
    Null = 5,

    /// <summary>OBJECT IDENTIFIER. X.680 §32.</summary>
    ObjectIdentifier = 6,

    /// <summary>UTF8String. X.680 §41.</summary>
    Utf8String = 12,

    /// <summary>SEQUENCE and SEQUENCE OF. X.680 §27.</summary>
    Sequence = 16,

    /// <summary>SET and SET OF. X.680 §28.</summary>
    Set = 17,

    /// <summary>PrintableString. X.680 §41.</summary>
    PrintableString = 19,

    /// <summary>T61String / TeletexString. X.680 §41.</summary>
    T61String = 20,

    /// <summary>IA5String. X.680 §41.</summary>
    IA5String = 22,

    /// <summary>UTCTime. X.680 §47.</summary>
    UtcTime = 23,

    /// <summary>GeneralizedTime. X.680 §46.</summary>
    GeneralizedTime = 24,

    /// <summary>BMPString. X.680 §41.</summary>
    BmpString = 30,
}
