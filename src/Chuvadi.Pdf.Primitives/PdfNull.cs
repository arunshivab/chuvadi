// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

namespace Chuvadi.Pdf.Primitives;

/// <summary>
/// Represents the PDF null object.
/// </summary>
/// <remarks>
/// There is exactly one null object in Chuvadi. Use <see cref="Value"/>
/// rather than constructing new instances.
///
/// PDF 32000-1:2008 §7.3.9 — Null object.
/// </remarks>
public sealed class PdfNull : PdfPrimitive
{
    /// <summary>The singleton null object.</summary>
    public static readonly PdfNull Value = new();

    private PdfNull() { }

    /// <inheritdoc/>
    public override PdfPrimitiveType PrimitiveType => PdfPrimitiveType.Null;

    /// <summary>Returns the PDF keyword <c>null</c>.</summary>
    public override string ToString() => "null";
}
