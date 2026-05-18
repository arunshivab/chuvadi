// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.1 — display-list intermediate

using System;
using System.Collections.Generic;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>Type of a path command.</summary>
public enum PathCommand
{
    /// <summary>Begin a new subpath at the given point.</summary>
    MoveTo = 0,
    /// <summary>Draw a straight line to the given point.</summary>
    LineTo = 1,
    /// <summary>Draw a cubic Bezier with two control points.</summary>
    CubicTo = 2,
    /// <summary>Close the current subpath.</summary>
    Close = 3,
}

/// <summary>A single path segment.</summary>
public readonly record struct PathSegment(PathCommand Command, double X1, double Y1, double X2, double Y2, double X3, double Y3);

/// <summary>Fill rule for a path or clip region.</summary>
public enum FillRule
{
    /// <summary>Non-zero winding (PDF default).</summary>
    NonZero = 0,
    /// <summary>Even-odd.</summary>
    EvenOdd = 1,
}

/// <summary>An ordered sequence of path segments.</summary>
public sealed class PathGeometry
{
    private readonly List<PathSegment> _segments;

    /// <summary>Initialises an empty path.</summary>
    public PathGeometry()
    {
        _segments = new List<PathSegment>();
    }

    /// <summary>Initialises from a sequence of segments.</summary>
    public PathGeometry(IEnumerable<PathSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        _segments = new List<PathSegment>(segments);
    }

    /// <summary>The ordered segments of this path.</summary>
    public IReadOnlyList<PathSegment> Segments => _segments;

    /// <summary>Whether this path has any segments.</summary>
    public bool IsEmpty => _segments.Count == 0;

    /// <summary>Adds a move-to command.</summary>
    public PathGeometry MoveTo(double x, double y)
    {
        _segments.Add(new PathSegment(PathCommand.MoveTo, x, y, 0, 0, 0, 0));
        return this;
    }

    /// <summary>Adds a line-to command.</summary>
    public PathGeometry LineTo(double x, double y)
    {
        _segments.Add(new PathSegment(PathCommand.LineTo, x, y, 0, 0, 0, 0));
        return this;
    }

    /// <summary>Adds a cubic-bezier-to command with two control points.</summary>
    public PathGeometry CubicTo(double x1, double y1, double x2, double y2, double x3, double y3)
    {
        _segments.Add(new PathSegment(PathCommand.CubicTo, x1, y1, x2, y2, x3, y3));
        return this;
    }

    /// <summary>Adds a close-path command.</summary>
    public PathGeometry Close()
    {
        _segments.Add(new PathSegment(PathCommand.Close, 0, 0, 0, 0, 0, 0));
        return this;
    }
}
