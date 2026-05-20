// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.9 — Chuvadi.Pdf.Images TIFF support

using System;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Images;

/// <summary>Thrown when a TIFF operation fails.</summary>
public sealed class TiffException : PdfException
{
    /// <summary>Initialises a new <see cref="TiffException"/> with a default message.</summary>
    public TiffException()
        : base("A TIFF error occurred.")
    {
    }

    /// <summary>Initialises a new <see cref="TiffException"/> with a message.</summary>
    public TiffException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="TiffException"/> with a message and an inner exception.
    /// </summary>
    public TiffException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
