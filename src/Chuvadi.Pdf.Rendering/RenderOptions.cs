// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2 — Chuvadi.Pdf.Rendering
// Rendering options: DPI, scale, background colour.

using System;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Rendering;

/// <summary>
/// Options that control how a PDF page is rasterized.
/// </summary>
public sealed class RenderOptions
{
    /// <summary>Default options: 96 DPI, opaque white background.</summary>
    public static RenderOptions Default { get; } = new RenderOptions();

    /// <summary>Initialises <see cref="RenderOptions"/> with default values.</summary>
    public RenderOptions()
    {
        Dpi = 96;
        Background = ColorF.White;
        FlatnessTolerance = 0.25;
    }

    /// <summary>
    /// Gets or initialises the output resolution in dots per inch.
    /// Higher values produce larger, sharper images.
    /// Typical values: 72 (screen), 96 (Windows default), 150, 300 (print).
    /// Default: 96.
    /// </summary>
    public double Dpi { get; init; }

    /// <summary>
    /// Gets or initialises the background colour painted before page content.
    /// Default: opaque white.
    /// </summary>
    public ColorF Background { get; init; }

    /// <summary>
    /// Gets or initialises the flatness tolerance for Bezier curve flattening
    /// in device pixels. Smaller = smoother curves, more segments.
    /// Default: 0.25 pixels.
    /// </summary>
    public double FlatnessTolerance { get; init; }

    /// <summary>
    /// Computes the pixel dimensions for a page of the given PDF point size
    /// at this option's DPI.
    /// </summary>
    public (int Width, int Height) PixelSize(double pageWidthPt, double pageHeightPt)
    {
        if (pageWidthPt <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageWidthPt), "Page width must be positive.");
        }

        if (pageHeightPt <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageHeightPt), "Page height must be positive.");
        }

        int w = Math.Max(1, (int)Math.Round(pageWidthPt  * Dpi / 72.0));
        int h = Math.Max(1, (int)Math.Round(pageHeightPt * Dpi / 72.0));
        return (w, h);
    }

    /// <summary>
    /// Computes the scale factor from PDF points to device pixels for this DPI.
    /// </summary>
    public double Scale => Dpi / 72.0;
}
