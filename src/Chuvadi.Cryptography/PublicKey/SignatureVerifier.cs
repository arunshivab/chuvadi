// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 8017, FIPS 186-4, RFC 5754
// PHASE: Phase 1.1.4 — Public-key cryptography

using System;
using System.Numerics;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Hashing;
using Chuvadi.Cryptography.Oids;
using Chuvadi.Cryptography.X509;

namespace Chuvadi.Cryptography.PublicKey;

/// <summary>
/// Top-level signature-verification dispatcher.
/// </summary>
/// <remarks>
/// Given an algorithm identifier (typically from a CMS SignerInfo's
/// signatureAlgorithm field), a public key, the message hash, and the signature
/// bytes, dispatches to <see cref="RsaVerifier"/> or <see cref="EcdsaVerifier"/>
/// with the correct hash algorithm and PSS parameters where applicable.
/// </remarks>
public static class SignatureVerifier
{
    /// <summary>
    /// Verifies a signature given its algorithm identifier.
    /// </summary>
    /// <param name="signatureAlgorithm">
    /// The signature algorithm (e.g. <see cref="KnownOids.Sha256WithRsa"/>,
    /// <see cref="KnownOids.Sha256WithEcdsa"/>, <see cref="KnownOids.RsaSsaPss"/>).
    /// </param>
    /// <param name="publicKey">The signer's public key.</param>
    /// <param name="messageHash">
    /// The pre-computed message digest. Must match the hash algorithm embedded
    /// in <paramref name="signatureAlgorithm"/>.
    /// </param>
    /// <param name="signature">The signature bytes.</param>
    public static bool Verify(
        AlgorithmIdentifier signatureAlgorithm,
        IPublicKey publicKey,
        ReadOnlySpan<byte> messageHash,
        ReadOnlySpan<byte> signature)
    {
        ArgumentNullException.ThrowIfNull(signatureAlgorithm);
        ArgumentNullException.ThrowIfNull(publicKey);

        ObjectIdentifier alg = signatureAlgorithm.Algorithm;

        // RSA PKCS#1 v1.5 with SHA-2
        if (alg.Equals(KnownOids.Sha256WithRsa))
        {
            return DispatchRsaPkcs1v15(publicKey, HashAlgorithmName.Sha256, messageHash, signature);
        }
        if (alg.Equals(KnownOids.Sha384WithRsa))
        {
            return DispatchRsaPkcs1v15(publicKey, HashAlgorithmName.Sha384, messageHash, signature);
        }
        if (alg.Equals(KnownOids.Sha512WithRsa))
        {
            return DispatchRsaPkcs1v15(publicKey, HashAlgorithmName.Sha512, messageHash, signature);
        }

        // RSA-PSS — parameters specify hash, MGF, salt length
        if (alg.Equals(KnownOids.RsaSsaPss))
        {
            return DispatchRsaPss(signatureAlgorithm, publicKey, messageHash, signature);
        }

        // ECDSA
        if (alg.Equals(KnownOids.Sha256WithEcdsa))
        {
            return DispatchEcdsa(publicKey, messageHash, signature);
        }
        if (alg.Equals(KnownOids.Sha384WithEcdsa))
        {
            return DispatchEcdsa(publicKey, messageHash, signature);
        }
        if (alg.Equals(KnownOids.Sha512WithEcdsa))
        {
            return DispatchEcdsa(publicKey, messageHash, signature);
        }

        throw new NotSupportedException(
            $"Signature algorithm {alg} is not supported. " +
            "Supported: SHA-256/384/512 with RSA (PKCS#1 v1.5), RSA-PSS, and ECDSA.");
    }

    private static bool DispatchRsaPkcs1v15(
        IPublicKey publicKey, HashAlgorithmName hashAlg,
        ReadOnlySpan<byte> messageHash, ReadOnlySpan<byte> signature)
    {
        if (publicKey is not RsaPublicKey rsa)
        {
            throw new ArgumentException(
                $"RSA signature algorithm requires an RsaPublicKey, got {publicKey.GetType().Name}.",
                nameof(publicKey));
        }
        return RsaVerifier.VerifyPkcs1v15(rsa, hashAlg, messageHash, signature);
    }

