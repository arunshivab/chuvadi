// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 6960 §4.2.1 — OCSPResponseStatus
// PHASE: Phase 1.1.4 — OCSP

namespace Chuvadi.Cryptography.Ocsp;

/// <summary>The top-level status of an OCSP response.</summary>
public enum OcspResponseStatus
{
    /// <summary>Response has valid confirmations.</summary>
    Successful = 0,

    /// <summary>Illegal confirmation request.</summary>
    MalformedRequest = 1,

    /// <summary>Internal error in issuer.</summary>
    InternalError = 2,

    /// <summary>Try again later.</summary>
    TryLater = 3,

    /// <summary>Must sign the request.</summary>
    SigRequired = 5,

    /// <summary>Request unauthorised.</summary>
    Unauthorized = 6,
}
