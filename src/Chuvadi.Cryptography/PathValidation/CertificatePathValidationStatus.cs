// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — X.509 path validation

namespace Chuvadi.Cryptography.PathValidation;

/// <summary>The outcome of validating a single certificate path.</summary>
public enum CertificatePathValidationStatus
{
    /// <summary>The path validates: every link is sound and chains to a trust anchor.</summary>
    Valid = 0,

    /// <summary>No path from the leaf to any trust anchor was found.</summary>
    NoPathFound = 1,

    /// <summary>Signature verification failed on at least one link in the chain.</summary>
    SignatureInvalid = 2,

    /// <summary>A certificate in the path has expired at the validation time.</summary>
    CertificateExpired = 3,

    /// <summary>A certificate in the path is not yet valid at the validation time.</summary>
    CertificateNotYetValid = 4,

    /// <summary>An intermediate certificate's BasicConstraints does not assert cA=TRUE.</summary>
    IntermediateNotACa = 5,

    /// <summary>The leaf certificate is missing the digitalSignature key-usage bit.</summary>
    LeafKeyUsageInvalid = 6,

    /// <summary>An intermediate certificate is missing the keyCertSign key-usage bit.</summary>
    IntermediateKeyUsageInvalid = 7,

    /// <summary>A path-length constraint was exceeded.</summary>
    PathLengthExceeded = 8,

    /// <summary>A critical extension in some certificate is not recognised by Chuvadi.</summary>
    UnsupportedCriticalExtension = 9,

    /// <summary>Name chaining is broken: an issuer DN does not match the next subject DN.</summary>
    NameChainBroken = 10,
}
