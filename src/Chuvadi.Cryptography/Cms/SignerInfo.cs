// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5652 §5.3 — SignerInfo
// PHASE: Phase 1.1.4 — CMS / PKCS#7 SignedData decoder

using System;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Oids;
using Chuvadi.Cryptography.X509;

namespace Chuvadi.Cryptography.Cms;

/// <summary>
/// One signer's contribution to a SignedData structure.
/// </summary>
/// <remarks>
/// Structure:
/// <code>
/// SignerInfo ::= SEQUENCE {
///   version            CMSVersion,
///   sid                SignerIdentifier,
///   digestAlgorithm    DigestAlgorithmIdentifier,
///   signedAttrs    [0] IMPLICIT SignedAttributes OPTIONAL,
///   signatureAlgorithm SignatureAlgorithmIdentifier,
///   signature          SignatureValue,
///   unsignedAttrs  [1] IMPLICIT UnsignedAttributes OPTIONAL
/// }
/// CMSVersion ::= INTEGER { v0(0), v1(1), v2(2), v3(3), v4(4), v5(5) }
/// SignatureValue ::= OCTET STRING
/// </code>
/// Verification flow (which lands in a later commit):
/// <list type="number">
///   <item>Locate the signer's certificate by matching <see cref="SignerId"/>
///         against SignedData.Certificates.</item>
///   <item>If <see cref="SignedAttributes"/> is present, compute the digest of
///         the eContent (or detached byte range) under <see cref="DigestAlgorithm"/>
///         and check it matches the messageDigest signed attribute.</item>
///   <item>Compute the digest of <see cref="CmsAttributeTable.DerEncodedForVerification"/>
///         (or the eContent when no signed attrs) under <see cref="DigestAlgorithm"/>.</item>
///   <item>Verify <see cref="Signature"/> over that digest using
///         <see cref="SignatureAlgorithm"/> and the signer's public key.</item>
/// </list>
/// </remarks>
public sealed class SignerInfo
{
    /// <summary>Initialises a new SignerInfo.</summary>
    public SignerInfo(
        int version,
        SignerIdentifier signerId,
        AlgorithmIdentifier digestAlgorithm,
        CmsAttributeTable? signedAttributes,
        AlgorithmIdentifier signatureAlgorithm,
        byte[] signature,
        CmsAttributeTable? unsignedAttributes)
    {
        ArgumentNullException.ThrowIfNull(signerId);
        ArgumentNullException.ThrowIfNull(digestAlgorithm);
        ArgumentNullException.ThrowIfNull(signatureAlgorithm);
        ArgumentNullException.ThrowIfNull(signature);

        Version = version;
        SignerId = signerId;
        DigestAlgorithm = digestAlgorithm;
        SignedAttributes = signedAttributes;
        SignatureAlgorithm = signatureAlgorithm;
        Signature = signature;
        UnsignedAttributes = unsignedAttributes;
    }

    /// <summary>The CMS version: v1 (1) for IssuerAndSerial, v3 (3) for SKI.</summary>
    public int Version { get; }

    /// <summary>Identifies which certificate produced this signature.</summary>
    public SignerIdentifier SignerId { get; }

    /// <summary>The digest algorithm used over the signed content / attributes.</summary>
    public AlgorithmIdentifier DigestAlgorithm { get; }

    /// <summary>The signed attributes (signature actually covers their DER encoding).</summary>
    public CmsAttributeTable? SignedAttributes { get; }

    /// <summary>The signature algorithm (combination of digest and key algorithm).</summary>
    public AlgorithmIdentifier SignatureAlgorithm { get; }

    /// <summary>The raw signature bytes.</summary>
    public byte[] Signature { get; }

    /// <summary>The unsigned attributes (often carries the RFC 3161 timestamp token).</summary>
    public CmsAttributeTable? UnsignedAttributes { get; }

    /// <summary>
    /// True when this SignerInfo uses signed attributes (the standard CMS profile).
    /// </summary>
    public bool HasSignedAttributes => SignedAttributes is not null;

    /// <summary>
    /// Locates the signer's certificate by matching <see cref="SignerId"/> against
    /// the given collection. Returns null when no match exists.
    /// </summary>
    public X509Certificate? FindSignerCertificate(System.Collections.Generic.IEnumerable<X509Certificate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        foreach (X509Certificate c in candidates)
        {
            if (SignerId.Matches(c)) { return c; }
        }
        return null;
    }

    /// <summary>
    /// Convenience accessor: the value of the signed messageDigest attribute, or null when absent.
    /// </summary>
    public byte[]? MessageDigest
    {
        get
        {
            if (SignedAttributes is null) { return null; }
            CmsAttribute? attr = SignedAttributes.Find(KnownOids.MessageDigest);
            if (attr is null) { return null; }
            // The single value is an OCTET STRING TLV; decode it.
            Asn1Reader r = new(attr.SingleValue);
            byte[] digest = r.ReadOctetString();
            r.ExpectEnd();
            return digest;
        }
    }

