// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 7932 — Brotli Compressed Data Format §4, §5
// PHASE: Phase 2.2 — Brotli LZ77 matcher + command stream

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Fonts.Woff2;

/// <summary>
/// LZ77 sliding-window matcher that produces a stream of Brotli
/// <see cref="BrotliCommand"/> records from raw input bytes.
/// </summary>
/// <remarks>
/// <para>
/// Brotli's compressed data is a sequence of <c>insert-and-copy</c> commands,
/// each of which inserts some number of literal bytes verbatim and then
/// optionally copies a back-reference from earlier in the output. This
/// class implements the matching half (finding good back-references) and
/// emits the corresponding command records; <c>BrotliEncoder</c>
/// consumes the stream and performs Huffman coding plus bit-level emission.
/// </para>
/// <para>
/// The matcher uses a chained hash table over 4-byte prefixes within a
/// 64 KB sliding window (the Brotli default <c>WBITS = 16</c> window).
/// At each position it considers a few candidate matches from the chain
/// and picks the longest, with a simple "lazy match" tie-breaker that
/// prefers a match one position later if it's longer than the current one.
/// </para>
/// </remarks>
internal static class BrotliCommandStream
{
    internal const int WindowBits = 16;
    internal const int WindowSize = 1 << WindowBits;
    internal const int HashBits = 17;
    internal const int HashSize = 1 << HashBits;
    internal const int MinMatch = 4;
    internal const int MaxMatch = 256 + 15; // Brotli's max non-dictionary copy length
    internal const int MaxChainDepth = 32;

    /// <summary>Produces a sequence of LZ77 commands from <paramref name="input"/>.</summary>
    internal static List<BrotliCommand> Encode(ReadOnlySpan<byte> input)
    {
        List<BrotliCommand> commands = new();
        if (input.Length == 0) { return commands; }

        int[] head = new int[HashSize];
        Array.Fill(head, -1);
        int[] prev = new int[input.Length];

        int pos = 0;
        int pendingLiteralStart = 0;
        while (pos < input.Length)
        {
            int bestLen = 0, bestDist = 0;
            if (pos + MinMatch <= input.Length)
            {
                (bestLen, bestDist) = FindMatch(input, pos, head, prev);

                // Lazy match: try one position later.
                if (bestLen >= MinMatch && pos + 1 + MinMatch <= input.Length)
                {
                    InsertHash(input, pos, head, prev);
                    pos++;
                    (int lazyLen, int lazyDist) = FindMatch(input, pos, head, prev);
                    if (lazyLen > bestLen + 1)
                    {
                        bestLen = lazyLen;
                        bestDist = lazyDist;
                    }
                    else
                    {
                        pos--;
                    }
                }
            }

            if (bestLen >= MinMatch)
            {
                int insertLen = pos - pendingLiteralStart;
                byte[] literals = new byte[insertLen];
                input.Slice(pendingLiteralStart, insertLen).CopyTo(literals);
                commands.Add(new BrotliCommand(literals, bestLen, bestDist));

                // Insert all positions covered by the match into the hash chain so
                // future matches can reference into the middle of long copies.
                int end = pos + bestLen;
                for (int p = pos; p < end && p + MinMatch <= input.Length; p++)
                {
                    InsertHash(input, p, head, prev);
                }
                pos = end;
                pendingLiteralStart = pos;
            }
            else
            {
                if (pos + MinMatch <= input.Length)
                {
                    InsertHash(input, pos, head, prev);
                }
                pos++;
            }
        }

        // Trailing literals with no copy.
        if (pendingLiteralStart < input.Length)
        {
            int insertLen = input.Length - pendingLiteralStart;
            byte[] literals = new byte[insertLen];
            input.Slice(pendingLiteralStart, insertLen).CopyTo(literals);
            commands.Add(new BrotliCommand(literals, copyLength: 0, copyDistance: 0));
        }

        return commands;
    }

    private static (int Length, int Distance) FindMatch(
        ReadOnlySpan<byte> input, int pos, int[] head, int[] prev)
    {
        int hash = Hash4(input, pos);
        int candidate = head[hash];
        int bestLen = 0, bestDist = 0;
        int depth = 0;
        int minCandidate = Math.Max(0, pos - WindowSize);

        while (candidate >= minCandidate && depth < MaxChainDepth)
        {
            // Quick reject: check the 4th byte first (most discriminating).
            if (pos + bestLen < input.Length
                && candidate + bestLen < input.Length
                && input[candidate + bestLen] == input[pos + bestLen])
            {
                int len = 0;
                int maxLen = Math.Min(MaxMatch, input.Length - pos);
                while (len < maxLen && input[candidate + len] == input[pos + len])
                {
                    len++;
                }
                if (len > bestLen)
                {
                    bestLen = len;
                    bestDist = pos - candidate;
                    if (len >= MaxMatch) { break; }
                }
            }
            candidate = prev[candidate];
            depth++;
        }

        return (bestLen, bestDist);
    }

    private static void InsertHash(ReadOnlySpan<byte> input, int pos, int[] head, int[] prev)
    {
        int hash = Hash4(input, pos);
        prev[pos] = head[hash];
        head[hash] = pos;
    }

    private static int Hash4(ReadOnlySpan<byte> input, int pos)
    {
        // FNV-1a-like 32→17 bit hash on 4 bytes.
        uint h = 0x811C9DC5u;
        h = (h ^ input[pos]) * 0x01000193u;
        h = (h ^ input[pos + 1]) * 0x01000193u;
        h = (h ^ input[pos + 2]) * 0x01000193u;
        h = (h ^ input[pos + 3]) * 0x01000193u;
        return (int)(h & (HashSize - 1));
    }
}

/// <summary>A single Brotli insert-and-copy command.</summary>
internal sealed class BrotliCommand
{
    internal BrotliCommand(byte[] literals, int copyLength, int copyDistance)
    {
        Literals = literals;
        CopyLength = copyLength;
        CopyDistance = copyDistance;
    }

    /// <summary>Literal bytes inserted verbatim before any copy.</summary>
    internal byte[] Literals { get; }

    /// <summary>Length of the back-reference copy (0 if this is the trailing terminator).</summary>
    internal int CopyLength { get; }

    /// <summary>Distance of the back-reference (positive offset back into the output stream).</summary>
    internal int CopyDistance { get; }
}
