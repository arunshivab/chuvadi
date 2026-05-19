// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  RFC 7932 §3.5 — Complex Prefix Codes (modify rule for consecutive 16/17 codes)
// PHASE: Phase 2.2 stage 3 fix

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Fonts.Woff2.Tests;

/// <summary>
/// Regression tests for the RFC 7932 §3.5 "modify rule" handling in the complex prefix code
/// run-length encoder.
/// </summary>
/// <remarks>
/// <para>
/// Per RFC 7932 §3.5, when a code length symbol of 17 (repeat-zero) immediately follows another
/// 17, the decoder applies a "modify" rule: instead of appending more zeros to the length
/// stream, it RE-COMPUTES the previous repeat count via
/// <c>new_count = 8 * (prev_count - 2) + (extras+3)</c>. The same rule applies to consecutive
/// 16 codes. The exponential blowup quickly exceeds the alphabet size, which makes the stream
/// invalid.
/// </para>
/// <para>
/// These tests ensure the encoder produces streams that round-trip through the .NET reference
/// <see cref="BrotliStream"/> decoder for inputs that force the complex prefix code path
/// (5+ distinct literals).
/// </para>
/// </remarks>
public sealed class BrotliRleModifyRuleTests
{
    private static byte[] Decode(byte[] encoded)
    {
        using MemoryStream src = new(encoded);
        using BrotliStream bs = new(src, CompressionMode.Decompress);
        using MemoryStream dst = new();
        bs.CopyTo(dst);
        return dst.ToArray();
    }

    private static void AssertRoundTrips(byte[] input)
    {
        byte[] encoded = BrotliEncoder.Encode(input);
        byte[] decoded = Decode(encoded);
        decoded.Should().Equal(input);
    }

    [Fact]
    public void Encode_5DistinctLiterals_20Bytes_RoundTrips()
    {
        // This is the exact regression case: 5 distinct literals (A..E) at byte positions 65..69
        // of the 256-symbol literal alphabet, leaving 65 leading zeros that need multiple
        // symbol-17 RLE codes. Pre-fix, the consecutive 17s triggered the modify rule and the
        // implied count blew up exponentially.
        byte[] input = new byte[20];
        for (int i = 0; i < input.Length; i++) { input[i] = (byte)('A' + (i % 5)); }
        AssertRoundTrips(input);
    }

    [Fact]
    public void Encode_6DistinctLiterals_180Bytes_RoundTrips()
    {
        byte[] input = new byte[180];
        const string Alphabet = "XYZABC";
        for (int i = 0; i < input.Length; i++) { input[i] = (byte)Alphabet[i % Alphabet.Length]; }
        AssertRoundTrips(input);
    }

    [Fact]
    public void Encode_20DistinctLiterals_500Bytes_RoundTrips()
    {
        byte[] input = new byte[500];
        for (int i = 0; i < input.Length; i++) { input[i] = (byte)('A' + (i % 20)); }
        AssertRoundTrips(input);
    }

    [Fact]
    public void Encode_HighEntropyRandom100KB_RoundTrips()
    {
        Random rnd = new(42);
        byte[] input = new byte[100_000];
        rnd.NextBytes(input);
        AssertRoundTrips(input);
    }

    [Fact]
    public void Encode_RepeatedTextBlock_RoundTrips()
    {
        byte[] phrase = System.Text.Encoding.ASCII.GetBytes(
            "The quick brown fox jumps over the lazy dog. ");
        byte[] input = Enumerable.Repeat(phrase, 20).SelectMany(x => x).ToArray();
        AssertRoundTrips(input);
    }

    [Theory]
    [InlineData(5, 30)]
    [InlineData(5, 60)]
    [InlineData(7, 100)]
    [InlineData(10, 200)]
    [InlineData(15, 300)]
    [InlineData(25, 500)]
    public void Encode_AlphabetVariations_RoundTrip(int distinctCount, int length)
    {
        byte[] input = new byte[length];
        for (int i = 0; i < length; i++) { input[i] = (byte)('A' + (i % distinctCount)); }
        AssertRoundTrips(input);
    }
}
