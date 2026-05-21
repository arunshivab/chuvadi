// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.7.2 — Document Catalog
//        PDF 32000-1:2008 §14.3.3 — Document information dictionary
// PHASE: Phase 1 — Chuvadi.Pdf.Documents
// High-level document model over a PdfReader.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Documents;

/// <summary>
/// Represents an opened PDF document.
/// </summary>
/// <remarks>
/// <see cref="PdfDocument"/> wraps a <see cref="PdfReader"/> and exposes
/// the document-level object model: pages, metadata, and the document catalog.
///
/// Open a document with <see cref="Open(Stream, bool)"/> or
/// <see cref="Open(string)"/> on desktop runtimes, or
/// <see cref="OpenAsync(Stream, CancellationToken)"/> /
/// <see cref="OpenAsync(string, CancellationToken)"/> on WebAssembly or
/// any caller that needs to integrate with asynchronous I/O. Dispose the
/// document when finished — it owns the underlying reader and stream.
///
/// PDF 32000-1:2008 §7.7.2 — Document Catalog.
/// PDF 32000-1:2008 §14.3.3 — Document information dictionary.
/// </remarks>
public sealed class PdfDocument : IDisposable
{
    private readonly PdfReader _reader;
    private PdfPageCollection? _pages;
    private bool _disposed;

    private PdfDocument(PdfReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _disposed = false;
    }

    // ── Factory: synchronous ──────────────────────────────────────────────

