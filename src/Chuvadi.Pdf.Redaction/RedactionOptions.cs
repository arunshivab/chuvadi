// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2 — Chuvadi.Pdf.Redaction
// Top-level redaction configuration.

using System.Collections.Generic;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Redaction;

/// <summary>
/// Top-level configuration for a redaction operation.
/// </summary>
public sealed class RedactionOptions
{
    /// <summary>Initialises <see cref="RedactionOptions"/> with default values.</summary>
    public RedactionOptions()
    {
        Rectangles = new List<RedactionRect>();
        OverlayColor = ColorF.Black;
    }

    /// <summary>
    /// Gets or initialises the list of rectangles to redact, by page.
    /// </summary>
    public IList<RedactionRect> Rectangles { get; init; }

    /// <summary>
    /// Gets or initialises the colour painted over each redacted rectangle.
    /// Default: opaque black.
    /// </summary>
    public ColorF OverlayColor { get; init; }
}
