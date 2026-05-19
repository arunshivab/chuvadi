// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 7932 §9.2, §9.3 — Meta-Block Header and Data
// PHASE: Phase 2.2 stage 4 — LZ77 multi-command emission

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Fonts.Woff2;

/// <summary>
/// Emits compressed Brotli meta-blocks from an LZ77 command stream.
/// </summary>
/// <remarks>
/// <para>
/// Consumes the output of <see cref="BrotliCommandStream.Encode"/> and emits one
/// compressed meta-block per chunk of at most 16 MiB (the maximum MLEN for
/// MNIBBLES=6 per RFC §9.2). Inputs larger than that are split across multiple
/// meta-blocks; only the final meta-block has <c>ISLAST=1</c>.
/// </para>
/// <para>
/// For each meta-block, the emitter computes literal, insert-and-copy, and distance
/// symbol frequencies across all commands in the chunk, then builds three Huffman
/// trees via <see cref="BrotliPrefixCode"/> (auto-selecting simple or complex form),
/// and emits the commands using those trees. Frequent literals get short codes,
/// which is where the compression gain over stage 3's single-command emitter comes
/// from.
/// </para>
/// </remarks>
internal static class BrotliCompressedEmitter
{
    private const int LiteralAlphabetBits = 8;
    private const int InsertCopyAlphabetBits = 10;
    private const int DistanceAlphabetBits = 6;

    // RFC §9.2: MNIBBLES=6 → MLEN-1 fits in 24 bits → max 2^24 = 16 MiB per meta-block.
    private const int MaxMetaBlockLen = 1 << 24;

    /// <summary>
    /// Emits one or more compressed meta-blocks covering all of <paramref name="data"/>.
    /// </summary>
    /// <param name="bw">The bit writer to emit into.</param>
    /// <param name="data">The full input bytes. Must be non-empty; callers handle empty input separately.</param>
    /// <remarks>
    /// The last meta-block emitted has <c>ISLAST=1</c>; preceding ones (if any) have
    /// <c>ISLAST=0</c>. No trailing empty meta-block is emitted; the final meta-block
    /// IS the terminator.
    /// </remarks>
    internal static void Emit(BrotliBitWriter bw, ReadOnlySpan<byte> data)
    {
        ArgumentNullException.ThrowIfNull(bw);
        if (data.Length == 0) { throw new ArgumentException("Empty input is handled by the caller.", nameof(data)); }

        int offset = 0;
        while (offset < data.Length)
        {
            int chunkLen = Math.Min(MaxMetaBlockLen, data.Length - offset);
            bool isLast = offset + chunkLen >= data.Length;
            ReadOnlySpan<byte> chunk = data.Slice(offset, chunkLen);
            EmitOneMetaBlock(bw, chunk, isLast);
            offset += chunkLen;
        }
    }

