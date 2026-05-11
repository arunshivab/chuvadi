// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.5.2 — Path construction operators
// PHASE: Phase 2 — Chuvadi.Pdf.Graphics
// One segment in a vector path.

namespace Chuvadi.Pdf.Graphics;

/// <summary>
/// The kind of a <see cref="PathSegment"/>.
/// PDF 32000-1:2008 §8.5.2 — Path construction operators.
/// </summary>
public enum PathSegmentKind
{
    /// <summary>
    /// Move to a new point without drawing. Starts a new sub-path.
    /// PDF operator 'm'.
    /// </summary>
    MoveTo,

    /// <summary>
    /// Draw a straight line from the current point to the endpoint.
    /// PDF operator 'l'.
    /// </summary>
    LineTo,

    /// <summary>
    /// Draw a cubic Bezier curve using two control points.
    /// PDF operator 'c'.
    /// </summary>
    CubicBezierTo,

    /// <summary>
    /// Close the current sub-path with a straight line to the start point.
    /// PDF operator 'h'.
    /// </summary>
    ClosePath,
}

/// <summary>
/// A single segment in a vector graphics path.
/// Stores up to three points (for cubic Bezier curves).
/// PDF 32000-1:2008 §8.5.2.
/// </summary>
public readonly struct PathSegment
{
    private PathSegment(PathSegmentKind kind, PointF p0, PointF p1, PointF p2)
    {
        Kind = kind;
        P0 = p0;
        P1 = p1;
        P2 = p2;
    }

    /// <summary>Gets the kind of this segment.</summary>
    public PathSegmentKind Kind { get; }

    /// <summary>
    /// The endpoint for MoveTo and LineTo; the first control point for CubicBezierTo.
    /// </summary>
    public PointF P0 { get; }

    /// <summary>The second control point for CubicBezierTo; unused otherwise.</summary>
    public PointF P1 { get; }

    /// <summary>The endpoint for CubicBezierTo; unused otherwise.</summary>
    public PointF P2 { get; }

    // ── Factory methods ───────────────────────────────────────────────────

    /// <summary>Creates a MoveTo segment. PDF operator 'm'.</summary>
    public static PathSegment MoveTo(PointF point)
    {
        return new PathSegment(PathSegmentKind.MoveTo, point, default, default);
    }

    /// <summary>Creates a MoveTo segment from coordinates.</summary>
    public static PathSegment MoveTo(double x, double y)
    {
        return MoveTo(new PointF(x, y));
    }

    /// <summary>Creates a LineTo segment. PDF operator 'l'.</summary>
    public static PathSegment LineTo(PointF point)
    {
        return new PathSegment(PathSegmentKind.LineTo, point, default, default);
    }

    /// <summary>Creates a LineTo segment from coordinates.</summary>
    public static PathSegment LineTo(double x, double y)
    {
        return LineTo(new PointF(x, y));
    }

    /// <summary>
    /// Creates a cubic Bezier curve segment.
    /// PDF operator 'c': current point → cp1 → cp2 → endpoint.
    /// </summary>
    public static PathSegment CubicBezierTo(PointF cp1, PointF cp2, PointF endpoint)
    {
        return new PathSegment(PathSegmentKind.CubicBezierTo, cp1, cp2, endpoint);
    }

    /// <summary>Creates a ClosePath segment. PDF operator 'h'.</summary>
    public static PathSegment ClosePath()
    {
        return new PathSegment(PathSegmentKind.ClosePath, default, default, default);
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return Kind switch
        {
            PathSegmentKind.MoveTo => $"M {P0}",
            PathSegmentKind.LineTo => $"L {P0}",
            PathSegmentKind.CubicBezierTo => $"C {P0} {P1} {P2}",
            PathSegmentKind.ClosePath => "Z",
            _ => Kind.ToString(),
        };
    }
}
