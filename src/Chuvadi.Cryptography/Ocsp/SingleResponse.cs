// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 6960 §4.2.1 — SingleResponse
// PHASE: Phase 1.1.4 — OCSP

using System;

namespace Chuvadi.Cryptography.Ocsp;

/// <summary>
/// One certificate's entry within an OCSP response's <c>responses</c> field.
/// </summary>
/// <remarks>
/// RFC 6960 §4.2.1:
/// <code>
/// SingleResponse ::= SEQUENCE {
///   certID            CertID,
///   certStatus        CertStatus,
///   thisUpdate        GeneralizedTime,
///   nextUpdate        [0] EXPLICIT GeneralizedTime OPTIONAL,
///   singleExtensions  [1] EXPLICIT Extensions OPTIONAL
/// }
/// </code>
/// </remarks>
public sealed class SingleResponse
{
    /// <summary>Initialises a new single response.</summary>
    public SingleResponse(CertId certId, CertStatus status,
        DateTimeOffset thisUpdate, DateTimeOffset? nextUpdate)
    {
        ArgumentNullException.ThrowIfNull(certId);
        ArgumentNullException.ThrowIfNull(status);
        CertId = certId;
        Status = status;
        ThisUpdate = thisUpdate;
        NextUpdate = nextUpdate;
    }

    /// <summary>The certificate this entry is about.</summary>
    public CertId CertId { get; }

    /// <summary>The responder's verdict.</summary>
    public CertStatus Status { get; }

    /// <summary>The time the status information was generated.</summary>
    public DateTimeOffset ThisUpdate { get; }

    /// <summary>The latest time newer information will be available. May be absent.</summary>
    public DateTimeOffset? NextUpdate { get; }
}
