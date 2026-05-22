// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: v2.1.0 — concrete IPdfReader implementation backed by Chuvadi

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Forms;
using Chuvadi.Pdf.Rendering.DisplayList;
using Chuvadi.Pdf.Svg;

namespace Chuvadi.Pdf.Reader;

/// <summary>
/// Production implementation of <see cref="IPdfReader"/> backed by the
/// Chuvadi PDF library. Register as a singleton in DI; methods are
/// stateless apart from the cached <see cref="SvgRenderer"/> instances
/// (full-page and thumbnail), which are thread-safe to share.
/// </summary>
public sealed class ChuvadiPdfReader : IPdfReader
{
    private readonly SvgRenderer _pageRenderer;
    private readonly SvgRenderer _thumbnailRenderer;

    /// <summary>Initialises a new <see cref="ChuvadiPdfReader"/>.</summary>
    public ChuvadiPdfReader()
    {
        _pageRenderer = new SvgRenderer(new SvgExportOptions
        {
            TextStrategy = SvgTextStrategy.Selectable,
            FontStrategy = SvgFontStrategy.EmbedAsWebFont,
            Precision = 4,
        });

        _thumbnailRenderer = new SvgRenderer(new SvgExportOptions
        {
            TextStrategy = SvgTextStrategy.Selectable,
            FontStrategy = SvgFontStrategy.CssFallbackOnly,
            Precision = 2,
        });
    }

    /// <inheritdoc />
    public Task<PdfDocument> OpenAsync(
        Stream stream,
        string fileName,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(fileName);

        return password is null
            ? PdfDocument.OpenAsync(stream, cancellationToken)
            : PdfDocument.OpenAsync(stream, password, cancellationToken);
    }

    /// <inheritdoc />
    public Task<string> RenderPageSvgAsync(
        PdfDocument document,
        int pageIndex,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidatePageIndex(document, pageIndex);
        cancellationToken.ThrowIfCancellationRequested();

        string svg = _pageRenderer.RenderPage(document, pageIndex);
        return Task.FromResult(svg);
    }

    /// <inheritdoc />
    public Task<string> RenderThumbnailAsync(
        PdfDocument document,
        int pageIndex,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidatePageIndex(document, pageIndex);
        cancellationToken.ThrowIfCancellationRequested();

        string svg = _thumbnailRenderer.RenderPage(document, pageIndex);
        return Task.FromResult(svg);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OutlineItem>> GetOutlinesAsync(
        PdfDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<OutlineItem> outlines = OutlineReader.GetOutlines(document);
        return Task.FromResult(outlines);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SearchMatch> SearchAsync(
        PdfDocument document,
        string query,
        SearchOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(options);

        await foreach (SearchMatch match in document.SearchAsync(query, options, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return match;
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TextRun>> GetTextRunsAsync(
        PdfDocument document,
        int pageIndex,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidatePageIndex(document, pageIndex);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<TextRun> runs = document.GetTextRuns(pageIndex);
        return Task.FromResult(runs);
    }

    private static void ValidatePageIndex(PdfDocument document, int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= document.PageCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageIndex),
                pageIndex,
                $"Page index {pageIndex} is outside the valid range [0, {document.PageCount}).");
        }
    }
}
