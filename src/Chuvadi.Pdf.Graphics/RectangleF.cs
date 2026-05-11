// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.3.2 — User space; §7.7.3.3 — MediaBox
// PHASE: Phase 2 — Chuvadi.Pdf.Graphics
// An axis-aligned rectangle in PDF user space.

using System;

namespace Chuvadi.Pdf.Graphics;

/// <summary>
/// An immutable axis-aligned rectangle in PDF user space (points, 1/72 inch).
/// Origin is bottom-left by PDF convention; Y increases upward.
/// </summary>
public readonly struct RectangleF : IEquatable<RectangleF>
{
    /// <summary>
    /// Initialises a <see cref="RectangleF"/> from origin and size.
    /// </summary>
    public RectangleF(double x, double y, double width, double height)
    {
        if (width < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be non-negative.");
        }

        if (height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be non-negative.");
        }

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>Left edge (X of origin).</summary>
    public double X { get; }

    /// <summary>Bottom edge in PDF space (Y of origin).</summary>
    public double Y { get; }

    /// <summary>Width in PDF points.</summary>
    public double Width { get; }

    /// <summary>Height in PDF points.</summary>
    public double Height { get; }

    /// <summary>Right edge (X + Width).</summary>
    public double Right => X + Width;

    /// <summary>Top edge in PDF space (Y + Height).</summary>
    public double Top => Y + Height;

    /// <summary>Bottom-left corner.</summary>
    public PointF BottomLeft => new PointF(X, Y);

    /// <summary>Top-right corner.</summary>
    public PointF TopRight => new PointF(Right, Top);

    /// <summary>Centre of the rectangle.</summary>
    public PointF Centre => new PointF(X + Width / 2.0, Y + Height / 2.0);

    /// <summary>Size of the rectangle.</summary>
    public SizeF Size => new SizeF(Width, Height);

    /// <summary>Empty rectangle at origin.</summary>
    public static RectangleF Zero { get; } = new RectangleF(0, 0, 0, 0);

    /// <summary>Returns true when Width or Height is zero.</summary>
    public bool IsEmpty => Width == 0 || Height == 0;

    /// <summary>
    /// Creates a <see cref="RectangleF"/> from two corner points.
    /// PDF MediaBox format: [x1 y1 x2 y2].
    /// </summary>
    public static RectangleF FromCorners(double x1, double y1, double x2, double y2)
    {
        double left = Math.Min(x1, x2);
        double bottom = Math.Min(y1, y2);
        double width = Math.Abs(x2 - x1);
        double height = Math.Abs(y2 - y1);
        return new RectangleF(left, bottom, width, height);
    }

    /// <summary>
    /// Returns the intersection of this rectangle with <paramref name="other"/>,
    /// or <see cref="Zero"/> when they do not intersect.
    /// </summary>
    public RectangleF Intersect(RectangleF other)
    {
        double left = Math.Max(X, other.X);
        double bottom = Math.Max(Y, other.Y);
        double right = Math.Min(Right, other.Right);
        double top = Math.Min(Top, other.Top);

        if (right < left || top < bottom)
        {
            return Zero;
        }

        return new RectangleF(left, bottom, right - left, top - bottom);
    }

    /// <summary>Returns whether <paramref name="point"/> lies inside this rectangle.</summary>
    public bool Contains(PointF point)
    {
        return point.X >= X && point.X <= Right &&
               point.Y >= Y && point.Y <= Top;
    }

    /// <summary>Returns this rectangle expanded by <paramref name="amount"/> on all sides.</summary>
    public RectangleF Inflate(double amount)
    {
        double newWidth = Width + amount * 2;
        double newHeight = Height + amount * 2;

        if (newWidth < 0)
        {
            newWidth = 0;
        }

        if (newHeight < 0)
        {
            newHeight = 0;
        }

        return new RectangleF(X - amount, Y - amount, newWidth, newHeight);
    }

    /// <inheritdoc/>
    public bool Equals(RectangleF other) =>
        X == other.X && Y == other.Y &&
        Width == other.Width && Height == other.Height;

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is RectangleF r && Equals(r);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(X, Y, Width, Height);

    /// <inheritdoc/>
    public override string ToString() =>
        $"[{X:G6} {Y:G6} {Right:G6} {Top:G6}]";

    /// <summary>Equality operator.</summary>
    public static bool operator ==(RectangleF left, RectangleF right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(RectangleF left, RectangleF right) => !left.Equals(right);
}
