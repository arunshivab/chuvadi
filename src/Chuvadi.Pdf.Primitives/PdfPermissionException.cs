// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Chuvadi v2.0.0 reader library plan §4.3
// PHASE: Phase 2.0 — exception hierarchy

using System;

namespace Chuvadi.Pdf.Primitives;

/// <summary>
/// Thrown when an operation is blocked because the document's permission
/// flags forbid it: extracting text from a copy-restricted document,
/// modifying a write-protected document, assembling a no-assembly document.
/// </summary>
/// <remarks>
/// The <see cref="Required"/> property tells the caller which permission
/// was missing, so they can either prompt for the owner password (which
/// bypasses permission checks) or surface a meaningful error to the end
/// user.
///
/// Distinguished from <see cref="PdfEncryptionException"/>: that signals an
/// inability to decrypt at all; this signals a successful decrypt followed
/// by a permission-denied operation.
///
/// This exception type is new in v2.0.0 — v1.x had no equivalent and
/// silently performed restricted operations.
/// </remarks>
public sealed class PdfPermissionException : PdfException
{
    /// <summary>Initialises a new instance with no message and no required permission.</summary>
    public PdfPermissionException()
    {
    }

    /// <summary>Initialises a new instance with the given message and no required permission.</summary>
    /// <param name="message">A human-readable description of the failure.</param>
    public PdfPermissionException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initialises a new instance with the given message and an inner exception.
    /// </summary>
    /// <param name="message">A human-readable description of the failure.</param>
    /// <param name="innerException">The exception that triggered this one.</param>
    public PdfPermissionException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Initialises a new instance with the given message and the permission
    /// that was required but not granted.
    /// </summary>
    /// <param name="message">A human-readable description of the failure.</param>
    /// <param name="required">The permission flag the caller lacked.</param>
    public PdfPermissionException(string message, PdfPermissions required) : base(message)
    {
        Required = required;
    }

    /// <summary>
    /// The permission flag that was required but not granted by the document.
    /// May be <see cref="PdfPermissions.None"/> if no specific permission
    /// was identified at throw time.
    /// </summary>
    public PdfPermissions Required { get; }
}
