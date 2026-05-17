// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 32000-2 §12.8.4.3 — Validation-Related Information (VRI)
// PHASE: Phase 1.1.4 — DSS VRI extraction

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Chuvadi.Cryptography.Ocsp;
using Chuvadi.Cryptography.Revocation;
using Chuvadi.Cryptography.X509;

namespace Chuvadi.Pdf.Signatures.Dss;

/// <summary>
/// Per-signature validation material from the <c>/DSS /VRI</c> sub-dictionary.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.8.4.3: the VRI sub-dictionary maps each signature's
/// SHA-1 hex (upper-case, 40 characters, of the binary bytes inside the
/// signature dictionary's <c>/Contents</c>) to a per-signature dictionary
/// containing <c>/Cert</c>, <c>/CRL</c>, and <c>/OCSP</c> arrays of stream
/// references — the validation material that applies specifically to that
/// signature, rather than the document as a whole. The optional <c>/TS</c>
/// (PDF 2.0 timestamp token) and <c>/TU</c> (creation time) entries are
/// not yet parsed.
/// </remarks>
public sealed class VriEntry
{
    private readonly X509Certificate[] _certificates;
    private readonly CertificateList[] _crls;
    private readonly OcspResponse[] _ocspResponses;

    /// <summary>Initialises a new VRI entry.</summary>
    public VriEntry(
        IList<X509Certificate> certificates,
        IList<CertificateList> crls,
        IList<OcspResponse> ocspResponses)
    {
        ArgumentNullException.ThrowIfNull(certificates);
        ArgumentNullException.ThrowIfNull(crls);
        ArgumentNullException.ThrowIfNull(ocspResponses);
        _certificates = new X509Certificate[certificates.Count];
        certificates.CopyTo(_certificates, 0);
        _crls = new CertificateList[crls.Count];
        crls.CopyTo(_crls, 0);
        _ocspResponses = new OcspResponse[ocspResponses.Count];
        ocspResponses.CopyTo(_ocspResponses, 0);
    }

    /// <summary>Certificates from the VRI entry's <c>/Cert</c> array.</summary>
    public ReadOnlyCollection<X509Certificate> Certificates => new(_certificates);

    /// <summary>CRLs from the VRI entry's <c>/CRL</c> array.</summary>
    public ReadOnlyCollection<CertificateList> Crls => new(_crls);

    /// <summary>OCSP responses from the VRI entry's <c>/OCSP</c> array.</summary>
    public ReadOnlyCollection<OcspResponse> OcspResponses => new(_ocspResponses);

    /// <summary>True iff the entry has no certs, CRLs, or OCSP responses.</summary>
    public bool IsEmpty =>
        _certificates.Length == 0 && _crls.Length == 0 && _ocspResponses.Length == 0;
}
