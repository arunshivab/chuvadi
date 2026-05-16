// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — Chuvadi.Cryptography ASN.1 foundation

using System;

namespace Chuvadi.Cryptography.Asn1;

/// <summary>
/// Raised when an ASN.1 decoder encounters malformed or non-conforming input.
/// </summary>
/// <remarks>
/// The decoder never throws NullReferenceException, IndexOutOfRangeException,
/// or InvalidCastException on malformed input; every defect surface as an
/// Asn1Exception with a message describing the violation and (where
/// applicable) the byte offset at which it was detected.
/// </remarks>
public sealed class Asn1Exception : Exception
{
	/// <summary>Initialises a new <see cref="Asn1Exception"/> with no message.</summary>
    public Asn1Exception() { }
	
    /// <summary>Initialises a new <see cref="Asn1Exception"/>.</summary>
    public Asn1Exception(string message) : base(message) { }

    /// <summary>Initialises a new <see cref="Asn1Exception"/> with an inner exception.</summary>
    public Asn1Exception(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>Initialises a new <see cref="Asn1Exception"/> annotated with a byte offset.</summary>
    public Asn1Exception(string message, long byteOffset)
        : base(FormatWithOffset(message, byteOffset))
    {
        ByteOffset = byteOffset;
    }

    /// <summary>The byte offset at which the defect was detected, or -1 if unknown.</summary>
    public long ByteOffset { get; } = -1;

    private static string FormatWithOffset(string message, long offset)
        => $"{message} (at byte offset {offset}).";
}
