// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.2 — Lexical conventions
// PHASE: Phase 1 — Chuvadi.Pdf.Primitives
// Exception raised when the tokenizer encounters bytes that cannot form a valid token.

using System;

namespace Chuvadi.Pdf.Primitives;

/// <summary>
/// Thrown when the <see cref="PdfTokenizer"/> encounters bytes that cannot
/// form a valid PDF token.
/// </summary>
public sealed class PdfTokenizerException : Exception
{
    // ── Standard constructors required by CA1032 ──────────────────────────
    // These ensure the exception can be constructed by serialisation
    // infrastructure and general exception-handling code.

    /// <summary>Initialises a new <see cref="PdfTokenizerException"/> with no message.</summary>
    public PdfTokenizerException()
        : base("A PDF tokenizer error occurred.")
    {
        ByteOffset = -1;
    }

    /// <summary>Initialises a new <see cref="PdfTokenizerException"/> with a message.</summary>
    public PdfTokenizerException(string message)
        : base(message)
    {
        ByteOffset = -1;
    }

    /// <summary>
    /// Initialises a new <see cref="PdfTokenizerException"/> with a message
    /// and an inner exception.
    /// </summary>
    public PdfTokenizerException(string message, Exception innerException)
        : base(message, innerException)
    {
        ByteOffset = -1;
    }

    // ── Chuvadi-specific constructors ─────────────────────────────────────

    /// <summary>
    /// Initialises a new <see cref="PdfTokenizerException"/> with a message
    /// and the byte offset at which the error was detected.
    /// </summary>
    /// <param name="message">A description of the error.</param>
    /// <param name="byteOffset">
    /// The byte offset in the stream where the error was detected.
    /// </param>
    public PdfTokenizerException(string message, long byteOffset)
        : base($"{message} (at byte offset {byteOffset})")
    {
        ByteOffset = byteOffset;
    }

    /// <summary>
    /// Initialises a new <see cref="PdfTokenizerException"/> with a message,
    /// byte offset, and an inner exception.
    /// </summary>
    public PdfTokenizerException(string message, long byteOffset, Exception innerException)
        : base($"{message} (at byte offset {byteOffset})", innerException)
    {
        ByteOffset = byteOffset;
    }

    /// <summary>
    /// Gets the byte offset in the stream where the error was detected.
    /// Returns -1 when the offset is not available.
    /// </summary>
    public long ByteOffset { get; }
}
