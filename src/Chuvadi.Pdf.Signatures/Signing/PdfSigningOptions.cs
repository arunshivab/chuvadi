// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.2.1 — PDF signing API

using System;
using System.Collections.Generic;
using Chuvadi.Cryptography.Timestamps;
using Chuvadi.Cryptography.X509;

namespace Chuvadi.Pdf.Signatures.Signing;

/// <summary>
/// Options for <see cref="PdfSigner.Sign"/>.
/// </summary>
public sealed class PdfSigningOptions
{
    /// <summary>
    /// The signature field's <c>/T</c> (partial field name).
    /// Defaults to <c>Signature1</c>.
    /// </summary>
    public string SignatureFieldName { get; init; } = "Signature1";

    /// <summary>
    /// Optional <c>/Reason</c> entry on the signature dictionary (e.g.
    /// <c>"I approve this document"</c>).
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Optional <c>/Location</c> entry on the signature dictionary
    /// (e.g. the signer's city).
    /// </summary>
    public string? Location { get; init; }

    /// <summary>
    /// Optional <c>/ContactInfo</c> entry (e.g. an email address for
    /// follow-up).
    /// </summary>
    public string? ContactInfo { get; init; }

    /// <summary>
    /// The signing time recorded both in the signature dictionary's
    /// <c>/M</c> entry and in the CMS <c>signingTime</c> signed attribute.
    /// Defaults to <see cref="DateTimeOffset.UtcNow"/> at sign time.
    /// </summary>
    public DateTimeOffset? SigningTime { get; init; }

    /// <summary>
    /// The number of bytes reserved for the CMS signature inside the
    /// <c>/Contents</c> placeholder. Must be at least as large as the
    /// produced CMS. Defaults to 16384 (16 KiB), which comfortably
    /// accommodates an RSA-2048 signature with a small chain.
    /// </summary>
    public int ContentsPlaceholderSize { get; init; } = 16384;

    /// <summary>
    /// Additional certificates to include in the CMS SignedData
    /// alongside the signer's own certificate (typically the issuing
    /// CA chain).
    /// </summary>
    public IEnumerable<X509Certificate>? ExtraCertificates { get; init; }

    /// <summary>
    /// When non-null, an RFC 3161 timestamp is fetched from this client
    /// over the SignerInfo's signature and embedded as an
    /// <c>id-aa-signatureTimeStampToken</c> unsigned attribute.
    /// </summary>
    /// <remarks>
    /// The same hash algorithm as the signer's is used for the TSA
    /// request. A <see cref="HttpTsaClient"/> is the typical
    /// implementation; for tests, callers can supply an in-memory
    /// <see cref="ITsaClient"/> that returns canned responses.
    /// </remarks>
    public ITsaClient? TsaClient { get; init; }

    /// <summary>
    /// Async counterpart to <see cref="TsaClient"/>. When non-null and
    /// the document is signed via <see cref="PdfSigner.SignAsync"/>, the
    /// timestamp is fetched without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// If both this and <see cref="TsaClient"/> are set, the async client
    /// is preferred under <see cref="PdfSigner.SignAsync"/> and the sync
    /// client is preferred under <see cref="PdfSigner.Sign"/>. Implementations
    /// such as <see cref="HttpTsaClient"/> implement both, so callers can
    /// supply a single instance and select the path at call-site.
    /// </remarks>
    public IAsyncTsaClient? AsyncTsaClient { get; init; }

    /// <summary>
    /// When non-null, the signature field is also rendered as a visible
    /// widget annotation on the page indicated by
    /// <see cref="SignatureAppearance.PageIndex"/>.
    /// </summary>
    public SignatureAppearance? Appearance { get; init; }

    /// <summary>
    /// When non-null, validation material is embedded in a <c>/DSS</c>
    /// dictionary (ISO 32000-2 §12.8.4.3) so that the signature can be
    /// validated offline at any time after signing — Long-Term Validation
    /// (LTV).
    /// </summary>
    /// <remarks>
    /// The typical LTV payload is the signer's full CA chain plus a CRL
    /// and/or OCSP response per chain link. Callers are responsible for
    /// fetching this material before signing; Chuvadi's
    /// <see cref="Chuvadi.Pdf.Signatures.Verification"/> facilities can
    /// be used to discover it from the certs themselves.
    /// </remarks>
    public LtvOptions? LtvOptions { get; init; }
}
