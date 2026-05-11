// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §9 — Text
// PHASE: Phase 1 — Chuvadi.Pdf.Fonts
// Exception raised when font parsing or character mapping fails.

using System;

namespace Chuvadi.Pdf.Fonts;

/// <summary>
/// Thrown when a font dictionary cannot be parsed or a character code
/// cannot be mapped to a Unicode codepoint.
/// </summary>
public sealed class FontException : Exception
{
    /// <summary>Initialises a new <see cref="FontException"/> with no message.</summary>
    public FontException()
        : base("A PDF font error occurred.")
    {
    }

    /// <summary>Initialises a new <see cref="FontException"/> with a message.</summary>
    public FontException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="FontException"/> with a message
    /// and an inner exception.
    /// </summary>
    public FontException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
