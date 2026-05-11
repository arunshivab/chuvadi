// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §7.7.3 — Page tree
// PHASE: Phase 1 — Chuvadi.Pdf.Documents
// Lazy, index-based access to a PDF document's pages.

using System;
using System.Collections;
using System.Collections.Generic;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Documents;

/// <summary>
/// Provides lazy, random-access to the pages of a PDF document.
/// </summary>
/// <remarks>
/// The PDF page tree is a balanced tree of /Pages nodes with /Page leaves.
/// <see cref="PdfPageCollection"/> traverses this tree on demand, caching
/// resolved pages after the first access.
///
/// <see cref="Count"/> is read directly from the root /Pages node's /Count
/// entry — it does not require traversing the tree.
///
/// PDF 32000-1:2008 §7.7.3 — Page tree.
/// </remarks>
public sealed class PdfPageCollection : IReadOnlyList<PdfPage>
{
    private readonly PdfDictionary _pagesRoot;
    private readonly IPdfObjectResolver _resolver;
    private readonly PdfPage?[] _cache;

    internal PdfPageCollection(PdfDictionary pagesRoot, IPdfObjectResolver resolver)
    {
        _pagesRoot = pagesRoot ?? throw new ArgumentNullException(nameof(pagesRoot));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));

        int count = _pagesRoot.GetInteger(PdfName.Count, 0);
        _cache = new PdfPage?[count];
    }

    /// <summary>Gets the total number of pages in the document.</summary>
    public int Count => _cache.Length;

    /// <summary>
    /// Gets the page at the given zero-based index.
    /// Pages are loaded lazily from the page tree.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="index"/> is outside [0, Count).
    /// </exception>
    public PdfPage this[int index]
    {
        get
        {
            if (index < 0 || index >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index),
                    $"Page index {index} is out of range [0, {Count}).");
            }

            if (_cache[index] is null)
            {
                _cache[index] = FindPage(_pagesRoot, index, 0);
            }

            return _cache[index]!;
        }
    }

    /// <inheritdoc/>
    public IEnumerator<PdfPage> GetEnumerator()
    {
        for (int i = 0; i < Count; i++)
        {
            yield return this[i];
        }
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // ── Private: page tree traversal ──────────────────────────────────────

    /// <summary>
    /// Recursively finds the page at document index (<paramref name="targetIndex"/>)
    /// within the subtree rooted at <paramref name="node"/>.
    /// <paramref name="offset"/> is the number of pages before this subtree.
    /// PDF 32000-1:2008 §7.7.3 — Page tree nodes.
    /// </summary>
    private PdfPage FindPage(PdfDictionary node, int targetIndex, int offset)
    {
        PdfArray kids = node.GetArray(PdfName.Kids) ??
            throw new PdfDocumentException(
                "Page tree node is missing the required /Kids array.");

        int localOffset = offset;

        for (int i = 0; i < kids.Count; i++)
        {
            PdfPrimitive kidRef = kids.GetAs<PdfPrimitive>(i) ?? PdfNull.Value;
            PdfPrimitive kid = _resolver.Resolve(kidRef);

            if (kid is not PdfDictionary kidDict)
            {
                throw new PdfDocumentException(
                    $"Page tree /Kids[{i}] is not a dictionary.");
            }

            PdfName? kidType = kidDict.Type;

            if (kidType == PdfName.Page)
            {
                if (localOffset == targetIndex)
                {
                    return new PdfPage(kidDict, _resolver, targetIndex);
                }

                localOffset++;
            }
            else if (kidType == PdfName.Pages)
            {
                int subtreeCount = kidDict.GetInteger(PdfName.Count, 0);

                if (targetIndex < localOffset + subtreeCount)
                {
                    return FindPage(kidDict, targetIndex, localOffset);
                }

                localOffset += subtreeCount;
            }
            else
            {
                // Unknown node type — skip.
                localOffset++;
            }
        }

        throw new PdfDocumentException(
            $"Page index {targetIndex} not found in page tree (offset={offset}).");
    }
}
