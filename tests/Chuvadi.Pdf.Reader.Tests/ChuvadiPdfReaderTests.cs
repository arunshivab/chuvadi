// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: v2.1.0 — IPdfReader facade tests

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Forms;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;
using Chuvadi.Pdf.Rendering.DisplayList;
using FluentAssertions;
using Xunit;

namespace Chuvadi.Pdf.Reader.Tests;

// ── Constructor and registration ─────────────────────────────────────────

public sealed class ChuvadiPdfReaderTests
{
    [Fact]
    public void Constructor_DoesNotThrow()
    {
        ChuvadiPdfReader reader = new ChuvadiPdfReader();
        reader.Should().NotBeNull();
    }

    [Fact]
    public void Implements_IPdfReader()
    {
        ChuvadiPdfReader reader = new ChuvadiPdfReader();
        reader.Should().BeAssignableTo<IPdfReader>();
    }
}

// ── OpenAsync ────────────────────────────────────────────────────────────

public sealed class OpenAsyncTests
{
    [Fact]
    public async Task NullStream_Throws()
    {
        ChuvadiPdfReader reader = new ChuvadiPdfReader();
        Func<Task> act = () => reader.OpenAsync(null!, "test.pdf");
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task NullFileName_Throws()
    {
        ChuvadiPdfReader reader = new ChuvadiPdfReader();
        using MemoryStream ms = TestBuilder.BuildPlainPdf();
        Func<Task> act = () => reader.OpenAsync(ms, null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PlainPdf_OpensWithoutPassword()
    {
        ChuvadiPdfReader reader = new ChuvadiPdfReader();
        using MemoryStream ms = TestBuilder.BuildPlainPdf();

        using PdfDocument doc = await reader.OpenAsync(ms, "test.pdf");

        doc.PageCount.Should().Be(1);
    }

    [Fact]
    public async Task PlainPdf_NullPasswordWorks()
    {
        ChuvadiPdfReader reader = new ChuvadiPdfReader();
        using MemoryStream ms = TestBuilder.BuildPlainPdf();

        using PdfDocument doc = await reader.OpenAsync(ms, "test.pdf", password: null);

        doc.PageCount.Should().Be(1);
    }

    [Fact]
    public async Task EncryptedPdf_OpensWithPassword()
    {
        ChuvadiPdfReader reader = new ChuvadiPdfReader();
        using MemoryStream ms = TestBuilder.BuildEncryptedPdf("secret");

        using PdfDocument doc = await reader.OpenAsync(ms, "encrypted.pdf", "secret");

        doc.PageCount.Should().Be(1);
        doc.Encryption.Should().NotBeNull();
    }

    [Fact]
    public async Task Cancellation_Honored()
    {
        ChuvadiPdfReader reader = new ChuvadiPdfReader();
        using MemoryStream ms = TestBuilder.BuildPlainPdf();
        using CancellationTokenSource cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => reader.OpenAsync(ms, "test.pdf", null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}

// ── RenderPageSvgAsync ───────────────────────────────────────────────────

public sealed class RenderPageSvgAsyncTests
{
    [Fact]
    public async Task NullDocument_Throws()
    {
        ChuvadiPdfReader reader = new ChuvadiPdfReader();
        Func<Task> act = () => reader.RenderPageSvgAsync(null!, 0);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task NegativePageIndex_Throws()
    {
        ChuvadiPdfReader reader = new ChuvadiPdfReader();
        using MemoryStream ms = TestBuilder.BuildPlainPdf();
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);

        Func<Task> act = () => reader.RenderPageSvgAsync(doc, -1);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task PageIndexOutOfRange_Throws()
    {
        ChuvadiPdfReader reader = new ChuvadiPdfReader();
        using MemoryStream ms = TestBuilder.BuildPlainPdf();
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);

        Func<Task> act = () => reader.RenderPageSvgAsync(doc, 99);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task PlainPage_ProducesNonEmptySvg()
    {
        ChuvadiPdfReader reader = new ChuvadiPdfReader();
        using MemoryStream ms = TestBuilder.BuildPlainPdf();
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);

        string svg = await reader.RenderPageSvgAsync(doc, 0);

        svg.Should().NotBeNullOrWhiteSpace();
        svg.Should().StartWith("<svg");
        svg.Should().EndWith("</svg>");
    }
}

// ── RenderThumbnailAsync ─────────────────────────────────────────────────

public sealed class RenderThumbnailAsyncTests
{
    [Fact]
    public async Task NullDocument_Throws()
    {
        ChuvadiPdfReader reader = new ChuvadiPdfReader();
        Func<Task> act = () => reader.RenderThumbnailAsync(null!, 0);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PageIndexOutOfRange_Throws()
    {
        ChuvadiPdfReader reader = new ChuvadiPdfReader();
        using MemoryStream ms = TestBuilder.BuildPlainPdf();
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);

        Func<Task> act = () => reader.RenderThumbnailAsync(doc, 5);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task PlainPage_ProducesValidSvg()
    {
        ChuvadiPdfReader reader = new ChuvadiPdfReader();
        using MemoryStream ms = TestBuilder.BuildPlainPdf();
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);

        string svg = await reader.RenderThumbnailAsync(doc, 0);

        svg.Should().NotBeNullOrWhiteSpace();
        svg.Should().StartWith("<svg");
    }
}

// ── GetOutlinesAsync ─────────────────────────────────────────────────────

public sealed class GetOutlinesAsyncTests
{
    [Fact]
    public async Task NullDocument_Throws()
    {
        ChuvadiPdfReader reader = new ChuvadiPdfReader();
        Func<Task> act = () => reader.GetOutlinesAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PlainPdf_NoOutlines_ReturnsEmpty()
    {
        ChuvadiPdfReader reader = new ChuvadiPdfReader();
        using MemoryStream ms = TestBuilder.BuildPlainPdf();
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);

        IReadOnlyList<OutlineItem> outlines = await reader.GetOutlinesAsync(doc);

        outlines.Should().BeEmpty();
    }
}

// ── SearchAsync ──────────────────────────────────────────────────────────

public sealed class SearchAsyncTests
{
    [Fact]
    public async Task NullDocument_Throws()
    {
        ChuvadiPdfReader reader = new ChuvadiPdfReader();
        IAsyncEnumerable<SearchMatch> enumerable = reader.SearchAsync(
            null!, "query", new SearchOptions());

        // ArgumentNullException is thrown on first MoveNextAsync of an async iterator.
        Func<Task> act = async () =>
        {
            await foreach (SearchMatch _ in enumerable) { /* enumerate */ }
        };

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task NullQuery_Throws()
    {
        ChuvadiPdfReader reader = new ChuvadiPdfReader();
        using MemoryStream ms = TestBuilder.BuildPlainPdf();
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);

        IAsyncEnumerable<SearchMatch> enumerable = reader.SearchAsync(
            doc, null!, new SearchOptions());

        Func<Task> act = async () =>
        {
            await foreach (SearchMatch _ in enumerable) { /* enumerate */ }
        };

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task NullOptions_Throws()
    {
        ChuvadiPdfReader reader = new ChuvadiPdfReader();
        using MemoryStream ms = TestBuilder.BuildPlainPdf();
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);

        IAsyncEnumerable<SearchMatch> enumerable = reader.SearchAsync(
            doc, "query", null!);

        Func<Task> act = async () =>
        {
            await foreach (SearchMatch _ in enumerable) { /* enumerate */ }
        };

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task EmptyPage_NoMatches()
    {
        ChuvadiPdfReader reader = new ChuvadiPdfReader();
        using MemoryStream ms = TestBuilder.BuildPlainPdf();
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);

        List<SearchMatch> matches = new List<SearchMatch>();
        await foreach (SearchMatch m in reader.SearchAsync(doc, "nonexistent", new SearchOptions()))
        {
            matches.Add(m);
        }

        matches.Should().BeEmpty();
    }
}

// ── GetTextRunsAsync ─────────────────────────────────────────────────────

public sealed class GetTextRunsAsyncTests
{
    [Fact]
    public async Task NullDocument_Throws()
    {
        ChuvadiPdfReader reader = new ChuvadiPdfReader();
        Func<Task> act = () => reader.GetTextRunsAsync(null!, 0);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PageIndexOutOfRange_Throws()
    {
        ChuvadiPdfReader reader = new ChuvadiPdfReader();
        using MemoryStream ms = TestBuilder.BuildPlainPdf();
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);

        Func<Task> act = () => reader.GetTextRunsAsync(doc, 99);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task PlainPage_ReturnsEmptyList()
    {
        ChuvadiPdfReader reader = new ChuvadiPdfReader();
        using MemoryStream ms = TestBuilder.BuildPlainPdf();
        using PdfDocument doc = PdfDocument.Open(ms, leaveOpen: true);

        IReadOnlyList<TextRun> runs = await reader.GetTextRunsAsync(doc, 0);

        runs.Should().NotBeNull();
        runs.Should().BeEmpty();
    }
}

// ── Test PDF builder ─────────────────────────────────────────────────────

internal static class TestBuilder
{
    internal static MemoryStream BuildPlainPdf()
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
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
            new PdfIndirectObject(pageId, pageDict),
        ];

        PdfDictionary trailer = new();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        MemoryStream ms = new();
        PdfWriter.Write(ms, objects, trailer);
        ms.Position = 0;
        return ms;
    }

    internal static MemoryStream BuildEncryptedPdf(string password)
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
            new PdfIndirectObject(catalogId, catalogDict),
            new PdfIndirectObject(pagesId, pagesDict),
            new PdfIndirectObject(pageId, pageDict),
        ];

        PdfDictionary trailer = new();
        trailer.Set(PdfName.Root, new PdfReference(catalogId));

        EncryptionOptions options = EncryptionOptions.Aes256(password);

        MemoryStream ms = new();
        PdfWriter.Write(ms, objects, trailer, options);
        ms.Position = 0;
        return ms;
    }
}
