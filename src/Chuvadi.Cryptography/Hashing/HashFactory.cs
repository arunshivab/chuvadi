// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Cryptographic primitives

using System;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Oids;

namespace Chuvadi.Cryptography.Hashing;

/// <summary>
/// Constructs hash algorithm instances by name or by OID.
/// </summary>
public static class HashFactory
{
    /// <summary>Creates a hash instance for the given algorithm name.</summary>
    public static IHashAlgorithm Create(HashAlgorithmName name)
        => name switch
        {
            HashAlgorithmName.Sha256 => new Sha256(),
            HashAlgorithmName.Sha384 => new Sha512(HashAlgorithmName.Sha384),
            HashAlgorithmName.Sha512 => new Sha512(HashAlgorithmName.Sha512),
            _ => throw new ArgumentException($"Unknown hash algorithm: {name}", nameof(name)),
        };

    /// <summary>
    /// Creates a hash instance for the given OID. Recognises the digest-algorithm
    /// OIDs in <see cref="KnownOids"/>: Sha256, Sha384, Sha512.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// Thrown when the OID is a known but unsupported hash (e.g. SHA-1, SHA-3 family).
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the OID does not name any recognised hash algorithm.
    /// </exception>
    public static IHashAlgorithm CreateFromOid(ObjectIdentifier oid)
    {
        ArgumentNullException.ThrowIfNull(oid);

        if (oid.Equals(KnownOids.Sha256)) { return new Sha256(); }
        if (oid.Equals(KnownOids.Sha384)) { return new Sha512(HashAlgorithmName.Sha384); }
        if (oid.Equals(KnownOids.Sha512)) { return new Sha512(HashAlgorithmName.Sha512); }

        if (oid.Equals(KnownOids.Sha1))
        {
            throw new NotSupportedException(
                "SHA-1 is deprecated for digital signatures (RFC 8017 §8.1) and is intentionally not supported by Chuvadi.");
        }
        if (oid.Equals(KnownOids.Sha224))
        {
            throw new NotSupportedException(
                "SHA-224 is not supported by Chuvadi. Use SHA-256 instead.");
        }
        if (oid.Equals(KnownOids.Sha3_256) || oid.Equals(KnownOids.Sha3_384) || oid.Equals(KnownOids.Sha3_512))
        {
            throw new NotSupportedException(
                "SHA-3 family is not yet supported by Chuvadi.");
        }

        throw new ArgumentException(
            $"OID {oid} does not name a recognised hash algorithm.", nameof(oid));
    }

    /// <summary>
    /// Returns true when <paramref name="oid"/> names a hash algorithm Chuvadi can compute.
    /// </summary>
    public static bool IsSupportedHash(ObjectIdentifier oid)
    {
        ArgumentNullException.ThrowIfNull(oid);
        return oid.Equals(KnownOids.Sha256)
            || oid.Equals(KnownOids.Sha384)
            || oid.Equals(KnownOids.Sha512);
    }
}
