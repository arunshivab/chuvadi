// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Signature verification orchestration

namespace Chuvadi.Pdf.Signatures.Verification;

/// <summary>
/// The overall outcome of verifying a PDF signature.
/// </summary>
public enum SignatureVerificationStatus
{
    /// <summary>
    /// The signature is cryptographically valid: the message digest matches the
    /// signed bytes and the signature decrypts cleanly against the signing
    /// certificate's public key.
    /// </summary>
    /// <remarks>
    /// Valid does NOT imply the signer can be trusted. That additional check —
    /// validating the certificate chain to a trusted root — is a separate
    /// (still-to-come) feature.
    /// </remarks>
    Valid = 0,

    /// <summary>The cryptographic signature does not match.</summary>
    Invalid = 1,

    /// <summary>The signature's message digest does not match the hash of the signed bytes.</summary>
    DigestMismatch = 2,

    /// <summary>The signer certificate could not be located inside the CMS envelope.</summary>
    SignerCertificateNotFound = 3,

    /// <summary>The /SubFilter is not CMS-based; Chuvadi does not know how to verify it.</summary>
    UnsupportedSubFilter = 4,

    /// <summary>The signature uses an algorithm Chuvadi does not implement.</summary>
    UnsupportedAlgorithm = 5,

    /// <summary>The signature container could not be parsed.</summary>
    MalformedSignature = 6,
}
