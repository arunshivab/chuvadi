// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5 — File structure
// PHASE: Phase 1 — Chuvadi.Pdf.IO
// Exception raised when the PDF reader encounters an unreadable file structure.

using System;

namespace Chuvadi.Pdf.IO;

/// <summary>
/// Thrown when <see cref="PdfReader"/> encounters a PDF file structure
/// it cannot parse or recover from.
/// </summary>
public sealed class PdfReaderException : Exception
{
    /// <summary>Initialises a new <see cref="PdfReaderException"/> with no message.</summary>
    public PdfReaderException()
        : base("A PDF reader error occurred.")
    {
    }

    /// <summary>Initialises a new <see cref="PdfReaderException"/> with a message.</summary>
    public PdfReaderException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="PdfReaderException"/> with a message
    /// and an inner exception.
    /// </summary>
    public PdfReaderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
