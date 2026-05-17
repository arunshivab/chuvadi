// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 8017 §8.1 — RSASSA-PSS
// PHASE: Phase 1.2.6 — primitives batch

using System;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Hashing;
using Chuvadi.Cryptography.Oids;
using Chuvadi.Cryptography.PublicKey;
using Chuvadi.Cryptography.X509;
using RsaPrimitive = Chuvadi.Cryptography.PublicKey.RsaSigner;

namespace Chuvadi.Cryptography.Signing;

/// <summary>
/// An <see cref="ISigner"/> backed by Chuvadi's RSASSA-PSS primitive.
/// </summary>
/// <remarks>
/// <para>
/// RSASSA-PSS is the modern RSA signature scheme; unlike PKCS#1 v1.5 it
/// uses a probabilistic encoding (EMSA-PSS), which provides a tight
/// security reduction to the RSA problem. The PSS parameters
/// (hash algorithm, MGF1 hash algorithm, salt length) are encoded
/// into the X.509 <c>AlgorithmIdentifier</c> so verifiers know exactly
/// which parameter set to use.
/// </para>
/// <para>
/// Conventional defaults: MGF1 hash = signing hash; salt length = digest
/// size. The constructor takes these as parameters in case the caller
/// needs a non-default profile.
/// </para>
/// </remarks>
public sealed class RsaPssSigner : ISigner
{
    private readonly RsaPrivateKey _privateKey;
    private readonly HashAlgorithmName _mgfHash;
    private readonly int _saltLength;

    /// <summary>
    /// Creates an RSASSA-PSS signer with conventional defaults
    /// (MGF1 hash = signing hash; salt length = digest size).
    /// </summary>
    public RsaPssSigner(
        RsaPrivateKey privateKey,
        X509Certificate certificate,
        HashAlgorithmName hashAlgorithm)
        : this(privateKey, certificate, hashAlgorithm, hashAlgorithm,
              HashFactory.Create(hashAlgorithm).DigestSize)
    {
    }

    /// <summary>
    /// Creates an RSASSA-PSS signer with the given parameters.
    /// </summary>
    /// <param name="privateKey">The RSA private key.</param>
    /// <param name="certificate">The matching certificate.</param>
    /// <param name="hashAlgorithm">Hash used for the message digest and EMSA-PSS.</param>
    /// <param name="mgfHashAlgorithm">Hash used inside MGF1.</param>
    /// <param name="saltLength">Salt length in bytes.</param>
    public RsaPssSigner(
        RsaPrivateKey privateKey,
        X509Certificate certificate,
        HashAlgorithmName hashAlgorithm,
        HashAlgorithmName mgfHashAlgorithm,
        int saltLength)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        ArgumentNullException.ThrowIfNull(certificate);
        if (saltLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(saltLength), "saltLength must be non-negative.");
        }

        _privateKey = privateKey;
        _mgfHash = mgfHashAlgorithm;
        _saltLength = saltLength;
        Certificate = certificate;
        HashAlgorithm = hashAlgorithm;
        SignatureAlgorithm = new AlgorithmIdentifier(
            KnownOids.RsaSsaPss,
            BuildPssParameters(hashAlgorithm, mgfHashAlgorithm, saltLength));
    }

    /// <inheritdoc/>
    public X509Certificate Certificate { get; }

    /// <inheritdoc/>
    public HashAlgorithmName HashAlgorithm { get; }

    /// <inheritdoc/>
    public AlgorithmIdentifier SignatureAlgorithm { get; }

    /// <inheritdoc/>
    public byte[] Sign(byte[] dataToSign)
    {
        ArgumentNullException.ThrowIfNull(dataToSign);
        IHashAlgorithm h = HashFactory.Create(HashAlgorithm);
        h.Update(dataToSign);
        byte[] digest = new byte[h.DigestSize];
        h.Finish(digest);
        return RsaPrimitive.SignPss(_privateKey, HashAlgorithm, _mgfHash, _saltLength, digest);
    }

    /// <summary>
    /// Builds the <c>RSASSA-PSS-params</c> SEQUENCE per RFC 8017 §A.2.3.
    /// </summary>
    private static byte[] BuildPssParameters(
        HashAlgorithmName hashAlgorithm,
        HashAlgorithmName mgfHashAlgorithm,
        int saltLength)
    {
        // RSASSA-PSS-params ::= SEQUENCE {
        //   hashAlgorithm      [0] HashAlgorithm    DEFAULT sha1,
        //   maskGenAlgorithm   [1] MaskGenAlgorithm DEFAULT mgf1SHA1,
        //   saltLength         [2] INTEGER          DEFAULT 20,
        //   trailerField       [3] INTEGER          DEFAULT 1
        // }
        // We emit non-default fields explicitly tagged.
        Asn1Writer w = new();
        w.PushSequence();
        {
            // [0] hashAlgorithm
            w.PushExplicit(0);
            {
                w.PushSequence();
                w.WriteObjectIdentifier(HashOidFor(hashAlgorithm));
                w.WriteNull();
                w.PopSequence();
            }
            w.PopExplicit(0);

            // [1] maskGenAlgorithm = mgf1WithHash
            w.PushExplicit(1);
            {
                w.PushSequence();
                w.WriteObjectIdentifier(KnownOids.Mgf1);
                // MGF1 parameters: AlgorithmIdentifier of the inner hash
                w.PushSequence();
                w.WriteObjectIdentifier(HashOidFor(mgfHashAlgorithm));
                w.WriteNull();
                w.PopSequence();
                w.PopSequence();
            }
            w.PopExplicit(1);

            // [2] saltLength
            w.PushExplicit(2);
            w.WriteInteger(saltLength);
            w.PopExplicit(2);

            // trailerField defaults to 1; we omit it.
        }
        w.PopSequence();
        return w.ToArray();
    }

    private static ObjectIdentifier HashOidFor(HashAlgorithmName name) => name switch
    {
        HashAlgorithmName.Sha256 => KnownOids.Sha256,
        HashAlgorithmName.Sha384 => KnownOids.Sha384,
        HashAlgorithmName.Sha512 => KnownOids.Sha512,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unsupported hash for PSS."),
    };
}
