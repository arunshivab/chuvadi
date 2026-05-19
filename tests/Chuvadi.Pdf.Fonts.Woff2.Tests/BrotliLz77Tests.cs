// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 7932 §4, §5 — LZ77 back-references and copy commands
// PHASE: Phase 2.2 stage 4 — LZ77 multi-command emission

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Woff2.Tests;

/// <summary>
/// Tests that the multi-command Brotli emitter produces output significantly smaller
/// than the equivalent stored meta-block when LZ77 back-references should fire.
/// </summary>
/// <remarks>
/// Stage 3's single-command emitter produced output proportional to input size, even
/// for highly repetitive data, because no back-references were used. Stage 4 wires
/// the LZ77 matcher in <see cref="BrotliCommandStream"/> into the emitter so
/// repeated content compresses dramatically. These tests verify that gain is
/// realized.
/// </remarks>
public sealed class BrotliLz77Tests
{
    private static byte[] Decode(byte[] encoded)
    {
        using MemoryStream src = new(encoded);
        using BrotliStream bs = new(src, CompressionMode.Decompress);
        using MemoryStream dst = new();
        bs.CopyTo(dst);
        return dst.ToArray();
    }

    [Fact]
    public void Encode_RepeatingPhrase_CompressesAtLeast10x()
    {
        byte[] phrase = System.Text.Encoding.ASCII.GetBytes("The quick brown fox jumps over the lazy dog. ");
        byte[] input = Enumerable.Repeat(phrase, 50).SelectMany(x => x).ToArray();
        // 50 × 45 bytes = 2250 bytes of input.

        byte[] encoded = BrotliEncoder.Encode(input);
        encoded.Length.Should().BeLessThan(input.Length / 10,
            "back-references should compress this repeating text by at least 10x");

        byte[] decoded = Decode(encoded);
        decoded.Should().Equal(input);
    }

    [Fact]
    public void Encode_RepeatedSinglePhrase_CompressesNearOptimal()
    {
        // 5KB of repeated identical phrase. LZ77 should produce one initial literal
        // run + many back-references, resulting in a tiny encoded output.
        byte[] phrase = System.Text.Encoding.ASCII.GetBytes("Lorem ipsum dolor sit amet, consectetur adipiscing elit. ");
        byte[] input = Enumerable.Repeat(phrase, 90).SelectMany(x => x).ToArray();

        byte[] encoded = BrotliEncoder.Encode(input);
        encoded.Length.Should().BeLessThan(200,
            "5KB of repeated text should compress to well under 200 bytes via back-references");

        Decode(encoded).Should().Equal(input);
    }

    [Fact]
    public void Encode_IncompressibleRandomData_FallsBackToStored()
    {
        // High-entropy random data: LZ77 finds no matches, compression overhead exceeds
        // savings, so the encoder should pick the stored variant.
        byte[] input = new byte[8192];
        new Random(42).NextBytes(input);

        byte[] encoded = BrotliEncoder.Encode(input);
        // Stored encoding adds modest framing overhead (~10 bytes for header + trailer).
        encoded.Length.Should().BeLessThan(input.Length + 50);

        Decode(encoded).Should().Equal(input);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(10_000)]
    [InlineData(100_000)]
    public void Encode_RepeatedAlphabet_RoundTrips(int length)
    {
        byte[] input = new byte[length];
        for (int i = 0; i < length; i++) { input[i] = (byte)('A' + (i % 26)); }
        byte[] encoded = BrotliEncoder.Encode(input);
        Decode(encoded).Should().Equal(input);
    }

    [Fact]
    public void Encode_OneMegabyte_RoundTrips()
    {
        // Verifies the multi-meta-block path: 1 MB still fits in one MNIBBLES=5 meta-block,
        // but exercises the larger MLEN encoding.
        byte[] input = new byte[1_000_000];
        for (int i = 0; i < input.Length; i++)
        {
            input[i] = (byte)((i * 7) & 0xFF);   // pseudo-random-ish but reproducible
        }
        byte[] encoded = BrotliEncoder.Encode(input);
        Decode(encoded).Should().Equal(input);
    }

    [Fact]
    public void Encode_BeatsStoredOnText_ByLargeMargin()
    {
        // Sanity check: real English text should compress dramatically vs. its raw size.
        string sample =
            "The quick brown fox jumps over the lazy dog. " +
            "Pack my box with five dozen liquor jugs. " +
            "How vexingly quick daft zebras jump! " +
            "Sphinx of black quartz, judge my vow. " +
            "Two driven jocks help fax my big quiz. ";
        byte[] phrase = System.Text.Encoding.ASCII.GetBytes(sample);
        byte[] input = Enumerable.Repeat(phrase, 20).SelectMany(x => x).ToArray();
        // ~4.5 KB.

        byte[] encoded = BrotliEncoder.Encode(input);

        // Compression ratio threshold: well under 7% expected. Pre-stage-4 this case was
        // 100% (fell back to stored framing). Sandbox measurements show ~5%, real Windows
        // .NET 10 measurements show ~5.2%; we leave headroom for further small variations
        // across CPUs and JIT versions. Lowering this threshold further requires the
        // RFC §7 context modeling and §8 static dictionary planned for v1.11.0.
        double ratio = (double)encoded.Length / input.Length;
        ratio.Should().BeLessThan(0.07,
            $"5x-repeated English prose should compress to under 7%; got {ratio:P1}");

        Decode(encoded).Should().Equal(input);
    }
}
