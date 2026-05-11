// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2 — Chuvadi.Pdf.Redaction; extended Phase 1.1.2 with Patterns
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
        Patterns = new List<PatternRule>();
        OverlayColor = ColorF.Black;
        PatternPadding = 1.0;
    }

    /// <summary>
    /// Gets the list of explicit rectangles to redact, by page.
    /// </summary>
    public IList<RedactionRect> Rectangles { get; init; }

    /// <summary>
    /// Gets the list of regex patterns to redact. Each matching span across
    /// extracted text on a targeted page is resolved to a device-space rectangle
    /// and added to the redaction set.
    /// </summary>
    public IList<PatternRule> Patterns { get; init; }

    /// <summary>
    /// Gets or initialises the colour painted over each redacted rectangle.
    /// Default: opaque black.
    /// </summary>
    public ColorF OverlayColor { get; init; }

    /// <summary>
    /// Gets or initialises the padding (PDF points) added around each pattern-derived
    /// rectangle to compensate for font-metric approximation. Default: 1.0.
    /// </summary>
    public double PatternPadding { get; init; }
}
