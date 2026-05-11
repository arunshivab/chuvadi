// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.5.4 — Cross-reference table
//        PDF 32000-1:2008 §7.5.8 — Cross-reference streams
// PHASE: Phase 1 — Chuvadi.Pdf.Objects
// One entry in a PDF cross-reference table or cross-reference stream.

using System;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Objects;

/// <summary>
/// Identifies the type of a <see cref="XrefEntry"/>.
/// PDF 32000-1:2008 §7.5.4, Table 17.
/// </summary>
public enum XrefEntryType
{
    /// <summary>
    /// The object is free. The entry's value is the object number of the
    /// next free object in the free list, and its generation is the
    /// generation number to use if the object is reused.
    /// </summary>
    Free = 0,

    /// <summary>
    /// The object is in use. The entry's value is the byte offset of the
    /// object definition in the PDF file.
    /// </summary>
    InUse = 1,

    /// <summary>
    /// The object is compressed inside an object stream (PDF 1.5+).
    /// The entry's value is the object number of the containing object stream,
    /// and its index is the position within that stream.
    /// PDF 32000-1:2008 §7.5.8.2, Table 18, type 2.
    /// </summary>
    Compressed = 2,
}

/// <summary>
/// Represents one entry in a PDF cross-reference table or stream.
/// </summary>
/// <remarks>
/// In the classic xref table (PDF 32000-1:2008 §7.5.4), each entry is
/// 20 bytes: a 10-digit byte offset, a 5-digit generation number, and
/// a keyword ('n' for in-use, 'f' for free).
///
/// In cross-reference streams (PDF 1.5+, §7.5.8), entries are encoded
/// as binary integers with configurable field widths.
///
/// This struct unifies both formats.
/// </remarks>
public readonly struct XrefEntry : IEquatable<XrefEntry>
{
    /// <summary>
    /// Initialises a new in-use <see cref="XrefEntry"/> pointing to a
    /// byte offset in the PDF file.
    /// </summary>
    public XrefEntry(int objectNumber, int generation, long byteOffset)
    {
        ObjectNumber = objectNumber;
        Generation = generation;
        Type = XrefEntryType.InUse;
        ByteOffset = byteOffset;
        StreamObjectNumber = 0;
        IndexInStream = 0;
    }

    /// <summary>
    /// Initialises a new free <see cref="XrefEntry"/>.
    /// </summary>
    /// <param name="objectNumber">This object's number.</param>
    /// <param name="generation">Generation to use if the object is reused.</param>
    /// <param name="nextFreeObjectNumber">
    /// Object number of the next free object (linked list of free objects).
    /// </param>
    public static XrefEntry Free(int objectNumber, int generation, int nextFreeObjectNumber)
    {
        return new XrefEntry(
            objectNumber,
            generation,
            XrefEntryType.Free,
            byteOffset: nextFreeObjectNumber,
            streamObjectNumber: 0,
            indexInStream: 0);
    }

    /// <summary>
    /// Initialises a new compressed <see cref="XrefEntry"/> for an object
    /// stored inside an object stream (PDF 1.5+).
    /// </summary>
    /// <param name="objectNumber">This object's number.</param>
    /// <param name="streamObjectNumber">
    /// Object number of the object stream containing this object.
    /// </param>
    /// <param name="indexInStream">
    /// Zero-based index of this object within the object stream.
    /// </param>
    public static XrefEntry Compressed(
        int objectNumber,
        int streamObjectNumber,
        int indexInStream)
    {
        return new XrefEntry(
            objectNumber,
            generation: 0,
            XrefEntryType.Compressed,
            byteOffset: 0,
            streamObjectNumber: streamObjectNumber,
            indexInStream: indexInStream);
    }

    private XrefEntry(
        int objectNumber,
        int generation,
        XrefEntryType type,
        long byteOffset,
        int streamObjectNumber,
        int indexInStream)
    {
        ObjectNumber = objectNumber;
        Generation = generation;
        Type = type;
        ByteOffset = byteOffset;
        StreamObjectNumber = streamObjectNumber;
        IndexInStream = indexInStream;
    }

    /// <summary>Gets the object number this entry describes.</summary>
    public int ObjectNumber { get; }

    /// <summary>Gets the generation number.</summary>
    public int Generation { get; }

    /// <summary>Gets the type of this xref entry.</summary>
    public XrefEntryType Type { get; }

    /// <summary>
    /// For <see cref="XrefEntryType.InUse"/>: the byte offset of the
    /// object in the PDF file.
    /// For <see cref="XrefEntryType.Free"/>: the object number of the
    /// next free object.
    /// </summary>
    public long ByteOffset { get; }

    /// <summary>
    /// For <see cref="XrefEntryType.Compressed"/>: the object number of
    /// the containing object stream.
    /// </summary>
    public int StreamObjectNumber { get; }

    /// <summary>
    /// For <see cref="XrefEntryType.Compressed"/>: the zero-based index
    /// of this object within the object stream.
    /// </summary>
    public int IndexInStream { get; }

    /// <summary>Gets the <see cref="PdfObjectId"/> for this entry.</summary>
    public PdfObjectId ObjectId => new PdfObjectId(ObjectNumber, Generation);

    /// <summary>Returns true when this is an in-use entry.</summary>
    public bool IsInUse => Type == XrefEntryType.InUse;

    /// <summary>Returns true when this is a free entry.</summary>
    public bool IsFree => Type == XrefEntryType.Free;

    /// <summary>Returns true when this is a compressed entry.</summary>
    public bool IsCompressed => Type == XrefEntryType.Compressed;

    /// <inheritdoc/>
    public bool Equals(XrefEntry other) =>
        ObjectNumber == other.ObjectNumber &&
        Generation == other.Generation &&
        Type == other.Type &&
        ByteOffset == other.ByteOffset &&
        StreamObjectNumber == other.StreamObjectNumber &&
        IndexInStream == other.IndexInStream;

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is XrefEntry other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(ObjectNumber, Generation, Type, ByteOffset);

    /// <summary>Value equality.</summary>
    public static bool operator ==(XrefEntry left, XrefEntry right) => left.Equals(right);

    /// <summary>Value inequality.</summary>
    public static bool operator !=(XrefEntry left, XrefEntry right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() =>
        Type switch
        {
            XrefEntryType.InUse =>
                $"{ObjectNumber} {Generation} obj @{ByteOffset}",
            XrefEntryType.Free =>
                $"{ObjectNumber} {Generation} free (next={ByteOffset})",
            XrefEntryType.Compressed =>
                $"{ObjectNumber} 0 compressed (stream={StreamObjectNumber}, idx={IndexInStream})",
            _ => $"{ObjectNumber} {Generation} unknown"
        };
}
