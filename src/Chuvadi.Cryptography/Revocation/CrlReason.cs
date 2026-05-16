// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5280 §5.3.1 — Reason Code
// PHASE: Phase 1.1.4 — CRL parsing

namespace Chuvadi.Cryptography.Revocation;

/// <summary>
/// The reason a certificate was revoked, as encoded in the per-entry
/// <c>reasonCode</c> CRL extension (OID 2.5.29.21).
/// </summary>
/// <remarks>
/// RFC 5280 §5.3.1 reserves value 7. When the <c>reasonCode</c> extension is
/// absent, RFC 5280 says the reason is unspecified; Chuvadi exposes that as
/// <see cref="Unspecified"/>.
/// </remarks>
public enum CrlReason
{
    /// <summary>No reason given (extension absent or value 0).</summary>
    Unspecified = 0,

    /// <summary>Subject's private key has been compromised (value 1).</summary>
    KeyCompromise = 1,

    /// <summary>Issuing CA's private key has been compromised (value 2).</summary>
    CaCompromise = 2,

    /// <summary>Subject has changed affiliation (value 3).</summary>
    AffiliationChanged = 3,

    /// <summary>Certificate has been superseded (value 4).</summary>
    Superseded = 4,

    /// <summary>Subject is no longer operational (value 5).</summary>
    CessationOfOperation = 5,

    /// <summary>Certificate is on hold (value 6, reversible).</summary>
    CertificateHold = 6,

    /// <summary>Certificate previously on hold is removed from CRL (value 8).</summary>
    RemoveFromCrl = 8,

    /// <summary>Certificate's privileges have been withdrawn (value 9).</summary>
    PrivilegeWithdrawn = 9,

    /// <summary>Attribute authority compromise (value 10).</summary>
    AaCompromise = 10,
}
