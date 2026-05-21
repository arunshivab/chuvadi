// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.6.3.2 — User access permissions
// PHASE: Phase 2.0 — exception hierarchy

using System;

namespace Chuvadi.Pdf.Primitives;

/// <summary>
/// Permission bits carried in a PDF document's encryption dictionary,
/// controlling which operations a user-password holder is allowed to
/// perform. PDF 32000-1:2008 §7.6.3.2 — User access permissions.
/// </summary>
/// <remarks>
/// Permissions are advisory and enforced cooperatively by conforming
/// readers; a sufficiently determined attacker who already has the user
/// password can ignore them. The flags exist to let well-behaved software
/// honour the document author's intent.
///
/// The owner password bypasses all permission checks. Documents opened
/// with the owner password should not surface
/// <see cref="PdfPermissionException"/> for any operation.
///
/// Bit values match the PDF specification's P-entry semantics: a set bit
/// means the corresponding action is <em>allowed</em>, a cleared bit means
/// it is <em>denied</em>.
/// </remarks>
[Flags]
public enum PdfPermissions
{
    /// <summary>No permissions granted. All restricted operations denied.</summary>
    None = 0,

    /// <summary>Permission to print the document (low-resolution if
    /// <see cref="PrintHighQuality"/> is not also set).</summary>
    Print = 1,

    /// <summary>Permission to modify the document's contents other than
    /// by adding annotations or filling form fields.</summary>
    ModifyContents = 2,

    /// <summary>Permission to copy or otherwise extract text and graphics
    /// from the document. Required for text selection and clipboard copy.</summary>
    CopyContents = 4,

    /// <summary>Permission to add, modify, or delete annotations and form
    /// fields (including signature fields).</summary>
    ModifyAnnotations = 8,

    /// <summary>Permission to fill existing interactive form fields,
    /// including signature fields, without altering the form's structure.</summary>
    FillForms = 16,

    /// <summary>Permission to extract text and graphics for accessibility
    /// purposes (screen readers, content reflow).</summary>
    ExtractAccessibility = 32,

    /// <summary>Permission to assemble the document (insert, rotate, or
    /// delete pages and create bookmarks or thumbnails), even when
    /// <see cref="ModifyContents"/> is denied.</summary>
    Assemble = 64,

    /// <summary>Permission to print the document at high resolution.
    /// Without this flag but with <see cref="Print"/>, the document may be
    /// printed only at a degraded resolution.</summary>
    PrintHighQuality = 128,

    /// <summary>All permissions granted. Used as the default when
    /// authoring a new encrypted document without explicit restrictions.</summary>
    All = Print
        | ModifyContents
        | CopyContents
        | ModifyAnnotations
        | FillForms
        | ExtractAccessibility
        | Assemble
        | PrintHighQuality,
}
