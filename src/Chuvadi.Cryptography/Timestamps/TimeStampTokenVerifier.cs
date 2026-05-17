// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 3161 §2.4.2; RFC 5652 §5 — CMS verification
// PHASE: Phase 1.1.4 — RFC 3161 timestamps

using System;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Cms;
using Chuvadi.Cryptography.Hashing;
using Chuvadi.Cryptography.Oids;
using Chuvadi.Cryptography.PublicKey;
using Chuvadi.Cryptography.X509;

namespace Chuvadi.Cryptography.Timestamps;

/// <summary>Outcome of <see cref="TimeStampTokenVerifier"/>.</summary>
public enum TimeStampVerificationStatus
{
    /// <summary>The TST is cryptographically valid: TSTInfo's signed bytes match the signer's signature.</summary>
    Valid = 0,
    /// <summary>The TST's signer certificate is not embedded in the token.</summary>
    SignerCertificateNotFound = 1,
    /// <summary>The signer's certificate is missing the id-kp-timeStamping extended key usage.</summary>
    SignerNotAuthorisedForTimestamping = 2,
    /// <summary>The signature digest does not match.</summary>
    DigestMismatch = 3,
    /// <summary>The signature does not verify against the signer's public key.</summary>
    SignatureInvalid = 4,
    /// <summary>The TST's messageImprint does not match the bytes the caller said it should cover.</summary>
    MessageImprintMismatch = 5,
    /// <summary>The TST uses algorithms Chuvadi does not implement.</summary>
    UnsupportedAlgorithm = 6,
    /// <summary>The TST envelope could not be parsed.</summary>
    MalformedToken = 7,
}

/// <summary>The result of verifying a TimeStampToken.</summary>
public sealed class TimeStampVerificationResult
{
    /// <summary>Initialises a new result.</summary>
    public TimeStampVerificationResult(
        TimeStampVerificationStatus status,
        string message,
        X509Certificate? signerCertificate,
        DateTimeOffset? timestamp)
    {
        ArgumentNullException.ThrowIfNull(message);
        Status = status;
        Message = message;
        SignerCertificate = signerCertificate;
        Timestamp = timestamp;
    }

    /// <summary>The outcome.</summary>
    public TimeStampVerificationStatus Status { get; }

    /// <summary>Human-readable explanation.</summary>
    public string Message { get; }

    /// <summary>The TSA's signing certificate, when located inside the token.</summary>
    public X509Certificate? SignerCertificate { get; }

    /// <summary>The genTime claimed by the TSA, when the token parsed successfully.</summary>
    public DateTimeOffset? Timestamp { get; }

    /// <summary>Convenience: true iff <see cref="Status"/> is Valid.</summary>
    public bool IsValid => Status == TimeStampVerificationStatus.Valid;
}

/// <summary>
/// Verifies an RFC 3161 TimeStampToken cryptographically and against a known
/// message-imprint payload.
/// </summary>
public static class TimeStampTokenVerifier
{
    /// <summary>
    /// Verifies the token's CMS signature and confirms its messageImprint hashes
    /// <paramref name="expectedSignedBytes"/>.
    /// </summary>
    /// <param name="token">The token to verify.</param>
    /// <param name="expectedSignedBytes">
    /// The bytes the caller expected the TSA to have hashed. For an Adobe-style
    /// signatureTimeStampToken on a PDF, this is the SignerInfo.signature bytes
    /// of the outer CMS (i.e. the PDF signer's signature value).
    /// </param>
    public static TimeStampVerificationResult Verify(
        TimeStampToken token, byte[] expectedSignedBytes)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(expectedSignedBytes);

        SignedData sd = token.SignedData;
        if (sd.SignerInfos.Count == 0)
        {
            return Fail(TimeStampVerificationStatus.MalformedToken,
                "TimeStampToken contains no SignerInfo.", null, token);
        }
        SignerInfo signer = sd.SignerInfos[0];

        X509Certificate? signerCert = signer.FindSignerCertificate(sd.Certificates);
        if (signerCert is null)
        {
            return Fail(TimeStampVerificationStatus.SignerCertificateNotFound,
                "TSA signing certificate not present inside the token.", null, token);
        }

        // Per RFC 3161 §2.3, the TSA certificate MUST have EKU id-kp-timeStamping
        // and it MUST be the only EKU. Chuvadi enforces presence; sole-EKU is a
        // best-practice rule but we don't fail closed on it.
        if (!HasTimeStampingEku(signerCert))
        {
            return Fail(TimeStampVerificationStatus.SignerNotAuthorisedForTimestamping,
                "TSA signing certificate does not include EKU id-kp-timeStamping.",
                signerCert, token);
        }

        // CMS verification: TSTInfo bytes are eContent. Hash with signer.DigestAlgorithm,
        // verify messageDigest signed attribute, then sign DER-encoded SignedAttrs SET.
        HashAlgorithmName? hashName = HashFromOid(signer.DigestAlgorithm.Algorithm);
        if (hashName is null)
        {
            return Fail(TimeStampVerificationStatus.UnsupportedAlgorithm,
                $"DigestAlgorithm {signer.DigestAlgorithm.Algorithm} not supported.",
                signerCert, token);
        }

        IHashAlgorithm h = HashFactory.Create(hashName.Value);
        h.Update(token.TstInfo.RawEncoding);
        byte[] eContentHash = new byte[h.DigestSize];
        h.Finish(eContentHash);

        byte[] toHash;
        if (signer.HasSignedAttributes)
        {
            byte[]? declaredDigest = signer.MessageDigest;
            if (declaredDigest is null
                || !ConstantTimeEquals(declaredDigest, eContentHash))
            {
                return Fail(TimeStampVerificationStatus.DigestMismatch,
                    "TimeStampToken's messageDigest signed attribute does not match the TSTInfo hash.",
                    signerCert, token);
            }
            toHash = signer.SignedAttributes!.DerEncodedForVerification;
        }
        else
        {
            toHash = token.TstInfo.RawEncoding;
        }

