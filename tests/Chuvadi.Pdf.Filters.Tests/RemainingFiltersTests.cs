// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.4.2, §7.4.3, §7.4.5, §7.4.6
// PHASE: Phase 1 — Chuvadi.Pdf.Filters tests

using System;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Filters.Tests;

// ── ASCIIHex ─────────────────────────────────────────────────────────────

public sealed class AsciiHexFilterTests
{
    private static readonly AsciiHexFilter Filter = new();
    private static readonly FilterPipeline Pipeline = FilterRegistry.CreateDefaultPipeline();

    [Fact]
    public void FilterName_IsAsciiHexDecode()
    {
        Filter.FilterName.Should().Be("ASCIIHexDecode");
    }

    [Fact]
    public void Encode_ThenDecode_RoundTrips()
    {
        byte[] original = Encoding.UTF8.GetBytes("Hello, Chuvadi!");
        byte[] encoded = Pipeline.Encode("ASCIIHexDecode", original);
        byte[] decoded = Pipeline.Decode("ASCIIHexDecode", encoded);
        decoded.Should().Equal(original);
    }

    [Fact]
    public void Encode_ProducesUppercaseHex()
    {
        byte[] data = [0xAB, 0xCD, 0xEF];
        byte[] encoded = Pipeline.Encode("ASCIIHexDecode", data);
        string text = Encoding.ASCII.GetString(encoded);
        text.Should().Contain("AB");
        text.Should().Contain("CD");
        text.Should().Contain("EF");
        text.Should().EndWith(">");
    }

    [Fact]
    public void Decode_HexWithEodMarker_Succeeds()
    {
        byte[] encoded = Encoding.ASCII.GetBytes("48656C6C6F>");
        byte[] decoded = Pipeline.Decode("ASCIIHexDecode", encoded);
        Encoding.Latin1.GetString(decoded).Should().Be("Hello");
    }

    [Fact]
    public void Decode_HexWithWhitespace_SkipsWhitespace()
    {
        byte[] encoded = Encoding.ASCII.GetBytes("48 65 6C 6C 6F>");
        byte[] decoded = Pipeline.Decode("ASCIIHexDecode", encoded);
        Encoding.Latin1.GetString(decoded).Should().Be("Hello");
    }

    [Fact]
    public void Decode_OddNibbles_PadsWithZero()
    {
        // Odd digit count: last nibble padded with 0 per spec.
        byte[] encoded = Encoding.ASCII.GetBytes("A>");
        byte[] decoded = Pipeline.Decode("ASCIIHexDecode", encoded);
        decoded.Should().Equal([0xA0]);
    }

    [Fact]
    public void Decode_Empty_ProducesEmpty()
    {
        byte[] encoded = Encoding.ASCII.GetBytes(">");
        byte[] decoded = Pipeline.Decode("ASCIIHexDecode", encoded);
        decoded.Should().BeEmpty();
    }

    [Fact]
    public void Decode_AllByteValues_RoundTrips()
    {
        byte[] original = new byte[256];

        for (int i = 0; i < 256; i++)
        {
            original[i] = (byte)i;
        }

        byte[] encoded = Pipeline.Encode("ASCIIHexDecode", original);
        byte[] decoded = Pipeline.Decode("ASCIIHexDecode", encoded);
        decoded.Should().Equal(original);
    }

    [Fact]
    public void Decode_InvalidChar_ThrowsFilterException()
    {
        byte[] encoded = Encoding.ASCII.GetBytes("GG>");
        Action act = () => Pipeline.Decode("ASCIIHexDecode", encoded);
        act.Should().Throw<FilterException>();
    }
}

// ── ASCII85 ───────────────────────────────────────────────────────────────

public sealed class Ascii85FilterTests
{
    private static readonly FilterPipeline Pipeline = FilterRegistry.CreateDefaultPipeline();

    [Fact]
    public void FilterName_IsAscii85Decode()
    {
        new Ascii85Filter().FilterName.Should().Be("ASCII85Decode");
    }

