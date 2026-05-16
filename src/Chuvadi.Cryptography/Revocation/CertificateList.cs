// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5280 §5.1 — Certificate List structure (CRL)
// PHASE: Phase 1.1.4 — CRL parsing

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Numerics;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Oids;
using Chuvadi.Cryptography.X509;

namespace Chuvadi.Cryptography.Revocation;

/// <summary>
/// A parsed X.509 Certificate Revocation List (CRL).
/// </summary>
/// <remarks>
/// RFC 5280 §5.1 defines a CRL as:
/// <code>
/// CertificateList ::= SEQUENCE {
///     tbsCertList         TBSCertList,
///     signatureAlgorithm  AlgorithmIdentifier,
///     signatureValue      BIT STRING
/// }
/// </code>
/// <para>
/// This decoder accepts v1 and v2 CRLs but rejects delta CRLs (indicated by
/// the <c>deltaCRLIndicator</c> extension being present). Indirect CRLs
/// (with <c>issuingDistributionPoint</c>'s <c>indirectCRL</c> flag set) are
/// also rejected — Chuvadi assumes the CRL is issued by the certificate's
/// own issuer. A future session will lift these restrictions.
/// </para>
/// </remarks>
public sealed class CertificateList
{
    private readonly RevokedCertificate[] _revokedCertificates;
    private readonly Dictionary<BigInteger, RevokedCertificate> _bySerial;

    /// <summary>Initialises a new CertificateList.</summary>
    public CertificateList(
        int version,
        AlgorithmIdentifier tbsSignatureAlgorithm,
        X509Name issuer,
        DateTimeOffset thisUpdate,
        DateTimeOffset? nextUpdate,
        IList<RevokedCertificate> revokedCertificates,
        BigInteger? crlNumber,
        byte[] tbsRawEncoding,
        AlgorithmIdentifier signatureAlgorithm,
        BitStringValue signatureValue,
        byte[] rawEncoding)
    {
        ArgumentNullException.ThrowIfNull(tbsSignatureAlgorithm);
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(revokedCertificates);
        ArgumentNullException.ThrowIfNull(tbsRawEncoding);
        ArgumentNullException.ThrowIfNull(signatureAlgorithm);
        ArgumentNullException.ThrowIfNull(signatureValue);
        ArgumentNullException.ThrowIfNull(rawEncoding);

        Version = version;
        TbsSignatureAlgorithm = tbsSignatureAlgorithm;
        Issuer = issuer;
        ThisUpdate = thisUpdate;
        NextUpdate = nextUpdate;
        _revokedCertificates = new RevokedCertificate[revokedCertificates.Count];
        revokedCertificates.CopyTo(_revokedCertificates, 0);
        CrlNumber = crlNumber;
        TbsRawEncoding = tbsRawEncoding;
        SignatureAlgorithm = signatureAlgorithm;
        SignatureValue = signatureValue;
        RawEncoding = rawEncoding;

        _bySerial = new Dictionary<BigInteger, RevokedCertificate>(_revokedCertificates.Length);
        foreach (RevokedCertificate r in _revokedCertificates)
        {
            // Duplicates: keep the first (RFC 5280 doesn't forbid them, but most
            // implementations don't issue duplicates; if they appear, the earliest
            // revocationDate is the authoritative one).
            _bySerial.TryAdd(r.UserCertificateSerial, r);
        }
    }

    /// <summary>CRL version: 1 (encoded as 0) or 2 (encoded as 1).</summary>
    public int Version { get; }

    /// <summary>The signature algorithm declared inside TBSCertList.</summary>
    public AlgorithmIdentifier TbsSignatureAlgorithm { get; }

    /// <summary>The issuing CA's distinguished name.</summary>
    public X509Name Issuer { get; }

    /// <summary>The time this CRL was issued.</summary>
    public DateTimeOffset ThisUpdate { get; }

    /// <summary>The time by which the next CRL will be issued. May be absent.</summary>
    public DateTimeOffset? NextUpdate { get; }

    /// <summary>The revoked entries.</summary>
    public ReadOnlyCollection<RevokedCertificate> RevokedCertificates
        => new(_revokedCertificates);

    /// <summary>
    /// The CRL's <c>crlNumber</c> extension value, when present. Useful for
    /// ordering: a CRL with a higher number supersedes one with a lower number
    /// from the same issuer.
    /// </summary>
    public BigInteger? CrlNumber { get; }

    /// <summary>The raw DER bytes of TBSCertList — hashed for signature verification.</summary>
    public byte[] TbsRawEncoding { get; }

