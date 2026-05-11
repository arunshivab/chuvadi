// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  OpenType spec §hmtx — Horizontal Metrics table
//        OpenType spec §hhea — Horizontal Header table
// PHASE: Phase 2 — Chuvadi.Pdf.Fonts.Rendering
// Advance width, side bearings, and bounding box for a single glyph.

using Chuvadi.Pdf.Graphics;

namespace Chuvadi.Pdf.Fonts.Rendering;

/// <summary>
/// Typographic metrics for a single glyph, in font units (unscaled).
/// </summary>
/// <remarks>
/// All values are in the font's internal coordinate system (font design units).
/// To convert to PDF points at a given point size:
///   value_in_points = (value_in_font_units / unitsPerEm) × pointSize
///
/// OpenType spec §hmtx — Horizontal Metrics table.
/// </remarks>
public sealed class GlyphMetrics
{
    /// <summary>
    /// Initialises a <see cref="GlyphMetrics"/> instance.
    /// </summary>
    public GlyphMetrics(
        int advanceWidth,
        int leftSideBearing,
        int unitsPerEm,
        RectangleF bounds)
    {
        AdvanceWidth = advanceWidth;
        LeftSideBearing = leftSideBearing;
        UnitsPerEm = unitsPerEm;
        Bounds = bounds;
    }

    /// <summary>
    /// Horizontal advance width in font design units.
    /// The cursor advances by this much after drawing the glyph.
    /// </summary>
    public int AdvanceWidth { get; }

    /// <summary>
    /// Left side bearing in font design units.
    /// Horizontal distance from the origin to the left edge of the bounding box.
    /// </summary>
    public int LeftSideBearing { get; }

    /// <summary>
    /// Font units per em square. Typically 1000 (PostScript) or 2048 (TrueType).
    /// </summary>
    public int UnitsPerEm { get; }

    /// <summary>
    /// Glyph bounding box in font design units.
    /// </summary>
    public RectangleF Bounds { get; }

    /// <summary>
    /// Scales the advance width to PDF points at the given point size.
    /// </summary>
    public double AdvanceWidthAt(double pointSize)
    {
        return UnitsPerEm > 0
            ? AdvanceWidth * pointSize / UnitsPerEm
            : 0;
    }
}
