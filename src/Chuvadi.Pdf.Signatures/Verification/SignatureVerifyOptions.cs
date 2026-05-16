// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Signature verification orchestration

namespace Chuvadi.Pdf.Signatures.Verification;

/// <summary>
/// Options controlling signature verification.
/// </summary>
/// <remarks>
/// Currently a placeholder with no options. The shape is reserved for future
/// additions such as configurable trust anchors, OCSP/CRL revocation checks,
/// and clock-skew tolerances.
/// </remarks>
public sealed class SignatureVerifyOptions
{
    /// <summary>The default options.</summary>
    public static readonly SignatureVerifyOptions Default = new();
}
