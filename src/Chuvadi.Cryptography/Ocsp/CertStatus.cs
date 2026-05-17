// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 6960 §4.2.1 — CertStatus
// PHASE: Phase 1.1.4 — OCSP

using System;
using Chuvadi.Cryptography.Revocation;

namespace Chuvadi.Cryptography.Ocsp;

/// <summary>
/// The OCSP responder's verdict on one certificate.
/// </summary>
/// <remarks>
/// RFC 6960 §4.2.1:
/// <code>
/// CertStatus ::= CHOICE {
///   good     [0] IMPLICIT NULL,
///   revoked  [1] IMPLICIT RevokedInfo,
///   unknown  [2] IMPLICIT UnknownInfo
/// }
/// RevokedInfo ::= SEQUENCE {
///   revocationTime  GeneralizedTime,
///   revocationReason [0] EXPLICIT CRLReason OPTIONAL
/// }
/// </code>
/// </remarks>
public sealed class CertStatus
{
    private CertStatus(CertStatusKind kind, DateTimeOffset? revokedAt, CrlReason reason)
    {
        Kind = kind;
        RevocationTime = revokedAt;
        RevocationReason = reason;
    }

    /// <summary>The status kind.</summary>
    public CertStatusKind Kind { get; }

    /// <summary>When status is <see cref="CertStatusKind.Revoked"/>, the time of revocation.</summary>
    public DateTimeOffset? RevocationTime { get; }

    /// <summary>When status is <see cref="CertStatusKind.Revoked"/>, the reason if reported.</summary>
    public CrlReason RevocationReason { get; }

    /// <summary>Convenience: true iff the responder said this cert is OK.</summary>
    public bool IsGood => Kind == CertStatusKind.Good;

    /// <summary>Convenience: true iff the responder said this cert is revoked.</summary>
    public bool IsRevoked => Kind == CertStatusKind.Revoked;

    /// <summary>Convenience: true iff the responder reported it doesn't know about this cert.</summary>
    public bool IsUnknown => Kind == CertStatusKind.Unknown;

    /// <summary>Factory: the responder says this certificate is good.</summary>
    public static CertStatus Good() => new(CertStatusKind.Good, null, CrlReason.Unspecified);

    /// <summary>Factory: the responder says this certificate is revoked.</summary>
    public static CertStatus Revoked(DateTimeOffset revokedAt, CrlReason reason)
        => new(CertStatusKind.Revoked, revokedAt, reason);

    /// <summary>Factory: the responder does not know about this certificate.</summary>
    public static CertStatus Unknown() => new(CertStatusKind.Unknown, null, CrlReason.Unspecified);
}

/// <summary>The three possible <see cref="CertStatus.Kind"/> values.</summary>
public enum CertStatusKind
{
    /// <summary>The certificate is valid per the responder.</summary>
    Good = 0,
    /// <summary>The certificate has been revoked.</summary>
    Revoked = 1,
    /// <summary>The responder does not know about this certificate.</summary>
    Unknown = 2,
}
