// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// PHASE: Phase 1.1.4 — PDF signature field reading
//
// Extension methods adding signature-reading APIs to PdfDocument without creating
// a cycle between Chuvadi.Pdf.Documents and Chuvadi.Pdf.Signatures. Calling code
// gets the friendly `document.Signatures()` and `document.GetSignedBytes(sig)`
// surface; the dependency arrow stays one-directional.

using System;
using System.Collections.Generic;
using Chuvadi.Pdf.Documents;

namespace Chuvadi.Pdf.Signatures;

/// <summary>
/// Signature-related extensions on <see cref="PdfDocument"/>.
/// </summary>
public static class PdfDocumentSignatureExtensions
{
    /// <summary>
    /// Returns the digital signatures found in <paramref name="document"/>'s
    /// AcroForm tree, in field order.
    /// </summary>
    /// <returns>An empty list when the document has no signatures.</returns>
    public static IReadOnlyList<PdfSignature> Signatures(this PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return SignatureReader.Read(document.Catalog, document.Objects);
    }

    /// <summary>
    /// Reads the bytes covered by <paramref name="signature"/>'s /ByteRange from
    /// the underlying file.
    /// </summary>
    /// <remarks>
    /// These are the bytes whose hash the signature actually covers. Pass them
    /// (or a hash of them) to the verification step alongside the decoded CMS
    /// SignedData.
    /// </remarks>
    public static byte[] GetSignedBytes(this PdfDocument document, PdfSignature signature)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(signature);

        ByteRange range = signature.ByteRange;
        long totalLong = range.TotalLength;
        if (totalLong > int.MaxValue)
        {
            throw new InvalidOperationException(
                "Signed byte range exceeds Int32.MaxValue; use CopySignedBytes instead.");
        }
        int total = (int)totalLong;

        byte[] first = document.Reader.ReadFileBytes(range.FirstOffset, (int)range.FirstLength);
        byte[] second = document.Reader.ReadFileBytes(range.SecondOffset, (int)range.SecondLength);

        byte[] result = new byte[total];
        Buffer.BlockCopy(first, 0, result, 0, first.Length);
        Buffer.BlockCopy(second, 0, result, first.Length, second.Length);
        return result;
    }

    /// <summary>
    /// Streams the bytes covered by <paramref name="signature"/>'s /ByteRange into
    /// <paramref name="destination"/>. Use for files larger than 2 GiB or when
    /// feeding a hash incrementally.
    /// </summary>
    public static void CopySignedBytes(this PdfDocument document, PdfSignature signature,
        System.IO.Stream destination)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(destination);

        ByteRange range = signature.ByteRange;
        document.Reader.CopyFileBytes(range.FirstOffset, range.FirstLength, destination);
        document.Reader.CopyFileBytes(range.SecondOffset, range.SecondLength, destination);
    }
}
