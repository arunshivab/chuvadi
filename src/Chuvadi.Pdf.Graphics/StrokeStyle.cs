// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.4.3 — Stroke properties
// PHASE: Phase 2 — Chuvadi.Pdf.Graphics
// All parameters controlling how a path is stroked.


namespace Chuvadi.Pdf.Graphics;

/// <summary>
/// Encapsulates all stroke properties: width, cap, join, dash pattern, and miter limit.
/// PDF 32000-1:2008 §8.4.3 — Graphics state parameters for stroking.
/// </summary>
public sealed class StrokeStyle
{
    /// <summary>The default stroke style: 1pt solid black, butt cap, miter join.</summary>
    public static StrokeStyle Default { get; } = new StrokeStyle();

    /// <summary>Initialises a <see cref="StrokeStyle"/> with default values.</summary>
    public StrokeStyle()
    {
        Width = 1.0;
        Cap = LineCap.Butt;
        Join = LineJoin.Miter;
        MiterLimit = 10.0;
        DashPattern = [];
        DashOffset = 0.0;
        Color = ColorF.Black;
    }

    /// <summary>
    /// Gets or initialises the stroke width in user space units.
    /// PDF 32000-1:2008 §8.4.3.2 — Line width.
    /// </summary>
    public double Width { get; init; }

    /// <summary>
    /// Gets or initialises the line cap style.
    /// PDF 32000-1:2008 §8.4.3.3.
    /// </summary>
    public LineCap Cap { get; init; }

    /// <summary>
    /// Gets or initialises the line join style.
    /// PDF 32000-1:2008 §8.4.3.4.
    /// </summary>
    public LineJoin Join { get; init; }

    /// <summary>
    /// Gets or initialises the miter limit.
    /// When the miter length exceeds Width × MiterLimit, a bevel join is used.
    /// PDF 32000-1:2008 §8.4.3.5. Default 10.
    /// </summary>
    public double MiterLimit { get; init; }

    /// <summary>
    /// Gets or initialises the dash pattern. Empty array means solid stroke.
    /// PDF 32000-1:2008 §8.4.3.6 — Line dash pattern.
    /// </summary>
    public double[] DashPattern { get; init; }

    /// <summary>
    /// Gets or initialises the dash phase offset.
    /// PDF 32000-1:2008 §8.4.3.6.
    /// </summary>
    public double DashOffset { get; init; }

    /// <summary>Gets or initialises the stroke colour.</summary>
    public ColorF Color { get; init; }

    /// <summary>Returns true when the stroke is a solid line (no dash pattern).</summary>
    public bool IsSolid => DashPattern.Length == 0;

    /// <summary>Returns a copy with the given width.</summary>
    public StrokeStyle WithWidth(double width)
    {
        return new StrokeStyle
        {
            Width = width,
            Cap = Cap,
            Join = Join,
            MiterLimit = MiterLimit,
            DashPattern = DashPattern,
            DashOffset = DashOffset,
            Color = Color,
        };
    }

    /// <summary>Returns a copy with the given colour.</summary>
    public StrokeStyle WithColor(ColorF color)
    {
        return new StrokeStyle
        {
            Width = Width,
            Cap = Cap,
            Join = Join,
            MiterLimit = MiterLimit,
            DashPattern = DashPattern,
            DashOffset = DashOffset,
            Color = color,
        };
    }
}
