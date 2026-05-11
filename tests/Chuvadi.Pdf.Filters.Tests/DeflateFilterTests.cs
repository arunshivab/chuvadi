// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.4.4 — FlateDecode, RFC 1950, RFC 1951
// PHASE: Phase 1 — Chuvadi.Pdf.Filters tests

using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Filters.Tests;

/// <summary>
/// Tests for DeflateFilter (FlateDecode).
/// Uses .NET's built-in ZLibStream as the reference implementation
/// to generate valid zlib-compressed test vectors.
/// </summary>
public sealed class DeflateFilterTests
{
    private static readonly DeflateFilter Filter = new();
    private static readonly FilterPipeline Pipeline = new();

    // ── Helper: compress with .NET ZLibStream (reference implementation) ──

    private static byte[] ZlibCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }

    // ── FilterName ────────────────────────────────────────────────────────

    [Fact]
    public void FilterName_IsFlateDecode()
    {
        Filter.FilterName.Should().Be("FlateDecode");
    }

    // ── Basic round-trip ──────────────────────────────────────────────────

    [Fact]
    public void Decode_SimpleText_RoundTrips()
    {
        byte[] original = Encoding.UTF8.GetBytes("Hello, Chuvadi!");
        byte[] compressed = ZlibCompress(original);

        byte[] decoded = Pipeline.Decode("FlateDecode", compressed);

        decoded.Should().Equal(original);
    }

    [Fact]
    public void Decode_EmptyInput_ProducesEmpty()
    {
        byte[] original = [];
        byte[] compressed = ZlibCompress(original);

        byte[] decoded = Pipeline.Decode("FlateDecode", compressed);

        decoded.Should().BeEmpty();
    }

    [Fact]
    public void Decode_SingleByte_RoundTrips()
    {
        byte[] original = [0x42];
        byte[] compressed = ZlibCompress(original);

        byte[] decoded = Pipeline.Decode("FlateDecode", compressed);

        decoded.Should().Equal(original);
    }

    [Fact]
    public void Decode_AllByteValues_RoundTrips()
    {
        byte[] original = new byte[256];

        for (int i = 0; i < 256; i++)
        {
            original[i] = (byte)i;
        }

        byte[] compressed = ZlibCompress(original);
        byte[] decoded = Pipeline.Decode("FlateDecode", compressed);

        decoded.Should().Equal(original);
    }

    [Fact]
    public void Decode_LargeRepetitiveData_RoundTrips()
    {
        // Repetitive data compresses well and exercises back-references.
        byte[] original = new byte[10000];

        for (int i = 0; i < original.Length; i++)
        {
            original[i] = (byte)(i % 17);
        }

        byte[] compressed = ZlibCompress(original);
        byte[] decoded = Pipeline.Decode("FlateDecode", compressed);

        decoded.Should().Equal(original);
    }

    [Fact]
    public void Decode_LargeRandomLikeData_RoundTrips()
    {
        // Pseudo-random data exercises stored blocks or minimal compression.
        byte[] original = new byte[8192];

        for (int i = 0; i < original.Length; i++)
        {
            original[i] = (byte)((i * 13 + 7) & 0xFF);
        }

        byte[] compressed = ZlibCompress(original);
        byte[] decoded = Pipeline.Decode("FlateDecode", compressed);

        decoded.Should().Equal(original);
    }

    // ── Encode + Decode round-trip ────────────────────────────────────────

    [Fact]
    public void Encode_ThenDecode_RoundTrips()
    {
        byte[] original = Encoding.UTF8.GetBytes("Chuvadi PDF Library — Phase 1");
        byte[] encoded = Pipeline.Encode("FlateDecode", original);
        byte[] decoded = Pipeline.Decode("FlateDecode", encoded);

        decoded.Should().Equal(original);
    }

    [Fact]
    public void Encode_ProducesValidZlib()
    {
        byte[] original = Encoding.UTF8.GetBytes("test data");
        byte[] encoded = Pipeline.Encode("FlateDecode", original);

        // Verify .NET's ZLibStream can also decode our output.
        using var input = new MemoryStream(encoded);
        using var output = new MemoryStream();
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        zlib.CopyTo(output);
        byte[] decoded = output.ToArray();

        decoded.Should().Equal(original);
    }

    [Fact]
    public void Encode_EmptyData_RoundTrips()
    {
        byte[] original = [];
        byte[] encoded = Pipeline.Encode("FlateDecode", original);
        byte[] decoded = Pipeline.Decode("FlateDecode", encoded);

        decoded.Should().BeEmpty();
    }

    [Fact]
    public void Encode_LargeData_RoundTrips()
    {
        byte[] original = new byte[100_000];

        for (int i = 0; i < original.Length; i++)
        {
            original[i] = (byte)(i & 0xFF);
        }

        byte[] encoded = Pipeline.Encode("FlateDecode", original);
        byte[] decoded = Pipeline.Decode("FlateDecode", encoded);

        decoded.Should().Equal(original);
    }

    // ── Error handling ────────────────────────────────────────────────────

    [Fact]
    public void Decode_NullInput_Throws()
    {
        Action act = () => Filter.Decode(null!, new MemoryStream());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Decode_NullOutput_Throws()
    {
        Action act = () => Filter.Decode(new MemoryStream(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Encode_NullInput_Throws()
    {
        Action act = () => Filter.Encode(null!, new MemoryStream());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Encode_NullOutput_Throws()
    {
        Action act = () => Filter.Encode(new MemoryStream(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Decode_TooShort_ThrowsFilterException()
    {
        byte[] tooShort = [0x78];
        Action act = () => Pipeline.Decode("FlateDecode", tooShort);
        act.Should().Throw<FilterException>();
    }

    [Fact]
    public void Decode_InvalidZlibHeader_ThrowsFilterException()
    {
        // Compression method != 8 — invalid.
        byte[] badHeader = [0x79, 0x9C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01];
        Action act = () => Pipeline.Decode("FlateDecode", badHeader);
        act.Should().Throw<FilterException>();
    }

    [Fact]
    public void Decode_CorruptedPayload_ThrowsFilterException()
    {
        byte[] original = Encoding.UTF8.GetBytes("Hello World");
        byte[] compressed = ZlibCompress(original);

        // Corrupt the compressed payload (not header, not checksum).
        compressed[5] ^= 0xFF;

        Action act = () => Pipeline.Decode("FlateDecode", compressed);
        act.Should().Throw<FilterException>();
    }

    // ── FilterPipeline ────────────────────────────────────────────────────

    [Fact]
    public void Pipeline_UnknownFilter_ThrowsFilterException()
    {
        Action act = () => Pipeline.Decode("UnknownFilter", []);
        act.Should().Throw<FilterException>();
    }

    [Fact]
    public void Pipeline_IsRegistered_TrueForFlateDecode()
    {
        Pipeline.IsRegistered("FlateDecode").Should().BeTrue();
    }

    [Fact]
    public void Pipeline_IsRegistered_FalseForUnknown()
    {
        Pipeline.IsRegistered("NoSuchFilter").Should().BeFalse();
    }

    [Fact]
    public void Pipeline_DecodeChain_SingleFilter_Works()
    {
        byte[] original = Encoding.UTF8.GetBytes("chain test");
        byte[] compressed = ZlibCompress(original);

        byte[] result = Pipeline.DecodeChain(["FlateDecode"], compressed);

        result.Should().Equal(original);
    }

    // ── FilterParameters ──────────────────────────────────────────────────

    [Fact]
    public void FilterParameters_Defaults_AreCorrect()
    {
        var parms = new FilterParameters();
        parms.Predictor.Should().Be(1);
        parms.Colors.Should().Be(1);
        parms.BitsPerComponent.Should().Be(8);
        parms.Columns.Should().Be(1);
        parms.EarlyChange.Should().Be(1);
    }

    // ── PNG predictor reversal ────────────────────────────────────────────

    [Fact]
    public void Decode_WithPngPredictor_Sub_Reverses()
    {
        // Build a row using Sub filter type (1).
        // Row: [1, 10, 1, 1] -> Sub filter applies deltas.
        // Filtered row: filter_type=1, then deltas from left.
        // Original: 10, 11, 12, 13 (each +1 from previous)
        // Sub-filtered: 10, 1, 1, 1
        byte[] filtered =
        [
            1,   // filter type = Sub
            10, 1, 1, 1,  // sub-filtered values
        ];

        var parms = new FilterParameters
        {
            Predictor = 12, // Sub
            Colors = 1,
            BitsPerComponent = 8,
            Columns = 4
        };

        // Compress the filtered data so Decode can process it.
        byte[] compressed = ZlibCompress(filtered);
        byte[] decoded = Pipeline.Decode("FlateDecode", compressed, parms);

        decoded.Should().Equal([10, 11, 12, 13]);
    }

    [Fact]
    public void Decode_WithPngPredictor_Up_Reverses()
    {
        // Two rows. Row 2 uses Up filter (delta from row above).
        // Row 1 original: 1, 2, 3
        // Row 2 original: 4, 6, 8
        // Row 1 filtered: [2, 1, 2, 3] (filter_type=2, no prev row so same)
        // Row 2 filtered: [2, 3, 4, 5] (filter_type=2, delta from row 1)
        byte[] filtered =
        [
            2, 1, 2, 3,
            2, 3, 4, 5,
        ];

        var parms = new FilterParameters
        {
            Predictor = 12,
            Colors = 1,
            BitsPerComponent = 8,
            Columns = 3
        };

        byte[] compressed = ZlibCompress(filtered);
        byte[] decoded = Pipeline.Decode("FlateDecode", compressed, parms);

        decoded.Should().Equal([1, 2, 3, 4, 6, 8]);
    }

    // ── Adler32 checksum (verified indirectly — Adler32 is internal) ──────

    [Fact]
    public void Adler32_ChecksumMismatch_ThrowsFilterException()
    {
        // Adler32 is internal — tested by corrupting the checksum trailer
        // and verifying the decoder detects the mismatch.
        byte[] original = Encoding.UTF8.GetBytes("checksum test");
        byte[] compressed = ZlibCompress(original);

        // The last 4 bytes are the Adler-32 checksum. Corrupt one byte.
        compressed[compressed.Length - 1] ^= 0xFF;

        Action act = () => Pipeline.Decode("FlateDecode", compressed);
        act.Should().Throw<FilterException>()
            .WithMessage("*checksum*");
    }

    [Fact]
    public void Adler32_ValidChecksum_DecodesSuccessfully()
    {
        // A valid zlib stream (correct checksum) decodes without exception.
        byte[] original = Encoding.UTF8.GetBytes("Mark Adler");
        byte[] compressed = ZlibCompress(original);
        byte[] decoded = Pipeline.Decode("FlateDecode", compressed);
        decoded.Should().Equal(original);
    }
}
