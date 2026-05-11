// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Globalization;

namespace Chuvadi.Pdf.Primitives;

/// <summary>
/// Represents a PDF real (floating-point) object.
/// PDF 32000-1:2008 §7.3.3 — Numeric objects.
/// </summary>
public sealed class PdfReal : PdfPrimitive
{
    /// <summary>The real value zero, cached to avoid allocations.</summary>
    public static readonly PdfReal Zero = new(0.0);

    /// <summary>Initialises a new <see cref="PdfReal"/> with the given value.</summary>
    public PdfReal(double value)
    {
        Value = value;
    }

    /// <summary>Gets the real value.</summary>
    public double Value { get; }

    /// <inheritdoc/>
    public override PdfPrimitiveType PrimitiveType => PdfPrimitiveType.Real;

    /// <summary>
    /// Returns the value formatted as a decimal string using invariant culture.
    /// Uses G6: up to 6 significant digits, no trailing zeros.
    /// </summary>
    public override string ToString()
    {
        if (double.IsNaN(Value) || double.IsInfinity(Value))
        {
            return "0";
        }

        return Value.ToString("G6", CultureInfo.InvariantCulture);
    }

    /// <summary>Implicitly converts a <see cref="PdfReal"/> to a <see cref="double"/>.</summary>
    public static implicit operator double(PdfReal r)
    {
        if (r is null)
        {
            throw new ArgumentNullException(nameof(r));
        }

        return r.Value;
    }

    /// <summary>Implicitly converts a <see cref="double"/> to a <see cref="PdfReal"/>.</summary>
    public static implicit operator PdfReal(double d) => new(d);

    /// <summary>Implicitly converts a <see cref="float"/> to a <see cref="PdfReal"/>.</summary>
    public static implicit operator PdfReal(float f) => new(f);

    /// <summary>
    /// Returns the numeric value as a double, whether the primitive is a
    /// <see cref="PdfInteger"/> or <see cref="PdfReal"/>.
    /// </summary>
    /// <exception cref="InvalidCastException">
    /// Thrown when <paramref name="primitive"/> is neither integer nor real.
    /// </exception>
    public static double ToDouble(PdfPrimitive primitive)
    {
        if (primitive is null)
        {
            throw new ArgumentNullException(nameof(primitive));
        }

        return primitive switch
        {
            PdfReal r => r.Value,
            PdfInteger i => i.Value,
            _ => throw new InvalidCastException(
                $"Expected PdfReal or PdfInteger, got {primitive.PrimitiveType}.")
        };
    }
}
