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
        CertificatePath? validatedPath = null,
        bool timestampValidated = false,
        DateTimeOffset? signatureTimestamp = null,
        X509Certificate? timestampCertificate = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        Status = status;
        Message = message;
        SignerCertificate = signerCertificate;
        IntegrityVerified = integrityVerified;
        TrustValidated = trustValidated;
        ValidatedPath = validatedPath;
        TimestampValidated = timestampValidated;
        SignatureTimestamp = signatureTimestamp;
        TimestampCertificate = timestampCertificate;
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

    /// <summary>
    /// True iff a signature timestamp (RFC 3161) was present in the CMS
    /// unsigned attributes, decoded cleanly, signature-verified, and its
    /// messageImprint matched the SignerInfo signature bytes.
    /// </summary>
    public bool TimestampValidated { get; }

    /// <summary>
    /// When a signature timestamp was present, the genTime claimed by the TSA.
    /// Even when <see cref="TimestampValidated"/> is false (e.g. the TST was
    /// present but did not verify), this carries the genTime declared by the
    /// token for debugging.
    /// </summary>
    public DateTimeOffset? SignatureTimestamp { get; }

    /// <summary>The TSA's signing certificate, when a timestamp was found.</summary>
    public X509Certificate? TimestampCertificate { get; }

    /// <summary>Convenience shorthand for <c>Status == Valid</c>.</summary>
    public bool IsValid => Status == SignatureVerificationStatus.Valid;
}