    /// <summary>The outer signatureAlgorithm. Must equal <see cref="TbsSignatureAlgorithm"/>.</summary>
    public AlgorithmIdentifier SignatureAlgorithm { get; }

    /// <summary>The signature over TBSCertList.</summary>
    public BitStringValue SignatureValue { get; }

    /// <summary>The DER encoding of the whole CertificateList.</summary>
    public byte[] RawEncoding { get; }

    /// <summary>
    /// True iff <paramref name="serial"/> appears in this CRL's revoked list.
    /// </summary>
    public bool IsRevoked(BigInteger serial) => _bySerial.ContainsKey(serial);

    /// <summary>
    /// Returns the revocation entry for <paramref name="serial"/>, or null if not revoked.
    /// </summary>
    public RevokedCertificate? FindRevocation(BigInteger serial)
        => _bySerial.TryGetValue(serial, out RevokedCertificate? r) ? r : null;

    // ── Decoder ──────────────────────────────────────────────────────────

    /// <summary>Parses a CertificateList from its DER encoding.</summary>
    /// <exception cref="Asn1Exception">If the bytes are not a well-formed CRL.</exception>
    /// <exception cref="NotSupportedException">If the CRL is a delta CRL or indirect CRL.</exception>
    public static CertificateList Decode(byte[] der)
    {
        ArgumentNullException.ThrowIfNull(der);
        Asn1Reader r = new(der);
        Asn1Reader outer = r.ReadSequence();

        // tbsCertList
        byte[] tbsRaw = outer.PeekEncoded();
        Asn1Reader tbs = outer.ReadSequence();

        // version OPTIONAL (INTEGER); if present, MUST be v2 (encoded as 1)
        int version = 1;
        if (tbs.TryPeekTag(Asn1Tag.Primitive(Asn1UniversalTag.Integer)))
        {
            BigInteger v = tbs.ReadInteger();
            if (v != BigInteger.Zero && v != BigInteger.One)
            {
                throw new Asn1Exception($"CRL version field must be 0 (v1) or 1 (v2); got {v}.");
            }
            // ASN.1 encodes Version v2 as 1; expose 1-based numbering externally.
            version = (int)v + 1;
        }

        AlgorithmIdentifier tbsSig = AlgorithmIdentifier.Read(tbs);
        X509Name issuer = X509Name.Read(tbs);

        DateTimeOffset thisUpdate = ReadTime(tbs);
        DateTimeOffset? nextUpdate = null;
        // nextUpdate is OPTIONAL — present only when the next peek is a Time, not the
        // revokedCertificates SEQUENCE.
        if (tbs.TryPeekTag(Asn1Tag.Primitive(Asn1UniversalTag.UtcTime))
            || tbs.TryPeekTag(Asn1Tag.Primitive(Asn1UniversalTag.GeneralizedTime)))
        {
            nextUpdate = ReadTime(tbs);
        }

        // revokedCertificates OPTIONAL — SEQUENCE OF revokedCertificate
        List<RevokedCertificate> revoked = new();
        if (!tbs.IsAtEnd
            && tbs.TryPeekTag(Asn1Tag.Constructed(Asn1UniversalTag.Sequence)))
        {
            Asn1Reader revokedSeq = tbs.ReadSequence();
            while (!revokedSeq.IsAtEnd)
            {
                revoked.Add(ReadRevokedEntry(revokedSeq));
            }
        }

        // crlExtensions [0] EXPLICIT Extensions OPTIONAL
        BigInteger? crlNumber = null;
        if (!tbs.IsAtEnd
            && tbs.TryPeekTag(Asn1Tag.ContextSpecific(0, isConstructed: true)))
        {
            Asn1Reader extWrapper = tbs.ReadExplicit(0);
            Asn1Reader extSeq = extWrapper.ReadSequence();
            while (!extSeq.IsAtEnd)
            {
                X509Extension ext = X509Extension.Read(extSeq);
                if (ext.Oid.Equals(KnownOids.DeltaCrlIndicator))
                {
                    throw new NotSupportedException(
                        "Delta CRLs are not supported in this version of Chuvadi.");
                }
                if (ext.Oid.Equals(KnownOids.IssuingDistributionPoint))
                {
                    // RFC 5280 §5.2.5: if this extension is critical and asserts
                    // indirectCRL = TRUE, the CRL covers certs issued by others.
                    // We conservatively reject when the extension is critical and
                    // present at all — a future session will parse it properly.
                    if (ext.Critical)
                    {
                        throw new NotSupportedException(
                            "CRLs with a critical issuingDistributionPoint extension are not yet supported.");
                    }
                }
                if (ext.Oid.Equals(KnownOids.CrlNumber))
                {
                    Asn1Reader cn = new(ext.Value);
                    crlNumber = cn.ReadInteger();
                    cn.ExpectEnd();
                }
            }
            extWrapper.ExpectEnd();
        }
        tbs.ExpectEnd();

        AlgorithmIdentifier outerSig = AlgorithmIdentifier.Read(outer);
        BitStringValue sigValue = outer.ReadBitString();
        outer.ExpectEnd();

        if (!tbsSig.Equals(outerSig))
        {
            throw new Asn1Exception(
                $"CRL inner and outer signatureAlgorithm differ ({tbsSig.Algorithm} vs {outerSig.Algorithm}).");
        }

        return new CertificateList(
            version, tbsSig, issuer, thisUpdate, nextUpdate,
            revoked, crlNumber, tbsRaw, outerSig, sigValue, der);
    }

