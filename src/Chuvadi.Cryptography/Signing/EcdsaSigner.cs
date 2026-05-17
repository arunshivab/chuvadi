// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.2.2 — CMS signing with ECDSA

using System;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Hashing;
using Chuvadi.Cryptography.Oids;
using Chuvadi.Cryptography.PublicKey;
using Chuvadi.Cryptography.X509;
using EcdsaPrimitive = Chuvadi.Cryptography.PublicKey.EcdsaSigner;

namespace Chuvadi.Cryptography.Signing;

/// <summary>
/// An <see cref="ISigner"/> backed by Chuvadi's hand-rolled ECDSA primitive
/// (<see cref="Chuvadi.Cryptography.PublicKey.EcdsaSigner"/>).
/// </summary>
/// <remarks>
/// The signature algorithm OID is chosen from the hash:
/// <list type="bullet">
///   <item>SHA-256 → ecdsa-with-SHA256 (1.2.840.10045.4.3.2)</item>
///   <item>SHA-384 → ecdsa-with-SHA384 (1.2.840.10045.4.3.3)</item>
///   <item>SHA-512 → ecdsa-with-SHA512 (1.2.840.10045.4.3.4)</item>
/// </list>
/// Any of these can be paired with any supported curve (P-256, P-384, P-521).
/// </remarks>
public sealed class EcdsaCmsSigner : ISigner
{
    private readonly EcdsaPrivateKey _privateKey;

    /// <summary>Initialises a new signer.</summary>
    /// <param name="privateKey">The ECDSA private key.</param>
    /// <param name="certificate">
    /// The X.509 certificate whose public key matches <paramref name="privateKey"/>.
    /// Chuvadi does not verify that the cert and key match — it is the caller's
    /// responsibility to ensure consistency.
    /// </param>
    /// <param name="hashAlgorithm">SHA-256, SHA-384, or SHA-512.</param>
    public EcdsaCmsSigner(
        EcdsaPrivateKey privateKey,
        X509Certificate certificate,
        HashAlgorithmName hashAlgorithm)
        : this(privateKey, certificate, hashAlgorithm, deterministic: false)
    {
    }

    /// <summary>
    /// Same as the three-argument constructor but lets the caller opt into
    /// RFC 6979 deterministic nonces. When <paramref name="deterministic"/>
    /// is true, signatures are bit-for-bit reproducible from the same
    /// inputs and immune to RNG failures.
    /// </summary>
    public EcdsaCmsSigner(
        EcdsaPrivateKey privateKey,
        X509Certificate certificate,
        HashAlgorithmName hashAlgorithm,
        bool deterministic)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        ArgumentNullException.ThrowIfNull(certificate);
        _privateKey = privateKey;
        Certificate = certificate;
        HashAlgorithm = hashAlgorithm;
        Deterministic = deterministic;
        SignatureAlgorithm = new AlgorithmIdentifier(SignatureOidFor(hashAlgorithm), null);
    }

    /// <summary>True when nonces are derived per RFC 6979.</summary>
    public bool Deterministic { get; }

    /// <inheritdoc />
    public X509Certificate Certificate { get; }

    /// <inheritdoc />
    public HashAlgorithmName HashAlgorithm { get; }

    /// <inheritdoc />
    public AlgorithmIdentifier SignatureAlgorithm { get; }

    /// <inheritdoc />
    public byte[] Sign(byte[] dataToSign)
    {
        ArgumentNullException.ThrowIfNull(dataToSign);
        if (Deterministic)
        {
            // Hash explicitly then call the deterministic primitive.
            IHashAlgorithm h = HashFactory.Create(HashAlgorithm);
            h.Update(dataToSign);
            byte[] digest = new byte[h.DigestSize];
            h.Finish(digest);
            return EcdsaPrimitive.SignDeterministic(_privateKey, digest, HashAlgorithm);
        }
        return EcdsaPrimitive.Sign(_privateKey, HashAlgorithm, dataToSign, hashIsAlreadyDigest: false);
    }

    private static ObjectIdentifier SignatureOidFor(HashAlgorithmName h) => h switch
    {
        HashAlgorithmName.Sha256 => KnownOids.Sha256WithEcdsa,
        HashAlgorithmName.Sha384 => KnownOids.Sha384WithEcdsa,
        HashAlgorithmName.Sha512 => KnownOids.Sha512WithEcdsa,
        _ => throw new ArgumentException($"Unsupported hash algorithm: {h}", nameof(h)),
    };
}
