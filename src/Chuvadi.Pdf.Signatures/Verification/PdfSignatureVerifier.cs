// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5652 — CMS, §5.4 Message Digest Calculation Process
//                  §5.6 Signature Verification Process
//        PDF 32000-1 §12.8 — Digital Signatures
// PHASE: Phase 1.1.4 — Signature verification orchestration
//
// Verification recipe for a CMS-based PDF signature:
//
//   1. Decode the /Contents bytes as a CMS SignedData container.
//   2. Locate the SignerInfo (PDF signatures always carry exactly one).
//   3. Find the signer's X.509 certificate inside SignedData.Certificates,
//      keyed by SignerInfo.SignerId (issuer+serial or subjectKeyIdentifier).
//   4. Extract the signer's public key from the certificate's
//      SubjectPublicKeyInfo, dispatching to RsaPublicKey or EcdsaPublicKey.
//   5. Compute the digest of the file bytes covered by /ByteRange using the
//      hash algorithm named in SignerInfo.DigestAlgorithm.
//   6. Two paths depending on whether SignerInfo has signed attributes:
//      a) WITH signed attributes (typical for PDF):
//         - The messageDigest signed attribute MUST equal the hash from step 5.
//           If not, the document was tampered with after signing.
//         - The signature itself is over the DER-encoded SET of signed attributes
//           (CmsAttributeTable.DerEncodedForVerification). Hash that with
//           DigestAlgorithm, then verify the signature.
//      b) WITHOUT signed attributes (rare in PDF):
//         - The signature is over the raw signed bytes from step 5 directly.
//   7. Call SignatureVerifier.Verify with the SignerInfo.SignatureAlgorithm,
//      the extracted public key, the chosen hash, and SignerInfo.Signature.

using System;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Cms;
using Chuvadi.Cryptography.Hashing;
using Chuvadi.Cryptography.Oids;
using Chuvadi.Cryptography.PublicKey;
using Chuvadi.Cryptography.X509;
using Chuvadi.Pdf.Documents;

namespace Chuvadi.Pdf.Signatures.Verification;

/// <summary>
/// Orchestrates verification of a single <see cref="PdfSignature"/>.
/// </summary>
public static class PdfSignatureVerifier
{
    /// <summary>
    /// Verifies <paramref name="signature"/> against the bytes it covers in
    /// <paramref name="document"/>.
    /// </summary>
    public static SignatureVerificationResult Verify(
        PdfSignature signature,
        PdfDocument document,
        SignatureVerifyOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(document);
        _ = options ?? SignatureVerifyOptions.Default;

        if (!signature.IsCmsBased)
        {
            return Fail(SignatureVerificationStatus.UnsupportedSubFilter,
                $"SubFilter '{signature.SubFilter ?? "<null>"}' is not CMS-based; Chuvadi can verify only CMS-based signatures.");
        }

        SignedData signedData;
        try
        {
            signedData = signature.DecodeCms();
        }
        catch (Exception ex) when (ex is Chuvadi.Cryptography.Asn1.Asn1Exception
                                     or ArgumentException
                                     or InvalidOperationException)
        {
            return Fail(SignatureVerificationStatus.MalformedSignature,
                $"Could not parse the CMS envelope: {ex.Message}");
        }

        if (signedData.SignerInfos.Count == 0)
        {
            return Fail(SignatureVerificationStatus.MalformedSignature,
                "The CMS envelope contains no SignerInfo.");
        }

        // PDF signatures carry exactly one signer. If more are present, only
        // the first is verified — this matches the practical interpretation in
        // every PDF viewer Chuvadi targets.
        SignerInfo signer = signedData.SignerInfos[0];

        // Step 3 — locate the signing certificate.
        X509Certificate? signerCert = signer.FindSignerCertificate(signedData.Certificates);
        if (signerCert is null)
        {
            return Fail(SignatureVerificationStatus.SignerCertificateNotFound,
                "The signer's certificate is not embedded in the CMS envelope.");
        }

        // Step 4 — extract the public key.
        IPublicKey publicKey;
        try
        {
            publicKey = ExtractPublicKey(signerCert);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return new SignatureVerificationResult(
                SignatureVerificationStatus.UnsupportedAlgorithm,
                $"Could not extract a usable public key from the signer's certificate: {ex.Message}",
                signerCert,
                integrityVerified: false);
        }

        // Step 5 — hash the byte-range bytes with the digest algorithm.
        IHashAlgorithm hash;
        try
        {
            hash = HashFactory.CreateFromOid(signer.DigestAlgorithm.Algorithm);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return new SignatureVerificationResult(
                SignatureVerificationStatus.UnsupportedAlgorithm,
                $"DigestAlgorithm {signer.DigestAlgorithm.Algorithm} is not supported by Chuvadi: {ex.Message}",
                signerCert,
                integrityVerified: false);
        }

        byte[] signedBytes = document.GetSignedBytes(signature);
        hash.Update(signedBytes);
        byte[] eContentHash = new byte[hash.DigestSize];
        hash.Finish(eContentHash);

        // Step 6 — decide what to hash and verify.
        byte[] toHash;
        if (signer.HasSignedAttributes)
        {
            // 6a — verify messageDigest attribute then sign over the DER-encoded SET.
            byte[]? declaredDigest = signer.MessageDigest;
            if (declaredDigest is null)
            {
                return new SignatureVerificationResult(
                    SignatureVerificationStatus.MalformedSignature,
                    "SignedAttributes is present but the required messageDigest attribute is missing.",
                    signerCert,
                    integrityVerified: false);
            }
            if (!ConstantTimeEquals(declaredDigest, eContentHash))
            {
                return new SignatureVerificationResult(
                    SignatureVerificationStatus.DigestMismatch,
                    "The messageDigest signed attribute does not match the hash of the document's byte-range content. The document was tampered with after signing.",
                    signerCert,
                    integrityVerified: false);
            }
            // The signature is over the DER encoding of the SET OF signed attributes.
            toHash = signer.SignedAttributes!.DerEncodedForVerification;
        }
        else
        {
            // 6b — signature is over the eContent hash itself. With detached signatures,
            // that's the byte-range content directly.
            toHash = signedBytes;
        }

        // Compute the actual hash to feed to the verifier.
        IHashAlgorithm sigHash = HashFactory.CreateFromOid(signer.DigestAlgorithm.Algorithm);
        sigHash.Update(toHash);
        byte[] finalDigest = new byte[sigHash.DigestSize];
        sigHash.Finish(finalDigest);

        // Step 7 — verify the signature.
        AlgorithmIdentifier sigAlg = NormaliseSignatureAlgorithm(
            signer.SignatureAlgorithm, signer.DigestAlgorithm, publicKey);

        bool cryptoOk;
        try
        {
            cryptoOk = SignatureVerifier.Verify(
                sigAlg,
                publicKey,
                finalDigest,
                signer.Signature);
        }
        catch (NotSupportedException ex)
        {
            return new SignatureVerificationResult(
                SignatureVerificationStatus.UnsupportedAlgorithm,
                $"Signature algorithm not supported: {ex.Message}",
                signerCert,
                integrityVerified: false);
        }
        catch (ArgumentException ex)
        {
            return new SignatureVerificationResult(
                SignatureVerificationStatus.MalformedSignature,
                $"Signature could not be verified: {ex.Message}",
                signerCert,
                integrityVerified: false);
        }

        if (!cryptoOk)
        {
            return new SignatureVerificationResult(
                SignatureVerificationStatus.Invalid,
                "The signature does not verify against the signer's public key.",
                signerCert,
                integrityVerified: false);
        }

        return new SignatureVerificationResult(
            SignatureVerificationStatus.Valid,
            "Signature is cryptographically valid. (Trust evaluation of the signer's certificate is a separate check.)",
            signerCert,
            integrityVerified: true);
    }