    private static void EmitOneMetaBlock(BrotliBitWriter bw, ReadOnlySpan<byte> data, bool isLast)
    {
        // ── LZ77 pass: produce the command stream for this chunk ────────────────────────
        List<BrotliCommand> commands = BrotliCommandStream.Encode(data);

        // ── Resolve each command's per-symbol fields and accumulate frequencies ────────
        // Each command produces:
        //   - One IC symbol (= insertLenCode × copyLenCode × useDistance)
        //   - insertLen literal bytes (one Huffman code each)
        //   - One distance code (if useDistance)
        //
        // The trailing-literals-only command (CopyLength == 0) is the terminator. Per
        // RFC §9.3, the decoder produces exactly MLEN bytes; the final copy is
        // truncated by MLEN. So for the terminator we pick an IC symbol with
        // implicit distance (copy_len arbitrary; copy never runs because MLEN reached)
        // to avoid emitting a distance code we don't need.

        ResolvedCommand[] resolved = new ResolvedCommand[commands.Count];
        int[] literalFreq = new int[256];
        int[] icFreq = new int[704];
        int[] distFreq = new int[64];

        for (int i = 0; i < commands.Count; i++)
        {
            BrotliCommand cmd = commands[i];
            int insertLen = cmd.Literals.Length;
            int copyLen = cmd.CopyLength;
            bool useDistance = cmd.CopyDistance > 0;

            // Terminator commands (the very last command with no copy) use implicit
            // distance — copy_len is arbitrary because MLEN is reached by inserts.
            // We pick copy_len = 2 (the minimum) and useDistance = false to land in
            // the IC implicit-distance row (cells 0..63).
            if (!useDistance && copyLen == 0)
            {
                copyLen = 2;     // arbitrary placeholder; copy never executes
            }

            int insertCode = BrotliCodeTables.InsertLenCode(insertLen);
            int copyCode = BrotliCodeTables.CopyLenCode(copyLen);
            int icSymbol = BrotliCodeTables.InsertAndCopySymbol(insertCode, copyCode, useDistance);

            int insertExtraValue = insertLen - BrotliCodeTables.InsertLenCodes[insertCode].Base;
            int copyExtraValue = copyLen - BrotliCodeTables.CopyLenCodes[copyCode].Base;

            int distCode = 0, distExtraBits = 0, distExtraValue = 0;
            if (useDistance)
            {
                (distCode, distExtraBits, distExtraValue) = BrotliCodeTables.DistanceToCode(cmd.CopyDistance);
                distFreq[distCode]++;
            }

            resolved[i] = new ResolvedCommand(
                cmd, insertCode, copyCode, icSymbol,
                insertExtraValue, copyExtraValue,
                useDistance, distCode, distExtraBits, distExtraValue);

            icFreq[icSymbol]++;
            foreach (byte b in cmd.Literals) { literalFreq[b]++; }
        }

        // Guarantee at least one non-zero entry in distFreq so that BrotliPrefixCode
        // can always build a valid tree. (literalFreq and icFreq are guaranteed non-empty
        // because commands is non-empty when data is non-empty.)
        if (NonZeroCount(distFreq) == 0) { distFreq[0] = 1; }

        BrotliPrefixCode literalCode = new(literalFreq, 15);
        BrotliPrefixCode icCode = new(icFreq, 15);
        BrotliPrefixCode distanceCode = new(distFreq, 15);

        // ── Meta-block header (RFC §9.2) ────────────────────────────────────────────────
        bw.WriteBits(isLast ? 1UL : 0UL, 1);              // ISLAST
        if (isLast) { bw.WriteBits(0, 1); }               // ISLASTEMPTY = 0
        // MNIBBLES: pick smallest that covers length. 4 nibbles (=16 bits) covers up to 65536.
        // 5 nibbles covers up to 2^20, 6 nibbles up to 2^24.
        int mnibbles = data.Length <= (1 << 16) ? 4 : (data.Length <= (1 << 20) ? 5 : 6);
        ulong mnibblesField = mnibbles switch
        {
            4 => 0UL,
            5 => 1UL,
            6 => 2UL,
            _ => 0UL,
        };
        bw.WriteBits(mnibblesField, 2);                   // MNIBBLES
        bw.WriteBits((ulong)(data.Length - 1), mnibbles * 4);   // MLEN - 1
        if (!isLast) { bw.WriteBits(0, 1); }              // ISUNCOMPRESSED = 0 (compressed mode)

        // ── Block type / count fields ───────────────────────────────────────────────────
        bw.WriteBits(0, 1);     // NBLTYPESL = 1 (single bit "0" for variable-length value 1)
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

        // ── Commands (RFC §9.3) ────────────────────────────────────────────────────────
        foreach (ResolvedCommand r in resolved)
        {
            // a. IC symbol via IC Huffman tree.
            (int icCodeValue, int icBitLen) = icCode.GetCode(r.IcSymbol);
            bw.WriteBits((ulong)icCodeValue, icBitLen);

            // b. Insert extra bits.
            int insertExtraBits = BrotliCodeTables.InsertLenCodes[r.InsertCode].Extra;
            if (insertExtraBits > 0) { bw.WriteBits((ulong)r.InsertExtraValue, insertExtraBits); }

            // c. Copy extra bits.
            int copyExtraBits = BrotliCodeTables.CopyLenCodes[r.CopyCode].Extra;
            if (copyExtraBits > 0) { bw.WriteBits((ulong)r.CopyExtraValue, copyExtraBits); }

            // d. Literal bytes via literal Huffman tree.
            foreach (byte b in r.Command.Literals)
            {
                (int lv, int ll) = literalCode.GetCode(b);
                bw.WriteBits((ulong)lv, ll);
            }

            // e. Distance code via distance Huffman tree (only if back-reference used).
            if (r.UseDistance)
            {
                (int dv, int dl) = distanceCode.GetCode(r.DistCode);
                bw.WriteBits((ulong)dv, dl);
                if (r.DistExtraBits > 0) { bw.WriteBits((ulong)r.DistExtraValue, r.DistExtraBits); }
            }
        }
    }

    private static int NonZeroCount(int[] freq)
    {
        int n = 0;
        foreach (int f in freq) { if (f > 0) { n++; } }
        return n;
    }

    /// <summary>Per-command precomputed fields needed during the emission pass.</summary>
    private readonly struct ResolvedCommand
    {
        internal ResolvedCommand(
            BrotliCommand cmd,
            int insertCode, int copyCode, int icSymbol,
            int insertExtraValue, int copyExtraValue,
            bool useDistance, int distCode, int distExtraBits, int distExtraValue)
        {
            Command = cmd;
            InsertCode = insertCode;
            CopyCode = copyCode;
            IcSymbol = icSymbol;
            InsertExtraValue = insertExtraValue;
            CopyExtraValue = copyExtraValue;
            UseDistance = useDistance;
            DistCode = distCode;
            DistExtraBits = distExtraBits;
            DistExtraValue = distExtraValue;
        }

        internal BrotliCommand Command { get; }
        internal int InsertCode { get; }
        internal int CopyCode { get; }
        internal int IcSymbol { get; }
        internal int InsertExtraValue { get; }
        internal int CopyExtraValue { get; }
        internal bool UseDistance { get; }
        internal int DistCode { get; }
        internal int DistExtraBits { get; }
        internal int DistExtraValue { get; }
    }
}
