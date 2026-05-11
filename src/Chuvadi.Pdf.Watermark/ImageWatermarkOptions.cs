// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2 — Chuvadi.Pdf.Watermark
// Options for image watermark stamping.


namespace Chuvadi.Pdf.Watermark;

/// <summary>
/// Options for stamping an image watermark onto PDF pages.
/// </summary>
public sealed class ImageWatermarkOptions
{
    /// <summary>
    /// Initialises an <see cref="ImageWatermarkOptions"/> with default values.
    /// The image is centred, at 30% opacity, at 50% of page width.
    /// </summary>
    public ImageWatermarkOptions()
    {
        Opacity = 0.3f;
        ScaleFraction = 0.5;
        RotationDegrees = 0.0;
    }

    /// <summary>
    /// Gets or initialises the opacity from 0 (transparent) to 1 (opaque).
    /// Default: 0.3.
    /// </summary>
    public float Opacity { get; init; }

    /// <summary>
    /// Gets or initialises the image width as a fraction of the page width.
    /// 0.5 = 50% of the page width. Default: 0.5.
    /// </summary>
    public double ScaleFraction { get; init; }

    /// <summary>
    /// Gets or initialises the rotation in degrees. Default: 0.
    /// </summary>
    public double RotationDegrees { get; init; }

    /// <summary>
    /// Gets or initialises which pages to watermark.
    /// Null means all pages. Otherwise a zero-based page index set.
    /// </summary>
    public int[]? PageIndices { get; init; }
}
