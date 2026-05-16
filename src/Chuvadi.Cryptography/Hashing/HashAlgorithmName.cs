// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Cryptographic primitives

namespace Chuvadi.Cryptography.Hashing;

/// <summary>
/// Enumeration of the hash algorithms Chuvadi implements.
/// </summary>
/// <remarks>
/// SHA-1 is deliberately excluded. It is deprecated for new digital signatures
/// per RFC 8017 §8.1 and prohibited by eIDAS qualified-signature regulations.
/// Verification of legacy SHA-1 signatures is intentionally unsupported.
/// </remarks>
public enum HashAlgorithmName
{
    /// <summary>SHA-256 (FIPS 180-4 §6.2). 256-bit digest.</summary>
    Sha256 = 0,

    /// <summary>SHA-384 (FIPS 180-4 §6.5). 384-bit digest using SHA-512 internals.</summary>
    Sha384 = 1,

    /// <summary>SHA-512 (FIPS 180-4 §6.4). 512-bit digest.</summary>
    Sha512 = 2,
}
