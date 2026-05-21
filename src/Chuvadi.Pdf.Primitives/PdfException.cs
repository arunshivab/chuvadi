// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  Chuvadi v2.0.0 reader library plan §4.3
// PHASE: Phase 2.0 — exception hierarchy

using System;

namespace Chuvadi.Pdf.Primitives;

/// <summary>
/// Abstract base class for every exception raised by the Chuvadi library.
/// </summary>
/// <remarks>
/// Callers that want to catch "any Chuvadi error" without caring about the
/// specific kind catch this type. Callers that want to react to a specific
/// failure mode (a parse error, a permission denial, an encryption fault)
/// catch one of the sealed subtypes:
/// <see cref="PdfParseException"/>, <see cref="PdfCorruptionException"/>,
/// <see cref="PdfEncryptionException"/>, <see cref="PdfPermissionException"/>.
///
/// Module-specific exceptions (e.g. <c>AnnotationException</c>,
/// <c>RenderingException</c>) also derive from this type so a single
/// <c>catch (PdfException)</c> covers them too.
///
/// This class is abstract on purpose: every throw site must categorise its
/// failure as one of the concrete subtypes. There is no general-purpose
/// "something went wrong with the PDF" exception — that signal is too weak
/// to act on.
/// </remarks>
public abstract class PdfException : Exception
{
    /// <summary>Initialises a new instance with no message.</summary>
    protected PdfException()
    {
    }

    /// <summary>Initialises a new instance with the given message.</summary>
    /// <param name="message">A human-readable description of the failure.</param>
    protected PdfException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initialises a new instance with the given message and an inner exception
    /// that caused the failure.
    /// </summary>
    /// <param name="message">A human-readable description of the failure.</param>
    /// <param name="innerException">The exception that triggered this one.</param>
    protected PdfException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
