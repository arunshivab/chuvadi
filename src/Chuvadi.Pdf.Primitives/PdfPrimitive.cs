// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System;

namespace Chuvadi.Pdf.Primitives;

/// <summary>
/// Abstract base class for all PDF primitive object types.
/// </summary>
/// <remarks>
/// The PDF specification defines eight primitive types:
/// <list type="bullet">
///   <item><see cref="PdfNull"/> — the null object</item>
///   <item><see cref="PdfBoolean"/> — true or false</item>
///   <item><see cref="PdfInteger"/> — a signed integer</item>
///   <item><see cref="PdfReal"/> — a floating-point number</item>
///   <item><see cref="PdfName"/> — an interned symbolic name (e.g. /Type)</item>
///   <item><see cref="PdfString"/> — a byte string (literal or hex-encoded)</item>
///   <item><see cref="PdfArray"/> — an ordered sequence of primitives</item>
///   <item><see cref="PdfDictionary"/> — a keyed map of primitives</item>
///   <item><see cref="PdfStream"/> — a dictionary plus a binary byte payload</item>
///   <item><see cref="PdfReference"/> — an indirect reference to another object</item>
/// </list>
/// All primitive instances are immutable. Mutable document structures
/// (pages, annotations, form fields) are in <c>Chuvadi.Pdf.Documents</c>.
///
/// PDF 32000-1:2008 §7.3 — Objects.
/// </remarks>
public abstract class PdfPrimitive
{
    // Sealed constructor — only the types in this assembly may derive.
    private protected PdfPrimitive() { }

    /// <summary>Gets the PDF type of this primitive.</summary>
    public abstract PdfPrimitiveType PrimitiveType { get; }

    /// <summary>
    /// Returns true if this primitive is <see cref="PdfNull"/>.
    /// </summary>
    public bool IsNull => PrimitiveType == PdfPrimitiveType.Null;

    /// <summary>
    /// Attempts to cast this primitive to <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The target primitive type.</typeparam>
    /// <returns>
    /// This instance cast to <typeparamref name="T"/>,
    /// or <c>null</c> if the cast is not valid.
    /// </returns>
    public T? As<T>() where T : PdfPrimitive => this as T;

    /// <summary>
    /// Casts this primitive to <typeparamref name="T"/>,
    /// throwing if the cast is not valid.
    /// </summary>
    /// <typeparam name="T">The target primitive type.</typeparam>
    /// <returns>This instance cast to <typeparamref name="T"/>.</returns>
    /// <exception cref="InvalidCastException">
    /// Thrown when this primitive is not of type <typeparamref name="T"/>.
    /// </exception>
    public T Cast<T>() where T : PdfPrimitive
    {
        if (this is T typed)
        {
            return typed;
        }

        throw new InvalidCastException(
            $"Cannot cast PDF primitive of type {PrimitiveType} to {typeof(T).Name}.");
    }

    /// <summary>
    /// Returns a PDF-syntax string representation of this primitive,
    /// suitable for use in a PDF content stream or object definition.
    /// </summary>
    public abstract override string ToString();
}

/// <summary>
/// Identifies the concrete type of a <see cref="PdfPrimitive"/>.
/// </summary>
public enum PdfPrimitiveType
{
    /// <summary>The null object. PDF 32000-1:2008 §7.3.9.</summary>
    Null,

    /// <summary>A boolean value. PDF 32000-1:2008 §7.3.2.</summary>
    Boolean,

    /// <summary>A signed integer. PDF 32000-1:2008 §7.3.3.</summary>
    Integer,

    /// <summary>A floating-point real number. PDF 32000-1:2008 §7.3.3.</summary>
    Real,

    /// <summary>A symbolic name. PDF 32000-1:2008 §7.3.5.</summary>
    Name,

    /// <summary>A byte string. PDF 32000-1:2008 §7.3.4.</summary>
    String,

    /// <summary>An ordered array of primitives. PDF 32000-1:2008 §7.3.6.</summary>
    Array,

    /// <summary>A keyed dictionary of primitives. PDF 32000-1:2008 §7.3.7.</summary>
    Dictionary,

    /// <summary>
    /// A dictionary with an attached byte payload. PDF 32000-1:2008 §7.3.8.
    /// </summary>
    Stream,

    /// <summary>
    /// An indirect reference to another object. PDF 32000-1:2008 §7.3.10.
    /// </summary>
    Reference,
}
