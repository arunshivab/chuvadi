// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.3.10 — Indirect objects
// PHASE: Phase 1 — Chuvadi.Pdf.Objects
// Exception for object-model and xref errors.

using System;

namespace Chuvadi.Pdf.Objects;

/// <summary>
/// Thrown when the PDF object model encounters an invalid structure,
/// such as a malformed xref table or an unresolvable object reference.
/// </summary>
public sealed class PdfObjectException : Exception
{
    /// <summary>Initialises a new <see cref="PdfObjectException"/> with no message.</summary>
    public PdfObjectException()
        : base("A PDF object error occurred.")
    {
    }

    /// <summary>Initialises a new <see cref="PdfObjectException"/> with a message.</summary>
    public PdfObjectException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="PdfObjectException"/> with a message
    /// and an inner exception.
    /// </summary>
    public PdfObjectException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
