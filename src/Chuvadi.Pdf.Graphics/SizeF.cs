// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §8.3.2 — User space coordinates
// PHASE: Phase 2 — Chuvadi.Pdf.Graphics
// Width and height dimensions in PDF points.

using System;

namespace Chuvadi.Pdf.Graphics;

/// <summary>
/// An immutable size (width × height) in PDF points (1/72 inch).
/// </summary>
public readonly struct SizeF : IEquatable<SizeF>
{
    /// <summary>Initialises a new <see cref="SizeF"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="width"/> or <paramref name="height"/> is negative.
    /// </exception>
    public SizeF(double width, double height)
    {
        if (width < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be non-negative.");
        }

        if (height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be non-negative.");
        }

        Width = width;
        Height = height;
    }

    /// <summary>Gets the width in PDF points.</summary>
    public double Width { get; }

    /// <summary>Gets the height in PDF points.</summary>
    public double Height { get; }

    /// <summary>Zero-size (0 × 0).</summary>
    public static SizeF Zero { get; } = new SizeF(0, 0);

    /// <summary>Returns true when both dimensions are zero.</summary>
    public bool IsEmpty => Width == 0 && Height == 0;

    /// <inheritdoc/>
    public bool Equals(SizeF other) =>
        Width == other.Width && Height == other.Height;

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is SizeF s && Equals(s);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(Width, Height);

    /// <inheritdoc/>
    public override string ToString() =>
        $"{Width:G6} × {Height:G6}";

    /// <summary>Equality operator.</summary>
    public static bool operator ==(SizeF left, SizeF right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(SizeF left, SizeF right) => !left.Equals(right);
}
