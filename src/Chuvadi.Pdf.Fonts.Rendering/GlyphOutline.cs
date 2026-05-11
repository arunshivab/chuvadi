// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  OpenType spec §glyf — Glyph Data table
// PHASE: Phase 2 — Chuvadi.Pdf.Fonts.Rendering
// A glyph outline as a Graphics Path plus its typographic metrics.

using System;
using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Fonts.Rendering;

/// <summary>
/// The outline of a single glyph as a <see cref="Path"/> of contours,
/// together with its <see cref="GlyphMetrics"/>.
/// </summary>
/// <remarks>
/// The <see cref="Outline"/> path is in font design units with Y increasing
/// upward (TrueType convention). The rasterizer scales and flips Y when
/// drawing to a <see cref="Chuvadi.Pdf.Graphics.PixelBuffer"/>.
///
/// An empty <see cref="Outline"/> (no segments) is valid for whitespace
/// glyphs such as space and non-breaking space.
///
/// OpenType spec §glyf — Glyph Data table.
/// </remarks>
public sealed class GlyphOutline
{
    /// <summary>
    /// Initialises a <see cref="GlyphOutline"/>.
    /// </summary>
    public GlyphOutline(Path outline, GlyphMetrics metrics)
    {
        Outline = outline ?? throw new ArgumentNullException(nameof(outline));
        Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    /// <summary>Gets the glyph contours as a <see cref="Path"/>.</summary>
    public Path Outline { get; }

    /// <summary>Gets the typographic metrics for this glyph.</summary>
    public GlyphMetrics Metrics { get; }

    /// <summary>
    /// Returns true when the glyph has no visible outline
    /// (for example, a space character).
    /// </summary>
    public bool IsEmpty => Outline.IsEmpty;

    /// <summary>
    /// Returns a new <see cref="GlyphOutline"/> scaled to the given point size,
    /// suitable for rendering.
    /// </summary>
    /// <param name="pointSize">The target size in PDF points (1/72 inch).</param>
    public GlyphOutline Scale(double pointSize)
    {
        if (pointSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pointSize), "Point size must be positive.");
        }

        int unitsPerEm = Metrics.UnitsPerEm > 0 ? Metrics.UnitsPerEm : 1000;
        double scale = pointSize / unitsPerEm;

        Path scaled = ScalePath(Outline, scale);

        GlyphMetrics scaledMetrics = new GlyphMetrics(
            advanceWidth: (int)(Metrics.AdvanceWidth * scale),
            leftSideBearing: (int)(Metrics.LeftSideBearing * scale),
            unitsPerEm: unitsPerEm,
            bounds: new RectangleF(
                Metrics.Bounds.X * scale,
                Metrics.Bounds.Y * scale,
                Metrics.Bounds.Width * scale,
                Metrics.Bounds.Height * scale));

        return new GlyphOutline(scaled, scaledMetrics);
    }

    private static Path ScalePath(Path source, double scale)
    {
        Path result = new Path();

        foreach (PathSegment seg in source.Segments)
        {
            switch (seg.Kind)
            {
                case PathSegmentKind.MoveTo:
                    result.MoveTo(seg.P0.X * scale, seg.P0.Y * scale);
                    break;

                case PathSegmentKind.LineTo:
                    result.LineTo(seg.P0.X * scale, seg.P0.Y * scale);
                    break;

                case PathSegmentKind.CubicBezierTo:
                    result.CubicBezierTo(
                        new PointF(seg.P0.X * scale, seg.P0.Y * scale),
                        new PointF(seg.P1.X * scale, seg.P1.Y * scale),
                        new PointF(seg.P2.X * scale, seg.P2.Y * scale));
                    break;

                case PathSegmentKind.ClosePath:
                    result.ClosePath();
                    break;
            }
        }

        return result;
    }
}
