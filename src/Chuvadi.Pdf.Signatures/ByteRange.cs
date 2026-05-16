// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1 §12.8.1 — Signature dictionary /ByteRange
// PHASE: Phase 1.1.4 — PDF signature field reading

using System;

namespace Chuvadi.Pdf.Signatures;

/// <summary>
/// The /ByteRange of a PDF signature — two disjoint regions of the file that
/// together form the bytes the signature actually covers.
/// </summary>
/// <remarks>
/// PDF 32000-1 §12.8.1 defines /ByteRange as an array of four integers
/// <c>[a b c d]</c> meaning:
/// <list type="bullet">
///   <item><c>a</c> — the byte offset of the first range (almost always 0).</item>
///   <item><c>b</c> — the length of the first range.</item>
///   <item><c>c</c> — the byte offset of the second range.</item>
///   <item><c>d</c> — the length of the second range.</item>
/// </list>
/// The gap between the two ranges contains the hex-encoded /Contents value
/// of the signature itself — the signature cannot cover its own bytes.
/// </remarks>
public sealed class ByteRange
{
    /// <summary>Initialises a new ByteRange.</summary>
    /// <exception cref="ArgumentOutOfRangeException">If any value is negative or the ranges overlap.</exception>
    public ByteRange(long firstOffset, long firstLength, long secondOffset, long secondLength)
    {
        if (firstOffset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(firstOffset),
                "ByteRange first offset must be non-negative.");
        }
        if (firstLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(firstLength),
                "ByteRange first length must be non-negative.");
        }
        if (secondOffset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(secondOffset),
                "ByteRange second offset must be non-negative.");
        }
        if (secondLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(secondLength),
                "ByteRange second length must be non-negative.");
        }
        if (secondOffset < firstOffset + firstLength)
        {
            throw new ArgumentException(
                "ByteRange second offset must be at or after the end of the first range.");
        }

        FirstOffset = firstOffset;
        FirstLength = firstLength;
        SecondOffset = secondOffset;
        SecondLength = secondLength;
    }

    /// <summary>Offset of the first signed region (PDF spec: a).</summary>
    public long FirstOffset { get; }

    /// <summary>Length of the first signed region (PDF spec: b).</summary>
    public long FirstLength { get; }

    /// <summary>Offset of the second signed region (PDF spec: c).</summary>
    public long SecondOffset { get; }

    /// <summary>Length of the second signed region (PDF spec: d).</summary>
    public long SecondLength { get; }

    /// <summary>Total number of signed bytes (b + d).</summary>
    public long TotalLength => FirstLength + SecondLength;

    /// <summary>Offset of the gap (end of first range).</summary>
    public long GapOffset => FirstOffset + FirstLength;

    /// <summary>Length of the gap between the two ranges.</summary>
    public long GapLength => SecondOffset - GapOffset;

    /// <inheritdoc/>
    public override string ToString()
        => System.FormattableString.Invariant(
            $"[{FirstOffset} {FirstLength} {SecondOffset} {SecondLength}]");
}
