// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Signature verification orchestration

using System;
using Chuvadi.Cryptography.PathValidation;
using Chuvadi.Cryptography.X509;

namespace Chuvadi.Pdf.Signatures.Verification;

/// <summary>
/// The result of verifying a PDF digital signature.
/// </summary>
public sealed class SignatureVerificationResult
{
    /// <summary>Initialises a new result.</summary>
    public SignatureVerificationResult(
        SignatureVerificationStatus status,
        string message,
        X509Certificate? signerCertificate,
        bool integrityVerified,
        bool trustValidated = false,
        CertificatePath? validatedPath = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        Status = status;
        Message = message;
        SignerCertificate = signerCertificate;
        IntegrityVerified = integrityVerified;
        TrustValidated = trustValidated;
        ValidatedPath = validatedPath;
    }

    /// <summary>The overall outcome.</summary>
    public SignatureVerificationStatus Status { get; }

    /// <summary>A human-readable explanation of the result.</summary>
    public string Message { get; }

    /// <summary>The signer's certificate, when located inside the CMS envelope.</summary>
    public X509Certificate? SignerCertificate { get; }

    /// <summary>
    /// True iff the cryptographic signature checks out AND the signed bytes' digest
    /// matches the messageDigest signed attribute. This is the strict cryptographic
    /// answer regardless of whether the signer is to be believed.
    /// </summary>
    public bool IntegrityVerified { get; }

    /// <summary>
    /// True iff <see cref="IntegrityVerified"/> is true AND the signer's certificate
    /// chain validates to a configured trust anchor per RFC 5280 §6.1.
    /// False when no trust store was supplied or path validation failed.
    /// </summary>
    public bool TrustValidated { get; }

    /// <summary>
    /// The certificate path that validated against the trust store, when
    /// <see cref="TrustValidated"/> is true.
    /// </summary>
    public CertificatePath? ValidatedPath { get; }

    /// <summary>Convenience shorthand for <c>Status == Valid</c>.</summary>
    public bool IsValid => Status == SignatureVerificationStatus.Valid;
}
