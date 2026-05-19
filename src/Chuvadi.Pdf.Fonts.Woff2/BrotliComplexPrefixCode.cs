// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 7932 §3.5 — Complex Prefix Codes
// PHASE: Phase 2.2 stage 3

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Fonts.Woff2;

/// <summary>
/// Emitter for RFC 7932 §3.5 complex prefix codes (used for alphabets >4 symbols).
/// </summary>
/// <remarks>
/// <para>
/// The emission has three layers:
/// </para>
/// <list type="number">
///   <item>
///     <description>Layer 1 (fixed): a hardcoded variable-length code for the 6 possible
///       lengths (0..5) of Layer 2's prefix code. See <see cref="Layer1CodeBits"/>.</description>
///   </item>
///   <item>
///     <description>Layer 2: a freshly-built Huffman code over the 18-symbol "code-length
///       alphabet" (symbols 0..15 represent literal code lengths; symbol 16 = repeat-previous;
///       symbol 17 = repeat-zero). Its code lengths are emitted using Layer 1.</description>
///   </item>
///   <item>
///     <description>Layer 3: the actual code lengths of the target alphabet, run-length-encoded
///       using symbols 16/17 to compress repeats. Each Layer 3 symbol is emitted using Layer 2.</description>
///   </item>
/// </list>
/// </remarks>
internal static class BrotliComplexPrefixCode
{
    // Layer 1 fixed code (RFC §3.5): each code-length symbol 0..5 has a fixed 2..4 bit code.
    // Format: (value, bit_length). Bits parsed from right to left in the spec → emit LSB-first
    // as the integer value directly.
    private static readonly (int Value, int Bits)[] Layer1CodeBits =
    {
        (0, 2),     // sym 0: "00" → value 0, 2 bits
        (7, 4),     // sym 1: "0111" → value 7, 4 bits
        (3, 3),     // sym 2: "011" → value 3, 3 bits
        (2, 2),     // sym 3: "10" → value 2, 2 bits
        (1, 2),     // sym 4: "01" → value 1, 2 bits
        (15, 4),    // sym 5: "1111" → value 15, 4 bits
    };

    // RFC §3.5: code-length symbols are emitted in this specific order so that more
    // common lengths come first.
    private static readonly int[] EmitOrder =
    {
        1, 2, 3, 4, 0, 5, 17, 6, 16, 7, 8, 9, 10, 11, 12, 13, 14, 15,
    };

    /// <summary>
    /// Emits a complex prefix code that assigns the given <paramref name="targetLengths"/>
    /// to the symbols of the target alphabet.
    /// </summary>
    /// <param name="bw">Output bit stream.</param>
    /// <param name="targetLengths">Code length per target alphabet symbol (0 = symbol unused).</param>
    internal static void EmitDeclaration(BrotliBitWriter bw, int[] targetLengths)
    {
        ArgumentNullException.ThrowIfNull(bw);
        ArgumentNullException.ThrowIfNull(targetLengths);

        // Run-length-encode the target length sequence into Layer 3 symbols (0..17).
        List<(int Symbol, int Extra, int ExtraBits)> layer3 = RunLengthEncode(targetLengths);

        // Compute frequencies of Layer 3 symbols (only 0..17).
        int[] codeLenFreq = new int[18];
        foreach (var (sym, _, _) in layer3) { codeLenFreq[sym]++; }

        // Build the Layer 2 Huffman code (max length 5 per RFC §3.5).
        int[] layer2Lengths = BrotliHuffman.ComputeCodeLengths(codeLenFreq, 5);
        int[] layer2Codes = BrotliHuffman.BuildCanonicalCodes(layer2Lengths);

        // ── Emit Layer 1: code lengths of the Layer 2 code ───────────────────────────────
        // HSKIP = 0 (no leading symbols skipped). HSKIP value 1 means "simple code"; we use
        // 0 here to declare a complex code.
        bw.WriteBits(0, 2);

        // For each symbol in the canonical emit order, emit its Layer 2 length using Layer 1.
        // Per spec: trailing zero lengths may be omitted once at least 2 non-zero lengths
        // have been emitted, i.e., the last emitted length must be non-zero.
        int lastNonZeroIdx = -1;
        int nonZeroCount = 0;
        for (int i = 0; i < EmitOrder.Length; i++)
        {
            if (layer2Lengths[EmitOrder[i]] != 0)
            {
                lastNonZeroIdx = i;
                nonZeroCount++;
            }
        }
        // If only one non-zero (RFC §3.5 single-symbol case): emit it then stop. We don't
        // handle that case here because our Layer 3 sequences always include both real-length
        // symbols and the various lengths, ensuring ≥2 non-zero entries in Layer 2.
        int stopAt = nonZeroCount >= 2 ? lastNonZeroIdx : EmitOrder.Length - 1;
        for (int i = 0; i <= stopAt; i++)
        {
            int len = layer2Lengths[EmitOrder[i]];
            var (val, bits) = Layer1CodeBits[len];
            bw.WriteBits((ulong)val, bits);
        }

        // ── Emit Layer 3: the run-length-encoded sequence, using Layer 2 codes ────────────
        foreach (var (sym, extra, extraBits) in layer3)
        {
            bw.WriteBits((ulong)layer2Codes[sym], layer2Lengths[sym]);
            if (extraBits > 0) { bw.WriteBits((ulong)extra, extraBits); }
        }
    }

