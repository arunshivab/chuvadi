// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 7932 §9.2, §9.3 — Meta-Block Header and Data
// PHASE: Phase 2.2 stage 2

using System;
using System.Collections.Generic;
using System.Linq;

namespace Chuvadi.Pdf.Fonts.Woff2;

/// <summary>
/// Emits a single compressed Brotli meta-block consisting of one insert-and-copy
/// command that inserts the entire input as literals with no back-reference.
/// </summary>
/// <remarks>
/// <para>
/// This is the "minimum-viable compressed" path for Phase 2.2 stage 2. It produces
/// a valid compressed meta-block when the input has 1..4 distinct byte values, so
/// all three Huffman alphabets fit within RFC §3.4 simple prefix codes. For inputs
/// with 5+ distinct byte values, <see cref="TryEmit"/> returns false and the caller
/// falls back to a stored meta-block.
/// </para>
/// <para>
/// Per RFC §9.3, the copy length of the final command is ignored when MLEN bytes
/// have already been produced by inserts. We exploit this: pick an IC symbol with
/// implicit distance (range 0..127), set <c>insert_len = MLEN</c> and any copy_len,
/// and the copy never executes. No distance code is emitted.
/// </para>
/// </remarks>
internal static class BrotliCompressedEmitter
{
    private const int LiteralAlphabetBits = 8;
    private const int InsertCopyAlphabetBits = 10;
    private const int DistanceAlphabetBits = 6;

    /// <summary>
    /// Attempts to emit a single compressed meta-block for <paramref name="data"/>.
    /// </summary>
    /// <returns>
    /// True if the data could be encoded with simple prefix codes; false if it has
    /// too many distinct literal byte values for stage 2's encoder.
    /// </returns>
    internal static bool TryEmit(BrotliBitWriter bw, ReadOnlySpan<byte> data, bool isLast)
    {
        ArgumentNullException.ThrowIfNull(bw);
        if (data.Length == 0) { return false; }
        if (data.Length > (1 << 24)) { return false; }   // we use MNIBBLES=6 max

        // Collect distinct literals. Simple prefix code supports ≤4 symbols.
        HashSet<int> literals = new();
        foreach (byte b in data) { literals.Add(b); }
        if (literals.Count > 4) { return false; }

        // Choose the insert length code that covers data.Length, then the IC symbol with
        // implicit distance (cell 0..63 row).
        int insertCode = BrotliCodeTables.InsertLenCode(data.Length);
        int insertExtraBits = BrotliCodeTables.InsertLenCodes[insertCode].Extra;
        int insertExtraValue = data.Length - BrotliCodeTables.InsertLenCodes[insertCode].Base;

        // Always use explicit-distance row (IC cell 128+) so the same code path handles
        // any insert length 0..1089 (codes 0..19). Larger inputs get cap-rejected below.
        if (insertCode > 19) { return false; }      // beyond this, we'd need NPOSTFIX/NDIRECT tuning
        int copyCode = 0;
        int copyExtraBits = 0;
        int copyExtraValue = 0;
        int icSymbol = BrotliCodeTables.InsertAndCopySymbol(insertCode, copyCode, useDistance: true);

        // Placeholder distance: dcode=16 (smallest explicit), extra value 0 → distance = 1.
        // The copy is ignored because MLEN is reached by the inserts, so this distance is
        // emitted but never used.
        (int distCode, int distExtraBits, int distExtraValue) = BrotliCodeTables.DistanceToCode(1);

        // Build the three simple prefix codes.
        BrotliSimplePrefixCode literalCode = new(literals.Select(x => (int)x));
        BrotliSimplePrefixCode icCode = new(new[] { icSymbol });
        BrotliSimplePrefixCode distanceCode = new(new[] { distCode });

        // ── Meta-block header (RFC §9.2) ────────────────────────────────────────────────
        bw.WriteBits(isLast ? 1UL : 0UL, 1);              // ISLAST
        if (isLast) { bw.WriteBits(0, 1); }               // ISLASTEMPTY = 0
        // MNIBBLES: pick smallest that covers length. 4 nibbles (=16 bits) covers up to 65536.
        // 5 nibbles covers up to 2^20, 6 nibbles up to 2^24.
        int mnibbles = data.Length <= (1 << 16) ? 4 : (data.Length <= (1 << 20) ? 5 : 6);
        ulong mnibblesField = mnibbles switch { 4 => 0UL, 5 => 1UL, 6 => 2UL, _ => 0UL };
        bw.WriteBits(mnibblesField, 2);                   // MNIBBLES
        bw.WriteBits((ulong)(data.Length - 1), mnibbles * 4);   // MLEN - 1
        if (!isLast) { bw.WriteBits(0, 1); }              // ISUNCOMPRESSED = 0 (compressed mode)

        // ── Block type / count fields ───────────────────────────────────────────────────
        // NBLTYPESL = 1 → variable-length code value 0 = single bit "0".
        bw.WriteBits(0, 1);     // NBLTYPESL = 1
        bw.WriteBits(0, 1);     // NBLTYPESI = 1
        bw.WriteBits(0, 1);     // NBLTYPESD = 1

        bw.WriteBits(0, 2);     // NPOSTFIX = 0
        bw.WriteBits(0, 4);     // NDIRECT high-4-bits = 0

        // Context modes (2 bits per literal block type, NBLTYPESL=1 → 2 bits total). LSB6 = 0.
        bw.WriteBits(0, 2);

        bw.WriteBits(0, 1);     // NTREESL = 1 (no literal context map)
        bw.WriteBits(0, 1);     // NTREESD = 1 (no distance context map)

        // ── Prefix codes (order per §9.2 final paragraph: literal, IC, distance) ─────────
        literalCode.EmitDeclaration(bw, LiteralAlphabetBits);
        icCode.EmitDeclaration(bw, InsertCopyAlphabetBits);
        distanceCode.EmitDeclaration(bw, DistanceAlphabetBits);

        // ── The single command (RFC §9.3) ───────────────────────────────────────────────
        // a. IC symbol via IC Huffman tree.
        var (icCodeValue, icBitLen) = icCode.GetCode(icSymbol);
        bw.WriteBits((ulong)icCodeValue, icBitLen);
        // b. Insert extra bits.
        if (insertExtraBits > 0) { bw.WriteBits((ulong)insertExtraValue, insertExtraBits); }
        // c. Copy extra bits.
        if (copyExtraBits > 0) { bw.WriteBits((ulong)copyExtraValue, copyExtraBits); }
        // d. Literal bytes via literal tree.
        foreach (byte b in data)
        {
            var (lv, ll) = literalCode.GetCode(b);
            bw.WriteBits((ulong)lv, ll);
        }
        // e. Distance code via distance Huffman tree, then distance extra bits.
        var (dv, dl) = distanceCode.GetCode(distCode);
        bw.WriteBits((ulong)dv, dl);
        if (distExtraBits > 0) { bw.WriteBits((ulong)distExtraValue, distExtraBits); }
        // f. No copy: the copy operation produces zero output because MLEN is reached by inserts.

        return true;
    }
}
