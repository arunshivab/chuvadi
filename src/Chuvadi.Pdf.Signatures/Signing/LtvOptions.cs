// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 32000-2 §12.8.4.3 — Document Security Store (DSS) and VRI
// PHASE: Phase 1.2.4 — LTV signing

using System.Collections.Generic;
using Chuvadi.Cryptography.Ocsp;
using Chuvadi.Cryptography.Revocation;
using Chuvadi.Cryptography.X509;

namespace Chuvadi.Pdf.Signatures.Signing;

/// <summary>
/// Long-term validation material to embed in a PDF at sign time.
/// </summary>
/// <remarks>
/// <para>
/// When supplied via <see cref="PdfSigningOptions.LtvOptions"/>, Chuvadi
/// emits a <c>/DSS</c> dictionary (ISO 32000-2 §12.8.4.3) into the
/// document during signing. The DSS carries certificates (typically the
/// signer's chain), CRLs covering each chain link, and OCSP responses for
/// any link relying on OCSP for revocation. With this material baked
/// into the document, a verifier can check the signature offline at any
/// point in the future without re-contacting the issuing CAs.
/// </para>
/// <para>
/// When <see cref="IncludeVri"/> is true, the same material is
/// additionally emitted as a per-signature <c>/VRI</c> sub-dictionary
/// entry keyed by SHA-1 of the CMS <c>/Contents</c> bytes. This is
/// optional — the document-level material is usually enough — but is
/// the convention used by Adobe products and what the Phase 1.1.4
/// verifier picks up when present.
/// </para>
/// </remarks>
public sealed class LtvOptions
{
    /// <summary>
    /// Certificates to embed (typically the signer's CA chain).
    /// The signer's own certificate is already embedded in the CMS;
    /// adding it here is harmless but redundant.
    /// </summary>
    public IReadOnlyList<X509Certificate>? Certificates { get; init; }

    /// <summary>CRLs to embed, covering links in the signer's chain.</summary>
    public IReadOnlyList<CertificateList>? Crls { get; init; }

    /// <summary>OCSP responses to embed, covering links in the signer's chain.</summary>
    public IReadOnlyList<OcspResponse>? OcspResponses { get; init; }

    /// <summary>
    /// When true, additionally emit a <c>/VRI</c> sub-dictionary entry
    /// keyed by SHA-1 of the signature's <c>/Contents</c> bytes,
    /// carrying the same material.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Currently unsupported in single-pass signing.</strong>
    /// The VRI entry's key is SHA-1 of the CMS bytes inside the
    /// signature's <c>/Contents</c>, which is only known after signing
    /// completes. Patching the key in place after signing breaks the
    /// byte-range hash (the key bytes fall inside the signed region).
    /// Real-world LTV workflows handle VRI via an incremental update
    /// appended after the signature, so the VRI bytes sit outside the
    /// original byte range. Chuvadi's incremental-update support is
    /// deferred to a future session; in the meantime, the document-level
    /// <c>/DSS</c> arrays (<see cref="Certificates"/>,
    /// <see cref="Crls"/>, <see cref="OcspResponses"/>) cover the
    /// common LTV scenario.
    /// </para>
    /// <para>
    /// Setting this to true currently throws
    /// <see cref="System.NotSupportedException"/>.
    /// </para>
    /// </remarks>
    public bool IncludeVri { get; init; }

    /// <summary>True iff at least one of certs / CRLs / OCSPs is supplied.</summary>
    public bool HasMaterial =>
        (Certificates is not null && Certificates.Count > 0)
        || (Crls is not null && Crls.Count > 0)
        || (OcspResponses is not null && OcspResponses.Count > 0);
}
