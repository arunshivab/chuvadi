// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — X.509 path validation

using System;
using System.Collections.Generic;
using Chuvadi.Cryptography.X509;

namespace Chuvadi.Cryptography.PathValidation;

/// <summary>
/// A collection of trust anchors, with subject-name lookup.
/// </summary>
public sealed class TrustStore
{
    private readonly List<TrustAnchor> _anchors;

    /// <summary>Initialises an empty trust store.</summary>
    public TrustStore()
    {
        _anchors = new List<TrustAnchor>();
    }

    /// <summary>Initialises a trust store populated with <paramref name="anchors"/>.</summary>
    public TrustStore(IEnumerable<TrustAnchor> anchors)
    {
        ArgumentNullException.ThrowIfNull(anchors);
        _anchors = new List<TrustAnchor>(anchors);
    }

    /// <summary>Adds a trust anchor.</summary>
    public void Add(TrustAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        _anchors.Add(anchor);
    }

    /// <summary>Adds a trust anchor built from a trusted certificate.</summary>
    public void Add(X509Certificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        _anchors.Add(new TrustAnchor(certificate));
    }

    /// <summary>The trust anchors in this store.</summary>
    public IReadOnlyList<TrustAnchor> Anchors => _anchors;

    /// <summary>
    /// Returns all trust anchors whose subject DN matches <paramref name="issuer"/>
    /// by DER byte equality.
    /// </summary>
    public IEnumerable<TrustAnchor> FindBySubject(X509Name issuer)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        foreach (TrustAnchor anchor in _anchors)
        {
            if (NameEquals(anchor.Subject, issuer))
            {
                yield return anchor;
            }
        }
    }

    /// <summary>
    /// Compares two distinguished names by DER byte equality, as required by
    /// RFC 5280 §7.1 for name chaining during path validation.
    /// </summary>
    public static bool NameEquals(X509Name a, X509Name b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        byte[] ar = a.RawEncoding;
        byte[] br = b.RawEncoding;
        if (ar.Length != br.Length) { return false; }
        for (int i = 0; i < ar.Length; i++)
        {
            if (ar[i] != br[i]) { return false; }
        }
        return true;
    }
}
