// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5280 §6.1.1(d) — Trust anchor
// PHASE: Phase 1.1.4 — X.509 path validation

using System;
using Chuvadi.Cryptography.X509;

namespace Chuvadi.Cryptography.PathValidation;

/// <summary>
/// A trust anchor — a CA the verifier trusts to vouch for certificates it issues.
/// </summary>
/// <remarks>
/// RFC 5280 §6.1.1(d) defines a trust anchor as a (trusted CA name, trusted CA
/// public key) pair, optionally with initial path-validation constraints. In
/// practice, most consumers carry trust anchors as full certificates (typically
/// self-signed roots from a system or curated trust store). Chuvadi supports
/// both representations.
/// </remarks>
public sealed class TrustAnchor
{
    /// <summary>Builds a trust anchor from a full trusted certificate.</summary>
    public TrustAnchor(X509Certificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        Certificate = certificate;
        Subject = certificate.Subject;
        SubjectPublicKeyInfo = certificate.Tbs.SubjectPublicKeyInfo;
    }

    /// <summary>
    /// Builds a trust anchor from a name + key pair (no full certificate).
    /// </summary>
    public TrustAnchor(X509Name subject, SubjectPublicKeyInfo subjectPublicKeyInfo)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(subjectPublicKeyInfo);
        Certificate = null;
        Subject = subject;
        SubjectPublicKeyInfo = subjectPublicKeyInfo;
    }

    /// <summary>The trusted CA's distinguished name.</summary>
    public X509Name Subject { get; }

    /// <summary>The trusted CA's public key (the algorithm and key bits).</summary>
    public SubjectPublicKeyInfo SubjectPublicKeyInfo { get; }

    /// <summary>
    /// The full certificate, when this trust anchor was built from one.
    /// May be null when the anchor was constructed from name + key only.
    /// </summary>
    public X509Certificate? Certificate { get; }
}
