// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.5 — Chuvadi.Pdf.Encryption

using System;

namespace Chuvadi.Pdf.Encryption;

/// <summary>Thrown when a PDF encryption or decryption operation fails.</summary>
public sealed class EncryptionException : Exception
{
    /// <summary>Initialises a new <see cref="EncryptionException"/>.</summary>
    public EncryptionException()
        : base("A PDF encryption error occurred.")
    {
    }

    /// <summary>Initialises a new <see cref="EncryptionException"/> with a message.</summary>
    public EncryptionException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="EncryptionException"/> with a message and an inner exception.
    /// </summary>
    public EncryptionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