    private static bool DispatchRsaPss(
        AlgorithmIdentifier signatureAlgorithm, IPublicKey publicKey,
        ReadOnlySpan<byte> messageHash, ReadOnlySpan<byte> signature)
    {
        if (publicKey is not RsaPublicKey rsa)
        {
            throw new ArgumentException(
                $"RSA-PSS requires an RsaPublicKey, got {publicKey.GetType().Name}.",
                nameof(publicKey));
        }

        // RFC 4055 §3.1 RSASSA-PSS-params:
        //   hashAlgorithm     [0] HashAlgorithm DEFAULT sha1,
        //   maskGenAlgorithm  [1] MaskGenAlgorithm DEFAULT mgf1SHA1,
        //   saltLength        [2] INTEGER DEFAULT 20,
        //   trailerField      [3] INTEGER DEFAULT 1
        // For PDF signatures the parameters are explicit. Defaults (SHA-1) are
        // deliberately rejected — see SHA-1 policy in HashFactory.
        HashAlgorithmName hashAlg = HashAlgorithmName.Sha256;
        HashAlgorithmName mgfHashAlg = HashAlgorithmName.Sha256;
        int saltLength = 32;
        bool parametersFound = false;

        if (signatureAlgorithm.Parameters is not null)
        {
            (hashAlg, mgfHashAlg, saltLength) = ParsePssParameters(signatureAlgorithm.Parameters);
            parametersFound = true;
        }

        if (!parametersFound)
        {
            throw new NotSupportedException(
                "RSA-PSS signature has no parameters; defaults imply SHA-1 which Chuvadi does not support.");
        }

        return RsaVerifier.VerifyPss(rsa, hashAlg, mgfHashAlg, saltLength, messageHash, signature);
    }

    private static bool DispatchEcdsa(
        IPublicKey publicKey, ReadOnlySpan<byte> messageHash, ReadOnlySpan<byte> signature)
    {
        if (publicKey is not EcdsaPublicKey ecdsa)
        {
            throw new ArgumentException(
                $"ECDSA requires an EcdsaPublicKey, got {publicKey.GetType().Name}.",
                nameof(publicKey));
        }
        return EcdsaVerifier.Verify(ecdsa, messageHash, signature);
    }

    /// <summary>
    /// Parses the RFC 4055 §3.1 RSASSA-PSS-params SEQUENCE.
    /// </summary>
    private static (HashAlgorithmName Hash, HashAlgorithmName MgfHash, int SaltLength)
        ParsePssParameters(byte[] parameters)
    {
        // SEQUENCE with three or four EXPLICIT-tagged optional fields.
        Asn1Reader r = new(parameters);
        Asn1Reader seq = r.ReadSequence();

        HashAlgorithmName hash = HashAlgorithmName.Sha256;
        HashAlgorithmName mgfHash = HashAlgorithmName.Sha256;
        int saltLength = 32;
        bool hashSeen = false;

        while (!seq.IsAtEnd)
        {
            Asn1Tag peek = seq.PeekTag();
            if (peek.TagClass != Asn1TagClass.ContextSpecific)
            {
                throw new Asn1Exception(
                    $"Expected context-specific tag inside PSS parameters; got class {peek.TagClass}.");
            }

            switch (peek.TagNumber)
            {
                case 0:
                    {
                        Asn1Reader explicit0 = seq.ReadExplicit(0);
                        Asn1Reader hashAlgSeq = explicit0.ReadSequence();
                        ObjectIdentifier hashOid = hashAlgSeq.ReadObjectIdentifier();
                        hash = OidToHash(hashOid);
                        hashSeen = true;
                        // Skip optional NULL parameters; we don't care about the rest.
                        break;
                    }
                case 1:
                    {
                        Asn1Reader explicit1 = seq.ReadExplicit(1);
                        Asn1Reader mgfSeq = explicit1.ReadSequence();
                        ObjectIdentifier mgfOid = mgfSeq.ReadObjectIdentifier();
                        if (!mgfOid.Equals(KnownOids.Mgf1))
                        {
                            throw new NotSupportedException(
                                $"Unsupported mask generation algorithm: {mgfOid}.");
                        }
                        // MGF1 parameters is an AlgorithmIdentifier naming the inner hash
                        if (!mgfSeq.IsAtEnd)
                        {
                            Asn1Reader innerHashSeq = mgfSeq.ReadSequence();
                            ObjectIdentifier innerHashOid = innerHashSeq.ReadObjectIdentifier();
                            mgfHash = OidToHash(innerHashOid);
                        }
                        break;
                    }
                case 2:
                    {
                        Asn1Reader explicit2 = seq.ReadExplicit(2);
                        BigInteger saltLenBig = explicit2.ReadInteger();
                        saltLength = (int)saltLenBig;
                        break;
                    }
                case 3:
                    // trailerField — always 1 in practice; skip
                    seq.Skip();
                    break;
                default:
                    throw new Asn1Exception(
                        $"Unexpected context-specific tag [{peek.TagNumber}] in PSS parameters.");
            }
        }

        // If hash was named but MGF wasn't, MGF hash defaults to the same hash.
        if (hashSeen) { mgfHash = mgfHash == HashAlgorithmName.Sha256 ? hash : mgfHash; }

        return (hash, mgfHash, saltLength);
    }

    private static HashAlgorithmName OidToHash(ObjectIdentifier oid)
    {
        if (oid.Equals(KnownOids.Sha256)) { return HashAlgorithmName.Sha256; }
        if (oid.Equals(KnownOids.Sha384)) { return HashAlgorithmName.Sha384; }
        if (oid.Equals(KnownOids.Sha512)) { return HashAlgorithmName.Sha512; }
        throw new NotSupportedException($"Hash algorithm {oid} is not supported.");
    }
}
