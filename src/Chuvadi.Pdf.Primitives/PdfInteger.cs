// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Globalization;

namespace Chuvadi.Pdf.Primitives;

/// <summary>
/// Represents a PDF integer object.
/// PDF 32000-1:2008 §7.3.3 — Numeric objects.
/// </summary>
public sealed class PdfInteger : PdfPrimitive
{
    /// <summary>The integer value zero, cached to avoid allocations.</summary>
    public static readonly PdfInteger Zero = new(0);

    /// <summary>The integer value one, cached to avoid allocations.</summary>
    public static readonly PdfInteger One = new(1);

    /// <summary>Initialises a new <see cref="PdfInteger"/> with the given value.</summary>
    public PdfInteger(int value)
    {
        Value = value;
    }

    /// <summary>Gets the integer value.</summary>
    public int Value { get; }

    /// <inheritdoc/>
    public override PdfPrimitiveType PrimitiveType => PdfPrimitiveType.Integer;

    /// <summary>Returns the integer formatted as a decimal string.</summary>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Implicitly converts a <see cref="PdfInteger"/> to an <see cref="int"/>.</summary>
    public static implicit operator int(PdfInteger i)
    {
        if (i is null)
        {
            throw new ArgumentNullException(nameof(i));
        }

        return i.Value;
    }

    /// <summary>Implicitly converts an <see cref="int"/> to a <see cref="PdfInteger"/>.</summary>
    public static implicit operator PdfInteger(int i) => new(i);

    /// <summary>Converts this integer to a <see cref="PdfReal"/>.</summary>
    public PdfReal ToReal() => new(Value);
}
