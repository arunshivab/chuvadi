// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.7.3 — Field dictionaries
// PHASE: Phase 2 — Chuvadi.Pdf.Forms

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Forms;

/// <summary>
/// A single AcroForm field, read from a PDF document.
/// PDF 32000-1:2008 §12.7.3 — Field dictionaries.
/// </summary>
public sealed class FormField
{
    /// <summary>Initialises a new <see cref="FormField"/>.</summary>
    public FormField(
        string fullyQualifiedName,
        FormFieldType type,
        string? value,
        PdfObjectId objectId,
        IReadOnlyList<FormField> children)
    {
        FullyQualifiedName = fullyQualifiedName ?? throw new ArgumentNullException(nameof(fullyQualifiedName));
        Type = type;
        Value = value;
        ObjectId = objectId;
        Children = children ?? throw new ArgumentNullException(nameof(children));
    }

    /// <summary>
    /// Gets the fully qualified field name (e.g., "patient.firstName"),
    /// formed by joining ancestor partial names with periods.
    /// PDF 32000-1:2008 §12.7.3.2.
    /// </summary>
    public string FullyQualifiedName { get; }

    /// <summary>Gets the field type.</summary>
    public FormFieldType Type { get; }

    /// <summary>
    /// Gets the current field value as a string. Null for unset fields,
    /// signature fields, or button parent fields.
    /// </summary>
    public string? Value { get; }

    /// <summary>
    /// Gets the object ID of the underlying field dictionary in the PDF.
    /// Used by <see cref="FormFiller"/> to locate fields for value updates.
    /// </summary>
    public PdfObjectId ObjectId { get; }

    /// <summary>Gets the nested child fields, if any.</summary>
    public IReadOnlyList<FormField> Children { get; }

    /// <summary>Returns true when this is a leaf field (no children).</summary>
    public bool IsLeaf => Children.Count == 0;
}
