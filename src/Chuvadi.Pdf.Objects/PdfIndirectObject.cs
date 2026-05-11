// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.3.10 — Indirect objects
// PHASE: Phase 1 — Chuvadi.Pdf.Objects
// A PDF primitive paired with its indirect object identity.

using System;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Objects;

/// <summary>
/// Represents an indirect object — a <see cref="PdfPrimitive"/> paired with
/// the <see cref="PdfObjectId"/> that identifies it in the PDF file.
/// </summary>
/// <remarks>
/// Every named object in a PDF file is an indirect object. Direct objects
/// (values inside dictionaries and arrays) are not indirect objects.
///
/// An indirect object definition in PDF syntax looks like:
/// <code>
/// 12 0 obj
/// &lt;&lt; /Type /Page ... &gt;&gt;
/// endobj
/// </code>
///
/// The object number (12) and generation number (0) together form the
/// <see cref="PdfObjectId"/>. The primitive (the dictionary) is the value.
///
/// PDF 32000-1:2008 §7.3.10 — Indirect objects.
/// </remarks>
public sealed class PdfIndirectObject
{
    /// <summary>
    /// Initialises a new <see cref="PdfIndirectObject"/>.
    /// </summary>
    /// <param name="id">The object identity. Must be valid (ObjectNumber > 0).</param>
    /// <param name="value">
    /// The primitive value of this object. Must not be null.
    /// Use <see cref="PdfNull.Value"/> when the object has a null value.
    /// </param>
    public PdfIndirectObject(PdfObjectId id, PdfPrimitive value)
    {
        if (!id.IsValid)
        {
            throw new ArgumentException(
                $"PdfObjectId must be valid (ObjectNumber > 0), got {id}.",
                nameof(id));
        }

        Id = id;
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Gets the identity of this indirect object.</summary>
    public PdfObjectId Id { get; }

    /// <summary>Gets the primitive value of this indirect object.</summary>
    public PdfPrimitive Value { get; }

    /// <summary>
    /// Gets the value cast to <typeparamref name="T"/>, or null if the
    /// value is not of the expected type.
    /// </summary>
    public T? GetAs<T>() where T : PdfPrimitive => Value as T;

    /// <summary>
    /// Gets the value cast to <typeparamref name="T"/>.
    /// </summary>
    /// <exception cref="InvalidCastException">
    /// Thrown when the value is not of type <typeparamref name="T"/>.
    /// </exception>
    public T Cast<T>() where T : PdfPrimitive => Value.Cast<T>();

    /// <summary>
    /// Returns the PDF indirect object definition syntax,
    /// e.g. <c>12 0 obj ... endobj</c>.
    /// </summary>
    public override string ToString() =>
        $"{Id.ObjectNumber} {Id.Generation} obj\n{Value}\nendobj";
}
