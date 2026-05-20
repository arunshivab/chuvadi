// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Chuvadi v2.0.0 reader library plan §4.3
// PHASE: Phase 2.0 — exception hierarchy

using System;

namespace Chuvadi.Pdf.Primitives;

/// <summary>
/// Thrown when an encryption or decryption operation fails: wrong password,
/// unsupported security handler revision, malformed encryption dictionary,
/// missing required encryption metadata, or a cryptographic primitive that
/// could not produce the expected output.
/// </summary>
/// <remarks>
/// In v2.0.0 this type replaces the v1.x <c>EncryptionException</c> from
/// <c>Chuvadi.Pdf.Encryption</c>.
///
/// Distinguished from <see cref="PdfPermissionException"/>: this signals an
/// inability to decrypt or encrypt at all; that signals a successful decrypt
/// followed by a permission-denied operation (e.g. content extraction
/// blocked by the document's permission flags).
/// </remarks>
public sealed class PdfEncryptionException : PdfException
{
    /// <summary>Initialises a new instance with no message.</summary>
    public PdfEncryptionException()
    {
    }

    /// <summary>Initialises a new instance with the given message.</summary>
    /// <param name="message">A human-readable description of the failure.</param>
    public PdfEncryptionException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initialises a new instance with the given message and an inner exception
    /// that caused the failure.
    /// </summary>
    /// <param name="message">A human-readable description of the failure.</param>
    /// <param name="innerException">The exception that triggered this one.</param>
    public PdfEncryptionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
