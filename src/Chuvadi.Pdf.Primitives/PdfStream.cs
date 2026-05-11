// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0

using System;

namespace Chuvadi.Pdf.Primitives;

/// <summary>
/// Represents a PDF stream object — a dictionary plus a binary byte payload.
/// </summary>
/// <remarks>
/// A stream consists of a dictionary (which must contain a <c>/Length</c> entry)
/// followed by the keywords <c>stream</c> and <c>endstream</c> surrounding
/// the raw byte data.
///
/// Streams may be compressed with one or more filters specified in the
/// <c>/Filter</c> entry. The raw bytes stored here are the bytes as they
/// appear in the file — i.e., possibly still compressed.
/// Filter application and decompression happen in <c>Chuvadi.Pdf.Filters</c>.
///
/// PDF 32000-1:2008 §7.3.8 — Stream objects.
/// </remarks>
public sealed class PdfStream : PdfPrimitive
{
    /// <summary>
    /// Initialises a new <see cref="PdfStream"/> with the given dictionary
    /// and raw (possibly compressed) bytes.
    /// </summary>
    /// <param name="dictionary">
    /// The stream dictionary. Must not be null. A reference is kept —
    /// the dictionary is not copied.
    /// </param>
    /// <param name="rawBytes">
    /// The raw byte content as it appears in the PDF file,
    /// before any filter decoding. A copy is taken.
    /// </param>
    public PdfStream(PdfDictionary dictionary, ReadOnlySpan<byte> rawBytes)
    {
        Dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
        RawBytes = rawBytes.ToArray();
    }

    /// <summary>
    /// Initialises a new <see cref="PdfStream"/> with the given dictionary
    /// and a pre-allocated byte array (zero-copy).
    /// </summary>
    /// <param name="dictionary">The stream dictionary.</param>
    /// <param name="rawBytes">
    /// The raw bytes. The array is owned by this stream — do not modify it
    /// after passing it here.
    /// </param>
    internal PdfStream(PdfDictionary dictionary, byte[] rawBytes)
    {
        Dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
        RawBytes = rawBytes;
    }

    /// <summary>Gets the stream's dictionary.</summary>
    public PdfDictionary Dictionary { get; }

    /// <summary>
    /// Gets the raw byte content as it appears in the PDF file,
    /// before any filter decoding.
    /// </summary>
    public byte[] RawBytes { get; }

    /// <summary>
    /// Gets the number of raw bytes in this stream.
    /// Equivalent to <c>RawBytes.Length</c>.
    /// </summary>
    public int RawLength => RawBytes.Length;

    /// <summary>
    /// Gets the <c>/Filter</c> entry from the stream dictionary,
    /// which identifies the compression filter(s) applied to the data.
    /// Returns <c>null</c> if the stream is uncompressed.
    /// </summary>
    /// <remarks>
    /// The filter may be a single <see cref="PdfName"/> or a
    /// <see cref="PdfArray"/> of names for chained filters.
    /// </remarks>
    public PdfPrimitive? Filter => Dictionary.GetAs<PdfPrimitive>(PdfName.Filter);

    /// <summary>
    /// Returns true when the stream has at least one filter applied.
    /// </summary>
    public bool IsFiltered => Filter is not null;

    /// <summary>
    /// Gets a read-only span over the raw bytes.
    /// Preferred over <see cref="RawBytes"/> for processing — avoids
    /// pinning the array and communicates read-only intent.
    /// </summary>
    public ReadOnlySpan<byte> RawSpan => RawBytes;

    /// <inheritdoc/>
    public override PdfPrimitiveType PrimitiveType => PdfPrimitiveType.Stream;

    /// <summary>
    /// Returns the stream dictionary followed by <c>stream ... endstream</c>.
    /// The raw bytes are represented as a byte-count summary, not their content,
    /// since streams may be large and binary.
    /// </summary>
    public override string ToString() =>
        $"{Dictionary}\nstream\n[{RawLength} bytes]\nendstream";
}
