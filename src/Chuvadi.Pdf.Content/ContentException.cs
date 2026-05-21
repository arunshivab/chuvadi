// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.8.2 — Content streams
// PHASE: Phase 1 — Chuvadi.Pdf.Content
// Exception raised when a content stream cannot be parsed.

using System;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Content;

/// <summary>
/// Thrown when a PDF content stream contains invalid or unsupported operators.
/// </summary>
public sealed class ContentException : PdfException
{
    /// <summary>Initialises a new <see cref="ContentException"/> with no message.</summary>
    public ContentException()
        : base("A PDF content stream error occurred.")
    {
    }

    /// <summary>Initialises a new <see cref="ContentException"/> with a message.</summary>
    public ContentException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="ContentException"/> with a message
    /// and an inner exception.
    /// </summary>
    public ContentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
