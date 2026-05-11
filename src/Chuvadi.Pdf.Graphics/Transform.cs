// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.3.3 — Transformation matrices
// PHASE: Phase 2 — Chuvadi.Pdf.Graphics
// A 2D affine transformation matrix (the public Graphics layer version).

using System;

namespace Chuvadi.Pdf.Graphics;

/// <summary>
/// An immutable 2D affine transformation matrix.
/// </summary>
/// <remarks>
/// PDF represents a 2D affine matrix as six values [a b c d e f]:
/// <code>
/// | a  b  0 |
/// | c  d  0 |
/// | e  f  1 |
/// </code>
/// This maps user-space coordinates to device space via:
///   x' = a*x + c*y + e
///   y' = b*x + d*y + f
///
/// PDF 32000-1:2008 §8.3.3 — Transformation matrices.
/// </remarks>
public readonly struct Transform : IEquatable<Transform>
{
    /// <summary>Initialises a transform from the six affine components.</summary>
    public Transform(double a, double b, double c, double d, double e, double f)
    {
        A = a; B = b; C = c; D = d; E = e; F = f;
    }

    /// <summary>Horizontal scaling / cosine of rotation.</summary>
    public double A { get; }

    /// <summary>Horizontal shearing / sine of rotation.</summary>
    public double B { get; }

    /// <summary>Vertical shearing / negative sine of rotation.</summary>
    public double C { get; }

    /// <summary>Vertical scaling / cosine of rotation.</summary>
    public double D { get; }

    /// <summary>Horizontal translation (X offset).</summary>
    public double E { get; }

    /// <summary>Vertical translation (Y offset).</summary>
    public double F { get; }

    // ── Common transforms ─────────────────────────────────────────────────

    /// <summary>The identity matrix [1 0 0 1 0 0].</summary>
    public static Transform Identity { get; } = new Transform(1, 0, 0, 1, 0, 0);

    /// <summary>Creates a translation matrix.</summary>
    public static Transform CreateTranslation(double tx, double ty)
    {
        return new Transform(1, 0, 0, 1, tx, ty);
    }

    /// <summary>Creates a uniform scaling matrix.</summary>
    public static Transform CreateScale(double scale)
    {
        return new Transform(scale, 0, 0, scale, 0, 0);
    }

    /// <summary>Creates a non-uniform scaling matrix.</summary>
    public static Transform CreateScale(double sx, double sy)
    {
        return new Transform(sx, 0, 0, sy, 0, 0);
    }

    /// <summary>
    /// Creates a rotation matrix.
    /// PDF 32000-1:2008 §8.3.4 — Rotation.
    /// </summary>
    /// <param name="radians">Angle in radians, counter-clockwise.</param>
    public static Transform CreateRotation(double radians)
    {
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        return new Transform(cos, sin, -sin, cos, 0, 0);
    }

    /// <summary>Creates a rotation matrix from degrees.</summary>
    public static Transform CreateRotationDegrees(double degrees)
    {
        return CreateRotation(degrees * Math.PI / 180.0);
    }

    // ── Operations ────────────────────────────────────────────────────────

    /// <summary>
    /// Concatenates this matrix with <paramref name="other"/> (this × other).
    /// PDF 32000-1:2008 §8.3.3 — Matrix multiplication.
    /// </summary>
    public Transform Multiply(Transform other)
    {
        return new Transform(
            a: A * other.A + B * other.C,
            b: A * other.B + B * other.D,
            c: C * other.A + D * other.C,
            d: C * other.B + D * other.D,
            e: E * other.A + F * other.C + other.E,
            f: E * other.B + F * other.D + other.F);
    }

    /// <summary>Applies this transform to a point.</summary>
    public PointF TransformPoint(PointF p)
    {
        return new PointF(
            A * p.X + C * p.Y + E,
            B * p.X + D * p.Y + F);
    }

    /// <summary>Applies only the linear part (no translation) to a vector.</summary>
    public PointF TransformVector(double dx, double dy)
    {
        return new PointF(A * dx + C * dy, B * dx + D * dy);
    }

    /// <summary>
    /// Returns the inverse of this transform.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the matrix is singular (determinant is zero).
    /// </exception>
    public Transform Invert()
    {
        double det = A * D - B * C;

        if (Math.Abs(det) < 1e-12)
        {
            throw new InvalidOperationException("Transform matrix is singular and cannot be inverted.");
        }

        double invDet = 1.0 / det;
        return new Transform(
            a: D * invDet,
            b: -B * invDet,
            c: -C * invDet,
            d: A * invDet,
            e: (C * F - D * E) * invDet,
            f: (B * E - A * F) * invDet);
    }

    /// <summary>Returns a copy of this transform with a translation prepended.</summary>
    public Transform Translate(double tx, double ty)
    {
        return CreateTranslation(tx, ty).Multiply(this);
    }

    /// <summary>Gets the translation component as a point.</summary>
    public PointF Translation => new PointF(E, F);

    /// <summary>Returns true when this is the identity matrix (within floating-point tolerance).</summary>
    public bool IsIdentity =>
        Math.Abs(A - 1) < 1e-10 && Math.Abs(B) < 1e-10 &&
        Math.Abs(C) < 1e-10 && Math.Abs(D - 1) < 1e-10 &&
        Math.Abs(E) < 1e-10 && Math.Abs(F) < 1e-10;

    // ── Equality ──────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public bool Equals(Transform other) =>
        A == other.A && B == other.B && C == other.C &&
        D == other.D && E == other.E && F == other.F;

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is Transform t && Equals(t);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(A, B, C, D, E, F);

    /// <inheritdoc/>
    public override string ToString() =>
        $"[{A:G4} {B:G4} {C:G4} {D:G4} {E:G4} {F:G4}]";

    /// <summary>Equality operator.</summary>
    public static bool operator ==(Transform left, Transform right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(Transform left, Transform right) => !left.Equals(right);

    /// <summary>Matrix multiplication operator.</summary>
    public static Transform operator *(Transform left, Transform right) => left.Multiply(right);
}