    /// <summary>
    /// Convert a sequence of target code lengths into RLE-encoded Layer 3 symbols.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per RFC §3.5: symbol 16 repeats the previous non-zero length 3..6 times (with 2 extra
    /// bits indicating the repeat count - 3). Symbol 17 repeats a zero length 3..10 times
    /// (with 3 extra bits indicating count - 3). Each subsequent 16-after-16 or 17-after-17
    /// MODIFIES the previous count via the modification rule, but we keep it simple here:
    /// emit chunks of at most one 16 or 17 in a row, splitting longer runs into multiple
    /// symbols if needed. Suboptimal but always correct.
    /// </para>
    /// </remarks>
    private static List<(int Symbol, int Extra, int ExtraBits)> RunLengthEncode(int[] lengths)
    {
        List<(int Symbol, int Extra, int ExtraBits)> result = new();
        // Trim trailing zeros: per §3.5 "any trailing 0 or 17 must be omitted".
        int end = lengths.Length;
        while (end > 0 && lengths[end - 1] == 0) { end--; }

        // Per RFC §3.5: consecutive 17 (or 16) codes trigger the "modify" rule, where the
        // second one MODIFIES the previous count via the formula instead of appending more.
        // To avoid accidentally invoking the modify rule, we never emit two 17s in a row, and
        // never two 16s in a row. We break runs with a non-modifying symbol when needed.
        //
        // For long zero runs (>10), we emit (17, extra=7) for chunks of 10, separated by a
        // single literal (0, 0, 0) entry (which encodes "this symbol has length 0"). Similarly
        // for long same-value runs (>6), we emit (16, extra=3) chunks separated by the literal
        // length symbol.

        int i = 0;
        while (i < end)
        {
            int v = lengths[i];
            if (v == 0)
            {
                int runEnd = i;
                while (runEnd < end && lengths[runEnd] == 0) { runEnd++; }
                int runLen = runEnd - i;
                // Emit zeros in chunks. Each (17, extra) chunk encodes 3..10 zeros. Between
                // chunks, we insert ONE literal-0 to avoid the 17-after-17 modify rule.
                bool justEmittedSeventeen = false;
                while (runLen > 0)
                {
                    if (runLen >= 3 && !justEmittedSeventeen)
                    {
                        int chunk = Math.Min(runLen, 10);
                        result.Add((17, chunk - 3, 3));
                        runLen -= chunk;
                        justEmittedSeventeen = true;
                    }
                    else
                    {
                        result.Add((0, 0, 0));
                        runLen--;
                        justEmittedSeventeen = false;
                    }
                }
                i = runEnd;
            }
            else
            {
                // Emit this value at least once as a literal length symbol.
                result.Add((v, 0, 0));
                i++;
                // Look for a run of the same value following.
                int sameRun = 0;
                while (i < end && lengths[i] == v) { sameRun++; i++; }
                // Emit same-value runs in chunks of (16, extra) for 3..6 repeats. Between
                // chunks, insert a literal value to avoid the 16-after-16 modify rule.
                bool justEmittedSixteen = false;
                while (sameRun > 0)
                {
                    if (sameRun >= 3 && !justEmittedSixteen)
                    {
                        int chunk = Math.Min(sameRun, 6);
                        result.Add((16, chunk - 3, 2));
                        sameRun -= chunk;
                        justEmittedSixteen = true;
                    }
                    else
                    {
                        // Run too short for symbol 16, or we just emitted a 16: emit raw length.
                        result.Add((v, 0, 0));
                        sameRun--;
                        justEmittedSixteen = false;
                    }
                }
            }
        }
        return result;
    }
}
