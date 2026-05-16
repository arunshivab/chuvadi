// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5280 §5.1 — TBSCertList.revokedCertificates entry
// PHASE: Phase 1.1.4 — CRL parsing

using System;
using System.Numerics;

namespace Chuvadi.Cryptography.Revocation;

/// <summary>
/// One revocation entry from a CRL.
/// </summary>
/// <remarks>
/// RFC 5280 §5.1:
/// <code>
/// revokedCertificates SEQUENCE OF SEQUENCE {
///     userCertificate     CertificateSerialNumber,
///     revocationDate      Time,
///     crlEntryExtensions  Extensions OPTIONAL
/// } OPTIONAL
/// </code>
/// </remarks>
public sealed class RevokedCertificate
{
    /// <summary>Initialises a new revocation entry.</summary>
    public RevokedCertificate(
        BigInteger userCertificateSerial,
        DateTimeOffset revocationDate,
        CrlReason reason,
        DateTimeOffset? invalidityDate)
    {
        UserCertificateSerial = userCertificateSerial;
        RevocationDate = revocationDate;
        Reason = reason;
        InvalidityDate = invalidityDate;
    }

    /// <summary>The revoked certificate's serial number.</summary>
    public BigInteger UserCertificateSerial { get; }

    /// <summary>The time the certificate was revoked.</summary>
    public DateTimeOffset RevocationDate { get; }

    /// <summary>The revocation reason (Unspecified when the extension is absent).</summary>
    public CrlReason Reason { get; }

    /// <summary>
    /// The invalidity date from the per-entry <c>invalidityDate</c> extension
    /// (RFC 5280 §5.3.2), when present. May predate <see cref="RevocationDate"/>
    /// for revocations issued after the key was suspected compromised.
    /// </summary>
    public DateTimeOffset? InvalidityDate { get; }
}
