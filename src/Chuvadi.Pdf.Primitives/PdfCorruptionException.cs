// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Chuvadi v2.0.0 reader library plan §4.3
// PHASE: Phase 2.0 — exception hierarchy

using System;

namespace Chuvadi.Pdf.Primitives;

/// <summary>
/// Thrown when a PDF parses cleanly at the byte level but is semantically
/// inconsistent: cyclic <c>/Kids</c> page tree references, missing required
/// catalog entries, unresolvable indirect references, pages claiming a count
/// that does not match their actual children, and other structural integrity
/// failures.
/// </summary>
/// <remarks>
/// Distinguished from <see cref="PdfParseException"/> — that signals
/// byte-level syntax errors. This signals a document where the bytes are
/// fine but the document they describe is broken.
///
/// In v2.0.0 this type replaces the v1.x <c>PdfDocumentException</c> and
/// the semantic-integrity subset of <c>PdfObjectException</c>.
/// </remarks>
public sealed class PdfCorruptionException : PdfException
{
    /// <summary>Initialises a new instance with no message.</summary>
    public PdfCorruptionException()
    {
    }

    /// <summary>Initialises a new instance with the given message.</summary>
    /// <param name="message">A human-readable description of the failure.</param>
    public PdfCorruptionException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initialises a new instance with the given message and an inner exception
    /// that caused the failure.
    /// </summary>
    /// <param name="message">A human-readable description of the failure.</param>
    /// <param name="innerException">The exception that triggered this one.</param>
    public PdfCorruptionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
