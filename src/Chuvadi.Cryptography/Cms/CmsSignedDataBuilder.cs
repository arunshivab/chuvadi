// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5652 §5 — Signed-data Content Type
// PHASE: Phase 1.1.4 — CMS signing

using System;
using System.Collections.Generic;
using System.Linq;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Hashing;
using Chuvadi.Cryptography.Oids;
using Chuvadi.Cryptography.Signing;
using Chuvadi.Cryptography.X509;

namespace Chuvadi.Cryptography.Cms;

/// <summary>
/// Builds a CMS SignedData (RFC 5652 §5) wrapped in a ContentInfo, ready for
/// embedding in a PDF signature dictionary's <c>/Contents</c>.
/// </summary>
/// <remarks>
/// <para>
/// The output structure is:
/// <code>
/// ContentInfo {
///   contentType: id-signedData (1.2.840.113549.1.7.2),
///   content [0] EXPLICIT SignedData {
///     version: 1,
///     digestAlgorithms: SET OF { the signer's digestAlgorithm },
///     encapContentInfo {
///       eContentType: id-data  (detached signature, no eContent),
///     },
///     certificates [0] IMPLICIT SET OF Certificate,
///     signerInfos SET OF { the one SignerInfo }
///   }
/// }
/// </code>
/// </para>
/// <para>
/// SignerInfo always includes the signed attributes <c>contentType</c> and
/// <c>messageDigest</c> (mandatory per RFC 5652 §11) plus <c>signingTime</c>
/// when supplied. The signature is computed over the DER encoding of the
/// signed-attributes SET with the SET tag (0x31), not the [0] IMPLICIT tag
/// that the wire encoding uses — this is the RFC 5652 §5.4 distinction
/// Chuvadi's verifier already handles.
/// </para>
/// </remarks>
public static class CmsSignedDataBuilder
{
    /// <summary>
    /// Builds a detached CMS SignedData over <paramref name="dataToSign"/>.
    /// </summary>
    /// <param name="dataToSign">The bytes that the SignedData asserts a signature over.</param>
    /// <param name="signer">The signer.</param>
    /// <param name="signingTime">Optional signing time; included as a signed attribute when set.</param>
    /// <param name="extraCertificates">Additional certs to embed in the SignedData (typically the signer's CA chain). The signer's own cert is always included.</param>
    /// <param name="unsignedAttributes">Optional pre-encoded SEQUENCE-DER unsigned attributes (RFC 5652 §5.3); each entry is a complete <c>SEQUENCE { attrType OID, attrValues SET OF AttributeValue }</c>. Use <see cref="BuildSignatureTimeStampAttribute"/> for the RFC 3161 signature timestamp attribute.</param>
    /// <returns>DER bytes of the CMS ContentInfo wrapping the SignedData. These bytes are what goes into a PDF signature dictionary's <c>/Contents</c>.</returns>
    public static byte[] BuildDetached(
        byte[] dataToSign,
        ISigner signer,
        DateTimeOffset? signingTime = null,
        IEnumerable<X509Certificate>? extraCertificates = null,
        IReadOnlyList<byte[]>? unsignedAttributes = null)
    {
        ArgumentNullException.ThrowIfNull(dataToSign);
        ArgumentNullException.ThrowIfNull(signer);

        // ── Step 1: digest the data being signed ─────────────────────────
        IHashAlgorithm h = HashFactory.Create(signer.HashAlgorithm);
        h.Update(dataToSign);
        byte[] messageDigest = new byte[h.DigestSize];
        h.Finish(messageDigest);

        // ── Step 2: build SignedAttributes ──────────────────────────────
        // Each Attribute ::= SEQUENCE { attrType OID, attrValues SET OF AttributeValue }.
        // The SET OF Attribute encoding is DER-sorted lexicographically by the encoded
        // bytes of each Attribute (RFC 5652 §5.4 / DER SET-OF rules).
        List<byte[]> attrs = new()
        {
            BuildContentTypeAttribute(KnownOids.CmsData),
            BuildMessageDigestAttribute(messageDigest),
        };
        if (signingTime is DateTimeOffset st)
        {
            attrs.Add(BuildSigningTimeAttribute(st));
        }
        attrs.Sort(CompareDerBytes);

        // The bytes the signer signs: the signedAttrs SET tag (0x31), length, and contents.
        Asn1Writer attrSet = new();
        attrSet.PushSet();
        foreach (byte[] a in attrs) { attrSet.WriteEncoded(a); }
        attrSet.PopSet();
        byte[] signedAttrsForSigning = attrSet.ToArray();

        // ── Step 3: sign the encoded signedAttrs ────────────────────────
        byte[] signature = signer.Sign(signedAttrsForSigning);

        // ── Step 4: emit the wire-format SignerInfo ─────────────────────
        // SignerInfo's wire encoding wraps signedAttrs in [0] IMPLICIT, not SET.
        // Re-emit with the implicit tag so the on-wire bytes differ from the
        // signed bytes by tag only.
        byte[] signedAttrsForWire = WrapSetAsImplicitContext0(signedAttrsForSigning);

        Asn1Writer signerInfo = new();
        signerInfo.PushSequence();
        signerInfo.WriteInteger(1);  // version (for IssuerAndSerialNumber)
        WriteIssuerAndSerialNumber(signerInfo, signer.Certificate);
        WriteAlgorithmIdentifier(signerInfo, DigestAlgFor(signer.HashAlgorithm));
        signerInfo.WriteEncoded(signedAttrsForWire);
        WriteAlgorithmIdentifier(signerInfo, signer.SignatureAlgorithm);
        signerInfo.WriteOctetString(signature);

        // unsignedAttrs [1] IMPLICIT SET OF Attribute — RFC 5652 §5.3.
        // Most commonly used for the signature timestamp (id-aa-signatureTimeStampToken)
        // attribute. Emit as a SET (tag 0x31) and rewrite the tag to 0xA1 to apply
        // the [1] IMPLICIT context-specific tagging — body bytes are otherwise
        // identical to the SET-OF encoding.
        if (unsignedAttributes is not null && unsignedAttributes.Count > 0)
        {
            Asn1Writer ua = new();
            ua.PushSet();
            foreach (byte[] attr in unsignedAttributes)
            {
                ua.WriteEncoded(attr);
            }
            ua.PopSet();
            byte[] uaSet = ua.ToArray();
            signerInfo.WriteEncoded(WrapSetAsImplicitContext(uaSet, contextTag: 1));
        }

        signerInfo.PopSequence();
        byte[] signerInfoEncoded = signerInfo.ToArray();

        // ── Step 5: emit SignedData ─────────────────────────────────────
        Asn1Writer signedData = new();
        signedData.PushSequence();
        signedData.WriteInteger(1);  // version (matches SignerInfo v1)

        // digestAlgorithms SET OF AlgorithmIdentifier — just one
        signedData.PushSet();
        WriteAlgorithmIdentifier(signedData, DigestAlgFor(signer.HashAlgorithm));
        signedData.PopSet();

        // encapContentInfo SEQUENCE { eContentType OID } — detached, so no eContent
        signedData.PushSequence();
        signedData.WriteObjectIdentifier(KnownOids.CmsData);
        signedData.PopSequence();

        // certificates [0] IMPLICIT SET OF Certificate
        List<X509Certificate> certs = new() { signer.Certificate };
        if (extraCertificates is not null)
        {
            foreach (X509Certificate c in extraCertificates)
            {
                if (!CertsEqual(c, signer.Certificate)) { certs.Add(c); }
            }
        }
        signedData.PushExplicit(0);
        foreach (X509Certificate c in certs) { signedData.WriteEncoded(c.RawEncoding); }
        signedData.PopExplicit(0);

        // signerInfos SET OF SignerInfo
        signedData.PushSet();
        signedData.WriteEncoded(signerInfoEncoded);
        signedData.PopSet();
        signedData.PopSequence();
        byte[] signedDataEncoded = signedData.ToArray();

        // ── Step 6: wrap in ContentInfo ─────────────────────────────────
        Asn1Writer contentInfo = new();
        contentInfo.PushSequence();
        contentInfo.WriteObjectIdentifier(KnownOids.CmsSignedData);
        contentInfo.PushExplicit(0);
        contentInfo.WriteEncoded(signedDataEncoded);
        contentInfo.PopExplicit(0);
        contentInfo.PopSequence();
        return contentInfo.ToArray();
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a detached CMS SignedData and embeds an RFC 3161 signature
    /// timestamp from <paramref name="tsaClient"/> as an unsigned attribute.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The workflow:
    /// </para>
    /// <list type="number">
    ///   <item>Build the SignerInfo's signature exactly as
    ///   <see cref="BuildDetached"/> does.</item>
    ///   <item>Request a timestamp from <paramref name="tsaClient"/> over the
    ///   raw signature bytes (the value of <c>SignerInfo.signature</c>), per
    ///   RFC 3161 §3.3.3.1.</item>
    ///   <item>Embed the resulting TimeStampToken as an
    ///   <c>id-aa-signatureTimeStampToken</c> unsigned attribute on the
    ///   SignerInfo.</item>
    /// </list>
    /// <para>
    /// The same hash algorithm as the signer's is used for the TSA's
    /// MessageImprint. Callers wanting fine-grained control over the TSA
    /// request can invoke <see cref="BuildDetached"/> directly, then build
    /// the attribute via <see cref="BuildSignatureTimeStampAttribute"/> and
    /// emit a new CMS.
    /// </para>
    /// </remarks>
    /// <exception cref="Chuvadi.Cryptography.Timestamps.TsaException">
    /// If the TSA refuses the request or returns a non-granted status.
    /// </exception>
    public static byte[] BuildDetachedWithTimestamp(
        byte[] dataToSign,
        ISigner signer,
        Chuvadi.Cryptography.Timestamps.ITsaClient tsaClient,
        DateTimeOffset? signingTime = null,
        IEnumerable<X509Certificate>? extraCertificates = null,
        IReadOnlyList<byte[]>? additionalUnsignedAttributes = null)
    {
        ArgumentNullException.ThrowIfNull(dataToSign);
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentNullException.ThrowIfNull(tsaClient);

        // Phase 1: build the un-timestamped CMS so we know the SignerInfo.signature value.
        byte[] firstCms = BuildDetached(dataToSign, signer, signingTime, extraCertificates);
        SignedData firstSd = CmsDecoder.DecodeSignedData(firstCms);
        if (firstSd.SignerInfos.Count != 1)
        {
            throw new InvalidOperationException(
                "Expected exactly one SignerInfo in the freshly-built CMS.");
        }
        byte[] sigToStamp = firstSd.SignerInfos[0].Signature;

        // Phase 2: fetch the TSA timestamp over the raw signature bytes.
        Chuvadi.Cryptography.Timestamps.TimeStampRequest req =
            Chuvadi.Cryptography.Timestamps.TimeStampRequest.ForData(
                sigToStamp, signer.HashAlgorithm, certReq: true);
        Chuvadi.Cryptography.Timestamps.TimeStampResponse resp = tsaClient.Fetch(req);
        if (!resp.IsGranted || resp.TimeStampToken is null)
        {
            string detail = resp.StatusStrings.Count > 0
                ? string.Join("; ", resp.StatusStrings)
                : "no status string supplied";
            throw new Chuvadi.Cryptography.Timestamps.TsaException(
                $"TSA refused to grant a timestamp (status {resp.Status}: {detail}).");
        }

        // Phase 3: rebuild the CMS with the TST as an unsigned attribute. Note that
        // the SignerInfo's signature value depends on the signer; for ECDSA each call
        // produces different bytes (random k). But the timestamp is over the
        // *previous* signature — so a fresh sign in Phase 3 would invalidate the
        // timestamp. Solution: pass through the Phase-1 SignerInfo data instead of
        // calling BuildDetached again.
        //
        // Concrete plan: stash the Phase-1 CMS bytes' SignerInfo internals, splice in
        // the unsigned attribute. Surgical edit of the existing CMS rather than a
        // rebuild keeps the signature stable.
        byte[] tstAttribute = BuildSignatureTimeStampAttribute(resp.TimeStampToken.RawEncoding);

        List<byte[]> ua = new() { tstAttribute };
        if (additionalUnsignedAttributes is not null)
        {
            ua.AddRange(additionalUnsignedAttributes);
        }

        // Splice the unsigned attribute into the existing SignerInfo without re-signing.
        return InsertUnsignedAttributes(firstCms, ua);
    }

    /// <summary>
    /// Surgically inserts a SET of unsigned attributes into the single
    /// SignerInfo of a CMS SignedData, without re-signing. Used by
    /// <see cref="BuildDetachedWithTimestamp"/>.
    /// </summary>
    private static byte[] InsertUnsignedAttributes(byte[] cmsBytes, IReadOnlyList<byte[]> attrs)
    {
        // Strategy: re-encode the SignerInfo with the unsigned attrs, then rebuild
        // the outer SignedData / ContentInfo. We do this by decoding everything,
        // then re-emitting bit-by-bit. This is more work than a byte-level splice
        // but it keeps Chuvadi's encoder authoritative.
        SignedData sd = CmsDecoder.DecodeSignedData(cmsBytes);
        SignerInfo si = sd.SignerInfos[0];

        // Re-emit the SignerInfo with the unsigned attributes appended.
        Asn1Writer signerInfo = new();
        signerInfo.PushSequence();
        signerInfo.WriteInteger(1);
        WriteIssuerAndSerialNumber(signerInfo, si.FindSignerCertificate(sd.Certificates)
            ?? throw new InvalidOperationException("Signer's certificate not found in CMS."));
        WriteAlgorithmIdentifier(signerInfo, si.DigestAlgorithm);
        // signedAttrs in wire form already; re-emit by taking the inner SET-OF and
        // wrapping with [0] IMPLICIT.
        byte[] signedAttrsForWire = WrapSetAsImplicitContext0(
            si.SignedAttributes!.DerEncodedForVerification);
        signerInfo.WriteEncoded(signedAttrsForWire);
        WriteAlgorithmIdentifier(signerInfo, si.SignatureAlgorithm);
        signerInfo.WriteOctetString(si.Signature);

        Asn1Writer ua = new();
        ua.PushSet();
        foreach (byte[] attr in attrs) { ua.WriteEncoded(attr); }
        ua.PopSet();
        signerInfo.WriteEncoded(WrapSetAsImplicitContext(ua.ToArray(), contextTag: 1));
        signerInfo.PopSequence();
        byte[] signerInfoEncoded = signerInfo.ToArray();

        // Rebuild SignedData
        Asn1Writer signedData = new();
        signedData.PushSequence();
        signedData.WriteInteger(1);
        // digestAlgorithms
        signedData.PushSet();
        WriteAlgorithmIdentifier(signedData, si.DigestAlgorithm);
        signedData.PopSet();
        // encapContentInfo (detached)
        signedData.PushSequence();
        signedData.WriteObjectIdentifier(KnownOids.CmsData);
        signedData.PopSequence();
        // certificates [0] IMPLICIT
        signedData.PushExplicit(0);
        foreach (X509Certificate c in sd.Certificates)
        {
            signedData.WriteEncoded(c.RawEncoding);
        }
        signedData.PopExplicit(0);
        // signerInfos
        signedData.PushSet();
        signedData.WriteEncoded(signerInfoEncoded);
        signedData.PopSet();
        signedData.PopSequence();
        byte[] signedDataEncoded = signedData.ToArray();

        // ContentInfo
        Asn1Writer contentInfo = new();
        contentInfo.PushSequence();
        contentInfo.WriteObjectIdentifier(KnownOids.CmsSignedData);
        contentInfo.PushExplicit(0);
        contentInfo.WriteEncoded(signedDataEncoded);
        contentInfo.PopExplicit(0);
        contentInfo.PopSequence();
        return contentInfo.ToArray();
    }

    private static byte[] BuildContentTypeAttribute(ObjectIdentifier eContentType)
    {
        Asn1Writer w = new();
        w.PushSequence();
        w.WriteObjectIdentifier(KnownOids.ContentType);
        w.PushSet();
        w.WriteObjectIdentifier(eContentType);
        w.PopSet();
        w.PopSequence();
        return w.ToArray();
    }

    private static byte[] BuildMessageDigestAttribute(byte[] digest)
    {
        Asn1Writer w = new();
        w.PushSequence();
        w.WriteObjectIdentifier(KnownOids.MessageDigest);
        w.PushSet();
        w.WriteOctetString(digest);
        w.PopSet();
        w.PopSequence();
        return w.ToArray();
    }

    private static byte[] BuildSigningTimeAttribute(DateTimeOffset time)
    {
        Asn1Writer w = new();
        w.PushSequence();
        w.WriteObjectIdentifier(KnownOids.SigningTime);
        w.PushSet();
        // RFC 5652 §11.3: UTCTime for years 1950..2049, GeneralizedTime otherwise.
        DateTimeOffset utc = time.ToUniversalTime();
        if (utc.Year >= 1950 && utc.Year <= 2049)
        {
            w.WriteUtcTime(utc);
        }
        else
        {
            w.WriteGeneralizedTime(utc);
        }
        w.PopSet();
        w.PopSequence();
        return w.ToArray();
    }

    /// <summary>
    /// Re-tags a SET-OF encoding (tag 0x31) as a [0] IMPLICIT context-specific
    /// (tag 0xA0) without recomputing the length. The body bytes after the
    /// tag and the length-of-length encoding are identical.
    /// </summary>
    private static byte[] WrapSetAsImplicitContext0(byte[] setEncoded)
        => WrapSetAsImplicitContext(setEncoded, contextTag: 0);

    /// <summary>
    /// Re-tags a SET-OF encoding (tag 0x31) as <c>[n] IMPLICIT CONSTRUCTED</c>
    /// for any context-specific tag number 0..30. Body and length are
    /// preserved verbatim; only the tag byte changes.
    /// </summary>
    private static byte[] WrapSetAsImplicitContext(byte[] setEncoded, int contextTag)
    {
        if (setEncoded.Length == 0 || setEncoded[0] != 0x31)
        {
            throw new ArgumentException("Expected DER SET OF (tag 0x31).", nameof(setEncoded));
        }
        if (contextTag is < 0 or > 30)
        {
            throw new ArgumentOutOfRangeException(nameof(contextTag),
                "Only short-form context tags 0..30 are supported here.");
        }
        byte[] copy = (byte[])setEncoded.Clone();
        copy[0] = (byte)(0xA0 | contextTag);
        return copy;
    }

    /// <summary>
    /// Builds an <c>id-aa-signatureTimeStampToken</c> unsigned attribute
    /// (RFC 3161 §3.3.3.1, OID 1.2.840.113549.1.9.16.2.14) wrapping a
    /// pre-fetched RFC 3161 timestamp token. The result is suitable for
    /// passing in the <c>unsignedAttributes</c> parameter of
    /// <see cref="BuildDetached"/>.
    /// </summary>
    /// <param name="timeStampTokenDer">The DER bytes of the TSA's TimeStampToken (a ContentInfo wrapping a SignedData).</param>
    public static byte[] BuildSignatureTimeStampAttribute(byte[] timeStampTokenDer)
    {
        ArgumentNullException.ThrowIfNull(timeStampTokenDer);
        Asn1Writer w = new();
        w.PushSequence();
        w.WriteObjectIdentifier(KnownOids.SignatureTimeStampToken);
        w.PushSet();
        w.WriteEncoded(timeStampTokenDer);
        w.PopSet();
        w.PopSequence();
        return w.ToArray();
    }

    private static void WriteIssuerAndSerialNumber(Asn1Writer w, X509Certificate cert)
    {
        // IssuerAndSerialNumber ::= SEQUENCE { issuer Name, serialNumber CertificateSerialNumber }
        w.PushSequence();
        // Issuer is already DER-encoded inside the cert's TBS — use the raw bytes.
        w.WriteEncoded(cert.Issuer.RawEncoding);
        w.WriteInteger(cert.Tbs.SerialNumber);
        w.PopSequence();
    }

    private static void WriteAlgorithmIdentifier(Asn1Writer w, AlgorithmIdentifier alg)
    {
        w.PushSequence();
        w.WriteObjectIdentifier(alg.Algorithm);
        // Parameters: NULL when absent for hash and RSA algorithm identifiers.
        if (alg.ParametersAreAbsent || alg.ParametersAreNull)
        {
            w.WriteNull();
        }
        else
        {
            w.WriteEncoded(alg.Parameters);
        }
        w.PopSequence();
    }

    private static AlgorithmIdentifier DigestAlgFor(HashAlgorithmName h)
    {
        ObjectIdentifier oid = h switch
        {
            HashAlgorithmName.Sha256 => KnownOids.Sha256,
            HashAlgorithmName.Sha384 => KnownOids.Sha384,
            HashAlgorithmName.Sha512 => KnownOids.Sha512,
            _ => throw new ArgumentException($"Unsupported hash algorithm: {h}", nameof(h)),
        };
        return new AlgorithmIdentifier(oid, null);
    }

    private static int CompareDerBytes(byte[] a, byte[] b)
    {
        int len = Math.Min(a.Length, b.Length);
        for (int i = 0; i < len; i++)
        {
            int diff = a[i] - b[i];
            if (diff != 0) { return diff; }
        }
        return a.Length - b.Length;
    }

    private static bool CertsEqual(X509Certificate a, X509Certificate b)
    {
        if (a.RawEncoding.Length != b.RawEncoding.Length) { return false; }
        return a.RawEncoding.SequenceEqual(b.RawEncoding);
    }
}
