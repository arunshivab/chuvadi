// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.7.4 — Field types
// PHASE: Phase 2 — Chuvadi.Pdf.Forms

namespace Chuvadi.Pdf.Forms;

/// <summary>
/// Type of an AcroForm field.
/// PDF 32000-1:2008 §12.7.4 — Field types.
/// </summary>
public enum FormFieldType
{
    /// <summary>Unknown or non-terminal field (parent of other fields).</summary>
    Unknown,

    /// <summary>Text input field (/FT /Tx). PDF 32000-1:2008 §12.7.4.3.</summary>
    Text,

    /// <summary>Button field (/FT /Btn). Subtypes: pushbutton, checkbox, radio. §12.7.4.2.</summary>
    Button,

    /// <summary>Choice field (/FT /Ch). List box or combo box. §12.7.4.4.</summary>
    Choice,

    /// <summary>Signature field (/FT /Sig). §12.7.4.5.</summary>
    Signature,
}
