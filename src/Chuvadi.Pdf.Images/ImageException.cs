// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 10918-1 (JPEG), PNG 1.2, BMP
// PHASE: Phase 2 — Chuvadi.Pdf.Images
// Exception raised when an image cannot be decoded or encoded.

using System;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Images;

/// <summary>
/// Thrown when an image cannot be decoded or encoded due to an invalid
/// format, unsupported feature, or data corruption.
/// </summary>
public sealed class ImageException : PdfException
{
    /// <summary>Initialises a new <see cref="ImageException"/> with no message.</summary>
    public ImageException()
        : base("An image codec error occurred.")
    {
    }

    /// <summary>Initialises a new <see cref="ImageException"/> with a message.</summary>
    public ImageException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="ImageException"/> with a message
    /// and an inner exception.
    /// </summary>
    public ImageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
