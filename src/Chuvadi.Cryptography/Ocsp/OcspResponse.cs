// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 6960 §4.2.1 — OCSPResponse, ResponseBytes
// PHASE: Phase 1.1.4 — OCSP

using System;
using System.Collections.Generic;
using System.Numerics;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.Oids;
using Chuvadi.Cryptography.X509;

namespace Chuvadi.Cryptography.Ocsp;

/// <summary>
/// A parsed OCSP response.
/// </summary>
/// <remarks>
/// RFC 6960 §4.2.1:
/// <code>
/// OCSPResponse ::= SEQUENCE {
///   responseStatus  OCSPResponseStatus,
///   responseBytes   [0] EXPLICIT ResponseBytes OPTIONAL
/// }
/// ResponseBytes ::= SEQUENCE {
///   responseType    OBJECT IDENTIFIER,
///   response        OCTET STRING
/// }
/// </code>
/// Chuvadi decodes the BasicOCSPResponse payload when present (the dominant
/// response type in practice).
/// </remarks>
public sealed class OcspResponse
{
    /// <summary>Initialises a new OcspResponse.</summary>
    public OcspResponse(
        OcspResponseStatus status,
        ObjectIdentifier? responseType,
        BasicOcspResponse? basicResponse,
        byte[] rawEncoding)
    {
        ArgumentNullException.ThrowIfNull(rawEncoding);
        Status = status;
        ResponseType = responseType;
        BasicResponse = basicResponse;
        RawEncoding = rawEncoding;
    }

    /// <summary>The response status.</summary>
    public OcspResponseStatus Status { get; }

    /// <summary>The response-type OID. Null when <see cref="Status"/> is not Successful.</summary>
    public ObjectIdentifier? ResponseType { get; }

    /// <summary>
    /// The parsed BasicOCSPResponse payload. Null when the response was not Successful
    /// or its responseType is not <c>id-pkix-ocsp-basic</c>.
    /// </summary>
    public BasicOcspResponse? BasicResponse { get; }

    /// <summary>The full DER bytes of the response.</summary>
    public byte[] RawEncoding { get; }

    // ── Decoder ──────────────────────────────────────────────────────────

    /// <summary>Parses an OCSP response from its DER encoding.</summary>
    public static OcspResponse Decode(byte[] der)
    {
        ArgumentNullException.ThrowIfNull(der);
        Asn1Reader r = new(der);
        Asn1Reader outer = r.ReadSequence();

        int statusValue = ReadEnumerated(outer);
        OcspResponseStatus status = (OcspResponseStatus)statusValue;

        // responseBytes [0] EXPLICIT ResponseBytes OPTIONAL
        ObjectIdentifier? responseType = null;
        BasicOcspResponse? basic = null;

        if (!outer.IsAtEnd && outer.TryPeekTag(Asn1Tag.ContextSpecific(0, isConstructed: true)))
        {
            Asn1Reader respBytesWrapper = outer.ReadExplicit(0);
            Asn1Reader respBytes = respBytesWrapper.ReadSequence();
            responseType = respBytes.ReadObjectIdentifier();
            byte[] responseOctets = respBytes.ReadOctetString();
            respBytes.ExpectEnd();
            respBytesWrapper.ExpectEnd();

            if (responseType.Equals(KnownOids.OcspBasicResponse))
            {
                basic = DecodeBasic(responseOctets);
            }
        }
        outer.ExpectEnd();

        return new OcspResponse(status, responseType, basic, der);
    }

