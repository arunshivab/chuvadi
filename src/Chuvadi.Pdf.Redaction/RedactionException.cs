// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2 — Chuvadi.Pdf.Redaction

using System;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Redaction;

/// <summary>Thrown when a redaction operation fails.</summary>
public sealed class RedactionException : PdfException
{
    /// <summary>Initialises a new <see cref="RedactionException"/> with no message.</summary>
    public RedactionException()
        : base("A redaction error occurred.")
    {
    }

    /// <summary>Initialises a new <see cref="RedactionException"/> with a message.</summary>
    public RedactionException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="RedactionException"/> with a message
    /// and an inner exception.
    /// </summary>
    public RedactionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
