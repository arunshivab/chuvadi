// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 7932 §3.2, §3.5
// PHASE: Phase 2.2 stage 3 — Huffman tree construction

using System;
using System.Collections.Generic;
using System.Linq;

namespace Chuvadi.Pdf.Fonts.Woff2;

/// <summary>
/// Length-limited Huffman tree construction via the package-merge algorithm.
/// </summary>
/// <remarks>
/// <para>
/// Given a frequency count per symbol and a maximum code length, produces optimal
/// canonical prefix codes whose lengths do not exceed the limit. This is essential
/// for Brotli because RFC §3.5 caps code lengths at 15 for the main alphabets and
/// at 5 for the code-length alphabet itself.
/// </para>
/// <para>
/// The package-merge algorithm (Larmore-Hirschberg 1990): for each of <c>maxLength</c>
/// levels, take the bottom <c>2*n - 2</c> nodes (where n is the count of non-zero-frequency
/// symbols) by cost. Pair them into "packages." Merge with the original symbols for
/// the next level. After <c>maxLength</c> levels, count how many surviving packages
/// each original symbol appears in — that's its code length.
/// </para>
/// </remarks>
internal static class BrotliHuffman
{
    /// <summary>
    /// Compute optimal code lengths for the given symbol frequencies, with each length ≤ maxLength.
    /// </summary>
    /// <param name="frequencies">Per-symbol frequency. Zero-frequency symbols get length 0.</param>
    /// <param name="maxLength">Maximum allowed code length.</param>
    /// <returns>Code length per symbol (0 for unused).</returns>
    internal static int[] ComputeCodeLengths(int[] frequencies, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(frequencies);
        if (maxLength is < 1 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLength));
        }
        int n = frequencies.Length;
        int[] lengths = new int[n];

        // Collect non-zero symbols with frequencies.
        List<(int Symbol, long Cost)> active = new();
        for (int i = 0; i < n; i++)
        {
            if (frequencies[i] > 0) { active.Add((i, frequencies[i])); }
        }
        if (active.Count == 0) { return lengths; }
        if (active.Count == 1)
        {
            // Single non-zero symbol: by RFC §3.4, NSYM=1 has length 0 (zero bits per use).
            // Callers expecting at least 1 bit must handle this separately.
            lengths[active[0].Symbol] = 0;
            return lengths;
        }

        // Each "Node" tracks which original symbols are inside it. We use a list of symbol
        // indices for the membership representation; small enough for our alphabets.
        // Sort active ascending by cost (then by symbol for stability).
        active.Sort((a, b) =>
        {
            int c = a.Cost.CompareTo(b.Cost);
            return c != 0 ? c : a.Symbol.CompareTo(b.Symbol);
        });

        // Initial level: each active symbol is a singleton package.
        List<PackageNode> currentLevel = active.Select(t => new PackageNode(t.Cost, new[] { t.Symbol })).ToList();

        // Package-merge: at each of (maxLength - 1) levels, take pairs from the current level
        // (after merging with originals), creating packages of double-symbol membership.
        for (int level = 0; level < maxLength - 1; level++)
        {
            // Sort by cost.
            currentLevel.Sort((a, b) => a.Cost.CompareTo(b.Cost));

            // Take pairs (drop odd one if any).
            List<PackageNode> packages = new();
            for (int i = 0; i + 1 < currentLevel.Count; i += 2)
            {
                PackageNode left = currentLevel[i];
                PackageNode right = currentLevel[i + 1];
                long packageCost = left.Cost + right.Cost;
                int[] merged = new int[left.Members.Length + right.Members.Length];
                Array.Copy(left.Members, 0, merged, 0, left.Members.Length);
                Array.Copy(right.Members, 0, merged, left.Members.Length, right.Members.Length);
                packages.Add(new PackageNode(packageCost, merged));
            }

            // Merge with originals for the next level.
            List<PackageNode> nextLevel = new(active.Count + packages.Count);
            nextLevel.AddRange(active.Select(t => new PackageNode(t.Cost, new[] { t.Symbol })));
            nextLevel.AddRange(packages);
            currentLevel = nextLevel;
        }

        // Take the bottom (2 * active.Count - 2) packages by cost from the final level.
        currentLevel.Sort((a, b) => a.Cost.CompareTo(b.Cost));
        int keep = 2 * active.Count - 2;
        for (int i = 0; i < keep && i < currentLevel.Count; i++)
        {
            foreach (int sym in currentLevel[i].Members)
            {
                lengths[sym]++;
            }
        }

        return lengths;
    }

    /// <summary>Build canonical Huffman code values from per-symbol bit lengths.</summary>
    /// <remarks>
    /// Per RFC §3.2 canonical construction: symbols sorted by (length asc, symbol asc) get
    /// successive code values; transitioning to a longer length left-shifts the running code.
    /// The output code values are LSB-first-emit ready (bit-reversed from canonical MSB-first
    /// form) to suit the Brotli bit stream.
    /// </remarks>
    internal static int[] BuildCanonicalCodes(int[] lengths)
    {
        ArgumentNullException.ThrowIfNull(lengths);
        int n = lengths.Length;
        int[] codes = new int[n];

        int[] ordered = Enumerable.Range(0, n)
            .Where(i => lengths[i] > 0)
            .OrderBy(i => lengths[i])
            .ThenBy(i => i)
            .ToArray();
        if (ordered.Length == 0) { return codes; }

        int code = 0;
        int prevLen = lengths[ordered[0]];
        foreach (int sym in ordered)
        {
            int len = lengths[sym];
            if (len > prevLen)
            {
                code <<= (len - prevLen);
                prevLen = len;
            }
            codes[sym] = BitReverse(code, len);
            code++;
        }
        return codes;
    }

    private static int BitReverse(int value, int bits)
    {
        int reversed = 0;
        for (int i = 0; i < bits; i++)
        {
            reversed = (reversed << 1) | (value & 1);
            value >>= 1;
        }
        return reversed;
    }

    private sealed class PackageNode
    {
        internal long Cost { get; }
        internal int[] Members { get; }

        internal PackageNode(long cost, int[] members) { Cost = cost; Members = members; }
    }
}