    private static BasicOcspResponse DecodeBasic(byte[] der)
    {
        Asn1Reader r = new(der);
        Asn1Reader outer = r.ReadSequence();

        // tbsResponseData
        byte[] tbsRaw = outer.PeekEncoded();
        Asn1Reader tbs = outer.ReadSequence();

        // version [0] EXPLICIT INTEGER DEFAULT 0
        int version = 1;
        if (tbs.TryPeekTag(Asn1Tag.ContextSpecific(0, isConstructed: true)))
        {
            Asn1Reader vWrap = tbs.ReadExplicit(0);
            BigInteger v = vWrap.ReadInteger();
            vWrap.ExpectEnd();
            version = (int)v + 1;
        }

        // responderID CHOICE [1] EXPLICIT Name | [2] EXPLICIT OCTET STRING
        ResponderID responderId;
        Asn1Tag peek = tbs.PeekTag();
        if (peek == Asn1Tag.ContextSpecific(1, isConstructed: true))
        {
            Asn1Reader nameWrapper = tbs.ReadExplicit(1);
            X509Name name = X509Name.Read(nameWrapper);
            nameWrapper.ExpectEnd();
            responderId = ResponderID.FromName(name);
        }
        else if (peek == Asn1Tag.ContextSpecific(2, isConstructed: true))
        {
            Asn1Reader keyWrapper = tbs.ReadExplicit(2);
            byte[] keyHash = keyWrapper.ReadOctetString();
            keyWrapper.ExpectEnd();
            responderId = ResponderID.FromKeyHash(keyHash);
        }
        else
        {
            throw new Asn1Exception($"Unexpected ResponderID tag: {peek}.");
        }

        DateTimeOffset producedAt = tbs.ReadGeneralizedTime();

        // responses SEQUENCE OF SingleResponse
        List<SingleResponse> responses = new();
        Asn1Reader respList = tbs.ReadSequence();
        while (!respList.IsAtEnd)
        {
            responses.Add(ReadSingleResponse(respList));
        }

        // responseExtensions [1] EXPLICIT OPTIONAL — we don't currently consume them
        if (!tbs.IsAtEnd && tbs.TryPeekTag(Asn1Tag.ContextSpecific(1, isConstructed: true)))
        {
            tbs.Skip();
        }
        tbs.ExpectEnd();

        AlgorithmIdentifier sigAlg = AlgorithmIdentifier.Read(outer);
        BitStringValue sigValue = outer.ReadBitString();

        // certs [0] EXPLICIT SEQUENCE OF Certificate OPTIONAL
        List<X509Certificate> certs = new();
        if (!outer.IsAtEnd && outer.TryPeekTag(Asn1Tag.ContextSpecific(0, isConstructed: true)))
        {
            Asn1Reader certWrapper = outer.ReadExplicit(0);
            Asn1Reader certSeq = certWrapper.ReadSequence();
            while (!certSeq.IsAtEnd)
            {
                byte[] certDer = certSeq.ReadEncoded();
                certs.Add(X509Certificate.Decode(certDer));
            }
            certWrapper.ExpectEnd();
        }
        outer.ExpectEnd();

        return new BasicOcspResponse(version, responderId, producedAt, responses,
            tbsRaw, sigAlg, sigValue, certs);
    }

