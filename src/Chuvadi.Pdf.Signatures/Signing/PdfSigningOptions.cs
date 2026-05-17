// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.2.1 — PDF signing API

using System;
using System.Collections.Generic;
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
}
