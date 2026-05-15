// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.6 — linearization tests

using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.IO.Tests;

public sealed class BitWriterReaderTests
{
    [Fact]
    public void WriteThenRead_SingleByte()
    {
        BitWriter w = new();
        w.WriteBits(0xAB, 8);
        byte[] bytes = w.ToArray();
        bytes.Should().Equal(0xAB);
    }

    [Fact]
    public void WriteThenRead_SubByteValues()
    {
        // Pack 5 + 3 = 8 bits → exactly one byte
        BitWriter w = new();
        w.WriteBits(0b10101, 5);  // 21
        w.WriteBits(0b110, 3);    // 6
        byte[] bytes = w.ToArray();
        bytes.Length.Should().Be(1);
        bytes[0].Should().Be(0b10101110);

        BitReader r = new(bytes);
        r.ReadBits(5).Should().Be(21);
        r.ReadBits(3).Should().Be(6);
    }

    [Fact]
    public void WriteThenRead_PartialByte_PaddedOnRequest()
    {
        BitWriter w = new();
        w.WriteBits(0b101, 3);
        byte[] bytes = w.ToArray();
        // Padded to MSB
        bytes.Should().Equal(0b10100000);
    }

    [Fact]
    public void RoundTrip_VariableWidth()
    {
        BitWriter w = new();
        w.WriteBits(123, 7);
        w.WriteBits(4567, 13);
        w.WriteBits(89, 7);
        byte[] bytes = w.ToArray();

        BitReader r = new(bytes);
        r.ReadBits(7).Should().Be(123);
        r.ReadBits(13).Should().Be(4567);
        r.ReadBits(7).Should().Be(89);
    }
}

public sealed class PageHintTableTests
{
    [Fact]
    public void RoundTrip_TwoPages()
    {
        List<PageHintEntry> input = new()
        {
            new() { ObjectCount = 5, LengthInBytes = 1000,
                    ContentStreamOffset = 100, ContentStreamLength = 200 },
            new() { ObjectCount = 3, LengthInBytes = 800,
                    ContentStreamOffset = 50,  ContentStreamLength = 150 },
        };

        byte[] encoded = PageHintTable.Encode(input, firstPageObjectNumber: 5);
        List<PageHintEntry> decoded = PageHintTable.Decode(encoded, pageCount: 2);

        decoded.Should().HaveCount(2);
        decoded[0].LengthInBytes.Should().Be(1000);
        decoded[1].LengthInBytes.Should().Be(800);
        decoded[0].ContentStreamLength.Should().Be(200);
        decoded[1].ContentStreamLength.Should().Be(150);
    }

    [Fact]
    public void Encode_Empty_ReturnsEmpty()
    {
        byte[] encoded = PageHintTable.Encode(new List<PageHintEntry>(), 0);
        encoded.Should().BeEmpty();
    }
}

public sealed class LinearizationReaderTests
{
    [Fact]
    public void TryRead_OnNonLinearizedDocument_ReturnsNull()
    {
        using PdfDocument doc = BuildMinimalDocument();
        LinearizationInfo? info = LinearizationReader.TryRead(doc.Objects);
        info.Should().BeNull();
    }

    private static PdfDocument BuildMinimalDocument()
    {
        PdfObjectId catalogId = new(1, 0);
        PdfObjectId pagesId = new(2, 0);

        PdfDictionary catalogDict = new();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        PdfDictionary pagesDict = new();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray([]));
        pagesDict.Set(PdfName.Count, 0);

        List<PdfIndirectObject> objects = [
            new(catalogId, catalogDict),
            new(pagesId, pagesDict),
        ];

        PdfDictionary trailer = new();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new();
        PdfWriter.Write(ms, objects, trailer);
        ms.Seek(0, SeekOrigin.Begin);
        return PdfDocument.Open(ms, leaveOpen: true);
    }
}

