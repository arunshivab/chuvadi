// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5280 §4.1.2 — TBSCertificate
// PHASE: Phase 1.1.4 — X.509 certificate decoder

using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
using System.Numerics;
using Chuvadi.Cryptography.Asn1;

namespace Chuvadi.Cryptography.X509;

/// <summary>
/// The "to-be-signed" body of an X.509 certificate.
/// </summary>
/// <remarks>
/// Structure:
/// <code>
/// TBSCertificate ::= SEQUENCE {
///   version          [0] EXPLICIT Version DEFAULT v1,
///   serialNumber         CertificateSerialNumber,
///   signature            AlgorithmIdentifier,
///   issuer               Name,
///   validity             Validity,
///   subject              Name,
///   subjectPublicKeyInfo SubjectPublicKeyInfo,
///   issuerUniqueID   [1] IMPLICIT UniqueIdentifier OPTIONAL,
///   subjectUniqueID  [2] IMPLICIT UniqueIdentifier OPTIONAL,
///   extensions       [3] EXPLICIT Extensions OPTIONAL
/// }
/// Version ::= INTEGER { v1(0), v2(1), v3(2) }
/// </code>
/// The RawEncoding property holds the exact bytes of this TBSCertificate
/// SEQUENCE — these are the bytes whose hash the signatureValue covers.
/// Preserving them losslessly is what makes signature verification possible.
/// </remarks>
public sealed class TbsCertificate
{
    private readonly X509Extension[] _extensions;

    /// <summary>Initialises a new TbsCertificate.</summary>
    public TbsCertificate(
        int version,
        BigInteger serialNumber,
        AlgorithmIdentifier signature,
        X509Name issuer,
        Validity validity,
        X509Name subject,
        SubjectPublicKeyInfo subjectPublicKeyInfo,
        BitStringValue? issuerUniqueId,
        BitStringValue? subjectUniqueId,
        IList<X509Extension>? extensions,
        byte[] rawEncoding)
    {
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(validity);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(subjectPublicKeyInfo);
        ArgumentNullException.ThrowIfNull(rawEncoding);

        Version = version;
        SerialNumber = serialNumber;
        Signature = signature;
        Issuer = issuer;
        Validity = validity;
        Subject = subject;
        SubjectPublicKeyInfo = subjectPublicKeyInfo;
        IssuerUniqueId = issuerUniqueId;
        SubjectUniqueId = subjectUniqueId;
        _extensions = extensions is null
            ? Array.Empty<X509Extension>()
            : Enumerable.ToArray(extensions);
        RawEncoding = rawEncoding;
    }

    /// <summary>The certificate version: 0=v1, 1=v2, 2=v3.</summary>
    public int Version { get; }

    /// <summary>The certificate serial number (unique within an issuer; can be any non-negative BigInteger).</summary>
    public BigInteger SerialNumber { get; }

    /// <summary>The signature algorithm declared inside the TBS body (must match the outer Certificate's algorithm).</summary>
    public AlgorithmIdentifier Signature { get; }

    /// <summary>The issuer distinguished name.</summary>
    public X509Name Issuer { get; }

    /// <summary>The validity period.</summary>
    public Validity Validity { get; }

    /// <summary>The subject distinguished name.</summary>
    public X509Name Subject { get; }

    /// <summary>The subject's public key.</summary>
    public SubjectPublicKeyInfo SubjectPublicKeyInfo { get; }

    /// <summary>Optional v2/v3 issuerUniqueID.</summary>
    public BitStringValue? IssuerUniqueId { get; }

    /// <summary>Optional v2/v3 subjectUniqueID.</summary>
    public BitStringValue? SubjectUniqueId { get; }

    /// <summary>The v3 extensions.</summary>
    public ReadOnlyCollection<X509Extension> Extensions => new(_extensions);

    /// <summary>
    /// The complete TBSCertificate encoding — the bytes whose hash is signed by
    /// the outer Certificate.signatureValue.
    /// </summary>
    public byte[] RawEncoding { get; }

    /// <summary>Finds an extension by OID, or returns null when absent.</summary>
    public X509Extension? FindExtension(ObjectIdentifier oid)
    {
        ArgumentNullException.ThrowIfNull(oid);
        foreach (X509Extension e in _extensions)
        {
            if (e.Oid.Equals(oid)) { return e; }
        }
        return null;
    }

    /// <summary>Reads a TBSCertificate from a reader positioned at its SEQUENCE.</summary>
    public static TbsCertificate Read(Asn1Reader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        byte[] raw = reader.PeekEncoded();
        Asn1Reader tbs = reader.ReadSequence();

        // [0] EXPLICIT version DEFAULT v1
        int version = 0;
        if (tbs.HasContextSpecific(0))
        {
            Asn1Reader v = tbs.ReadExplicit(0);
            version = v.ReadInt32();
            v.ExpectEnd();
        }

        BigInteger serial = tbs.ReadInteger();
        AlgorithmIdentifier sig = AlgorithmIdentifier.Read(tbs);
        X509Name issuer = X509Name.Read(tbs);
        Validity validity = Validity.Read(tbs);
        X509Name subject = X509Name.Read(tbs);
        SubjectPublicKeyInfo spki = SubjectPublicKeyInfo.Read(tbs);

        BitStringValue? issuerUid = null;
        BitStringValue? subjectUid = null;
        List<X509Extension>? exts = null;

        while (!tbs.IsAtEnd)
        {
            if (tbs.HasContextSpecific(1))
            {
                byte[] content = tbs.ReadImplicitOctets(1);
                issuerUid = DecodeUniqueIdContent(content);
            }
            else if (tbs.HasContextSpecific(2))
            {
                byte[] content = tbs.ReadImplicitOctets(2);
                subjectUid = DecodeUniqueIdContent(content);
            }
            else if (tbs.HasContextSpecific(3))
            {
                Asn1Reader extWrapper = tbs.ReadExplicit(3);
                Asn1Reader extSeq = extWrapper.ReadSequence();
                exts = new();
                while (!extSeq.IsAtEnd)
                {
                    exts.Add(X509Extension.Read(extSeq));
                }
                extSeq.ExpectEnd();
                extWrapper.ExpectEnd();
            }
            else
            {
                throw new Asn1Exception(
                    $"Unexpected element in TBSCertificate: {tbs.PeekTag()}");
            }
        }

        tbs.ExpectEnd();
        return new TbsCertificate(version, serial, sig, issuer, validity, subject, spki,
            issuerUid, subjectUid, exts, raw);
    }

    private static BitStringValue DecodeUniqueIdContent(byte[] content)
    {
        if (content.Length < 1)
        {
            throw new Asn1Exception("UniqueIdentifier content too short");
        }
        int unused = content[0];
        if (unused > 7)
        {
            throw new Asn1Exception($"UniqueIdentifier unused-bits must be 0..7, got {unused}");
        }
        byte[] payload = new byte[content.Length - 1];
        Array.Copy(content, 1, payload, 0, payload.Length);
        return new BitStringValue(payload, unused);
    }
}