    private static SingleResponse ReadSingleResponse(Asn1Reader parent)
    {
        Asn1Reader sr = parent.ReadSequence();

        // certID
        Asn1Reader idSeq = sr.ReadSequence();
        AlgorithmIdentifier hashAlg = AlgorithmIdentifier.Read(idSeq);
        byte[] nameHash = idSeq.ReadOctetString();
        byte[] keyHash = idSeq.ReadOctetString();
        BigInteger serial = idSeq.ReadInteger();
        idSeq.ExpectEnd();
        CertId certId = new(hashAlg, nameHash, keyHash, serial);

        // certStatus CHOICE
        Asn1Tag statusTag = sr.PeekTag();
        CertStatus status;
        if (statusTag == Asn1Tag.ContextSpecific(0, isConstructed: false))
        {
            // good [0] IMPLICIT NULL — bytes look like 80 00
            sr.ReadImplicitOctets(0);
            status = CertStatus.Good();
        }
        else if (statusTag == Asn1Tag.ContextSpecific(1, isConstructed: true))
        {
            // revoked [1] IMPLICIT RevokedInfo
            Asn1Reader revoked = ReadImplicitSequence(sr, 1);
            DateTimeOffset revocationTime = revoked.ReadGeneralizedTime();
            Chuvadi.Cryptography.Revocation.CrlReason reason
                = Chuvadi.Cryptography.Revocation.CrlReason.Unspecified;
            if (!revoked.IsAtEnd
                && revoked.TryPeekTag(Asn1Tag.ContextSpecific(0, isConstructed: true)))
            {
                Asn1Reader reasonWrapper = revoked.ReadExplicit(0);
                int reasonValue = ReadEnumerated(reasonWrapper);
                reasonWrapper.ExpectEnd();
                reason = MapReason(reasonValue);
            }
            revoked.ExpectEnd();
            status = CertStatus.Revoked(revocationTime, reason);
        }
        else if (statusTag == Asn1Tag.ContextSpecific(2, isConstructed: false)
              || statusTag == Asn1Tag.ContextSpecific(2, isConstructed: true))
        {
            // unknown [2] IMPLICIT UnknownInfo — encoded as NULL or empty SEQUENCE
            sr.Skip();
            status = CertStatus.Unknown();
        }
        else
        {
            throw new Asn1Exception($"Unexpected CertStatus tag: {statusTag}.");
        }

        DateTimeOffset thisUpdate = sr.ReadGeneralizedTime();
        DateTimeOffset? nextUpdate = null;
        if (!sr.IsAtEnd && sr.TryPeekTag(Asn1Tag.ContextSpecific(0, isConstructed: true)))
        {
            Asn1Reader nuWrap = sr.ReadExplicit(0);
            nextUpdate = nuWrap.ReadGeneralizedTime();
            nuWrap.ExpectEnd();
        }
        // singleExtensions [1] EXPLICIT OPTIONAL — skip
        if (!sr.IsAtEnd && sr.TryPeekTag(Asn1Tag.ContextSpecific(1, isConstructed: true)))
        {
            sr.Skip();
        }
        sr.ExpectEnd();

        return new SingleResponse(certId, status, thisUpdate, nextUpdate);
    }

    private static int ReadEnumerated(Asn1Reader r)
    {
        byte[] raw = r.ReadEncoded();
        if (raw.Length < 2 || raw[0] != 0x0A)
        {
            throw new Asn1Exception("Expected ENUMERATED.");
        }
        int length = raw[1];
        if (length < 1 || length > raw.Length - 2)
        {
            throw new Asn1Exception("Malformed ENUMERATED length.");
        }
        int value = 0;
        for (int i = 0; i < length; i++)
        {
            value = (value << 8) | raw[2 + i];
        }
        return value;
    }

    private static Asn1Reader ReadImplicitSequence(Asn1Reader r, int tagNumber)
    {
        byte[] raw = r.ReadEncoded();
        // First byte is the tag; rewrite [n] IMPLICIT CONSTRUCTED to UNIVERSAL SEQUENCE (0x30).
        // The original tag class+constructed encoding must match what we expect.
        byte expected = (byte)(0x80 | 0x20 | (tagNumber & 0x1F));  // context-specific, constructed
        if (raw[0] != expected)
        {
            throw new Asn1Exception($"Expected [{tagNumber}] IMPLICIT constructed tag, got 0x{raw[0]:X2}.");
        }
        raw[0] = 0x30;  // SEQUENCE
        Asn1Reader wrapped = new(raw);
        return wrapped.ReadSequence();  // consume the rewritten outer tag; return inside-cursor
    }

    private static Chuvadi.Cryptography.Revocation.CrlReason MapReason(int value)
        => value switch
        {
            0 => Chuvadi.Cryptography.Revocation.CrlReason.Unspecified,
            1 => Chuvadi.Cryptography.Revocation.CrlReason.KeyCompromise,
            2 => Chuvadi.Cryptography.Revocation.CrlReason.CaCompromise,
            3 => Chuvadi.Cryptography.Revocation.CrlReason.AffiliationChanged,
            4 => Chuvadi.Cryptography.Revocation.CrlReason.Superseded,
            5 => Chuvadi.Cryptography.Revocation.CrlReason.CessationOfOperation,
            6 => Chuvadi.Cryptography.Revocation.CrlReason.CertificateHold,
            8 => Chuvadi.Cryptography.Revocation.CrlReason.RemoveFromCrl,
            9 => Chuvadi.Cryptography.Revocation.CrlReason.PrivilegeWithdrawn,
            10 => Chuvadi.Cryptography.Revocation.CrlReason.AaCompromise,
            _ => Chuvadi.Cryptography.Revocation.CrlReason.Unspecified,
        };
}
