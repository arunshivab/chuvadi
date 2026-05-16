// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — X.509 path validation

using System;
using System.Collections.Generic;
using Chuvadi.Cryptography.X509;

namespace Chuvadi.Cryptography.PathValidation;

/// <summary>
/// A certificate path: an ordered sequence from the end-entity (leaf) to a
/// trust anchor, plus the matching anchor.
/// </summary>
/// <remarks>
/// Convention: <see cref="Certificates"/>[0] is the leaf, the last element is
/// the certificate issued by <see cref="Anchor"/>. The trust anchor's
/// certificate (when present) is NOT included in <see cref="Certificates"/> —
/// the anchor's public key is consumed to verify the last certificate, but the
/// anchor itself is not part of the path being validated.
/// </remarks>
public sealed class CertificatePath
{
    /// <summary>Initialises a new CertificatePath.</summary>
    public CertificatePath(IList<X509Certificate> certificates, TrustAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(certificates);
        ArgumentNullException.ThrowIfNull(anchor);
        if (certificates.Count == 0)
        {
            throw new ArgumentException("Certificate path must contain at least the leaf certificate.",
                nameof(certificates));
        }
        Certificates = new List<X509Certificate>(certificates).AsReadOnly();
        Anchor = anchor;
    }

    /// <summary>The certificates in the path, leaf first, intermediate-CA-issued-by-anchor last.</summary>
    public IReadOnlyList<X509Certificate> Certificates { get; }

    /// <summary>The trust anchor that issued the topmost certificate.</summary>
    public TrustAnchor Anchor { get; }

    /// <summary>The leaf certificate (end entity).</summary>
    public X509Certificate Leaf => Certificates[0];

    /// <summary>The number of certificates in the path (excluding the anchor).</summary>
    public int Length => Certificates.Count;
}
