// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System;

namespace Chuvadi.Pdf.Primitives;

/// <summary>
/// Represents a PDF boolean value (<c>true</c> or <c>false</c>).
/// Use <see cref="True"/> and <see cref="False"/> singletons rather than
/// constructing new instances.
/// PDF 32000-1:2008 §7.3.2 — Boolean objects.
/// </summary>
public sealed class PdfBoolean : PdfPrimitive
{
    /// <summary>The PDF boolean <c>true</c>.</summary>
    public static readonly PdfBoolean True = new(true);

    /// <summary>The PDF boolean <c>false</c>.</summary>
    public static readonly PdfBoolean False = new(false);

    private PdfBoolean(bool value)
    {
        Value = value;
    }

    /// <summary>Gets the boolean value.</summary>
    public bool Value { get; }

    /// <inheritdoc/>
    public override PdfPrimitiveType PrimitiveType => PdfPrimitiveType.Boolean;

    /// <summary>Returns the singleton corresponding to the given boolean value.</summary>
    public static PdfBoolean FromBool(bool value) => value ? True : False;

    /// <summary>Returns <c>true</c> or <c>false</c> as PDF keywords.</summary>
    public override string ToString() => Value ? "true" : "false";

    /// <summary>Implicitly converts a <see cref="PdfBoolean"/> to a <see cref="bool"/>.</summary>
    public static implicit operator bool(PdfBoolean b)
    {
        if (b is null)
        {
            throw new ArgumentNullException(nameof(b));
        }

        return b.Value;
    }

    /// <summary>Implicitly converts a <see cref="bool"/> to a <see cref="PdfBoolean"/>.</summary>
    public static implicit operator PdfBoolean(bool b) => FromBool(b);
}