        IHashAlgorithm h2 = HashFactory.Create(hashName.Value);
        h2.Update(toHash);
        byte[] finalDigest = new byte[h2.DigestSize];
        h2.Finish(finalDigest);

        try
        {
            IPublicKey signerKey = ExtractKey(signerCert.Tbs.SubjectPublicKeyInfo);
            AlgorithmIdentifier sigAlg = NormaliseSignatureAlgorithm(
                signer.SignatureAlgorithm, signer.DigestAlgorithm, signerKey);
            bool ok = SignatureVerifier.Verify(sigAlg, signerKey, finalDigest, signer.Signature);
            if (!ok)
            {
                return Fail(TimeStampVerificationStatus.SignatureInvalid,
                    "TimeStampToken's signature does not verify against the TSA's public key.",
                    signerCert, token);
            }
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException)
        {
            return Fail(TimeStampVerificationStatus.UnsupportedAlgorithm,
                $"Could not verify TimeStampToken signature: {ex.Message}", signerCert, token);
        }

        // Check messageImprint against the bytes the caller expected.
        HashAlgorithmName? miHashName = HashFromOid(token.TstInfo.MessageImprint.HashAlgorithm.Algorithm);
        if (miHashName is null)
        {
            return Fail(TimeStampVerificationStatus.UnsupportedAlgorithm,
                $"MessageImprint hashAlgorithm {token.TstInfo.MessageImprint.HashAlgorithm.Algorithm} not supported.",
                signerCert, token);
        }
        IHashAlgorithm mh = HashFactory.Create(miHashName.Value);
        mh.Update(expectedSignedBytes);
        byte[] computedImprint = new byte[mh.DigestSize];
        mh.Finish(computedImprint);

        if (!ConstantTimeEquals(computedImprint, token.TstInfo.MessageImprint.HashedMessage))
        {
            return Fail(TimeStampVerificationStatus.MessageImprintMismatch,
                "TimeStampToken's messageImprint does not match the hash of the expected signed bytes. "
                + "This token does not cover this signature.",
                signerCert, token);
        }

        return new TimeStampVerificationResult(
            TimeStampVerificationStatus.Valid,
            $"TimeStampToken is cryptographically valid. genTime = {token.TstInfo.GenTime:u}.",
            signerCert, token.TstInfo.GenTime);
    }

    private static bool HasTimeStampingEku(X509Certificate cert)
    {
        X509Extension? ext = cert.Tbs.FindExtension(KnownOids.ExtKeyUsage);
        if (ext is null) { return false; }
        try
        {
            Asn1Reader r = new(ext.Value);
            Asn1Reader seq = r.ReadSequence();
            while (!seq.IsAtEnd)
            {
                ObjectIdentifier oid = seq.ReadObjectIdentifier();
                if (oid.Equals(KnownOids.TimeStamping)) { return true; }
            }
        }
        catch (Asn1Exception) { return false; }
        return false;
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
        throw new NotSupportedException($"TSA public-key algorithm {alg} not supported.");
    }

    private static AlgorithmIdentifier NormaliseSignatureAlgorithm(
        AlgorithmIdentifier sigAlg, AlgorithmIdentifier digestAlg, IPublicKey publicKey)
    {
        // Mirrors the same RFC 5652 §10.1.2 normalisation used by PdfSignatureVerifier.
        ObjectIdentifier alg = sigAlg.Algorithm;
        if (alg.Equals(KnownOids.RsaEncryption) && publicKey is RsaPublicKey)
        {
            ObjectIdentifier d = digestAlg.Algorithm;
            if (d.Equals(KnownOids.Sha256)) { return new AlgorithmIdentifier(KnownOids.Sha256WithRsa, null); }
            if (d.Equals(KnownOids.Sha384)) { return new AlgorithmIdentifier(KnownOids.Sha384WithRsa, null); }
            if (d.Equals(KnownOids.Sha512)) { return new AlgorithmIdentifier(KnownOids.Sha512WithRsa, null); }
        }
        if (alg.Equals(KnownOids.EcPublicKey) && publicKey is EcdsaPublicKey)
        {
            ObjectIdentifier d = digestAlg.Algorithm;
            if (d.Equals(KnownOids.Sha256)) { return new AlgorithmIdentifier(KnownOids.Sha256WithEcdsa, null); }
            if (d.Equals(KnownOids.Sha384)) { return new AlgorithmIdentifier(KnownOids.Sha384WithEcdsa, null); }
            if (d.Equals(KnownOids.Sha512)) { return new AlgorithmIdentifier(KnownOids.Sha512WithEcdsa, null); }
        }
        return sigAlg;
    }

    private static HashAlgorithmName? HashFromOid(ObjectIdentifier oid)
    {
        if (oid.Equals(KnownOids.Sha256)) { return HashAlgorithmName.Sha256; }
        if (oid.Equals(KnownOids.Sha384)) { return HashAlgorithmName.Sha384; }
        if (oid.Equals(KnownOids.Sha512)) { return HashAlgorithmName.Sha512; }
        return null;
    }

    private static bool ConstantTimeEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) { return false; }
        int diff = 0;
        for (int i = 0; i < a.Length; i++) { diff |= a[i] ^ b[i]; }
        return diff == 0;
    }

    private static TimeStampVerificationResult Fail(
        TimeStampVerificationStatus status, string message,
        X509Certificate? cert, TimeStampToken token)
        => new(status, message, cert, token.TstInfo.GenTime);
}
