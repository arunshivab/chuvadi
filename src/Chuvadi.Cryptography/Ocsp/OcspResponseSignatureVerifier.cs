// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 6960 §4.2.2.2 — Authorized Responders
// PHASE: Phase 1.1.4 — OCSP

using System;
using System.Collections.Generic;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Hashing;
using Chuvadi.Cryptography.Oids;
using Chuvadi.Cryptography.PublicKey;
using Chuvadi.Cryptography.X509;

namespace Chuvadi.Cryptography.Ocsp;

/// <summary>
/// Verifies the signature on a <see cref="BasicOcspResponse"/>.
/// </summary>
/// <remarks>
/// RFC 6960 §4.2.2.2 names three legitimate responders for a given subject
/// certificate's issuer C:
/// <list type="bullet">
///   <item>C itself (direct responder), or</item>
///   <item>A cert issued by C with EKU <c>id-kp-OCSPSigning</c> (delegated responder), or</item>
///   <item>A pre-configured locally-trusted responder (not yet supported here).</item>
/// </list>
/// <para>
/// This verifier checks the first two by trying each candidate cert in turn:
/// the cert's issuer (if supplied) and any certs embedded inside the OCSP
/// response. The <c>responderID</c> field is used to filter candidates.
/// </para>
/// </remarks>
public static class OcspResponseSignatureVerifier
{
    /// <summary>
    /// Verifies the signature on <paramref name="basicResponse"/> and returns
    /// the certificate that signed it. Returns null when no candidate certificate
    /// produces a valid signature.
    /// </summary>
    /// <param name="basicResponse">The response to verify.</param>
    /// <param name="issuerCertificate">
    /// The issuer of the certificate that the response is about. Used as a direct
    /// responder candidate and as the authority that must have signed any
    /// delegated responder's certificate.
    /// </param>
    public static X509Certificate? VerifyAndIdentifyResponder(
        BasicOcspResponse basicResponse,
        X509Certificate issuerCertificate)
    {
        ArgumentNullException.ThrowIfNull(basicResponse);
        ArgumentNullException.ThrowIfNull(issuerCertificate);

        // Build the candidate list. Direct responder first, then any embedded certs.
        List<X509Certificate> candidates = new() { issuerCertificate };
        foreach (X509Certificate c in basicResponse.Certificates)
        {
            candidates.Add(c);
        }

        foreach (X509Certificate candidate in candidates)
        {
            if (!MatchesResponderId(candidate, basicResponse.ResponderId)) { continue; }

            // For a delegated responder, sanity-check: it must be issued by the
            // subject's issuer AND have EKU OCSP signing. We don't run a full path
            // validation here — that's the caller's responsibility. But we do verify
            // the responder cert is signed by issuerCertificate so a bogus cert
            // can't claim responder identity.
            if (!ReferenceEquals(candidate, issuerCertificate))
            {
                if (!IsDelegatedResponderValid(candidate, issuerCertificate)) { continue; }
            }

            if (VerifySignatureWith(basicResponse, candidate)) { return candidate; }
        }

        return null;
    }

    private static bool MatchesResponderId(X509Certificate candidate, ResponderID responderId)
    {
        if (responderId.IsByName)
        {
            return ByteArraysEqual(candidate.Subject.RawEncoding, responderId.ByName!.RawEncoding);
        }
        // byKey: SHA-1 hash of the candidate's subjectPublicKey BIT STRING content
        byte[] keyHash = HashSubjectPublicKey(candidate);
        return ByteArraysEqual(keyHash, responderId.ByKey!);
    }

    private static byte[] HashSubjectPublicKey(X509Certificate cert)
    {
        // RFC 6960 §4.2.1 KeyHash: "SHA-1 hash of the value of the BIT STRING
        // subjectPublicKey [excluding the tag, length, and number of unused bits]"
        byte[] pubKeyBits = cert.Tbs.SubjectPublicKeyInfo.SubjectPublicKey.Bytes;
        // SHA-1 is dead for signatures but still mandated for OCSP KeyHash. We
        // implement it inline here because HashFactory deliberately refuses SHA-1.
        return Sha1ForKeyHash(pubKeyBits);
    }

