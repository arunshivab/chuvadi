// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2 — Chuvadi.Pdf.Watermark

using System;

namespace Chuvadi.Pdf.Watermark;

/// <summary>Thrown when a watermark cannot be applied.</summary>
public sealed class WatermarkException : Exception
{
    /// <summary>Initialises a new <see cref="WatermarkException"/> with no message.</summary>
    public WatermarkException()
        : base("A watermarking error occurred.")
    {
    }

    /// <summary>Initialises a new <see cref="WatermarkException"/> with a message.</summary>
    public WatermarkException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="WatermarkException"/> with a message
    /// and an inner exception.
    /// </summary>
    public WatermarkException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
