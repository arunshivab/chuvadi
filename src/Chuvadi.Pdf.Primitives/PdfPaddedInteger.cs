// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Globalization;

namespace Chuvadi.Pdf.Primitives;

/// <summary>
/// A PDF integer that serialises to exactly <see cref="PaddedWidth"/> ASCII
/// characters, left-padded with leading zeros. Used by PDF signature emitters
/// to reserve fixed-width slots in the <c>/ByteRange</c> array so the byte
/// positions of subsequent dictionary entries (notably <c>/Contents</c>) do
/// not shift when the placeholder is patched with the actual values.
/// </summary>
/// <remarks>
/// PDF 32000-1:2008 §7.3.3 permits leading zeros in integer tokens; a value
/// of <c>42</c> with <see cref="PaddedWidth"/> <c>10</c> serialises as
/// <c>0000000042</c>, which parses back as <c>42</c> in any conforming
/// reader. The width must be at least wide enough to hold the largest value
/// the slot will ever carry.
/// </remarks>
public sealed class PdfPaddedInteger : PdfPrimitive
{
    /// <summary>Initialises a new padded integer with the given value and width.</summary>
    /// <param name="value">The integer value; must fit in <paramref name="paddedWidth"/> digits.</param>
    /// <param name="paddedWidth">The total number of characters to emit, &gt; 0.</param>
    public PdfPaddedInteger(int value, int paddedWidth)
    {
        if (paddedWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(paddedWidth), "Padded width must be positive.");
        }
        string s = value.ToString(CultureInfo.InvariantCulture);
        if (s.Length > paddedWidth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(paddedWidth),
                $"Value {value} requires {s.Length} characters but padded width is {paddedWidth}.");
        }
        Value = value;
        PaddedWidth = paddedWidth;
    }

    /// <summary>The integer value.</summary>
    public int Value { get; }

    /// <summary>The total width of the serialised form, in ASCII characters.</summary>
    public int PaddedWidth { get; }

    /// <inheritdoc/>
    public override PdfPrimitiveType PrimitiveType => PdfPrimitiveType.Integer;

    /// <summary>Returns the integer formatted with leading-zero padding.</summary>
    public override string ToString()
        => Value.ToString(CultureInfo.InvariantCulture).PadLeft(PaddedWidth, '0');
}
