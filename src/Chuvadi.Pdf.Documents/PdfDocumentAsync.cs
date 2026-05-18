// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.1 — async open + non-seekable tolerance

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Chuvadi.Pdf.Documents;

/// <summary>
/// Async-capable entry points for <see cref="PdfDocument"/>.
/// </summary>
/// <remarks>
/// <para>
/// PDF parsing is inherently random-access (cross-reference table at file
/// tail, indirect objects pointed to by absolute byte offsets) so the
/// underlying <see cref="Chuvadi.Pdf.IO.PdfReader"/> needs a seekable
/// stream. When the input stream is not seekable (e.g. a network response
/// stream in Blazor WebAssembly), the bytes are first buffered into a
/// <see cref="MemoryStream"/>.
/// </para>
/// <para>
/// Cancellation is checked at the buffering boundary. Once parsing begins,
/// it runs to completion synchronously on the calling thread.
/// </para>
/// </remarks>
public static class PdfDocumentAsync
{
    /// <summary>
    /// Opens a PDF from <paramref name="stream"/> asynchronously. Buffers
    /// non-seekable streams into memory before parsing.
    /// </summary>
    public static async Task<PdfDocument> OpenAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();

        if (stream.CanSeek)
        {
            // Seekable: parse directly (sync-on-current-thread inside the task).
            return await Task.Run(() => PdfDocument.Open(stream, leaveOpen: false),
                cancellationToken).ConfigureAwait(false);
        }

        // Non-seekable: buffer to MemoryStream first.
        MemoryStream buffer = new();
        byte[] block = new byte[81920];
        while (true)
        {
            int n = await stream.ReadAsync(block.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (n <= 0) { break; }
            await buffer.WriteAsync(block.AsMemory(0, n), cancellationToken).ConfigureAwait(false);
        }
        buffer.Position = 0;
        cancellationToken.ThrowIfCancellationRequested();
        return PdfDocument.Open(buffer, leaveOpen: false);
    }

    /// <summary>
    /// Opens an encrypted PDF asynchronously. Buffers non-seekable streams
    /// into memory before parsing.
    /// </summary>
    public static async Task<PdfDocument> OpenAsync(
        Stream stream,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(password);
        cancellationToken.ThrowIfCancellationRequested();

        if (stream.CanSeek)
        {
            return await Task.Run(() => PdfDocument.Open(stream, password, leaveOpen: false),
                cancellationToken).ConfigureAwait(false);
        }

        MemoryStream buffer = new();
        byte[] block = new byte[81920];
        while (true)
        {
            int n = await stream.ReadAsync(block.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (n <= 0) { break; }
            await buffer.WriteAsync(block.AsMemory(0, n), cancellationToken).ConfigureAwait(false);
        }
        buffer.Position = 0;
        cancellationToken.ThrowIfCancellationRequested();
        return PdfDocument.Open(buffer, password, leaveOpen: false);
    }
}