    [Fact]
    public void Encode_ThenDecode_RoundTrips()
    {
        byte[] original = Encoding.UTF8.GetBytes("Hello, Chuvadi!");
        byte[] encoded = Pipeline.Encode("ASCII85Decode", original);
        byte[] decoded = Pipeline.Decode("ASCII85Decode", encoded);
        decoded.Should().Equal(original);
    }

    [Fact]
    public void Encode_EndsWithEodMarker()
    {
        byte[] data = Encoding.UTF8.GetBytes("test");
        byte[] encoded = Pipeline.Encode("ASCII85Decode", data);
        string text = Encoding.ASCII.GetString(encoded);
        text.Should().EndWith("~>");
    }

    [Fact]
    public void Encode_AllZeroGroup_UsesZShorthand()
    {
        byte[] data = [0, 0, 0, 0];
        byte[] encoded = Pipeline.Encode("ASCII85Decode", data);
        string text = Encoding.ASCII.GetString(encoded);
        text.Should().Contain("z");
    }

    [Fact]
    public void Decode_ZShorthand_ProducesFourZeroBytes()
    {
        byte[] encoded = Encoding.ASCII.GetBytes("z~>");
        byte[] decoded = Pipeline.Decode("ASCII85Decode", encoded);
        decoded.Should().Equal([0, 0, 0, 0]);
    }

    [Fact]
    public void Encode_Empty_RoundTrips()
    {
        byte[] original = [];
        byte[] encoded = Pipeline.Encode("ASCII85Decode", original);
        byte[] decoded = Pipeline.Decode("ASCII85Decode", encoded);
        decoded.Should().BeEmpty();
    }

    [Fact]
    public void Encode_SingleByte_RoundTrips()
    {
        byte[] original = [0x42];
        byte[] encoded = Pipeline.Encode("ASCII85Decode", original);
        byte[] decoded = Pipeline.Decode("ASCII85Decode", encoded);
        decoded.Should().Equal(original);
    }

    [Fact]
    public void Encode_AllByteValues_RoundTrips()
    {
        byte[] original = new byte[256];

        for (int i = 0; i < 256; i++)
        {
            original[i] = (byte)i;
        }

        byte[] encoded = Pipeline.Encode("ASCII85Decode", original);
        byte[] decoded = Pipeline.Decode("ASCII85Decode", encoded);
        decoded.Should().Equal(original);
    }

    [Fact]
    public void Encode_LargeData_RoundTrips()
    {
        byte[] original = new byte[10000];

        for (int i = 0; i < original.Length; i++)
        {
            original[i] = (byte)(i % 251);
        }

        byte[] encoded = Pipeline.Encode("ASCII85Decode", original);
        byte[] decoded = Pipeline.Decode("ASCII85Decode", encoded);
        decoded.Should().Equal(original);
    }

    [Fact]
    public void Decode_WithWhitespace_SkipsWhitespace()
    {
        // Encode then manually add whitespace before decoding.
        byte[] original = Encoding.UTF8.GetBytes("Test");
        byte[] encoded = Pipeline.Encode("ASCII85Decode", original);

        // Insert a newline in the middle of the encoded data.
        string text = Encoding.ASCII.GetString(encoded);
        int mid = text.Length / 2;
        string withNewline = text[..mid] + "\n" + text[mid..];
        byte[] decoded = Pipeline.Decode("ASCII85Decode", Encoding.ASCII.GetBytes(withNewline));
        decoded.Should().Equal(original);
    }

    [Fact]
    public void Decode_InvalidChar_ThrowsFilterException()
    {
        // 'v' is ASCII 118, above the valid maximum 'u' (117). Must throw.
        byte[] encoded = Encoding.ASCII.GetBytes("!!!!!v~>");
        Action act = () => Pipeline.Decode("ASCII85Decode", encoded);
        act.Should().Throw<FilterException>();
    }
}

// ── RunLength ─────────────────────────────────────────────────────────────

public sealed class RunLengthFilterTests
{
    private static readonly FilterPipeline Pipeline = FilterRegistry.CreateDefaultPipeline();

    [Fact]
    public void FilterName_IsRunLengthDecode()
    {
        new RunLengthFilter().FilterName.Should().Be("RunLengthDecode");
    }