public sealed class LinearizedWriteThenReadTests
{
    [Fact]
    public void RoundTrip_SinglePagePdf()
    {
        using MemoryStream output = new();
        WriteLinearizedSinglePage(output);

        output.Seek(0, SeekOrigin.Begin);
        using PdfDocument reopened = PdfDocument.Open(output, leaveOpen: true);

        reopened.PageCount.Should().Be(1);
        reopened.IsLinearized.Should().BeTrue();

        LinearizationInfo info = reopened.Linearization!;
        info.PageCount.Should().Be(1);
        info.FileLength.Should().BeGreaterThan(0);
        info.HintOffsetsAndLengths.Should().HaveCount(2);
        info.FirstPageObjectNumber.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RoundTrip_FileLengthMatchesActual()
    {
        using MemoryStream output = new();
        WriteLinearizedSinglePage(output);

        output.Seek(0, SeekOrigin.Begin);
        using PdfDocument reopened = PdfDocument.Open(output, leaveOpen: true);

        // /L should equal the actual file length
        reopened.Linearization!.FileLength.Should().Be(output.Length);
    }

    [Fact]
    public void RoundTrip_ThreePagePdf()
    {
        using MemoryStream output = new();
        WriteLinearizedMultiPage(output, pageCount: 3);

        output.Seek(0, SeekOrigin.Begin);
        using PdfDocument reopened = PdfDocument.Open(output, leaveOpen: true);

        reopened.PageCount.Should().Be(3);
        reopened.IsLinearized.Should().BeTrue();
        reopened.Linearization!.PageCount.Should().Be(3);
    }

    [Fact]
    public void Linearized_ParamDictAppears_NearFileHead()
    {
        using MemoryStream output = new();
        WriteLinearizedSinglePage(output);

        // The /Linearized keyword should appear within the first 1024 bytes
        // (required by the spec for Fast Web View detection by HTTP-range clients).
        byte[] bytes = output.ToArray();
        int searchLen = System.Math.Min(1024, bytes.Length);
        string prefix = Encoding.Latin1.GetString(bytes, 0, searchLen);
        prefix.Should().Contain("/Linearized");
    }

    private static void WriteLinearizedSinglePage(Stream output)
    {
        PdfObjectId catalogId = new(1, 0);
        PdfObjectId pagesId = new(2, 0);
        PdfObjectId pageId = new(3, 0);

        PdfDictionary pageDict = new();
        pageDict.Set(PdfName.Type, PdfName.Page);
        pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
        pageDict.Set(PdfName.MediaBox, new PdfArray([
            new PdfInteger(0), new PdfInteger(0),
            new PdfInteger(612), new PdfInteger(792),
        ]));

        PdfDictionary pagesDict = new();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray([new PdfReference(pageId)]));
        pagesDict.Set(PdfName.Count, 1);

        PdfDictionary catalogDict = new();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        List<PdfIndirectObject> objects = [
            new(catalogId, catalogDict),
            new(pagesId, pagesDict),
            new(pageId, pageDict),
        ];

        PdfDictionary trailer = new();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        PdfWriter.WriteLinearized(output, objects, trailer);
    }

    private static void WriteLinearizedMultiPage(Stream output, int pageCount)
    {
        PdfObjectId catalogId = new(1, 0);
        PdfObjectId pagesId = new(2, 0);

        List<PdfIndirectObject> objects = new();
        List<PdfPrimitive> kids = new();

        for (int i = 0; i < pageCount; i++)
        {
            PdfObjectId pageId = new(3 + i, 0);
            PdfDictionary pageDict = new();
            pageDict.Set(PdfName.Type, PdfName.Page);
            pageDict.Set(PdfName.Parent, new PdfReference(pagesId));
            pageDict.Set(PdfName.MediaBox, new PdfArray([
                new PdfInteger(0), new PdfInteger(0),
                new PdfInteger(612), new PdfInteger(792),
            ]));
            objects.Add(new(pageId, pageDict));
            kids.Add(new PdfReference(pageId));
        }

        PdfDictionary pagesDict = new();
        pagesDict.Set(PdfName.Type, PdfName.Pages);
        pagesDict.Set(PdfName.Kids, new PdfArray(kids));
        pagesDict.Set(PdfName.Count, pageCount);

        PdfDictionary catalogDict = new();
        catalogDict.Set(PdfName.Type, PdfName.Catalog);
        catalogDict.Set(PdfName.Pages, new PdfReference(pagesId));

        objects.Insert(0, new(catalogId, catalogDict));
        objects.Insert(1, new(pagesId, pagesDict));

        PdfDictionary trailer = new();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        PdfWriter.WriteLinearized(output, objects, trailer);
    }
}
