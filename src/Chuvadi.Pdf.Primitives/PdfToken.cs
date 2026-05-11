// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Text;

namespace Chuvadi.Pdf.Primitives;

/// <summary>
/// A lightweight token produced by <see cref="PdfTokenizer"/>.
/// </summary>
/// <remarks>
/// A token is a (type, raw-bytes, byte-offset) triple. The raw bytes
/// are the exact bytes from the PDF stream that form the token, including
/// delimiters such as parentheses for literal strings and angle brackets
/// for hex strings — but NOT including the leading solidus for names.
///
/// PdfToken is a readonly struct to keep the tokenizer allocation-free.
/// Callers that need to retain a token beyond the next Read() call must
/// copy the bytes from <see cref="RawBytes"/>.
///
/// PDF 32000-1:2008 §7.2 — Lexical conventions.
/// </remarks>
public readonly struct PdfToken : IEquatable<PdfToken>
{
    /// <summary>
    /// A sentinel token representing the end of the stream.
    /// </summary>
    public static readonly PdfToken EndOfStream = new(
        PdfTokenType.EndOfStream,
        [],
        -1);

    /// <summary>
    /// Initialises a new <see cref="PdfToken"/>.
    /// </summary>
    /// <param name="type">The token type.</param>
    /// <param name="rawBytes">
    /// The raw bytes of the token as they appear in the PDF stream.
    /// The array is owned by the tokenizer — copy if you need to keep it.
    /// </param>
    /// <param name="byteOffset">
    /// The byte offset in the stream at which this token begins.
    /// </param>
    public PdfToken(PdfTokenType type, byte[] rawBytes, long byteOffset)
    {
        Type = type;
        RawBytes = rawBytes;
        ByteOffset = byteOffset;
    }

    /// <summary>Gets the type of this token.</summary>
    public PdfTokenType Type { get; }

    /// <summary>
    /// Gets the raw bytes of this token as they appear in the PDF stream.
    /// </summary>
    public byte[] RawBytes { get; }

    /// <summary>
    /// Gets the byte offset in the underlying stream at which this token begins.
    /// Used for error reporting and xref validation.
    /// </summary>
    public long ByteOffset { get; }

    /// <summary>
    /// Returns the raw bytes decoded as a Latin-1 string.
    /// Useful for keyword tokens and for debugging.
    /// </summary>
    public string RawText => Encoding.Latin1.GetString(RawBytes);

    /// <summary>
    /// Returns true if this token has type <see cref="PdfTokenType.EndOfStream"/>.
    /// </summary>
    public bool IsEndOfStream => Type == PdfTokenType.EndOfStream;

    /// <summary>
    /// Returns true if this token has type <see cref="PdfTokenType.Integer"/>
    /// or <see cref="PdfTokenType.Real"/>.
    /// </summary>
    public bool IsNumeric =>
        Type == PdfTokenType.Integer || Type == PdfTokenType.Real;

    // ── Equality ──────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public bool Equals(PdfToken other) =>
        Type == other.Type &&
        ByteOffset == other.ByteOffset &&
        RawBytes.AsSpan().SequenceEqual(other.RawBytes);

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is PdfToken other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Type, ByteOffset);

    /// <summary>Value equality.</summary>
    public static bool operator ==(PdfToken left, PdfToken right) => left.Equals(right);

    /// <summary>Value inequality.</summary>
    public static bool operator !=(PdfToken left, PdfToken right) => !left.Equals(right);

    // ── Diagnostics ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns a human-readable description of this token, suitable for
    /// error messages and debug output.
    /// </summary>
    public override string ToString()
    {
        string preview = RawBytes.Length <= 32
            ? Encoding.Latin1.GetString(RawBytes)
            : Encoding.Latin1.GetString(RawBytes, 0, 32) + "...";

        return $"[{Type} @{ByteOffset}: {preview}]";
    }
}
