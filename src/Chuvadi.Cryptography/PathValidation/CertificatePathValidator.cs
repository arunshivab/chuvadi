// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5280 §6.1 — Basic Path Validation
// PHASE: Phase 1.1.4 — X.509 path validation
//
// Scope of this initial implementation:
//   - Signature verification at every link (using SignatureVerifier)
//   - Validity period checks at a caller-supplied validation time
//   - Name chaining via DER byte equality on issuer/subject DNs
//   - BasicConstraints: intermediates must assert cA=TRUE
//   - Path length constraint enforcement
//   - KeyUsage:
//       * leaf must have digitalSignature (or nonRepudiation) set when KeyUsage is present
//       * intermediates must have keyCertSign set when KeyUsage is present
//   - Recognised critical extensions: BasicConstraints, KeyUsage, ExtendedKeyUsage,
//     SubjectKeyIdentifier, AuthorityKeyIdentifier, SubjectAltName,
//     CrlDistributionPoints, AuthorityInfoAccess. Any other critical extension
//     causes UnsupportedCriticalExtension.
//
// Deliberately NOT yet implemented (separate future sessions):
//   - Name constraints (§4.2.1.10) — they need a full subtree-matching engine
//   - Policy constraints / mappings / processing (§4.2.1.11–12, §6.1.2(d-f))
//   - Revocation: CRL fetching/validation, OCSP (§6.3, RFC 6960)
//   - Trust-anchor own-validity checks beyond what's encoded in the trust store

using System;
using System.Collections.Generic;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Hashing;
using Chuvadi.Cryptography.Oids;
using Chuvadi.Cryptography.PublicKey;
using Chuvadi.Cryptography.X509;

namespace Chuvadi.Cryptography.PathValidation;

/// <summary>
/// Validates X.509 certificate paths against a trust store, per RFC 5280 §6.1.
/// </summary>
public static class CertificatePathValidator
{
    private static readonly HashSet<string> RecognisedCriticalExtensions = new()
    {
        KnownOids.BasicConstraints.Dotted,
        KnownOids.KeyUsage.Dotted,
        KnownOids.ExtKeyUsage.Dotted,
        KnownOids.SubjectKeyIdentifier.Dotted,
        KnownOids.AuthorityKeyIdentifier.Dotted,
        KnownOids.SubjectAltName.Dotted,
        KnownOids.CrlDistributionPoints.Dotted,
        KnownOids.AuthorityInfoAccess.Dotted,
    };

    /// <summary>
    /// Validates each candidate path in turn and returns the first one that
    /// passes, or a failure result describing why no path was acceptable.
    /// </summary>
    /// <param name="paths">Candidate paths (typically from <see cref="CertificatePathBuilder"/>).</param>
    /// <param name="validationTime">The instant at which to check validity periods.</param>
    public static CertificatePathValidationResult Validate(
        IReadOnlyList<CertificatePath> paths,
        DateTimeOffset validationTime)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
        {
            return new CertificatePathValidationResult(
                CertificatePathValidationStatus.NoPathFound,
                "No candidate certificate path leads to a trust anchor.",
                validatedPath: null);
        }

