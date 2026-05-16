// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Public-key cryptography

namespace Chuvadi.Cryptography.PublicKey;

/// <summary>
/// Marker interface implemented by all Chuvadi public-key types.
/// </summary>
/// <remarks>
/// Concrete implementations carry the algorithm-specific key material
/// (RSA modulus + exponent, ECDSA point + curve, etc.). A SignatureVerifier
/// dispatches to the right verification routine based on the runtime type.
/// </remarks>
public interface IPublicKey
{
    /// <summary>The algorithm family this key belongs to.</summary>
    PublicKeyAlgorithm Algorithm { get; }
}

/// <summary>Public-key algorithm families Chuvadi recognises.</summary>
public enum PublicKeyAlgorithm
{
    /// <summary>RSA encryption / signature (RFC 8017).</summary>
    Rsa = 0,

    /// <summary>ECDSA over a prime field (FIPS 186-4, RFC 5480).</summary>
    Ecdsa = 1,
}
