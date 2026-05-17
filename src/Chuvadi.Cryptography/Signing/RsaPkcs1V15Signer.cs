// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — CMS signing

using System;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Hashing;
using Chuvadi.Cryptography.Oids;
using Chuvadi.Cryptography.PublicKey;
using Chuvadi.Cryptography.X509;

namespace Chuvadi.Cryptography.Signing;

/// <summary>
/// An <see cref="ISigner"/> implementation backed by Chuvadi's hand-rolled
/// RSASSA-PKCS1-v1_5 signing primitive (<see cref="RsaSigner"/>).
/// </summary>
/// <remarks>
/// Loaded from a PKCS#8 unencrypted private key plus the matching X.509
/// certificate. The signature algorithm OID is chosen from the hash:
/// <list type="bullet">
///   <item>SHA-256 → sha256WithRSAEncryption (1.2.840.113549.1.1.11)</item>
///   <item>SHA-384 → sha384WithRSAEncryption (1.2.840.113549.1.1.12)</item>
///   <item>SHA-512 → sha512WithRSAEncryption (1.2.840.113549.1.1.13)</item>
/// </list>
/// </remarks>
public sealed class RsaPkcs1V15Signer : ISigner
{
    private readonly RsaPrivateKey _privateKey;

    /// <summary>Initialises a new signer.</summary>
    /// <param name="privateKey">The RSA private key.</param>
    /// <param name="certificate">
    /// The X.509 certificate whose public key matches <paramref name="privateKey"/>.
    /// Chuvadi does not verify that the cert and key match — it is the caller's
    /// responsibility to ensure consistency.
    /// </param>
    /// <param name="hashAlgorithm">SHA-256, SHA-384, or SHA-512.</param>
    public RsaPkcs1V15Signer(
        RsaPrivateKey privateKey,
        X509Certificate certificate,
        HashAlgorithmName hashAlgorithm)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        ArgumentNullException.ThrowIfNull(certificate);
        _privateKey = privateKey;
        Certificate = certificate;
        HashAlgorithm = hashAlgorithm;
        SignatureAlgorithm = new AlgorithmIdentifier(SignatureOidFor(hashAlgorithm), null);
    }

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
        return RsaSigner.SignPkcs1v15(_privateKey, HashAlgorithm, dataToSign, hashIsAlreadyDigest: false);
    }

    private static ObjectIdentifier SignatureOidFor(HashAlgorithmName h) => h switch
    {
        HashAlgorithmName.Sha256 => KnownOids.Sha256WithRsa,
        HashAlgorithmName.Sha384 => KnownOids.Sha384WithRsa,
        HashAlgorithmName.Sha512 => KnownOids.Sha512WithRsa,
        _ => throw new ArgumentException($"Unsupported hash algorithm: {h}", nameof(h)),
    };
}