    /// <summary>
    /// Opens a PDF document from the given stream.
    /// </summary>
    /// <remarks>
    /// Synchronous blocking I/O. Not supported on WebAssembly; use
    /// <see cref="OpenAsync(Stream, CancellationToken)"/> for cross-platform code.
    /// </remarks>
    /// <param name="stream">A readable, seekable PDF stream.</param>
    /// <param name="leaveOpen">
    /// True to leave the stream open when this document is disposed.
    /// </param>
    public static PdfDocument Open(Stream stream, bool leaveOpen = false)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        PdfReader reader = PdfReader.Open(stream, leaveOpen);
        return new PdfDocument(reader);
    }

    /// <summary>
    /// Opens an encrypted PDF using the given user or owner password.
    /// </summary>
    /// <remarks>
    /// Synchronous blocking I/O. Not supported on WebAssembly; use
    /// <see cref="OpenAsync(Stream, string, CancellationToken)"/> for
    /// cross-platform code.
    /// </remarks>
    /// <param name="stream">Readable, seekable PDF stream.</param>
    /// <param name="password">User or owner password. Empty string for default.</param>
    /// <param name="leaveOpen">Whether to leave the underlying stream open on dispose.</param>
    public static PdfDocument Open(Stream stream, string password, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(password);

        PdfReader reader = PdfReader.Open(stream, password, leaveOpen);
        return new PdfDocument(reader);
    }

    /// <summary>Opens an encrypted PDF from a file path using the given password.</summary>
    /// <remarks>
    /// Synchronous blocking I/O against the file system. Use
    /// <see cref="OpenAsync(string, string, CancellationToken)"/> for
    /// cross-platform code.
    /// </remarks>
    public static PdfDocument Open(string path, string password)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(password);

        FileStream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        PdfReader reader = PdfReader.Open(stream, password, leaveOpen: false);
        return new PdfDocument(reader);
    }

    /// <summary>
    /// Opens a PDF document from a file path.
    /// </summary>
    /// <remarks>
    /// Synchronous blocking I/O against the file system. Use
    /// <see cref="OpenAsync(string, CancellationToken)"/> for cross-platform code.
    /// </remarks>
    /// <param name="path">The path to the PDF file.</param>
    public static PdfDocument Open(string path)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        FileStream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        PdfReader reader = PdfReader.Open(stream, leaveOpen: false);
        return new PdfDocument(reader);
    }

    // ── Factory: asynchronous (WASM-friendly) ─────────────────────────────

    /// <summary>
    /// Asynchronously opens a PDF document from the given stream.
    /// </summary>
    /// <remarks>
    /// The input stream is fully buffered into memory before parsing begins,
    /// making this method WebAssembly-compatible and tolerant of non-seekable
    /// streams. The document owns the internal buffer; the caller retains
    /// responsibility for disposing <paramref name="stream"/>.
    /// </remarks>
    /// <param name="stream">A readable PDF stream. Need not be seekable.</param>
    /// <param name="cancellationToken">A token that cancels the buffer fill.</param>
    public static async Task<PdfDocument> OpenAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        PdfReader reader = await PdfReader.OpenAsync(stream, cancellationToken)
            .ConfigureAwait(false);
        return new PdfDocument(reader);
    }

    /// <summary>
    /// Asynchronously opens an encrypted PDF document with the given password.
    /// </summary>
    /// <remarks>
    /// See <see cref="OpenAsync(Stream, CancellationToken)"/> for the buffering
    /// and cancellation semantics. For unencrypted PDFs the password is ignored.
    /// </remarks>
    /// <param name="stream">A readable PDF stream. Need not be seekable.</param>
    /// <param name="password">User or owner password. Empty string for default.</param>
    /// <param name="cancellationToken">A token that cancels the buffer fill.</param>
    public static async Task<PdfDocument> OpenAsync(
        Stream stream,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(password);

        PdfReader reader = await PdfReader.OpenAsync(stream, password, cancellationToken)
            .ConfigureAwait(false);
        return new PdfDocument(reader);
    }

    /// <summary>
    /// Asynchronously opens a PDF document from a file path.
    /// </summary>
    /// <remarks>
    /// Opens the file with <see cref="FileStream"/> configured for async I/O,
    /// buffers it fully into memory, then parses. The file handle is released
    /// before this method returns.
    /// </remarks>
    /// <param name="path">The path to the PDF file.</param>
    /// <param name="cancellationToken">A token that cancels the buffer fill.</param>
    public static async Task<PdfDocument> OpenAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);

        using FileStream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

        return await OpenAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously opens an encrypted PDF document from a file path.
    /// </summary>
    /// <remarks>
    /// See <see cref="OpenAsync(string, CancellationToken)"/> for I/O semantics.
    /// </remarks>
    /// <param name="path">The path to the PDF file.</param>
    /// <param name="password">User or owner password. Empty string for default.</param>
    /// <param name="cancellationToken">A token that cancels the buffer fill.</param>
    public static async Task<PdfDocument> OpenAsync(
        string path,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(password);

        using FileStream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

        return await OpenAsync(stream, password, cancellationToken).ConfigureAwait(false);
    }

    // ── Pages ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Gets the collection of pages in the document.
    /// Pages are resolved lazily from the page tree.
    /// PDF 32000-1:2008 §7.7.3 — Page tree.
    /// </summary>
    public PdfPageCollection Pages
    {
        get
        {
            if (_pages is null)
            {
                PdfDictionary? pagesDict = GetPagesRoot();
                _pages = new PdfPageCollection(pagesDict, _reader.Objects);
            }

            return _pages;
        }
    }

    /// <summary>Gets the total number of pages.</summary>
    public int PageCount => Pages.Count;

    // ── Document metadata ─────────────────────────────────────────────────

    /// <summary>
    /// Gets the document Title, or null when not set.
    /// PDF 32000-1:2008 §14.3.3, Table 317 — Title.
    /// </summary>
    public string? Title => GetInfoString(PdfName.Intern("Title"));

    /// <summary>Gets the document Author, or null when not set.</summary>
    public string? Author => GetInfoString(PdfName.Intern("Author"));

    /// <summary>Gets the document Subject, or null when not set.</summary>
    public string? Subject => GetInfoString(PdfName.Intern("Subject"));

    /// <summary>Gets the document Keywords, or null when not set.</summary>
    public string? Keywords => GetInfoString(PdfName.Intern("Keywords"));

    /// <summary>Gets the name of the application that created the document.</summary>
    public string? Creator => GetInfoString(PdfName.Intern("Creator"));

    /// <summary>Gets the name of the PDF producer application.</summary>
    public string? Producer => GetInfoString(PdfName.Intern("Producer"));

    // ── Document catalog ──────────────────────────────────────────────────

    /// <summary>
    /// Gets the raw document Catalog dictionary.
    /// PDF 32000-1:2008 §7.7.2 — Document Catalog.
    /// </summary>
    public PdfDictionary Catalog
    {
        get
        {
            return _reader.Catalog ??
                throw new PdfCorruptionException(
                    "The PDF file does not have a valid document Catalog. " +
                    "The trailer /Root entry is missing or does not point to a dictionary.");
        }
    }

    /// <summary>
    /// Gets the raw trailer dictionary.
    /// </summary>
    public PdfDictionary Trailer => _reader.Trailer;

    /// <summary>
    /// Gets the document's linearization parameter dictionary, or null when the
    /// document is not linearized (Fast Web View).
    /// </summary>
    public LinearizationInfo? Linearization
    {
        get
        {
            if (_linearization is null && !_linearizationProbed)
            {
                // Determine the highest object number from the trailer's /Size.
                int maxObjNum = 5;
                if (_reader.Trailer.TryGetValue(PdfName.Size, out PdfPrimitive? sizePrim) &&
                    sizePrim is PdfInteger sizeInt && sizeInt.Value > 0)
                {
                    maxObjNum = sizeInt.Value - 1;
                }
                _linearization = LinearizationReader.TryRead(_reader.Objects, maxObjNum);
                _linearizationProbed = true;
            }
            return _linearization;
        }
    }

    /// <summary>Returns true when the document is linearized (Fast Web View).</summary>
    public bool IsLinearized => Linearization is not null;

    private LinearizationInfo? _linearization;
    private bool _linearizationProbed;

    /// <summary>
    /// Gets the raw document information dictionary, or null when absent.
    /// </summary>
    public PdfDictionary? Info => _reader.Info;

    /// <summary>
    /// Gets the underlying object store for direct object access.
    /// </summary>
    public PdfObjectStore Objects => _reader.Objects;

    /// <summary>
    /// Gets the underlying <see cref="PdfReader"/> for low-level access such as
    /// reading raw file bytes for signature byte-range extraction.
    /// </summary>
    public PdfReader Reader => _reader;

    // ── IDisposable ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _reader.Dispose();
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private PdfDictionary GetPagesRoot()
    {
        PdfDictionary catalog = Catalog;

        if (!catalog.TryGetValue(PdfName.Pages, out PdfPrimitive? pagesRef))
        {
            throw new PdfCorruptionException(
                "Document Catalog is missing the required /Pages entry.");
        }

        PdfPrimitive resolved = _reader.Objects.Resolve(pagesRef);

        if (resolved is not PdfDictionary pagesDict)
        {
            throw new PdfCorruptionException(
                "The /Pages entry in the Catalog does not resolve to a dictionary.");
        }

        return pagesDict;
    }

    private string? GetInfoString(PdfName key)
    {
        PdfDictionary? info = _reader.Info;

        if (info is null)
        {
            return null;
        }

        PdfString? value = info.GetAs<PdfString>(key);

        if (value is null)
        {
            return null;
        }

        return value.ToTextString();
    }
}
