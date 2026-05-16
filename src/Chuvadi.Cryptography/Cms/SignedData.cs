// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5652 §5 — SignedData
// PHASE: Phase 1.1.4 — CMS / PKCS#7 SignedData decoder

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.X509;

namespace Chuvadi.Cryptography.Cms;

/// <summary>
/// A decoded CMS SignedData structure.
/// </summary>
/// <remarks>
/// Structure:
/// <code>
/// SignedData ::= SEQUENCE {
///   version           CMSVersion,
///   digestAlgorithms  DigestAlgorithmIdentifiers,
///   encapContentInfo  EncapsulatedContentInfo,
///   certificates  [0] IMPLICIT CertificateSet OPTIONAL,
///   crls          [1] IMPLICIT RevocationInfoChoices OPTIONAL,
///   signerInfos       SignerInfos
/// }
/// DigestAlgorithmIdentifiers ::= SET OF DigestAlgorithmIdentifier
/// SignerInfos ::= SET OF SignerInfo
/// CertificateSet ::= SET OF CertificateChoices
/// </code>
/// For PDF signatures the typical shape is:
/// <list type="bullet">
///   <item>One digestAlgorithm (SHA-256 or SHA-384 in modern signatures, SHA-1 in legacy).</item>
///   <item>One EncapsulatedContentInfo with eContentType = id-data and absent eContent (detached).</item>
///   <item>Certificates set containing the signer's cert and (typically) the issuing CA chain.</item>
///   <item>One SignerInfo with signedAttrs containing contentType, messageDigest, signingTime,
///         and SigningCertificateV2 (CAdES baseline).</item>
/// </list>
/// CRLs in the SignedData are rare; revocation information for CAdES typically arrives
/// via the revocationValues unsigned attribute.
/// </remarks>
public sealed class SignedData
{
    private readonly AlgorithmIdentifier[] _digestAlgorithms;
    private readonly X509Certificate[] _certificates;
    private readonly byte[][] _crls;
    private readonly SignerInfo[] _signerInfos;

    /// <summary>Initialises a new SignedData.</summary>
    public SignedData(
        int version,
        IList<AlgorithmIdentifier> digestAlgorithms,
        EncapsulatedContentInfo encapContentInfo,
        IList<X509Certificate> certificates,
        IList<byte[]> crls,
        IList<SignerInfo> signerInfos)
    {
        ArgumentNullException.ThrowIfNull(digestAlgorithms);
        ArgumentNullException.ThrowIfNull(encapContentInfo);
        ArgumentNullException.ThrowIfNull(certificates);
        ArgumentNullException.ThrowIfNull(crls);
        ArgumentNullException.ThrowIfNull(signerInfos);

        Version = version;
        _digestAlgorithms = digestAlgorithms.ToArray();
        EncapContentInfo = encapContentInfo;
        _certificates = certificates.ToArray();
        _crls = crls.ToArray();
        _signerInfos = signerInfos.ToArray();
    }

    /// <summary>The CMS version (1 for typical PDF signatures, 3 for SKI signers).</summary>
    public int Version { get; }

    /// <summary>The set of digest algorithms used by any SignerInfo.</summary>
    public ReadOnlyCollection<AlgorithmIdentifier> DigestAlgorithms => new(_digestAlgorithms);

    /// <summary>The encapsulated content (or its absence, for detached signatures).</summary>
    public EncapsulatedContentInfo EncapContentInfo { get; }

    /// <summary>The certificates embedded in the SignedData.</summary>
    public ReadOnlyCollection<X509Certificate> Certificates => new(_certificates);

    /// <summary>The CRLs embedded in the SignedData (raw bytes — CRL decoder lands later).</summary>
    public ReadOnlyCollection<byte[]> Crls => new(_crls);

    /// <summary>The SignerInfos.</summary>
    public ReadOnlyCollection<SignerInfo> SignerInfos => new(_signerInfos);

    /// <summary>Reads a SignedData from a reader at its SEQUENCE.</summary>
    public static SignedData Read(Asn1Reader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Asn1Reader seq = reader.ReadSequence();

        int version = seq.ReadInt32();

        // digestAlgorithms : SET OF AlgorithmIdentifier
        List<AlgorithmIdentifier> digestAlgs = new();
        Asn1Reader digestSet = seq.ReadSet();
        while (!digestSet.IsAtEnd)
        {
            digestAlgs.Add(AlgorithmIdentifier.Read(digestSet));
        }
        digestSet.ExpectEnd();

        EncapsulatedContentInfo encap = EncapsulatedContentInfo.Read(seq);

        List<X509Certificate> certificates = new();
        List<byte[]> crls = new();

        if (seq.HasContextSpecific(0))
        {
            byte[] certSetBytes = seq.ReadImplicitOctets(0);
            ReadCertificates(certSetBytes, certificates);
        }

        if (seq.HasContextSpecific(1))
        {
            byte[] crlSetBytes = seq.ReadImplicitOctets(1);
            ReadCrls(crlSetBytes, crls);
        }

        // signerInfos : SET OF SignerInfo
        List<SignerInfo> signerInfos = new();
        Asn1Reader signerSet = seq.ReadSet();
        while (!signerSet.IsAtEnd)
        {
            signerInfos.Add(SignerInfo.Read(signerSet));
        }
        signerSet.ExpectEnd();

        seq.ExpectEnd();
        return new SignedData(version, digestAlgs, encap, certificates, crls, signerInfos);
    }

    private static void ReadCertificates(byte[] body, List<X509Certificate> output)
    {
        // body is the unwrapped IMPLICIT [0] SET OF CertificateChoices content.
        // CertificateChoices is a CHOICE; we only handle the universal Certificate
        // variant (which is by far the most common). Other variants — extendedCert
        // (obsolete), v1AttrCert (deprecated), v2AttrCert, other [3] — are rare
        // and Chuvadi skips them with the raw bytes preserved.
        int pos = 0;
        while (pos < body.Length)
        {
            // Peek the tag to decide whether this is a vanilla Certificate (SEQUENCE)
            // or one of the alternative CHOICE arms.
            int after = Asn1TagLength.Read(body, pos, out Asn1Tag tag, out _, out _);
            int elementLength = after - pos;
            byte[] elementBytes = new byte[elementLength];
            Array.Copy(body, pos, elementBytes, 0, elementLength);

            if (tag.TagClass == Asn1TagClass.Universal &&
                tag.TagNumber == (int)Asn1UniversalTag.Sequence)
            {
                output.Add(X509Certificate.Decode(elementBytes));
            }
            // Else: silently skip non-Certificate CHOICE variants. A future
            // attribute-certificate decoder can lift these.

            pos = after;
        }
    }

    private static void ReadCrls(byte[] body, List<byte[]> output)
    {
        // For now, keep raw CRL bytes; the CRL decoder is a later commit.
        int pos = 0;
        while (pos < body.Length)
        {
            int after = Asn1TagLength.Read(body, pos, out _, out _, out _);
            int len = after - pos;
            byte[] crl = new byte[len];
            Array.Copy(body, pos, crl, 0, len);
            output.Add(crl);
            pos = after;
        }
    }
}
