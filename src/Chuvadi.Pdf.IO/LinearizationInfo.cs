// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  ISO 32000-1:2008 §F.2 — Linearization parameter dictionary
// PHASE: Phase 1.1.6 — Chuvadi.Pdf.IO linearization
//
// Parsed view of the /Linearized parameter dictionary that sits near the
// head of a linearized PDF and tells viewers how to fetch page 1 quickly.

using System.Collections.Generic;


namespace Chuvadi.Pdf.IO;

/// <summary>
/// Parsed view of a PDF's linearization parameter dictionary.
/// </summary>
/// <remarks>
/// Per ISO 32000-1:2008 §F.2 a linearized PDF's first object (or one very close
/// to the head) is a dictionary with /Linearized set to 1.0 and other entries
/// describing the layout. This class exposes those entries in a strongly-typed
/// form. Returned by <see cref="LinearizationReader.TryRead(Chuvadi.Pdf.Objects.PdfObjectStore)"/> when the document
/// is linearized.
/// </remarks>
public sealed class LinearizationInfo
{
    /// <summary>Initialises a new <see cref="LinearizationInfo"/>.</summary>
    public LinearizationInfo(
        double linearizedVersion,
        long fileLength,
        IReadOnlyList<long> hintOffsetsAndLengths,
        int firstPageObjectNumber,
        long endOfFirstPage,
        int pageCount,
        long mainXrefOffset)
    {
        LinearizedVersion = linearizedVersion;
        FileLength = fileLength;
        HintOffsetsAndLengths = hintOffsetsAndLengths;
        FirstPageObjectNumber = firstPageObjectNumber;
        EndOfFirstPage = endOfFirstPage;
        PageCount = pageCount;
        MainXrefOffset = mainXrefOffset;
    }

    /// <summary>/Linearized — version number, always 1.0 in practice.</summary>
    public double LinearizedVersion { get; }

    /// <summary>/L — total file length in bytes.</summary>
    public long FileLength { get; }

    /// <summary>
    /// /H — flattened array of [offset, length] pairs locating each hint stream.
    /// Always 2 or 4 entries: primary hint stream alone (2) or primary + shared (4).
    /// </summary>
    public IReadOnlyList<long> HintOffsetsAndLengths { get; }

    /// <summary>/O — object number of the page dictionary for page 1.</summary>
    public int FirstPageObjectNumber { get; }

    /// <summary>/E — byte offset of the end of page 1's first-page section.</summary>
    public long EndOfFirstPage { get; }

    /// <summary>/N — number of pages in the document.</summary>
    public int PageCount { get; }

    /// <summary>/T — byte offset of the main (end-of-file) xref table.</summary>
    public long MainXrefOffset { get; }
}