        CertificatePathValidationResult? lastFailure = null;
        foreach (CertificatePath path in paths)
        {
            CertificatePathValidationResult result = ValidatePath(path, validationTime);
            if (result.IsValid) { return result; }
            lastFailure = result;
        }
        return lastFailure!;
    }

    /// <summary>Validates a single path. Public for testability.</summary>
    public static CertificatePathValidationResult ValidatePath(
        CertificatePath path,
        DateTimeOffset validationTime)
    {
        ArgumentNullException.ThrowIfNull(path);

        // RFC 5280 §6.1: traverse from the anchor down to the leaf. We iterate in
        // reverse — index N-1 is the cert signed by the anchor, index 0 is the leaf.

        // working public key starts as the trust anchor's
        SubjectPublicKeyInfo currentIssuerSpki = path.Anchor.SubjectPublicKeyInfo;
        X509Name currentIssuerName = path.Anchor.Subject;
        int? maxRemainingPath = null;  // null = no constraint yet seen

        for (int i = path.Certificates.Count - 1; i >= 0; i--)
        {
            X509Certificate cert = path.Certificates[i];
            bool isLeaf = i == 0;

            // 6.1.3(a)(1) — signature verification
            CertificatePathValidationResult? sigFail = VerifyLinkSignature(cert, currentIssuerSpki);
            if (sigFail is not null) { return sigFail; }

            // 6.1.3(a)(2) — validity period
            if (validationTime < cert.Validity.NotBefore)
            {
                return Fail(CertificatePathValidationStatus.CertificateNotYetValid,
                    $"Certificate '{cert.Subject}' is not yet valid (NotBefore {cert.Validity.NotBefore:u}).");
            }
            if (validationTime > cert.Validity.NotAfter)
            {
                return Fail(CertificatePathValidationStatus.CertificateExpired,
                    $"Certificate '{cert.Subject}' expired at {cert.Validity.NotAfter:u}.");
            }

            // 6.1.3(a)(4) — issuer DN of this cert must match the working issuer name
            if (!TrustStore.NameEquals(cert.Issuer, currentIssuerName))
            {
                return Fail(CertificatePathValidationStatus.NameChainBroken,
                    $"Issuer DN of '{cert.Subject}' does not match expected '{currentIssuerName}'.");
            }

            // 6.1.4(n) — unrecognised critical extensions reject
            CertificatePathValidationResult? extFail = CheckCriticalExtensions(cert);
            if (extFail is not null) { return extFail; }

            // Key usage check
            CertificatePathValidationResult? kuFail = CheckKeyUsage(cert, isLeaf);
            if (kuFail is not null) { return kuFail; }

            if (!isLeaf)
            {
                // 6.1.4(k) — intermediate must be a CA
                BasicConstraintsExtension? bc = TryParseBasicConstraints(cert);
                if (bc is null || !bc.IsCa)
                {
                    return Fail(CertificatePathValidationStatus.IntermediateNotACa,
                        $"Intermediate certificate '{cert.Subject}' does not assert cA=TRUE in BasicConstraints.");
                }

                // 6.1.4(l) — path length constraint
                if (maxRemainingPath is not null && maxRemainingPath.Value <= 0)
                {
                    return Fail(CertificatePathValidationStatus.PathLengthExceeded,
                        "BasicConstraints pathLenConstraint exceeded.");
                }
                if (maxRemainingPath is not null)
                {
                    maxRemainingPath = maxRemainingPath.Value - 1;
                }

                // Tighten the cap if this cert specifies a smaller one.
                if (bc.PathLenConstraint is int newCap)
                {
                    if (maxRemainingPath is null || newCap < maxRemainingPath.Value)
                    {
                        maxRemainingPath = newCap;
                    }
                }
            }

            // Walk down: this cert's public key becomes the issuer for the next link
            currentIssuerSpki = cert.Tbs.SubjectPublicKeyInfo;
            currentIssuerName = cert.Subject;
        }

        return new CertificatePathValidationResult(
            CertificatePathValidationStatus.Valid,
            $"Certificate path of length {path.Length} validates against trust anchor '{path.Anchor.Subject}'.",
            path);
    }

    /// <summary>
    /// Verifies the signature on a certificate using <paramref name="issuerSpki"/>.
    /// Returns null on success, or a failure result on error.
    /// </summary>
    private static CertificatePathValidationResult? VerifyLinkSignature(
        X509Certificate cert, SubjectPublicKeyInfo issuerSpki)
    {
        try
        {
            // Extract issuer's public key
            IPublicKey issuerKey = ExtractKey(issuerSpki);

            // Hash the TBS bytes with the cert's signature digest algorithm
            // (cert.SignatureAlgorithm names the combined hash+key OID)
            HashAlgorithmName? hashName = HashFromSignatureAlgorithm(cert.SignatureAlgorithm.Algorithm);
            if (hashName is null)
            {
                return Fail(CertificatePathValidationStatus.SignatureInvalid,
                    $"Unsupported signature algorithm '{cert.SignatureAlgorithm.Algorithm}' on certificate '{cert.Subject}'.");
            }

            IHashAlgorithm hash = HashFactory.Create(hashName.Value);
            hash.Update(cert.Tbs.RawEncoding);
            byte[] digest = new byte[hash.DigestSize];
            hash.Finish(digest);

            byte[] signatureBytes = cert.SignatureValue.Bytes;
            bool ok = SignatureVerifier.Verify(
                cert.SignatureAlgorithm,
                issuerKey,
                digest,
                signatureBytes);

            if (!ok)
            {
                return Fail(CertificatePathValidationStatus.SignatureInvalid,
                    $"Signature verification failed for certificate '{cert.Subject}'.");
            }
            return null;
        }
        catch (NotSupportedException ex)
        {
            return Fail(CertificatePathValidationStatus.SignatureInvalid,
                $"Could not verify certificate signature: {ex.Message}");
        }
        catch (ArgumentException ex)
        {
            return Fail(CertificatePathValidationStatus.SignatureInvalid,
                $"Could not verify certificate signature: {ex.Message}");
        }
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
            $"Issuer public-key algorithm {alg} is not supported.");
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

    private static CertificatePathValidationResult? CheckCriticalExtensions(X509Certificate cert)
    {
        foreach (X509Extension ext in cert.Tbs.Extensions)
        {
            if (!ext.Critical) { continue; }
            if (!RecognisedCriticalExtensions.Contains(ext.Oid.Dotted))
            {
                return Fail(CertificatePathValidationStatus.UnsupportedCriticalExtension,
                    $"Certificate '{cert.Subject}' has unrecognised critical extension {ext.Oid}.");
            }
        }
        return null;
    }

    private static CertificatePathValidationResult? CheckKeyUsage(X509Certificate cert, bool isLeaf)
    {
        X509Extension? ext = cert.Tbs.FindExtension(KnownOids.KeyUsage);
        if (ext is null) { return null; }  // absent = permissive per RFC 5280

        KeyUsageExtension ku;
        try
        {
            ku = KeyUsageExtension.Parse(ext.Value);
        }
        catch (Asn1Exception)
        {
            return Fail(CertificatePathValidationStatus.UnsupportedCriticalExtension,
                $"Malformed KeyUsage extension on '{cert.Subject}'.");
        }

        if (isLeaf)
        {
            // For a signature-verifying leaf, digitalSignature OR nonRepudiation suffices.
            bool ok = ku.Has(KeyUsageFlags.DigitalSignature) || ku.Has(KeyUsageFlags.NonRepudiation);
            if (!ok)
            {
                return Fail(CertificatePathValidationStatus.LeafKeyUsageInvalid,
                    $"Leaf certificate '{cert.Subject}' KeyUsage permits neither digitalSignature nor nonRepudiation.");
            }
        }
        else
        {
            if (!ku.Has(KeyUsageFlags.KeyCertSign))
            {
                return Fail(CertificatePathValidationStatus.IntermediateKeyUsageInvalid,
                    $"Intermediate certificate '{cert.Subject}' KeyUsage does not permit keyCertSign.");
            }
        }
        return null;
    }

    private static BasicConstraintsExtension? TryParseBasicConstraints(X509Certificate cert)
    {
        X509Extension? ext = cert.Tbs.FindExtension(KnownOids.BasicConstraints);
        if (ext is null) { return null; }
        try
        {
            return BasicConstraintsExtension.Parse(ext.Value);
        }
        catch (Asn1Exception)
        {
            return null;
        }
    }

    private static CertificatePathValidationResult Fail(
        CertificatePathValidationStatus status, string message)
        => new(status, message, validatedPath: null);
}
