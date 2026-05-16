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
    /// The signature is cryptographically valid. If a trust store was supplied,
    /// the signer's certificate chain also validates to a trust anchor.
    /// </summary>
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

    /// <summary>
    /// The signature is cryptographically valid, but the signer's certificate
    /// does not chain to any trust anchor in the supplied trust store.
    /// </summary>
    TrustChainBroken = 7,

    /// <summary>
    /// The signature is cryptographically valid, but a certificate in the
    /// chain has expired or is not yet valid at the validation time.
    /// </summary>
    TrustChainCertificateOutOfValidity = 8,

    /// <summary>
    /// The signature is cryptographically valid, but the signer's certificate
    /// chain failed RFC 5280 §6.1 path validation for a reason other than
    /// validity-period violation.
    /// </summary>
    TrustChainInvalid = 9,
}