    /// <summary>
    /// Convenience accessor: the OID asserted by the signed contentType attribute,
    /// or null when absent. Per RFC 5652 §11.1, this must match SignedData's
    /// EncapsulatedContentInfo.eContentType.
    /// </summary>
    public ObjectIdentifier? AssertedContentType
    {
        get
        {
            if (SignedAttributes is null) { return null; }
            CmsAttribute? attr = SignedAttributes.Find(KnownOids.ContentType);
            if (attr is null) { return null; }
            Asn1Reader r = new(attr.SingleValue);
            ObjectIdentifier oid = r.ReadObjectIdentifier();
            r.ExpectEnd();
            return oid;
        }
    }

    /// <summary>
    /// Convenience accessor: the value of the signed signingTime attribute, or null when absent.
    /// </summary>
    public DateTimeOffset? SigningTime
    {
        get
        {
            if (SignedAttributes is null) { return null; }
            CmsAttribute? attr = SignedAttributes.Find(KnownOids.SigningTime);
            if (attr is null) { return null; }
            // The value is a Time CHOICE (UTCTime or GeneralizedTime).
            Asn1Reader r = new(attr.SingleValue);
            Asn1Tag tag = r.PeekTag();
            DateTimeOffset value = tag.TagNumber == (int)Asn1UniversalTag.UtcTime
                ? r.ReadUtcTime()
                : r.ReadGeneralizedTime();
            r.ExpectEnd();
            return value;
        }
    }

    /// <summary>Reads a SignerInfo from a reader at its SEQUENCE.</summary>
    public static SignerInfo Read(Asn1Reader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Asn1Reader seq = reader.ReadSequence();

        int version = seq.ReadInt32();
        SignerIdentifier sid = SignerIdentifier.Read(seq);
        AlgorithmIdentifier digestAlg = AlgorithmIdentifier.Read(seq);

        CmsAttributeTable? signedAttrs = null;
        if (seq.HasContextSpecific(0))
        {
            byte[] implicitContent = seq.ReadImplicitOctets(0);
            signedAttrs = ParseAttributesForVerification(implicitContent);
        }

        AlgorithmIdentifier sigAlg = AlgorithmIdentifier.Read(seq);
        byte[] signature = seq.ReadOctetString();

        CmsAttributeTable? unsignedAttrs = null;
        if (seq.HasContextSpecific(1))
        {
            byte[] implicitContent = seq.ReadImplicitOctets(1);
            unsignedAttrs = ParseAttributesForVerification(implicitContent);
        }

        seq.ExpectEnd();
        return new SignerInfo(version, sid, digestAlg, signedAttrs, sigAlg, signature, unsignedAttrs);
    }

    private static CmsAttributeTable ParseAttributesForVerification(byte[] implicitContent)
    {
        // The implicit content is the body of a SET. To get the DER form that's
        // actually signed, re-emit it with the universal SET tag (0x31) and the
        // same length prefix. Because we have the body bytes already, this is
        // a straightforward reassembly.
        byte[] derSet = EmitSetWrapper(implicitContent);

        // Parse the attributes by reading from the reassembled SET.
        Asn1Reader r = new(derSet);
        Asn1Reader set = r.ReadSet();
        System.Collections.Generic.List<CmsAttribute> attrs = new();
        while (!set.IsAtEnd)
        {
            attrs.Add(CmsAttribute.Read(set));
        }
        set.ExpectEnd();
        r.ExpectEnd();
        return new CmsAttributeTable(attrs, derSet);
    }

    private static byte[] EmitSetWrapper(byte[] body)
    {
        // X.690 §8.1.3: length octets. Reproduce the same length encoding the
        // wire used (always definite-length DER short or long form).
        using System.IO.MemoryStream ms = new();
        ms.WriteByte(0x31);  // universal SET, constructed
        WriteLength(ms, body.Length);
        ms.Write(body, 0, body.Length);
        return ms.ToArray();
    }

    private static void WriteLength(System.IO.Stream output, int length)
    {
        if (length < 0x80)
        {
            output.WriteByte((byte)length);
            return;
        }
        // Long form
        System.Collections.Generic.List<byte> octets = new();
        int n = length;
        while (n > 0)
        {
            octets.Insert(0, (byte)(n & 0xFF));
            n >>= 8;
        }
        output.WriteByte((byte)(0x80 | octets.Count));
        foreach (byte b in octets)
        {
            output.WriteByte(b);
        }
    }
}
