// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 6960 §4.2.1 — BasicOCSPResponse
// PHASE: Phase 1.1.4 — OCSP

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Chuvadi.Cryptography.Asn1;
using Chuvadi.Cryptography.X509;

namespace Chuvadi.Cryptography.Ocsp;

/// <summary>
/// A parsed BasicOCSPResponse — the typical OCSP response payload.
/// </summary>
/// <remarks>
/// RFC 6960 §4.2.1:
/// <code>
/// BasicOCSPResponse ::= SEQUENCE {
///   tbsResponseData       ResponseData,
///   signatureAlgorithm    AlgorithmIdentifier,
///   signature             BIT STRING,
///   certs                 [0] EXPLICIT SEQUENCE OF Certificate OPTIONAL
/// }
/// ResponseData ::= SEQUENCE {
///   version               [0] EXPLICIT INTEGER DEFAULT v1,
///   responderID           ResponderID,
///   producedAt            GeneralizedTime,
///   responses             SEQUENCE OF SingleResponse,
///   responseExtensions    [1] EXPLICIT Extensions OPTIONAL
/// }
/// </code>
/// </remarks>
public sealed class BasicOcspResponse
{
    private readonly SingleResponse[] _responses;
    private readonly X509Certificate[] _certs;

    /// <summary>Initialises a new BasicOCSPResponse.</summary>
    public BasicOcspResponse(
        int version,
        ResponderID responderId,
        DateTimeOffset producedAt,
        IList<SingleResponse> responses,
        byte[] tbsRawEncoding,
        AlgorithmIdentifier signatureAlgorithm,
        BitStringValue signatureValue,
        IList<X509Certificate> certs)
    {
        ArgumentNullException.ThrowIfNull(responderId);
        ArgumentNullException.ThrowIfNull(responses);
        ArgumentNullException.ThrowIfNull(tbsRawEncoding);
        ArgumentNullException.ThrowIfNull(signatureAlgorithm);
        ArgumentNullException.ThrowIfNull(signatureValue);
        ArgumentNullException.ThrowIfNull(certs);

        Version = version;
        ResponderId = responderId;
        ProducedAt = producedAt;
        _responses = new SingleResponse[responses.Count];
        responses.CopyTo(_responses, 0);
        TbsRawEncoding = tbsRawEncoding;
        SignatureAlgorithm = signatureAlgorithm;
        SignatureValue = signatureValue;
        _certs = new X509Certificate[certs.Count];
        certs.CopyTo(_certs, 0);
    }

    /// <summary>OCSP version (default 1).</summary>
    public int Version { get; }

    /// <summary>Identifies the responder.</summary>
    public ResponderID ResponderId { get; }

    /// <summary>The time the response was produced.</summary>
    public DateTimeOffset ProducedAt { get; }

    /// <summary>The per-certificate status entries.</summary>
    public ReadOnlyCollection<SingleResponse> Responses => new(_responses);

    /// <summary>The raw DER bytes of <c>tbsResponseData</c>, hashed for signature verification.</summary>
    public byte[] TbsRawEncoding { get; }

    /// <summary>Signature algorithm identifier.</summary>
    public AlgorithmIdentifier SignatureAlgorithm { get; }

    /// <summary>The signature over <see cref="TbsRawEncoding"/>.</summary>
    public BitStringValue SignatureValue { get; }

    /// <summary>
    /// Optional certificates attached to the response. When the responder is
    /// delegated, this typically contains the responder's signing cert.
    /// </summary>
    public ReadOnlyCollection<X509Certificate> Certificates => new(_certs);
}
