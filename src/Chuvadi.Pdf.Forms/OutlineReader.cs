// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.3.3 — Document outline
//        PDF 32000-1:2008 §12.3.2 — Destinations
// PHASE: Phase 2 — Chuvadi.Pdf.Forms
// Reads the document outline (bookmark) tree.

using System.Collections.Generic;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Forms;

/// <summary>
/// Reads the document outline (bookmark) tree from a PDF.
/// </summary>
/// <remarks>
/// Walks from <c>/Catalog/Outlines/First</c> through each item's
/// <c>/Next</c> and <c>/First</c> pointers, building a tree of
/// <see cref="OutlineItem"/> values. Destinations are resolved to
/// zero-based page indices where possible.
///
/// PDF 32000-1:2008 §12.3.3 — Document outline.
/// </remarks>
public static class OutlineReader
{
    /// <summary>
    /// Returns the top-level outline items. Empty when the document has
    /// no bookmarks.
    /// </summary>
    public static IReadOnlyList<OutlineItem> GetOutlines(PdfDocument document)
    {
        if (document is null)
        {
            throw new System.ArgumentNullException(nameof(document));
        }

        PdfDictionary catalog = document.Catalog;
        PdfObjectStore store = document.Objects;

        if (!catalog.TryGetValue(PdfName.Outlines, out PdfPrimitive? outlinesPrim))
        {
            return new List<OutlineItem>();
        }

        PdfDictionary? outlinesRoot = store.ResolveAs<PdfDictionary>(outlinesPrim ?? PdfNull.Value);

        if (outlinesRoot is null)
        {
            return new List<OutlineItem>();
        }

        // Build page reference → index map for destination resolution
        Dictionary<int, int> pageRefToIndex = BuildPageReferenceMap(document);

        List<OutlineItem> items = new List<OutlineItem>();
        HashSet<int> visited = new HashSet<int>();

        if (outlinesRoot.TryGetValue(PdfName.Intern("First"), out PdfPrimitive? firstPrim))
        {
            WalkSiblings(firstPrim, store, pageRefToIndex, visited, items);
        }

        return items;
    }

    // ── Outline tree traversal ────────────────────────────────────────────

    private static void WalkSiblings(
        PdfPrimitive startPrim,
        PdfObjectStore store,
        Dictionary<int, int> pageMap,
        HashSet<int> visited,
        List<OutlineItem> result)
    {
        PdfPrimitive? current = startPrim;

        while (current is not null)
        {
            int objNum = current is PdfReference r ? r.ObjectId.ObjectNumber : -1;

            if (objNum > 0 && !visited.Add(objNum))
            {
                break; // cycle detected
            }

            PdfDictionary? dict = store.ResolveAs<PdfDictionary>(current);

            if (dict is null)
            {
                break;
            }

            string title = ExtractTitle(dict);
            int pageIndex = ResolveDestinationPageIndex(dict, store, pageMap);

            // Recurse into children
            List<OutlineItem> children = new List<OutlineItem>();

            if (dict.TryGetValue(PdfName.Intern("First"), out PdfPrimitive? firstChild))
            {
                WalkSiblings(firstChild, store, pageMap, visited, children);
            }

            result.Add(new OutlineItem(title, pageIndex, children));

            if (!dict.TryGetValue(PdfName.Intern("Next"), out PdfPrimitive? nextPrim))
            {
                break;
            }

            current = nextPrim;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string ExtractTitle(PdfDictionary outlineDict)
    {
        if (!outlineDict.TryGetValue(PdfName.Intern("Title"), out PdfPrimitive? titlePrim))
        {
            return string.Empty;
        }

        if (titlePrim is PdfString s)
        {
            return Encoding.Latin1.GetString(s.Bytes);
        }

        return string.Empty;
    }

    private static int ResolveDestinationPageIndex(
        PdfDictionary outlineDict, PdfObjectStore store, Dictionary<int, int> pageMap)
    {
        // /Dest is an explicit destination: [pageRef /XYZ left top zoom]
        // /A is an action dictionary that may contain a GoTo destination
        PdfPrimitive? destPrim = null;

        if (outlineDict.TryGetValue(PdfName.Intern("Dest"), out PdfPrimitive? d))
        {
            destPrim = d;
        }
        else if (outlineDict.TryGetValue(PdfName.Intern("A"), out PdfPrimitive? actionPrim))
        {
            PdfDictionary? actionDict = store.ResolveAs<PdfDictionary>(actionPrim ?? PdfNull.Value);

            if (actionDict is not null &&
                actionDict.TryGetValue(PdfName.Intern("D"), out PdfPrimitive? actionDest))
            {
                destPrim = actionDest;
            }
        }

        if (destPrim is null)
        {
            return -1;
        }

        PdfPrimitive resolved = store.Resolve(destPrim);

        // Destination is an array: [pageRef /XYZ ...]
        if (resolved is PdfArray destArray && destArray.Count > 0)
        {
            if (destArray[0] is PdfReference pageRef &&
                pageMap.TryGetValue(pageRef.ObjectId.ObjectNumber, out int idx))
            {
                return idx;
            }
        }

        return -1;
    }

    private static Dictionary<int, int> BuildPageReferenceMap(PdfDocument document)
    {
        Dictionary<int, int> map = new Dictionary<int, int>();
        int pageCount = document.PageCount;

        // Find the indirect object IDs for each page by walking the page tree
        // We use the fact that PdfObjectStore.Objects contains all loaded Page objects
        // (PreloadAllObjects must have been called previously, otherwise pages may be missing)
        int idx = 0;

        foreach (PdfIndirectObject obj in document.Objects.Objects)
        {
            if (obj.Value is not PdfDictionary dict)
            {
                continue;
            }

            if (!dict.TryGetValue(PdfName.Type, out PdfPrimitive? typePrim))
            {
                continue;
            }

            if (typePrim is PdfName typeName && typeName.Value == "Page")
            {
                map[obj.Id.ObjectNumber] = idx++;

                if (idx >= pageCount)
                {
                    break;
                }
            }
        }

        return map;
    }
}
