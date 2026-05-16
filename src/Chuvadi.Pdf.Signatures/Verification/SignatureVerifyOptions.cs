// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Signature verification orchestration

using System;
using System.Collections.Generic;
using Chuvadi.Cryptography.PathValidation;
using Chuvadi.Cryptography.Revocation;
using Chuvadi.Cryptography.X509;

namespace Chuvadi.Pdf.Signatures.Verification;

/// <summary>
/// Options controlling signature verification.
/// </summary>
public sealed class SignatureVerifyOptions
{
    /// <summary>The default options (no trust evaluation).</summary>
    public static readonly SignatureVerifyOptions Default = new();

    /// <summary>
    /// Trust anchors to validate the signer's certificate against. When null,
    /// trust evaluation is skipped and the result reports only cryptographic
    /// integrity.
    /// </summary>
    public TrustStore? TrustStore { get; init; }

    /// <summary>
    /// Extra intermediate-CA certificates available for path building, in
    /// addition to those embedded in the CMS envelope.
    /// </summary>
    public IReadOnlyList<X509Certificate>? ExtraIntermediates { get; init; }

    /// <summary>
    /// The instant at which to evaluate certificate validity. Defaults to the
    /// signing time declared by the signature, or — failing that — the current
    /// UTC time.
    /// </summary>
    public DateTimeOffset? ValidationTime { get; init; }

    /// <summary>
    /// CRLs to consult for revocation checks during path validation. May be
    /// null. CRLs embedded inside the CMS envelope are still consumed
    /// automatically (subject to <see cref="AutoExtractCmsCrls"/>); this
    /// property provides extras such as locally-cached CRLs.
    /// </summary>
    public IReadOnlyList<CertificateList>? ExtraCrls { get; init; }

    /// <summary>
    /// When true (the default), CRLs embedded in the CMS SignedData envelope
    /// are decoded and added to the revocation set. Set to false to ignore
    /// embedded CRLs and rely only on <see cref="ExtraCrls"/>.
    /// </summary>
    public bool AutoExtractCmsCrls { get; init; } = true;
}
