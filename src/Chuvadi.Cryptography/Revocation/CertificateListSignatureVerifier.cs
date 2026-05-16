// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5280 §5.1 — CRL signature
// PHASE: Phase 1.1.4 — CRL parsing

using System;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Hashing;
using Chuvadi.Cryptography.Oids;
using Chuvadi.Cryptography.PublicKey;
using Chuvadi.Cryptography.X509;

namespace Chuvadi.Cryptography.Revocation;

/// <summary>
/// Verifies the signature on a <see cref="CertificateList"/> against the
/// issuing CA's public key.
/// </summary>
public static class CertificateListSignatureVerifier
{
    /// <summary>
    /// Verifies <paramref name="crl"/>'s signature using <paramref name="issuerPublicKeyInfo"/>.
    /// </summary>
    /// <returns>True iff the signature is cryptographically valid.</returns>
    public static bool Verify(CertificateList crl, SubjectPublicKeyInfo issuerPublicKeyInfo)
    {
        ArgumentNullException.ThrowIfNull(crl);
        ArgumentNullException.ThrowIfNull(issuerPublicKeyInfo);

        IPublicKey publicKey = ExtractKey(issuerPublicKeyInfo);

        HashAlgorithmName? hashName = HashFromSignatureAlgorithm(crl.SignatureAlgorithm.Algorithm);
        if (hashName is null) { return false; }

        IHashAlgorithm hash = HashFactory.Create(hashName.Value);
        hash.Update(crl.TbsRawEncoding);
        byte[] digest = new byte[hash.DigestSize];
        hash.Finish(digest);

        try
        {
            return SignatureVerifier.Verify(
                crl.SignatureAlgorithm,
                publicKey,
                digest,
                crl.SignatureValue.Bytes);
        }
        catch (NotSupportedException) { return false; }
        catch (ArgumentException) { return false; }
    }

    private static IPublicKey ExtractKey(SubjectPublicKeyInfo spki)
    {
        ObjectIdentifier alg = spki.Algorithm.Algorithm;
        if (alg.Equals(KnownOids.RsaEncryption))
        {
            return RsaPublicKey.FromSubjectPublicKeyInfo(spki);
        }
        if (alg.Equals(KnownOids.EcPublicKey))
        {
            return EcdsaPublicKey.FromSubjectPublicKeyInfo(spki);
        }
        throw new NotSupportedException(
            $"CRL issuer public-key algorithm {alg} is not supported.");
    }

    private static HashAlgorithmName? HashFromSignatureAlgorithm(ObjectIdentifier sigAlg)
    {
        if (sigAlg.Equals(KnownOids.Sha256WithRsa) || sigAlg.Equals(KnownOids.Sha256WithEcdsa))
        {
            return HashAlgorithmName.Sha256;
        }
        if (sigAlg.Equals(KnownOids.Sha384WithRsa) || sigAlg.Equals(KnownOids.Sha384WithEcdsa))
        {
            return HashAlgorithmName.Sha384;
        }
        if (sigAlg.Equals(KnownOids.Sha512WithRsa) || sigAlg.Equals(KnownOids.Sha512WithEcdsa))
        {
            return HashAlgorithmName.Sha512;
        }
        return null;
    }
}