    private static RevokedCertificate ReadRevokedEntry(Asn1Reader parent)
    {
        Asn1Reader entry = parent.ReadSequence();
        BigInteger serial = entry.ReadInteger();
        DateTimeOffset revocationDate = ReadTime(entry);

        CrlReason reason = CrlReason.Unspecified;
        DateTimeOffset? invalidityDate = null;

        if (!entry.IsAtEnd)
        {
            // crlEntryExtensions are bare (no [0] wrapper) per the inner SEQUENCE
            Asn1Reader extSeq = entry.ReadSequence();
            while (!extSeq.IsAtEnd)
            {
                X509Extension ext = X509Extension.Read(extSeq);
                if (ext.Oid.Equals(KnownOids.CrlReasonCode))
                {
                    reason = MapReason(DecodeEnumeratedValue(ext.Value));
                }
                else if (ext.Oid.Equals(KnownOids.InvalidityDate))
                {
                    Asn1Reader ir = new(ext.Value);
                    invalidityDate = ir.ReadGeneralizedTime();
                    ir.ExpectEnd();
                }
                // Unknown extensions are ignored unless marked critical; entry-level
                // critical extensions are exceptionally rare. RFC 5280 §5.3 says
                // applications MAY accept entries with unrecognised non-critical
                // extensions and MUST reject those with unrecognised critical ones.
                else if (ext.Critical)
                {
                    throw new Asn1Exception(
                        $"Unsupported critical CRL entry extension: {ext.Oid}");
                }
            }
        }
        entry.ExpectEnd();
        return new RevokedCertificate(serial, revocationDate, reason, invalidityDate);
    }

    private static DateTimeOffset ReadTime(Asn1Reader r)
    {
        if (r.TryPeekTag(Asn1Tag.Primitive(Asn1UniversalTag.UtcTime)))
        {
            return r.ReadUtcTime();
        }
        if (r.TryPeekTag(Asn1Tag.Primitive(Asn1UniversalTag.GeneralizedTime)))
        {
            return r.ReadGeneralizedTime();
        }
        throw new Asn1Exception("Expected UTCTime or GeneralizedTime.");
    }

    /// <summary>
    /// Decodes a DER-encoded ASN.1 ENUMERATED value (tag 0x0A) and returns its integer
    /// content. Used for CRL reasonCode entries, whose wire format mirrors INTEGER.
    /// </summary>
    private static int DecodeEnumeratedValue(byte[] der)
    {
        if (der is null || der.Length < 3 || der[0] != 0x0A)
        {
            throw new Asn1Exception("Expected ENUMERATED encoding for CRL reasonCode.");
        }
        int length = der[1];
        if (length < 1 || length > der.Length - 2)
        {
            throw new Asn1Exception("Malformed ENUMERATED length for CRL reasonCode.");
        }
        int value = 0;
        for (int i = 0; i < length; i++)
        {
            value = (value << 8) | der[2 + i];
        }
        return value;
    }

    private static CrlReason MapReason(int value) => value switch
    {
        0 => CrlReason.Unspecified,
        1 => CrlReason.KeyCompromise,
        2 => CrlReason.CaCompromise,
        3 => CrlReason.AffiliationChanged,
        4 => CrlReason.Superseded,
        5 => CrlReason.CessationOfOperation,
        6 => CrlReason.CertificateHold,
        8 => CrlReason.RemoveFromCrl,
        9 => CrlReason.PrivilegeWithdrawn,
        10 => CrlReason.AaCompromise,
        _ => CrlReason.Unspecified,
    };
}
