// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1 — Chuvadi.Pdf.Annotations

using System;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Annotations;

/// <summary>Thrown when an annotation operation fails.</summary>
public sealed class AnnotationException : PdfException
{
    /// <summary>Initialises a new <see cref="AnnotationException"/> with no message.</summary>
    public AnnotationException()
        : base("An annotation error occurred.")
    {
    }

    /// <summary>Initialises a new <see cref="AnnotationException"/> with a message.</summary>
    public AnnotationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="AnnotationException"/> with a message
    /// and an inner exception.
    /// </summary>
    public AnnotationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
