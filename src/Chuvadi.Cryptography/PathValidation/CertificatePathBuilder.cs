// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 5280 §6 — Certification Path Validation
// PHASE: Phase 1.1.4 — X.509 path validation

using System;
using System.Collections.Generic;
using Chuvadi.Cryptography.X509;

namespace Chuvadi.Cryptography.PathValidation;

/// <summary>
/// Builds candidate certificate paths from a leaf certificate to a trust anchor.
/// </summary>
/// <remarks>
/// Walks the issuer chain from leaf upward, using name chaining (issuer DN ==
/// subject DN on the next cert up). Multiple valid paths can exist when a CA
/// has been cross-signed; <see cref="BuildPaths"/> returns all of them so a
/// downstream validator can try each.
/// </remarks>
public static class CertificatePathBuilder
{
    private const int DefaultMaxPathLength = 16;

    /// <summary>
    /// Returns all candidate paths from <paramref name="leaf"/> to any trust
    /// anchor in <paramref name="trustStore"/>, using <paramref name="intermediates"/>
    /// for any links between leaf and anchor.
    /// </summary>
    /// <param name="leaf">The end-entity certificate to start from.</param>
    /// <param name="intermediates">Candidate intermediate CA certificates to link with.</param>
    /// <param name="trustStore">The trust anchors to terminate paths at.</param>
    /// <param name="maxPathLength">
    /// Safety cap on path depth (defaults to 16). Pathological inputs with
    /// cycles or huge fan-out are bounded.
    /// </param>
    public static IReadOnlyList<CertificatePath> BuildPaths(
        X509Certificate leaf,
        IEnumerable<X509Certificate> intermediates,
        TrustStore trustStore,
        int maxPathLength = DefaultMaxPathLength)
    {
        ArgumentNullException.ThrowIfNull(leaf);
        ArgumentNullException.ThrowIfNull(intermediates);
        ArgumentNullException.ThrowIfNull(trustStore);
        if (maxPathLength < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPathLength), "maxPathLength must be at least 1.");
        }

        List<X509Certificate> intermediateList = new(intermediates);
        List<CertificatePath> results = new();
        List<X509Certificate> current = new() { leaf };

        Search(current, intermediateList, trustStore, results, maxPathLength);
        return results;
    }

    private static void Search(
        List<X509Certificate> current,
        List<X509Certificate> intermediates,
        TrustStore trustStore,
        List<CertificatePath> results,
        int maxPathLength)
    {
        if (current.Count > maxPathLength) { return; }

        X509Certificate top = current[current.Count - 1];

        // 1. If the top cert is issued by a trust anchor, we have a complete path.
        foreach (TrustAnchor anchor in trustStore.FindBySubject(top.Issuer))
        {
            results.Add(new CertificatePath(current.ToArray(), anchor));
        }

        // 2. If the top cert is self-issued (subject == issuer) AND not chained to
        //    an anchor above, we stop here — no further intermediates can help.
        if (TrustStore.NameEquals(top.Subject, top.Issuer))
        {
            return;
        }

        // 3. Otherwise, look for intermediates whose subject matches top.Issuer.
        for (int i = 0; i < intermediates.Count; i++)
        {
            X509Certificate candidate = intermediates[i];
            if (!TrustStore.NameEquals(candidate.Subject, top.Issuer)) { continue; }

            // Loop guard — don't revisit a cert already on the path
            if (ContainsCertificate(current, candidate)) { continue; }

            current.Add(candidate);
            Search(current, intermediates, trustStore, results, maxPathLength);
            current.RemoveAt(current.Count - 1);
        }
    }

    private static bool ContainsCertificate(List<X509Certificate> path, X509Certificate cert)
    {
        // Compare by raw encoding — same DER bytes means same cert
        byte[] target = cert.RawEncoding;
        foreach (X509Certificate c in path)
        {
            byte[] existing = c.RawEncoding;
            if (existing.Length != target.Length) { continue; }
            bool match = true;
            for (int i = 0; i < target.Length; i++)
            {
                if (existing[i] != target[i]) { match = false; break; }
            }
            if (match) { return true; }
        }
        return false;
    }
}
