// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 2.1 — display-list intermediate

using System.Globalization;

namespace Chuvadi.Pdf.Rendering.DisplayList;

/// <summary>
/// 2D affine transformation matrix in PDF convention.
/// <code>
/// | A  B  0 |
/// | C  D  0 |
/// | E  F  1 |
/// </code>
/// Applied to a point (x, y): (A*x + C*y + E, B*x + D*y + F).
/// </summary>
public readonly record struct AffineMatrix(double A, double B, double C, double D, double E, double F)
{
    /// <summary>Identity transform.</summary>
    public static AffineMatrix Identity { get; } = new(1, 0, 0, 1, 0, 0);

    /// <summary>Returns this × <paramref name="other"/> (this is applied after other in PDF terms).</summary>
    public AffineMatrix Multiply(AffineMatrix other) => new(
        A: A * other.A + B * other.C,
        B: A * other.B + B * other.D,
        C: C * other.A + D * other.C,
        D: C * other.B + D * other.D,
        E: E * other.A + F * other.C + other.E,
        F: E * other.B + F * other.D + other.F);

    /// <summary>Applies this matrix to a point.</summary>
    public (double X, double Y) Apply(double x, double y)
        => (A * x + C * y + E, B * x + D * y + F);

    /// <summary>Returns the SVG matrix(...) function representation.</summary>
    public string ToSvgMatrix(string fmt = "0.######") => string.Format(
        CultureInfo.InvariantCulture, "matrix({0} {1} {2} {3} {4} {5})",
        A.ToString(fmt, CultureInfo.InvariantCulture),
        B.ToString(fmt, CultureInfo.InvariantCulture),
        C.ToString(fmt, CultureInfo.InvariantCulture),
        D.ToString(fmt, CultureInfo.InvariantCulture),
        E.ToString(fmt, CultureInfo.InvariantCulture),
        F.ToString(fmt, CultureInfo.InvariantCulture));
}
