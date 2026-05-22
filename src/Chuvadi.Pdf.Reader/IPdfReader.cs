// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: v2.1.0 — high-level reader facade for the Chuvadi PDF library

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Forms;
using Chuvadi.Pdf.Rendering.DisplayList;

namespace Chuvadi.Pdf.Reader;

/// <summary>
/// High-level facade over the Chuvadi library for interactive PDF readers.
/// Designed for Blazor WebAssembly apps and any other consumer that wants
/// a small, mockable surface area instead of wiring the lower-level
/// modules (Documents, Rendering, Svg, Text, etc.) directly.
/// </summary>
/// <remarks>
/// All methods are asynchronous. Some operations (rendering, outline
/// traversal) are CPU-bound and complete synchronously internally;
/// they are still surfaced as Task-returning methods so that callers
/// can use a uniform <c>await</c>-everywhere idiom and so that the
/// facade can become genuinely asynchronous in future without breaking
/// callers.
/// </remarks>
public interface IPdfReader
{
    /// <summary>
    /// Opens a PDF document from a stream.
    /// </summary>
    /// <param name="stream">
    /// The stream containing the PDF bytes. Caller retains ownership;
    /// the returned <see cref="PdfDocument"/> reads from the stream
    /// on demand and disposes its own internal state on dispose.
    /// </param>
    /// <param name="fileName">
    /// The original file name (for display and error messages).
    /// Pass <see cref="string.Empty"/> if not known.
    /// </param>
    /// <param name="password">
    /// User or owner password for encrypted documents. Pass null for
    /// unencrypted documents. Throws if the document is encrypted and
    /// no password (or a wrong password) is supplied.
    /// </param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<PdfDocument> OpenAsync(
        Stream stream,
        string fileName,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renders a single page as a self-contained SVG string with a
    /// selectable invisible text layer over the visible glyph paths.
    /// Suitable for injection into a Blazor component via
    /// <c>@((MarkupString)svg)</c>.
    /// </summary>
    /// <remarks>
    /// The SVG is emitted at native PDF user-space coordinates.
    /// Visual zoom is the caller's responsibility — apply CSS
    /// <c>transform: scale(...)</c> on the container, or set
    /// explicit <c>width</c> / <c>height</c> attributes on the
    /// <c>&lt;svg&gt;</c> element. Browser scaling of SVG is lossless.
    /// </remarks>
    /// <param name="document">An open document.</param>
    /// <param name="pageIndex">Zero-based page index.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<string> RenderPageSvgAsync(
        PdfDocument document,
        int pageIndex,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renders a thumbnail of the page as an SVG string. Same content
    /// as <see cref="RenderPageSvgAsync"/> but at reduced coordinate
    /// precision, producing smaller output for sidebar-strip cases
    /// where many pages are rendered at once. Visual sizing is the
    /// caller's responsibility — see remarks on
    /// <see cref="RenderPageSvgAsync"/>.
    /// </summary>
    Task<string> RenderThumbnailAsync(
        PdfDocument document,
        int pageIndex,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the outline (bookmark) tree of the document, or an
    /// empty list if the document has no outline.
    /// </summary>
    Task<IReadOnlyList<OutlineItem>> GetOutlinesAsync(
        PdfDocument document,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams search matches for the given query across all pages.
    /// Yields each match as it is found so a results panel can update
    /// progressively. Stops when the consumer stops enumerating or
    /// when cancellation is requested.
    /// </summary>
    IAsyncEnumerable<SearchMatch> SearchAsync(
        PdfDocument document,
        string query,
        SearchOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the text runs on a single page in reading order, with
    /// per-run geometry. Used to build the selectable text layer
    /// independently of <see cref="RenderPageSvgAsync"/>.
    /// </summary>
    Task<IReadOnlyList<TextRun>> GetTextRunsAsync(
        PdfDocument document,
        int pageIndex,
        CancellationToken cancellationToken = default);
}