    [Fact]
    public void Encode_ThenDecode_RoundTrips()
    {
        byte[] original = Encoding.UTF8.GetBytes("Hello, Chuvadi!");
        byte[] encoded = Pipeline.Encode("RunLengthDecode", original);
        byte[] decoded = Pipeline.Decode("RunLengthDecode", encoded);
        decoded.Should().Equal(original);
    }

    [Fact]
    public void Encode_Empty_RoundTrips()
    {
        byte[] original = [];
        byte[] encoded = Pipeline.Encode("RunLengthDecode", original);
        byte[] decoded = Pipeline.Decode("RunLengthDecode", encoded);
        decoded.Should().BeEmpty();
    }

    [Fact]
    public void Encode_SingleByte_RoundTrips()
    {
        byte[] original = [0x42];
        byte[] encoded = Pipeline.Encode("RunLengthDecode", original);
        byte[] decoded = Pipeline.Decode("RunLengthDecode", encoded);
        decoded.Should().Equal(original);
    }

    [Fact]
    public void Encode_RepetitiveData_CompressesWell()
    {
        // 128 identical bytes should encode very compactly.
        byte[] original = new byte[128];

        for (int i = 0; i < original.Length; i++)
        {
            original[i] = 0xFF;
        }

        byte[] encoded = Pipeline.Encode("RunLengthDecode", original);
        encoded.Length.Should().BeLessThan(original.Length);
    }

    [Fact]
    public void Encode_RepetitiveData_RoundTrips()
    {
        byte[] original = new byte[300];

        for (int i = 0; i < original.Length; i++)
        {
            original[i] = (byte)(i < 150 ? 0xAA : 0xBB);
        }

        byte[] encoded = Pipeline.Encode("RunLengthDecode", original);
        byte[] decoded = Pipeline.Decode("RunLengthDecode", encoded);
        decoded.Should().Equal(original);
    }

    [Fact]
    public void Encode_AllByteValues_RoundTrips()
    {
        byte[] original = new byte[256];

        for (int i = 0; i < 256; i++)
        {
            original[i] = (byte)i;
        }

        byte[] encoded = Pipeline.Encode("RunLengthDecode", original);
        byte[] decoded = Pipeline.Decode("RunLengthDecode", encoded);
        decoded.Should().Equal(original);
    }

    [Fact]
    public void Decode_EodByte128_TerminatesEarly()
    {
        // Manual stream: literal run [0x01, 0x02], then EOD (128).
        byte[] encoded = [1, 0x01, 0x02, 128];
        byte[] decoded = Pipeline.Decode("RunLengthDecode", encoded);
        decoded.Should().Equal([0x01, 0x02]);
    }
}

// ── LZW ──────────────────────────────────────────────────────────────────

public sealed class LzwFilterTests
{
    private static readonly FilterPipeline Pipeline = FilterRegistry.CreateDefaultPipeline();

    [Fact]
    public void FilterName_IsLzwDecode()
    {
        new LzwFilter().FilterName.Should().Be("LZWDecode");
    }

    [Fact]
    public void Encode_ThenDecode_RoundTrips()
    {
        byte[] original = Encoding.UTF8.GetBytes("Hello, Chuvadi!");
        byte[] encoded = Pipeline.Encode("LZWDecode", original);
        byte[] decoded = Pipeline.Decode("LZWDecode", encoded);
        decoded.Should().Equal(original);
    }

    [Fact]
    public void Encode_Empty_RoundTrips()
    {
        byte[] original = [];
        byte[] encoded = Pipeline.Encode("LZWDecode", original);
        byte[] decoded = Pipeline.Decode("LZWDecode", encoded);
        decoded.Should().BeEmpty();
    }

    [Fact]
    public void Encode_SingleByte_RoundTrips()
    {
        byte[] original = [0x42];
        byte[] encoded = Pipeline.Encode("LZWDecode", original);
        byte[] decoded = Pipeline.Decode("LZWDecode", encoded);
        decoded.Should().Equal(original);
    }