    /// <summary>
    /// SHA-1 used only for OCSP ResponderID KeyHash matching, per RFC 6960 §4.2.1.
    /// Not exposed as a general hash; SHA-1 is rejected by HashFactory.
    /// </summary>
    private static byte[] Sha1ForKeyHash(byte[] data)
    {
        // Minimal SHA-1 implementation. Used only for OCSP responder-key matching,
        // a non-security-critical comparison (we still validate the signature using
        // a strong algorithm — this is just a lookup key).
        uint h0 = 0x67452301, h1 = 0xEFCDAB89, h2 = 0x98BADCFE, h3 = 0x10325476, h4 = 0xC3D2E1F0;
        long bitLen = (long)data.Length * 8;
        int padLen = (56 - (data.Length + 1) % 64 + 64) % 64;
        byte[] padded = new byte[data.Length + 1 + padLen + 8];
        Buffer.BlockCopy(data, 0, padded, 0, data.Length);
        padded[data.Length] = 0x80;
        for (int i = 0; i < 8; i++)
        {
            padded[padded.Length - 1 - i] = (byte)(bitLen >> (8 * i));
        }

        for (int chunk = 0; chunk < padded.Length; chunk += 64)
        {
            uint[] w = new uint[80];
            for (int i = 0; i < 16; i++)
            {
                w[i] = ((uint)padded[chunk + i * 4] << 24)
                     | ((uint)padded[chunk + i * 4 + 1] << 16)
                     | ((uint)padded[chunk + i * 4 + 2] << 8)
                     | (uint)padded[chunk + i * 4 + 3];
            }
            for (int i = 16; i < 80; i++)
            {
                uint v = w[i - 3] ^ w[i - 8] ^ w[i - 14] ^ w[i - 16];
                w[i] = (v << 1) | (v >> 31);
            }
            uint a = h0, b = h1, c = h2, d = h3, e = h4;
            for (int i = 0; i < 80; i++)
            {
                uint f, k;
                if (i < 20) { f = (b & c) | (~b & d); k = 0x5A827999; }
                else if (i < 40) { f = b ^ c ^ d; k = 0x6ED9EBA1; }
                else if (i < 60) { f = (b & c) | (b & d) | (c & d); k = 0x8F1BBCDC; }
                else { f = b ^ c ^ d; k = 0xCA62C1D6; }
                uint temp = ((a << 5) | (a >> 27)) + f + e + k + w[i];
                e = d; d = c; c = (b << 30) | (b >> 2); b = a; a = temp;
            }
            h0 += a; h1 += b; h2 += c; h3 += d; h4 += e;
        }

        byte[] result = new byte[20];
        WriteUInt32(result, 0, h0);
        WriteUInt32(result, 4, h1);
        WriteUInt32(result, 8, h2);
        WriteUInt32(result, 12, h3);
        WriteUInt32(result, 16, h4);
        return result;
    }

    private static void WriteUInt32(byte[] dest, int offset, uint value)
    {
        dest[offset] = (byte)(value >> 24);
        dest[offset + 1] = (byte)(value >> 16);
        dest[offset + 2] = (byte)(value >> 8);
        dest[offset + 3] = (byte)value;
    }

    private static bool IsDelegatedResponderValid(
        X509Certificate responder, X509Certificate expectedIssuer)
    {
        // Issuer DN must match
        if (!ByteArraysEqual(responder.Issuer.RawEncoding, expectedIssuer.Subject.RawEncoding))
        {
            return false;
        }

        // EKU must include id-kp-OCSPSigning
        X509Extension? ekuExt = responder.Tbs.FindExtension(KnownOids.ExtKeyUsage);
        if (ekuExt is null) { return false; }
        if (!ContainsOcspSigningEku(ekuExt.Value)) { return false; }

        // Verify responder cert's signature against expected issuer's public key
        return VerifyCertificateSignature(responder, expectedIssuer);
    }

    private static bool ContainsOcspSigningEku(byte[] ekuExtValue)
    {
        // ExtKeyUsage ::= SEQUENCE OF OBJECT IDENTIFIER
        try
        {
            Asn1Reader r = new(ekuExtValue);
            Asn1Reader seq = r.ReadSequence();
            while (!seq.IsAtEnd)
            {
                ObjectIdentifier oid = seq.ReadObjectIdentifier();
                if (oid.Equals(KnownOids.OcspSigning)) { return true; }
            }
            return false;
        }
        catch (Asn1Exception) { return false; }
    }

    private static bool VerifyCertificateSignature(X509Certificate cert, X509Certificate signer)
    {
        try
        {
            IPublicKey signerKey = ExtractPublicKey(signer.Tbs.SubjectPublicKeyInfo);
            HashAlgorithmName? hash = HashFromSignatureAlgorithm(cert.SignatureAlgorithm.Algorithm);
            if (hash is null) { return false; }
            IHashAlgorithm h = HashFactory.Create(hash.Value);
            h.Update(cert.Tbs.RawEncoding);
            byte[] digest = new byte[h.DigestSize];
            h.Finish(digest);
            return SignatureVerifier.Verify(
                cert.SignatureAlgorithm, signerKey, digest, cert.SignatureValue.Bytes);
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    private static bool VerifySignatureWith(
        BasicOcspResponse basicResponse, X509Certificate responderCert)
    {
        try
        {
            IPublicKey responderKey = ExtractPublicKey(
                responderCert.Tbs.SubjectPublicKeyInfo);
            HashAlgorithmName? hash = HashFromSignatureAlgorithm(
                basicResponse.SignatureAlgorithm.Algorithm);
            if (hash is null) { return false; }
            IHashAlgorithm h = HashFactory.Create(hash.Value);
            h.Update(basicResponse.TbsRawEncoding);
            byte[] digest = new byte[h.DigestSize];
            h.Finish(digest);
            return SignatureVerifier.Verify(
                basicResponse.SignatureAlgorithm,
                responderKey,
                digest,
                basicResponse.SignatureValue.Bytes);
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    private static IPublicKey ExtractPublicKey(SubjectPublicKeyInfo spki)
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
            $"Public-key algorithm {alg} is not supported for OCSP verification.");
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

    private static bool ByteArraysEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) { return false; }
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i]) { return false; }
        }
        return true;
    }
}