    /// <summary>
    /// Some signers (notably OpenSSL's PKCS7_sign) set SignerInfo.signatureAlgorithm
    /// to the bare key-algorithm OID (rsaEncryption / id-ecPublicKey) rather than
    /// the combined "hashWithEncryption" OID. RFC 5652 §10.1.2 allows this; the
    /// hash is then taken from <c>digestAlgorithm</c>. This helper normalises that
    /// case so the downstream dispatcher always sees a fully-specified OID.
    /// </summary>
    private static AlgorithmIdentifier NormaliseSignatureAlgorithm(
        AlgorithmIdentifier signatureAlgorithm,
        AlgorithmIdentifier digestAlgorithm,
        IPublicKey publicKey)
    {
        ObjectIdentifier alg = signatureAlgorithm.Algorithm;

        if (alg.Equals(KnownOids.RsaEncryption) && publicKey is RsaPublicKey)
        {
            ObjectIdentifier digest = digestAlgorithm.Algorithm;
            if (digest.Equals(KnownOids.Sha256)) { return new AlgorithmIdentifier(KnownOids.Sha256WithRsa, null); }
            if (digest.Equals(KnownOids.Sha384)) { return new AlgorithmIdentifier(KnownOids.Sha384WithRsa, null); }
            if (digest.Equals(KnownOids.Sha512)) { return new AlgorithmIdentifier(KnownOids.Sha512WithRsa, null); }
        }
        if (alg.Equals(KnownOids.EcPublicKey) && publicKey is EcdsaPublicKey)
        {
            ObjectIdentifier digest = digestAlgorithm.Algorithm;
            if (digest.Equals(KnownOids.Sha256)) { return new AlgorithmIdentifier(KnownOids.Sha256WithEcdsa, null); }
            if (digest.Equals(KnownOids.Sha384)) { return new AlgorithmIdentifier(KnownOids.Sha384WithEcdsa, null); }
            if (digest.Equals(KnownOids.Sha512)) { return new AlgorithmIdentifier(KnownOids.Sha512WithEcdsa, null); }
        }

        return signatureAlgorithm;
    }

    /// <summary>
    /// Extracts an <see cref="IPublicKey"/> from a certificate's SubjectPublicKeyInfo,
    /// dispatching based on the algorithm OID.
    /// </summary>
    private static IPublicKey ExtractPublicKey(X509Certificate cert)
    {
        SubjectPublicKeyInfo spki = cert.Tbs.SubjectPublicKeyInfo;
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
            $"Public-key algorithm {alg} in signer certificate is not supported. " +
            "Chuvadi supports RSA (rsaEncryption) and ECDSA (id-ecPublicKey).");
    }

    private static bool ConstantTimeEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) { return false; }
        int diff = 0;
        for (int i = 0; i < a.Length; i++)
        {
            diff |= a[i] ^ b[i];
        }
        return diff == 0;
    }

    private static SignatureVerificationResult Fail(SignatureVerificationStatus status, string message)
        => new(status, message, signerCertificate: null, integrityVerified: false);
}
