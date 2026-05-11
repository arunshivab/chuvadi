// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.3.2 — User space coordinates
// PHASE: Phase 2 — Chuvadi.Pdf.Graphics
// A point in 2D user space (double precision, PDF points).

using System;

namespace Chuvadi.Pdf.Graphics;

/// <summary>
/// An immutable point in 2D user space, measured in PDF points (1/72 inch).
/// Origin is PDF convention: bottom-left, Y increases upward.
/// </summary>
public readonly struct PointF : IEquatable<PointF>
{
    /// <summary>Initialises a new <see cref="PointF"/>.</summary>
    public PointF(double x, double y)
    {
        X = x;
        Y = y;
    }

    /// <summary>Gets the X coordinate.</summary>
    public double X { get; }

    /// <summary>Gets the Y coordinate.</summary>
    public double Y { get; }

    /// <summary>The origin point (0, 0).</summary>
    public static PointF Zero { get; } = new PointF(0, 0);

    /// <summary>Returns a point offset by (<paramref name="dx"/>, <paramref name="dy"/>).</summary>
    public PointF Translate(double dx, double dy)
    {
        return new PointF(X + dx, Y + dy);
    }

    /// <summary>Returns the distance to another point.</summary>
    public double DistanceTo(PointF other)
    {
        double dx = X - other.X;
        double dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <inheritdoc/>
    public bool Equals(PointF other) =>
        X == other.X && Y == other.Y;

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is PointF p && Equals(p);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(X, Y);

    /// <inheritdoc/>
    public override string ToString() =>
        $"({X:G6}, {Y:G6})";

    /// <summary>Equality operator.</summary>
    public static bool operator ==(PointF left, PointF right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(PointF left, PointF right) => !left.Equals(right);
}
