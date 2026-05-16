// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5280 §4.1.2.5 — Validity
// PHASE: Phase 1.1.4 — X.509 certificate decoder

using System;
using Chuvadi.Cryptography.Asn1;

namespace Chuvadi.Cryptography.X509;

/// <summary>
/// The validity period of an X.509 certificate.
/// </summary>
/// <remarks>
/// Structure:
/// <code>
/// Validity ::= SEQUENCE {
///   notBefore  Time,
///   notAfter   Time
/// }
/// Time ::= CHOICE {
///   utcTime         UTCTime,
///   generalizedTime GeneralizedTime
/// }
/// </code>
/// Per RFC 5280 §4.1.2.5: certificates whose end date is before 2050 must use
/// UTCTime; certificates whose end date is 2050 or later must use GeneralizedTime.
/// Chuvadi tracks the original encoded form of each endpoint so it can
/// re-serialise without changing the wire format.
/// </remarks>
public sealed class Validity
{
    /// <summary>Initialises a new Validity.</summary>
    public Validity(DateTimeOffset notBefore, DateTimeOffset notAfter,
        Asn1UniversalTag notBeforeTag, Asn1UniversalTag notAfterTag)
    {
        if (notAfter < notBefore)
        {
            throw new ArgumentException("notAfter must be greater than or equal to notBefore.");
        }
        NotBefore = notBefore;
        NotAfter = notAfter;
        NotBeforeTag = notBeforeTag;
        NotAfterTag = notAfterTag;
    }

    /// <summary>The start of the validity period (inclusive).</summary>
    public DateTimeOffset NotBefore { get; }

    /// <summary>The end of the validity period (inclusive).</summary>
    public DateTimeOffset NotAfter { get; }

    /// <summary>The original encoding (UTCTime or GeneralizedTime) of NotBefore.</summary>
    public Asn1UniversalTag NotBeforeTag { get; }

    /// <summary>The original encoding (UTCTime or GeneralizedTime) of NotAfter.</summary>
    public Asn1UniversalTag NotAfterTag { get; }

    /// <summary>
    /// True when <paramref name="instant"/> lies within the validity period
    /// (inclusive at both endpoints).
    /// </summary>
    public bool IsWithin(DateTimeOffset instant)
        => instant >= NotBefore && instant <= NotAfter;

    /// <inheritdoc/>
    public override string ToString() => $"{NotBefore:u} → {NotAfter:u}";

    /// <summary>Reads a Validity from a reader positioned at its SEQUENCE.</summary>
    public static Validity Read(Asn1Reader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Asn1Reader seq = reader.ReadSequence();
        (DateTimeOffset nb, Asn1UniversalTag nbTag) = ReadTime(seq);
        (DateTimeOffset na, Asn1UniversalTag naTag) = ReadTime(seq);
        seq.ExpectEnd();
        return new Validity(nb, na, nbTag, naTag);
    }

    private static (DateTimeOffset value, Asn1UniversalTag tag) ReadTime(Asn1Reader reader)
    {
        Asn1Tag tag = reader.PeekTag();
        if (tag.TagClass != Asn1TagClass.Universal)
        {
            throw new Asn1Exception($"Time must have universal class tag, got {tag}");
        }
        Asn1UniversalTag universal = (Asn1UniversalTag)tag.TagNumber;
        return universal switch
        {
            Asn1UniversalTag.UtcTime => (reader.ReadUtcTime(), universal),
            Asn1UniversalTag.GeneralizedTime => (reader.ReadGeneralizedTime(), universal),
            _ => throw new Asn1Exception($"Time has unsupported tag {universal}"),
        };
    }
}