    [Fact]
    public void Encode_RepetitiveData_RoundTrips()
    {
        byte[] original = new byte[1000];

        for (int i = 0; i < original.Length; i++)
        {
            original[i] = (byte)(i % 7);
        }

        byte[] encoded = Pipeline.Encode("LZWDecode", original);
        byte[] decoded = Pipeline.Decode("LZWDecode", encoded);
        decoded.Should().Equal(original);
    }

    [Fact]
    public void Encode_AllByteValues_RoundTrips()
    {
        byte[] original = new byte[256];

        for (int i = 0; i < 256; i++)
        {
            original[i] = (byte)i;
        }

        byte[] encoded = Pipeline.Encode("LZWDecode", original);
        byte[] decoded = Pipeline.Decode("LZWDecode", encoded);
        decoded.Should().Equal(original);
    }

    [Fact]
    public void Encode_LargeData_RoundTrips()
    {
        byte[] original = new byte[5000];

        for (int i = 0; i < original.Length; i++)
        {
            original[i] = (byte)((i * 13 + 7) & 0xFF);
        }

        byte[] encoded = Pipeline.Encode("LZWDecode", original);
        byte[] decoded = Pipeline.Decode("LZWDecode", encoded);
        decoded.Should().Equal(original);
    }
}

// ── FilterRegistry ────────────────────────────────────────────────────────

public sealed class FilterRegistryTests
{
    [Fact]
    public void CreateDefaultPipeline_HasFlateDecode()
    {
        FilterPipeline pipeline = FilterRegistry.CreateDefaultPipeline();
        pipeline.IsRegistered("FlateDecode").Should().BeTrue();
    }

    [Fact]
    public void CreateDefaultPipeline_HasAsciiHexDecode()
    {
        FilterPipeline pipeline = FilterRegistry.CreateDefaultPipeline();
        pipeline.IsRegistered("ASCIIHexDecode").Should().BeTrue();
    }

    [Fact]
    public void CreateDefaultPipeline_HasAscii85Decode()
    {
        FilterPipeline pipeline = FilterRegistry.CreateDefaultPipeline();
        pipeline.IsRegistered("ASCII85Decode").Should().BeTrue();
    }

    [Fact]
    public void CreateDefaultPipeline_HasRunLengthDecode()
    {
        FilterPipeline pipeline = FilterRegistry.CreateDefaultPipeline();
        pipeline.IsRegistered("RunLengthDecode").Should().BeTrue();
    }

    [Fact]
    public void CreateDefaultPipeline_HasLzwDecode()
    {
        FilterPipeline pipeline = FilterRegistry.CreateDefaultPipeline();
        pipeline.IsRegistered("LZWDecode").Should().BeTrue();
    }

    [Fact]
    public void CreateDefaultPipeline_HasAliasFL()
    {
        FilterPipeline pipeline = FilterRegistry.CreateDefaultPipeline();
        pipeline.IsRegistered("Fl").Should().BeTrue();
    }

    [Fact]
    public void ResolveAlias_KnownAlias_ReturnsCanonical()
    {
        FilterRegistry.ResolveAlias("Fl").Should().Be("FlateDecode");
        FilterRegistry.ResolveAlias("AHx").Should().Be("ASCIIHexDecode");
        FilterRegistry.ResolveAlias("A85").Should().Be("ASCII85Decode");
        FilterRegistry.ResolveAlias("RL").Should().Be("RunLengthDecode");
        FilterRegistry.ResolveAlias("LZW").Should().Be("LZWDecode");
    }

    [Fact]
    public void ResolveAlias_CanonicalName_ReturnsUnchanged()
    {
        FilterRegistry.ResolveAlias("FlateDecode").Should().Be("FlateDecode");
    }

    [Fact]
    public void AliasFL_CanDecodeFlateEncodedData()
    {
        FilterPipeline pipeline = FilterRegistry.CreateDefaultPipeline();
        byte[] original = Encoding.UTF8.GetBytes("alias test");
        byte[] encoded = pipeline.Encode("FlateDecode", original);

        // Decode using alias "Fl" — should work via the same filter.
        byte[] decoded = pipeline.Decode("Fl", encoded);
        decoded.Should().Equal(original);
    }
}
