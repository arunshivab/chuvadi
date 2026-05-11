// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.5.2.3 — Cubic Bezier curves
// PHASE: Phase 2 — Chuvadi.Pdf.Graphics
// Converts Bezier curves to line segments for rasterization.

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Graphics;

/// <summary>
/// Flattens a <see cref="Path"/> containing cubic Bezier curves into a
/// sequence of straight line segments suitable for scanline rasterization.
/// </summary>
/// <remarks>
/// Uses adaptive subdivision: a curve segment is split in two when its
/// midpoint deviates from the chord by more than the flatness tolerance.
/// This produces fewer segments for nearly-straight curves while maintaining
/// accuracy where curvature is high.
///
/// The output is a list of sub-paths, each being an ordered list of
/// <see cref="PointF"/> vertices. Closed sub-paths include the closing edge
/// implicitly (the rasterizer connects the last point back to the first).
///
/// PDF 32000-1:2008 §8.5.2.3 — Bezier curves.
/// </remarks>
public sealed class PathFlattener
{
    private readonly double _flatness;

    /// <summary>
    /// Initialises a <see cref="PathFlattener"/> with the given flatness tolerance.
    /// </summary>
    /// <param name="flatness">
    /// Maximum permitted deviation of a flattened segment from the true curve,
    /// in the same units as the path coordinates (PDF points).
    /// Smaller values = more segments = higher accuracy.
    /// Typical values: 0.1 (high quality) to 1.0 (fast).
    /// </param>
    public PathFlattener(double flatness = 0.25)
    {
        if (flatness <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(flatness), "Flatness must be positive.");
        }

        _flatness = flatness;
    }

    /// <summary>
    /// Gets the flatness tolerance in path coordinate units.
    /// </summary>
    public double Flatness => _flatness;

    /// <summary>
    /// Flattens a path into a list of sub-paths.
    /// Each sub-path is a list of vertex points.
    /// </summary>
    /// <param name="path">The source path to flatten.</param>
    /// <returns>
    /// A list of sub-paths. Each sub-path is a non-empty list of points.
    /// The caller is responsible for applying the fill rule across sub-paths.
    /// </returns>
    public List<List<PointF>> Flatten(Path path)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        List<List<PointF>> result = new List<List<PointF>>();
        List<PointF>? currentSubPath = null;
        PointF subPathStart = PointF.Zero;
        PointF currentPoint = PointF.Zero;

        foreach (PathSegment seg in path.Segments)
        {
            switch (seg.Kind)
            {
                case PathSegmentKind.MoveTo:
                    if (currentSubPath is not null && currentSubPath.Count > 1)
                    {
                        result.Add(currentSubPath);
                    }

                    currentSubPath = new List<PointF>();
                    currentSubPath.Add(seg.P0);
                    subPathStart = seg.P0;
                    currentPoint = seg.P0;
                    break;

                case PathSegmentKind.LineTo:
                    if (currentSubPath is null)
                    {
                        currentSubPath = new List<PointF>();
                        currentSubPath.Add(currentPoint);
                        subPathStart = currentPoint;
                    }

                    currentSubPath.Add(seg.P0);
                    currentPoint = seg.P0;
                    break;

                case PathSegmentKind.CubicBezierTo:
                    if (currentSubPath is null)
                    {
                        currentSubPath = new List<PointF>();
                        currentSubPath.Add(currentPoint);
                        subPathStart = currentPoint;
                    }

                    FlattenCubic(currentPoint, seg.P0, seg.P1, seg.P2, currentSubPath);
                    currentPoint = seg.P2;
                    break;

                case PathSegmentKind.ClosePath:
                    if (currentSubPath is not null && currentSubPath.Count > 1)
                    {
                        // Add closing edge only if start != end.
                        PointF last = currentSubPath[currentSubPath.Count - 1];

                        if (last.X != subPathStart.X || last.Y != subPathStart.Y)
                        {
                            currentSubPath.Add(subPathStart);
                        }

                        result.Add(currentSubPath);
                    }

                    currentSubPath = null;
                    currentPoint = subPathStart;
                    break;
            }
        }

        // Add any open sub-path.
        if (currentSubPath is not null && currentSubPath.Count > 1)
        {
            result.Add(currentSubPath);
        }

        return result;
    }

    // ── Adaptive cubic Bezier subdivision ────────────────────────────────

    /// <summary>
    /// Recursively subdivides a cubic Bezier curve until all segments
    /// are within the flatness tolerance.
    /// de Casteljau's algorithm for subdivision.
    /// </summary>
    private void FlattenCubic(
        PointF p0, PointF p1, PointF p2, PointF p3,
        List<PointF> output)
    {
        // Measure how far the control points deviate from the chord p0→p3.
        // Use the max of the two control point deviations as the error estimate.
        double dx = p3.X - p0.X;
        double dy = p3.Y - p0.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);

        double d1;
        double d2;

        if (len < 1e-10)
        {
            d1 = Distance(p1, p0);
            d2 = Distance(p2, p0);
        }
        else
        {
            double invLen = 1.0 / len;
            // Signed distance of p1 from chord, then p2.
            double nx = -dy * invLen;
            double ny = dx * invLen;
            d1 = Math.Abs(nx * (p1.X - p0.X) + ny * (p1.Y - p0.Y));
            d2 = Math.Abs(nx * (p2.X - p0.X) + ny * (p2.Y - p0.Y));
        }

        double error = d1 > d2 ? d1 : d2;

        if (error <= _flatness)
        {
            // Curve is flat enough — emit the endpoint.
            output.Add(p3);
            return;
        }

        // Subdivide at t = 0.5 using de Casteljau's algorithm.
        double m01x = (p0.X + p1.X) * 0.5;
        double m01y = (p0.Y + p1.Y) * 0.5;
        double m12x = (p1.X + p2.X) * 0.5;
        double m12y = (p1.Y + p2.Y) * 0.5;
        double m23x = (p2.X + p3.X) * 0.5;
        double m23y = (p2.Y + p3.Y) * 0.5;
        double m012x = (m01x + m12x) * 0.5;
        double m012y = (m01y + m12y) * 0.5;
        double m123x = (m12x + m23x) * 0.5;
        double m123y = (m12y + m23y) * 0.5;
        double midx = (m012x + m123x) * 0.5;
        double midy = (m012y + m123y) * 0.5;

        PointF mid = new PointF(midx, midy);

        FlattenCubic(p0,
            new PointF(m01x, m01y),
            new PointF(m012x, m012y),
            mid,
            output);

        FlattenCubic(mid,
            new PointF(m123x, m123y),
            new PointF(m23x, m23y),
            p3,
            output);
    }

    private static double Distance(PointF a, PointF b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
