// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO/IEC 14496-22 (OpenType), Apple TrueType Reference Manual
// PHASE: Phase 2 — Chuvadi.Pdf.Fonts.Rendering
// Exception raised when a font cannot be loaded or a glyph cannot be extracted.

using System;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Fonts.Rendering;

/// <summary>
/// Thrown when a font file cannot be parsed or a glyph outline
/// cannot be extracted due to an invalid or unsupported font structure.
/// </summary>
public sealed class FontRenderingException : PdfException
{
    /// <summary>Initialises a new <see cref="FontRenderingException"/> with no message.</summary>
    public FontRenderingException()
        : base("A font rendering error occurred.")
    {
    }

    /// <summary>Initialises a new <see cref="FontRenderingException"/> with a message.</summary>
    public FontRenderingException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="FontRenderingException"/> with a message
    /// and an inner exception.
    /// </summary>
    public FontRenderingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
