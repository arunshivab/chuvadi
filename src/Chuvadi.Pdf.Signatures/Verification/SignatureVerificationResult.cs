// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Signature verification orchestration

using System;
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
        bool integrityVerified)
    {
        ArgumentNullException.ThrowIfNull(message);
        Status = status;
        Message = message;
        SignerCertificate = signerCertificate;
        IntegrityVerified = integrityVerified;
    }

    /// <summary>The overall outcome.</summary>
    public SignatureVerificationStatus Status { get; }

    /// <summary>A human-readable explanation of the result.</summary>
    public string Message { get; }

    /// <summary>
    /// The signer's certificate, when it was found inside the CMS envelope.
    /// May be null when <see cref="Status"/> is
    /// <see cref="SignatureVerificationStatus.SignerCertificateNotFound"/>.
    /// </summary>
    public X509Certificate? SignerCertificate { get; }

    /// <summary>
    /// True when the cryptographic signature checks out and the message digest
    /// matches the bytes covered by /ByteRange.
    /// </summary>
    /// <remarks>
    /// This is the strict cryptographic answer. It is set to true only for
    /// <see cref="SignatureVerificationStatus.Valid"/>. Trust evaluation
    /// (whether the signer is to be believed) is a separate concern.
    /// </remarks>
    public bool IntegrityVerified { get; }

    /// <summary>Convenience shorthand for <c>Status == Valid</c>.</summary>
    public bool IsValid => Status == SignatureVerificationStatus.Valid;
}
