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
using Chuvadi.Cryptography.Ocsp;
using Chuvadi.Cryptography.Revocation;
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
        => Validate(paths, validationTime, crls: null);

    /// <summary>
    /// Validates each candidate path against the trust store and the supplied
    /// CRLs, returning the first one that passes.
    /// </summary>
    /// <param name="paths">Candidate paths (typically from <see cref="CertificatePathBuilder"/>).</param>
    /// <param name="validationTime">The instant at which to check validity periods.</param>
    /// <param name="crls">
    /// Optional CRLs. When non-null, every certificate in the path is checked
    /// against any CRL whose issuer matches the certificate's issuer. Revocation
    /// is opportunistic: a cert without a covering CRL is accepted (soft-fail).
    /// </param>
    public static CertificatePathValidationResult Validate(
        IReadOnlyList<CertificatePath> paths,
        DateTimeOffset validationTime,
        IReadOnlyList<CertificateList>? crls)
        => Validate(paths, validationTime, crls, ocspResponses: null);

    /// <summary>
    /// Validates each candidate path against the trust store and the supplied
    /// CRLs and OCSP responses, returning the first one that passes.
    /// </summary>
    /// <param name="paths">Candidate paths.</param>
    /// <param name="validationTime">The instant at which to check validity periods.</param>
    /// <param name="crls">CRLs to consult, or null.</param>
    /// <param name="ocspResponses">OCSP responses to consult, or null.</param>
    public static CertificatePathValidationResult Validate(
        IReadOnlyList<CertificatePath> paths,
        DateTimeOffset validationTime,
        IReadOnlyList<CertificateList>? crls,
        IReadOnlyList<OcspResponse>? ocspResponses)
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
            CertificatePathValidationResult result = ValidatePath(path, validationTime, crls, ocspResponses);
            if (result.IsValid) { return result; }
            lastFailure = result;
        }
        return lastFailure!;
    }

    /// <summary>Validates a single path. Public for testability.</summary>
    public static CertificatePathValidationResult ValidatePath(
        CertificatePath path,
        DateTimeOffset validationTime)
        => ValidatePath(path, validationTime, crls: null);

    /// <summary>Validates a single path with optional CRL revocation checking.</summary>
    public static CertificatePathValidationResult ValidatePath(
        CertificatePath path,
        DateTimeOffset validationTime,
        IReadOnlyList<CertificateList>? crls)
        => ValidatePath(path, validationTime, crls, ocspResponses: null);

    /// <summary>Validates a single path with optional CRL and OCSP revocation checking.</summary>
    public static CertificatePathValidationResult ValidatePath(
        CertificatePath path,
        DateTimeOffset validationTime,
        IReadOnlyList<CertificateList>? crls,
        IReadOnlyList<OcspResponse>? ocspResponses)
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

            // 6.3 — revocation check: try CRLs first, then OCSP. Both are opportunistic
            // (soft-fail) — a cert without applicable revocation info is accepted.
            if (crls is not null && crls.Count > 0)
            {
                CertificatePathValidationResult? revFail =
                    CheckRevocation(cert, currentIssuerName, currentIssuerSpki, crls, validationTime);
                if (revFail is not null) { return revFail; }
            }
            if (ocspResponses is not null && ocspResponses.Count > 0)
            {
                // The OCSP responder must be the cert's issuer or a delegated responder.
                // We need the issuer's full certificate to verify response signatures.
                // The issuer is the next cert "above" in the path, or the trust anchor's
                // certificate. The validator already advances currentIssuerSpki down the
                // chain, so we need to remember the issuer cert too.
                X509Certificate? issuerCert = (i == path.Certificates.Count - 1)
                    ? path.Anchor.Certificate
                    : path.Certificates[i + 1];
                if (issuerCert is not null)
                {
                    CertificatePathValidationResult? ocspFail = CheckOcspRevocation(
                        cert, issuerCert, ocspResponses, validationTime);
                    if (ocspFail is not null) { return ocspFail; }
                }
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

    private static CertificatePathValidationResult? CheckRevocation(
        X509Certificate cert,
        X509Name issuerName,
        SubjectPublicKeyInfo issuerSpki,
        IReadOnlyList<CertificateList> crls,
        DateTimeOffset validationTime)
    {
        foreach (CertificateList crl in crls)
        {
            // CRL applies only if its issuer matches this certificate's issuer.
            if (!TrustStore.NameEquals(crl.Issuer, issuerName)) { continue; }

            // CRL must be valid at the validation time.
            if (validationTime < crl.ThisUpdate) { continue; }
            if (crl.NextUpdate is DateTimeOffset next && validationTime > next) { continue; }

            // The CRL must be signed by the issuer whose name matches.
            if (!CertificateListSignatureVerifier.Verify(crl, issuerSpki)) { continue; }

            // Is this cert's serial on it?
            RevokedCertificate? entry = crl.FindRevocation(cert.Tbs.SerialNumber);
            if (entry is null) { continue; }

            // Per RFC 5280 §5.3.3 — RemoveFromCrl reverses a prior CertificateHold;
            // its presence on a delta CRL means "no longer revoked". On a base
            // CRL it is anomalous but we treat it the same way (not revoked).
            if (entry.Reason == CrlReason.RemoveFromCrl) { continue; }

            // The cert was revoked. Honour the revocation date — a cert revoked
            // AFTER the validation time is still considered valid at that earlier
            // moment (this matters for retroactive verification of old signatures).
            if (entry.RevocationDate > validationTime) { continue; }

            return Fail(CertificatePathValidationStatus.CertificateRevoked,
                $"Certificate '{cert.Subject}' was revoked at {entry.RevocationDate:u} (reason: {entry.Reason}).");
        }
        return null;
    }

    private static CertificatePathValidationResult? CheckOcspRevocation(
        X509Certificate cert,
        X509Certificate issuerCert,
        IReadOnlyList<OcspResponse> ocspResponses,
        DateTimeOffset validationTime)
    {
        foreach (OcspResponse resp in ocspResponses)
        {
            if (resp.Status != OcspResponseStatus.Successful || resp.BasicResponse is null) { continue; }
            BasicOcspResponse basic = resp.BasicResponse;

            // Verify response signature; on failure, skip this response.
            X509Certificate? responderCert =
                OcspResponseSignatureVerifier.VerifyAndIdentifyResponder(basic, issuerCert);
            if (responderCert is null) { continue; }

            foreach (SingleResponse sr in basic.Responses)
            {
                if (!CertIdMatches(sr.CertId, cert, issuerCert)) { continue; }

                // SingleResponse must be in force at the validation time.
                if (validationTime < sr.ThisUpdate) { continue; }
                if (sr.NextUpdate is DateTimeOffset next && validationTime > next) { continue; }

                if (sr.Status.IsRevoked)
                {
                    DateTimeOffset? revoked = sr.Status.RevocationTime;
                    // Honour retroactive semantics: a revocation dated AFTER the validation
                    // time doesn't apply.
                    if (revoked is DateTimeOffset r && r > validationTime) { continue; }

                    return Fail(CertificatePathValidationStatus.CertificateRevoked,
                        $"Certificate '{cert.Subject}' was revoked per OCSP at {revoked:u} (reason: {sr.Status.RevocationReason}).");
                }
                // Good or Unknown: nothing to report (soft-fail for Unknown).
                return null;
            }
        }
        return null;
    }

    private static bool CertIdMatches(CertId certId, X509Certificate cert, X509Certificate issuer)
    {
        if (certId.SerialNumber != cert.Tbs.SerialNumber) { return false; }

        // Reconstruct the hashes using the certID's hash algorithm and compare.
        HashAlgorithmName? hash = HashFromOid(certId.HashAlgorithm.Algorithm);
        if (hash is null)
        {
            // SHA-1 is mandated by RFC 6960 but Chuvadi's HashFactory refuses it.
            // For matching purposes we compute SHA-1 inline if needed.
            if (certId.HashAlgorithm.Algorithm.Equals(KnownOids.Sha1))
            {
                byte[] nameHash = Sha1.Compute(issuer.Subject.RawEncoding);
                byte[] keyHash = Sha1.Compute(issuer.Tbs.SubjectPublicKeyInfo.SubjectPublicKey.Bytes);
                return ByteArraysEqual(nameHash, certId.IssuerNameHash)
                    && ByteArraysEqual(keyHash, certId.IssuerKeyHash);
            }
            return false;
        }

        IHashAlgorithm h1 = HashFactory.Create(hash.Value);
        h1.Update(issuer.Subject.RawEncoding);
        byte[] computedNameHash = new byte[h1.DigestSize];
        h1.Finish(computedNameHash);

        IHashAlgorithm h2 = HashFactory.Create(hash.Value);
        h2.Update(issuer.Tbs.SubjectPublicKeyInfo.SubjectPublicKey.Bytes);
        byte[] computedKeyHash = new byte[h2.DigestSize];
        h2.Finish(computedKeyHash);

        return ByteArraysEqual(computedNameHash, certId.IssuerNameHash)
            && ByteArraysEqual(computedKeyHash, certId.IssuerKeyHash);
    }

    private static HashAlgorithmName? HashFromOid(ObjectIdentifier oid)
    {
        if (oid.Equals(KnownOids.Sha256)) { return HashAlgorithmName.Sha256; }
        if (oid.Equals(KnownOids.Sha384)) { return HashAlgorithmName.Sha384; }
        if (oid.Equals(KnownOids.Sha512)) { return HashAlgorithmName.Sha512; }
        return null;
    }

    private static bool ByteArraysEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) { return false; }
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i]) { return false; }
        }
        return true;
    }

    private static CertificatePathValidationResult Fail(
        CertificatePathValidationStatus status, string message)
        => new(status, message, validatedPath: null);
}
