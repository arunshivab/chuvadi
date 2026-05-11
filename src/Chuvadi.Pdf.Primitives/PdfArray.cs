// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Chuvadi.Pdf.Primitives;

/// <summary>
/// Represents a PDF array object — an ordered sequence of primitives.
/// PDF 32000-1:2008 §7.3.6 — Array objects.
/// </summary>
public sealed class PdfArray : PdfPrimitive, IReadOnlyList<PdfPrimitive>
{
    private readonly List<PdfPrimitive> _items;

    /// <summary>Creates an empty <see cref="PdfArray"/>.</summary>
    public PdfArray()
    {
        _items = [];
    }

    /// <summary>Creates a <see cref="PdfArray"/> with the given initial capacity.</summary>
    public PdfArray(int capacity)
    {
        _items = new List<PdfPrimitive>(capacity);
    }

    /// <summary>Creates a <see cref="PdfArray"/> from an existing sequence.</summary>
    public PdfArray(IEnumerable<PdfPrimitive> items)
    {
        _items = [.. items];
    }

    /// <inheritdoc/>
    public int Count => _items.Count;

    /// <inheritdoc/>
    public PdfPrimitive this[int index] => _items[index];

    /// <inheritdoc/>
    public IEnumerator<PdfPrimitive> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    /// <summary>Appends a primitive to the end of the array.</summary>
    public void Add(PdfPrimitive item) => _items.Add(item);

    /// <summary>Inserts a primitive at the given index.</summary>
    public void Insert(int index, PdfPrimitive item) => _items.Insert(index, item);

    /// <summary>Removes the element at the given index.</summary>
    public void RemoveAt(int index) => _items.RemoveAt(index);

    /// <summary>Gets the element at <paramref name="index"/> cast to <typeparamref name="T"/>, or null.</summary>
    public T? GetAs<T>(int index) where T : PdfPrimitive => _items[index] as T;

    /// <summary>Gets the element at <paramref name="index"/> as an integer value.</summary>
    public int GetInteger(int index) => _items[index].Cast<PdfInteger>().Value;

    /// <summary>Gets the element at <paramref name="index"/> as a double value.</summary>
    public double GetNumber(int index) => PdfReal.ToDouble(_items[index]);

    /// <summary>
    /// Interprets this array as a PDF rectangle [x1, y1, x2, y2].
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the array does not contain exactly four numeric elements.
    /// </exception>
    public (double X1, double Y1, double X2, double Y2) AsRectangle()
    {
        if (_items.Count != 4)
        {
            throw new InvalidOperationException(
                $"Expected a PDF rectangle with 4 elements, got {_items.Count}.");
        }

        return (GetNumber(0), GetNumber(1), GetNumber(2), GetNumber(3));
    }

    /// <inheritdoc/>
    public override PdfPrimitiveType PrimitiveType => PdfPrimitiveType.Array;

    /// <summary>Returns the PDF syntax representation, e.g. <c>[1 0 R /Name 42]</c>.</summary>
    public override string ToString()
    {
        StringBuilder sb = new StringBuilder();
        sb.Append('[');

        for (int i = 0; i < _items.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            sb.Append(_items[i]);
        }

        sb.Append(']');
        return sb.ToString();
    }
}
