// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Tests for ExtractSignedBytes / WriteSignedBytes

using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Signatures.Tests;

public sealed class SignatureReaderByteRangeTests
{
    [Fact]
    public void ExtractSignedBytes_StitchesTwoRangesContiguously()
    {
        byte[] file = new byte[30];
        for (int i = 0; i < 30; i++) { file[i] = (byte)i; }

        ByteRange range = new(0, 10, 20, 10);
        byte[] signed = SignatureReader.ExtractSignedBytes(file, range);

        signed.Should().HaveCount(20);
        for (int i = 0; i < 10; i++) { signed[i].Should().Be((byte)i); }
        for (int i = 0; i < 10; i++) { signed[10 + i].Should().Be((byte)(20 + i)); }
    }

    [Fact]
    public void ExtractSignedBytes_RangeBeyondFile_Throws()
    {
        byte[] file = new byte[10];
        ByteRange range = new(0, 5, 20, 10);
        Action act = () => SignatureReader.ExtractSignedBytes(file, range);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WriteSignedBytes_MatchesExtractSignedBytes()
    {
        byte[] file = new byte[30];
        for (int i = 0; i < 30; i++) { file[i] = (byte)i; }
        ByteRange range = new(0, 10, 20, 10);

        using MemoryStream source = new(file);
        using MemoryStream destination = new();
        SignatureReader.WriteSignedBytes(source, range, destination);

        byte[] streamed = destination.ToArray();
        byte[] arrayBased = SignatureReader.ExtractSignedBytes(file, range);
        streamed.Should().Equal(arrayBased);
    }

    [Fact]
    public void WriteSignedBytes_PreservesByteOrder()
    {
        byte[] file = new byte[100];
        for (int i = 0; i < 100; i++) { file[i] = (byte)(i + 1); }
        ByteRange range = new(10, 20, 50, 30);

        using MemoryStream source = new(file);
        using MemoryStream destination = new();
        SignatureReader.WriteSignedBytes(source, range, destination);

        byte[] result = destination.ToArray();
        result.Should().HaveCount(50);
        for (int i = 0; i < 20; i++) { result[i].Should().Be((byte)(11 + i)); }
        for (int i = 0; i < 30; i++) { result[20 + i].Should().Be((byte)(51 + i)); }
    }
}
