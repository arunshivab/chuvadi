// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 7932 — Brotli Compressed Data Format
// PHASE: Phase 2.2 — Brotli encoder (header + stored meta-block)

using System;
using System.IO;

namespace Chuvadi.Pdf.Fonts.Woff2;

/// <summary>
/// Pure-C# Brotli encoder.
/// </summary>
/// <remarks>
/// <para>
/// Current scope: emits valid Brotli streams using uncompressed (stored) meta-blocks
/// only. The bit-level meta-block header is implemented from RFC 7932 §9 and
/// verified against <see cref="System.IO.Compression.BrotliStream"/> as a reference
/// decoder. Each call emits one stored meta-block per slice of input (max 16 MiB
/// per meta-block per the MNIBBLES=4 encoding) followed by a trailing empty
/// meta-block with <c>ISLAST=1</c>.
/// </para>
/// <para>
/// Compressed meta-blocks (with Huffman coding and LZ77 back-references) are
/// planned for subsequent Phase 2.2 stages; the LZ77 matcher in
/// <see cref="BrotliCommandStream"/> already produces the command stream they
/// will consume. The current public surface is stable across that transition.
/// </para>
/// </remarks>
public static class BrotliEncoder
{
    // RFC §9.2: with MNIBBLES=0 (4 nibbles), MLEN-1 fits in 16 bits → max 65536
    private const int MaxStoredMetaBlockLen = 1 << 16;   // 65536 bytes, fits in MNIBBLES=4

    /// <summary>Encodes <paramref name="data"/> as a valid Brotli stream.</summary>
    public static byte[] Encode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        using MemoryStream output = new();
        BrotliBitWriter bw = new(output);

        WriteStreamHeader(bw);

        if (data.Length == 0)
        {
            // Just emit the trailing empty meta-block.
            WriteTrailingEmpty(bw);
            bw.Flush();
            return output.ToArray();
        }

        // Try the compressed path. To decide whether to use it, encode BOTH the compressed
        // and the stored variant speculatively and pick whichever is smaller. This avoids
        // size regressions vs stage 1 when compressed overhead exceeds savings.
        byte[]? compressed = TryCompressed(data);

        int offset = 0;
        while (offset < data.Length)
        {
            int blockLen = Math.Min(MaxStoredMetaBlockLen, data.Length - offset);
            WriteStoredMetaBlock(bw, output, data, offset, blockLen);
            offset += blockLen;
        }

        WriteTrailingEmpty(bw);
        bw.Flush();
        byte[] stored = output.ToArray();

        return compressed is not null && compressed.Length < stored.Length ? compressed : stored;
    }

    private static byte[]? TryCompressed(byte[] data)
    {
        using MemoryStream scratch = new();
        BrotliBitWriter bw = new(scratch);
        WriteStreamHeader(bw);
        if (!BrotliCompressedEmitter.TryEmit(bw, data, isLast: true)) { return null; }
        bw.Flush();
        return scratch.ToArray();
    }

    private static void WriteStreamHeader(BrotliBitWriter bw)
    {
        // WBITS = 22. Encoding (RFC §9.1):
        //   first bit 1, then 3 bits N LSB-first.
        //   For WBITS in 18..24: N = WBITS - 17.
        //   N = 22 - 17 = 5 = 0b101.
        bw.WriteBits(1, 1);          // first bit
        bw.WriteBits(5, 3);          // N = 5 → WBITS = 22
    }

    private static void WriteStoredMetaBlock(
        BrotliBitWriter bw, MemoryStream output,
        byte[] data, int offset, int length)
    {
        // Meta-block header (RFC §9.2)
        bw.WriteBits(0, 1);          // ISLAST = 0
        bw.WriteBits(0, 2);          // MNIBBLES = 0 → 4 nibbles
        bw.WriteBits((ulong)(length - 1), 16);   // MLEN - 1 (16 bits)
        bw.WriteBits(1, 1);          // ISUNCOMPRESSED = 1

        // Byte-align before raw data
        bw.Flush();

        // Raw bytes
        output.Write(data, offset, length);
    }

    private static void WriteTrailingEmpty(BrotliBitWriter bw)
    {
        // Trailing empty meta-block: ISLAST=1, ISEMPTY=1, pad to byte boundary
        bw.WriteBits(1, 1);          // ISLAST = 1
        bw.WriteBits(1, 1);          // ISEMPTY = 1
        // Implicit pad with zeros to byte boundary happens at Flush()
    }
}
