// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.7 — Document structure
// PHASE: Phase 1 — Chuvadi.Pdf.Documents
// Exception raised when the document model encounters an invalid structure.

using System;

namespace Chuvadi.Pdf.Documents;

/// <summary>
/// Thrown when the PDF document model encounters an invalid or unsupported
/// structure, such as a malformed page tree or a missing required entry.
/// </summary>
public sealed class PdfDocumentException : Exception
{
    /// <summary>Initialises a new <see cref="PdfDocumentException"/> with no message.</summary>
    public PdfDocumentException()
        : base("A PDF document error occurred.")
    {
    }

    /// <summary>Initialises a new <see cref="PdfDocumentException"/> with a message.</summary>
    public PdfDocumentException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="PdfDocumentException"/> with a message
    /// and an inner exception.
    /// </summary>
    public PdfDocumentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
