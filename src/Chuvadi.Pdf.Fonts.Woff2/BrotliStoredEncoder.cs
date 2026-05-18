// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 7932 - Brotli Compressed Data Format
// PHASE: Phase 2.1 - Brotli stored-block encoder

using System;
using System.IO;
using System.IO.Compression;

namespace Chuvadi.Pdf.Fonts.Woff2;

/// <summary>
/// Emits Brotli-compatible bitstreams for WOFF2 packaging.
/// </summary>
/// <remarks>
/// <para>
/// Uses <see cref="System.IO.Compression.BrotliStream"/> under the hood with
/// <see cref="CompressionLevel.NoCompression"/> to emit valid Brotli streams
/// of stored blocks. This is pure-BCL (no external dependencies) and produces
/// byte streams accepted by every conforming Brotli decoder.
/// </para>
/// <para>
/// Phase 2.2 will replace this with a hand-rolled compressor that does
/// actual LZ77 matching and Huffman coding for better compression ratios.
/// The API is stable so swapping the implementation is non-breaking.
/// </para>
/// </remarks>
public static class BrotliStoredEncoder
{
    /// <summary>Encodes <paramref name="data"/> as a valid Brotli stream.</summary>
    public static byte[] Encode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        using MemoryStream output = new();
        using (BrotliStream bs = new(output, CompressionLevel.NoCompression, leaveOpen: true))
        {
            if (data.Length > 0) { bs.Write(data, 0, data.Length); }
        }
        return output.ToArray();
    }
}
