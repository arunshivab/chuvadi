// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2 — Chuvadi.Pdf.Forms

using System;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Forms;

/// <summary>Thrown when an AcroForm or outline operation fails.</summary>
public sealed class FormException : PdfException
{
    /// <summary>Initialises a new <see cref="FormException"/> with no message.</summary>
    public FormException()
        : base("A form operation error occurred.")
    {
    }

    /// <summary>Initialises a new <see cref="FormException"/> with a message.</summary>
    public FormException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="FormException"/> with a message and inner exception.
    /// </summary>
    public FormException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
