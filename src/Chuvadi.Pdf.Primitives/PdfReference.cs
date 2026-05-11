// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System;

namespace Chuvadi.Pdf.Primitives;

/// <summary>
/// Represents a PDF indirect object reference, e.g. <c>12 0 R</c>.
/// PDF 32000-1:2008 §7.3.10 — Indirect objects.
/// </summary>
public sealed class PdfReference : PdfPrimitive, IEquatable<PdfReference>
{
    /// <summary>Initialises a new <see cref="PdfReference"/>.</summary>
    public PdfReference(PdfObjectId objectId)
    {
        ObjectId = objectId;
    }

    /// <summary>Initialises a new <see cref="PdfReference"/> from object number and generation.</summary>
    public PdfReference(int objectNumber, int generation = 0)
        : this(new PdfObjectId(objectNumber, generation))
    {
    }

    /// <summary>Gets the identity of the referenced object.</summary>
    public PdfObjectId ObjectId { get; }

    /// <summary>Gets the object number.</summary>
    public int ObjectNumber => ObjectId.ObjectNumber;

    /// <summary>Gets the generation number.</summary>
    public int Generation => ObjectId.Generation;

    /// <inheritdoc/>
    public override PdfPrimitiveType PrimitiveType => PdfPrimitiveType.Reference;

    /// <inheritdoc/>
    public bool Equals(PdfReference? other) =>
        other is not null && ObjectId == other.ObjectId;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as PdfReference);

    /// <inheritdoc/>
    public override int GetHashCode() => ObjectId.GetHashCode();

    /// <summary>Value equality based on the referenced object ID.</summary>
    public static bool operator ==(PdfReference? left, PdfReference? right) =>
        left?.ObjectId == right?.ObjectId;

    /// <summary>Value inequality based on the referenced object ID.</summary>
    public static bool operator !=(PdfReference? left, PdfReference? right) =>
        !(left == right);

    /// <summary>Returns the PDF indirect reference syntax, e.g. <c>12 0 R</c>.</summary>
    public override string ToString() => ObjectId.ToString();
}
