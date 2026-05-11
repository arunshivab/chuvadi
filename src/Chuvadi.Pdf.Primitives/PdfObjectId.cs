// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System;

namespace Chuvadi.Pdf.Primitives;

/// <summary>
/// Uniquely identifies an indirect object in a PDF file.
/// </summary>
/// <remarks>
/// Every indirect object in a PDF is identified by a pair of non-negative integers:
/// an object number and a generation number. The generation number is almost always
/// zero in modern PDFs; it increments only when an object is deleted and a new object
/// reuses the same object number (a rare operation in incremental updates).
///
/// PDF 32000-1:2008 §7.3.10 — Indirect objects.
/// </remarks>
/// <param name="ObjectNumber">
/// The object number. Must be greater than zero for real objects.
/// Object number 0 is reserved by the PDF specification.
/// </param>
/// <param name="Generation">
/// The generation number. Zero in the vast majority of PDFs.
/// </param>
public readonly record struct PdfObjectId(int ObjectNumber, int Generation)
    : IComparable<PdfObjectId>
{
    /// <summary>
    /// The invalid / sentinel object ID. Represents "no object".
    /// Object number 0 is reserved and never used for real objects.
    /// </summary>
    public static readonly PdfObjectId Invalid = new(0, 0);

    /// <summary>
    /// Returns true if this ID refers to a real indirect object
    /// (object number greater than zero).
    /// </summary>
    public bool IsValid => ObjectNumber > 0;

    /// <summary>
    /// Validates that the object ID is well-formed according to the PDF specification.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <see cref="ObjectNumber"/> is negative,
    /// or when <see cref="Generation"/> is negative.
    /// </exception>
    public void ThrowIfInvalid()
    {
        if (ObjectNumber < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ObjectNumber),
                ObjectNumber,
                "PDF object number must be non-negative.");
        }

        if (Generation < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Generation),
                Generation,
                "PDF generation number must be non-negative.");
        }
    }

    /// <summary>
    /// Compares two object IDs. Ordered first by object number, then by generation.
    /// </summary>
    public int CompareTo(PdfObjectId other)
    {
        int cmp = ObjectNumber.CompareTo(other.ObjectNumber);
        return cmp != 0 ? cmp : Generation.CompareTo(other.Generation);
    }

    /// <summary>
    /// Returns the PDF indirect reference syntax, e.g. <c>12 0 R</c>.
    /// </summary>
    public override string ToString() => $"{ObjectNumber} {Generation} R";

    /// <summary>
    /// Parses a PDF indirect reference from its canonical string form,
    /// e.g. <c>"12 0 R"</c>.
    /// </summary>
    /// <param name="value">The string to parse.</param>
    /// <returns>The parsed <see cref="PdfObjectId"/>.</returns>
    /// <exception cref="FormatException">
    /// Thrown when the string is not a valid indirect reference.
    /// </exception>
    public static PdfObjectId Parse(ReadOnlySpan<char> value)
    {
        // Expected format: "<digits> <digits> R"
        value = value.Trim();

        int firstSpace = value.IndexOf(' ');
        if (firstSpace < 1)
        {
            throw new FormatException(
                $"Invalid PDF object ID format: '{value.ToString()}'");
        }

        if (!int.TryParse(value[..firstSpace], out int objectNumber))
        {
            throw new FormatException(
                $"Invalid PDF object number in: '{value.ToString()}'");
        }

        ReadOnlySpan<char> rest = value[(firstSpace + 1)..];
        int secondSpace = rest.IndexOf(' ');
        if (secondSpace < 1)
        {
            throw new FormatException(
                $"Invalid PDF object ID format: '{value.ToString()}'");
        }

        if (!int.TryParse(rest[..secondSpace], out int generation))
        {
            throw new FormatException(
                $"Invalid PDF generation number in: '{value.ToString()}'");
        }

        ReadOnlySpan<char> trailer = rest[(secondSpace + 1)..].Trim();
        if (!trailer.Equals("R", StringComparison.Ordinal))
        {
            throw new FormatException(
                $"PDF object ID must end with 'R', got: '{trailer.ToString()}'");
        }

        return new PdfObjectId(objectNumber, generation);
    }

    /// <summary>
    /// Attempts to parse a PDF indirect reference from its canonical string form.
    /// </summary>
    /// <param name="value">The string to parse.</param>
    /// <param name="result">
    /// When successful, the parsed <see cref="PdfObjectId"/>;
    /// otherwise <see cref="Invalid"/>.
    /// </param>
    /// <returns>True if parsing succeeded; false otherwise.</returns>
    public static bool TryParse(ReadOnlySpan<char> value, out PdfObjectId result)
    {
        try
        {
            result = Parse(value);
            return true;
        }
        catch (FormatException)
        {
            result = Invalid;
            return false;
        }
    }
}
