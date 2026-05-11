// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2 — Chuvadi.Pdf.Rendering
// Exception raised when a page cannot be rasterized.

using System;

namespace Chuvadi.Pdf.Rendering;

/// <summary>
/// Thrown when a PDF page cannot be rasterized due to an unsupported
/// feature, invalid data, or internal rasterizer error.
/// </summary>
public sealed class RenderingException : Exception
{
    /// <summary>Initialises a new <see cref="RenderingException"/> with no message.</summary>
    public RenderingException()
        : base("A PDF rendering error occurred.")
    {
    }

    /// <summary>Initialises a new <see cref="RenderingException"/> with a message.</summary>
    public RenderingException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="RenderingException"/> with a message
    /// and an inner exception.
    /// </summary>
    public RenderingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
