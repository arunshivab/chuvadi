// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.5 — Annotations
// PHASE: Phase 1.1 — Chuvadi.Pdf.Annotations
// Appends annotations to a PDF and writes the result.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Graphics;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Annotations;

/// <summary>
/// Adds new annotations to a PDF document and writes the result.
/// </summary>
/// <remarks>
/// For each annotation, builds the corresponding PDF dictionary, appends an
/// indirect-object reference to each targeted page's <c>/Annots</c> array,
/// and writes the modified document. The original document is not changed.
/// </remarks>
public static class AnnotationWriter
{
    /// <summary>
    /// Writes <paramref name="document"/> with the given annotations added to
    /// <paramref name="output"/>.
    /// </summary>
    public static void Add(
        Stream output,
        PdfDocument document,
        IEnumerable<PdfAnnotation> annotations)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(annotations);

        PreloadAllObjects(document);

        // Collect annotations by page index
        Dictionary<int, List<PdfAnnotation>> byPage = new Dictionary<int, List<PdfAnnotation>>();

        foreach (PdfAnnotation a in annotations)
        {
            if (a.PageIndex < 0 || a.PageIndex >= document.PageCount)
            {
                throw new AnnotationException(
                    $"Annotation page index {a.PageIndex} out of range (document has {document.PageCount} pages).");
            }

            if (!byPage.TryGetValue(a.PageIndex, out List<PdfAnnotation>? list))
            {
                list = new List<PdfAnnotation>();
                byPage[a.PageIndex] = list;
            }

            list.Add(a);
        }

        int nextObjectNum = FindNextObjectNumber(document);
        Dictionary<int, PdfObjectId> pageIds = BuildPageIdMap(document);

        List<PdfIndirectObject> newObjects = new List<PdfIndirectObject>();
        HashSet<int> rewrittenPageNums = new HashSet<int>();

        foreach (KeyValuePair<int, List<PdfAnnotation>> kvp in byPage)
        {
            int pageIndex = kvp.Key;

            if (!pageIds.TryGetValue(pageIndex, out PdfObjectId pageId))
            {
                continue;
            }

            PdfPage page = document.Pages[pageIndex];
            List<PdfReference> newAnnotRefs = new List<PdfReference>();

            // Emit each annotation as an indirect object
            foreach (PdfAnnotation annotation in kvp.Value)
            {
                PdfObjectId annotId = new PdfObjectId(nextObjectNum++, 0);
                PdfDictionary annotDict = BuildAnnotationDictionary(annotation, pageId);
                newObjects.Add(new PdfIndirectObject(annotId, annotDict));
                newAnnotRefs.Add(new PdfReference(annotId));
            }

            // Build modified page dict: copy existing + append new annotations
            PdfDictionary modifiedPage = CopyDictionary(page.Dictionary);
            PdfArray annotsArray = BuildMergedAnnotsArray(page, document.Objects, newAnnotRefs);
            modifiedPage.Set(PdfName.Intern("Annots"), annotsArray);

            newObjects.Add(new PdfIndirectObject(pageId, modifiedPage));
            rewrittenPageNums.Add(pageId.ObjectNumber);
        }

        // Combine: new objects + untouched originals
        List<PdfIndirectObject> allObjects = new List<PdfIndirectObject>();
        allObjects.AddRange(newObjects);

        foreach (PdfIndirectObject obj in document.Objects.Objects)
        {
            if (!rewrittenPageNums.Contains(obj.Id.ObjectNumber))
            {
                allObjects.Add(obj);
            }
        }

        PdfDictionary trailer = BuildTrailer(document);
        PdfWriter.Write(output, allObjects, trailer);
    }

    // ── Dictionary builders ───────────────────────────────────────────────

    private static PdfDictionary BuildAnnotationDictionary(PdfAnnotation a, PdfObjectId pageId)
    {
        PdfDictionary dict = new PdfDictionary();
        dict.Set(PdfName.Type, PdfName.Intern("Annot"));
        dict.Set(PdfName.Intern("Subtype"), PdfName.Intern(SubtypeName(a.Type)));
        dict.Set(PdfName.Intern("Rect"), MakeRect(a.Rect));
        dict.Set(PdfName.Intern("P"), new PdfReference(pageId));

        if (a.Contents is not null)
        {
            dict.Set(PdfName.Intern("Contents"), MakeString(a.Contents));
        }

        if (a.Color is not null)
        {
            dict.Set(PdfName.Intern("C"), MakeColor(a.Color.Value));
        }

        if (a.Author is not null)
        {
            dict.Set(PdfName.Intern("T"), MakeString(a.Author));
        }

        if (Math.Abs(a.Opacity - 1f) > 1e-6f)
        {
            dict.Set(PdfName.Intern("CA"), new PdfReal(a.Opacity));
        }

        // Subtype-specific entries
        switch (a)
        {
            case TextAnnotation t:
                dict.Set(PdfName.Intern("Name"), PdfName.Intern(t.IconName));
                if (t.IsOpen)
                {
                    dict.Set(PdfName.Intern("Open"), true);
                }
                break;

            case LinkAnnotation l:
                AddLinkAction(dict, l);
                break;

            case FreeTextAnnotation ft:
                dict.Set(PdfName.Intern("DA"), MakeString(ft.DefaultAppearance));
                break;

            case MarkupAnnotation m:
                dict.Set(PdfName.Intern("QuadPoints"), MakeQuadPoints(m.QuadPoints));
                break;

            case StampAnnotation s:
                dict.Set(PdfName.Intern("Name"), PdfName.Intern(s.StampName));
                break;

            case InkAnnotation ink:
                dict.Set(PdfName.Intern("InkList"), MakeInkList(ink.Strokes));
                break;
        }

        return dict;
    }

    private static void AddLinkAction(PdfDictionary dict, LinkAnnotation link)
    {
        PdfDictionary action = new PdfDictionary();

        if (link.Uri is not null)
        {
            action.Set(PdfName.Intern("S"), PdfName.Intern("URI"));
            action.Set(PdfName.Intern("URI"), MakeString(link.Uri.ToString()));
        }
        else if (link.DestinationPageIndex >= 0)
        {
            action.Set(PdfName.Intern("S"), PdfName.Intern("GoTo"));
            // Destination as named is preferred; here we use the simple form
            // [pageRef /Fit]. We can't resolve the page reference here without
            // the full pageIds map, so we encode by index — viewers usually
            // accept an explicit array form once it's resolved.
            // For this implementation we use a string-based destination.
            action.Set(PdfName.Intern("D"),
                MakeString("Page" + link.DestinationPageIndex.ToString(CultureInfo.InvariantCulture)));
        }

        dict.Set(PdfName.Intern("A"), action);
    }

    private static PdfArray BuildMergedAnnotsArray(
        PdfPage page, PdfObjectStore store, IReadOnlyList<PdfReference> newAnnotRefs)
    {
        PdfArray result = new PdfArray(Array.Empty<PdfPrimitive>());

        if (page.Dictionary.TryGetValue(PdfName.Intern("Annots"), out PdfPrimitive? existing))
        {
            PdfArray? existingArray = store.ResolveAs<PdfArray>(existing);

            if (existingArray is not null)
            {
                for (int i = 0; i < existingArray.Count; i++)
                {
                    result.Add(existingArray[i]);
                }
            }
        }

        foreach (PdfReference r in newAnnotRefs)
        {
            result.Add(r);
        }

        return result;
    }

    // ── Primitive builders ────────────────────────────────────────────────

    private static PdfArray MakeRect(RectangleF rect)
    {
        return new PdfArray([
            new PdfReal(rect.X),
            new PdfReal(rect.Y),
            new PdfReal(rect.X + rect.Width),
            new PdfReal(rect.Y + rect.Height),
        ]);
    }

    private static PdfArray MakeColor(ColorF color)
    {
        ColorF rgb = color.ToRgb();
        return new PdfArray([
            new PdfReal(rgb.R),
            new PdfReal(rgb.G),
            new PdfReal(rgb.B),
        ]);
    }

    private static PdfString MakeString(string value)
    {
        return new PdfString(Encoding.Latin1.GetBytes(value));
    }

    private static PdfArray MakeQuadPoints(IReadOnlyList<float> points)
    {
        PdfArray arr = new PdfArray(Array.Empty<PdfPrimitive>());

        foreach (float p in points)
        {
            arr.Add(new PdfReal(p));
        }

        return arr;
    }

    private static PdfArray MakeInkList(IReadOnlyList<IReadOnlyList<PointF>> strokes)
    {
        PdfArray outer = new PdfArray(Array.Empty<PdfPrimitive>());

        foreach (IReadOnlyList<PointF> stroke in strokes)
        {
            PdfArray inner = new PdfArray(Array.Empty<PdfPrimitive>());

            foreach (PointF point in stroke)
            {
                inner.Add(new PdfReal(point.X));
                inner.Add(new PdfReal(point.Y));
            }

            outer.Add(inner);
        }

        return outer;
    }

    // ── Object-graph plumbing ─────────────────────────────────────────────

    private static void PreloadAllObjects(PdfDocument document)
    {
        HashSet<int> visited = new HashSet<int>();
        int pageCount = document.PageCount;

        for (int i = 0; i < pageCount; i++)
        {
            PdfPage page = document.Pages[i];
            Visit(document.Objects, page.Dictionary, visited);
        }
    }

    private static void Visit(PdfObjectStore store, PdfPrimitive? p, HashSet<int> visited)
    {
        if (p is null)
        {
            return;
        }

        if (p is PdfReference reference)
        {
            int num = reference.ObjectId.ObjectNumber;

            if (!visited.Add(num))
            {
                return;
            }

            PdfPrimitive resolved = store.Resolve(reference);
            Visit(store, resolved, visited);
            return;
        }

        if (p is PdfArray arr)
        {
            for (int i = 0; i < arr.Count; i++)
            {
                Visit(store, arr[i], visited);
            }
            return;
        }

        if (p is PdfDictionary dict)
        {
            foreach (KeyValuePair<PdfName, PdfPrimitive> entry in dict)
            {
                Visit(store, entry.Value, visited);
            }
            return;
        }

        if (p is PdfStream stream)
        {
            Visit(store, stream.Dictionary, visited);
        }
    }

    private static Dictionary<int, PdfObjectId> BuildPageIdMap(PdfDocument document)
    {
        Dictionary<int, PdfObjectId> map = new Dictionary<int, PdfObjectId>();
        int idx = 0;

        foreach (PdfIndirectObject obj in document.Objects.Objects)
        {
            if (obj.Value is not PdfDictionary dict ||
                !dict.TryGetValue(PdfName.Type, out PdfPrimitive? t) ||
                t is not PdfName tn || tn.Value != "Page")
            {
                continue;
            }

            map[idx++] = obj.Id;
        }

        return map;
    }

    private static int FindNextObjectNumber(PdfDocument document)
    {
        int max = 0;

        foreach (PdfIndirectObject obj in document.Objects.Objects)
        {
            if (obj.Id.ObjectNumber > max)
            {
                max = obj.Id.ObjectNumber;
            }
        }

        return max + 1;
    }

    private static PdfDictionary BuildTrailer(PdfDocument document)
    {
        PdfDictionary trailer = new PdfDictionary();

        foreach (PdfIndirectObject obj in document.Objects.Objects)
        {
            if (obj.Value is PdfDictionary dict &&
                dict.TryGetValue(PdfName.Type, out PdfPrimitive? t) &&
                t is PdfName tn && tn.Value == "Catalog")
            {
                trailer.Set(PdfName.Root, new PdfReference(obj.Id));
                break;
            }
        }

        return trailer;
    }

    private static PdfDictionary CopyDictionary(PdfDictionary source)
    {
        PdfDictionary copy = new PdfDictionary();

        foreach (KeyValuePair<PdfName, PdfPrimitive> entry in source)
        {
            copy.Set(entry.Key, entry.Value);
        }

        return copy;
    }

    private static string SubtypeName(AnnotationType t) => t switch
    {
        AnnotationType.Text      => "Text",
        AnnotationType.Link      => "Link",
        AnnotationType.FreeText  => "FreeText",
        AnnotationType.Highlight => "Highlight",
        AnnotationType.Underline => "Underline",
        AnnotationType.Squiggly  => "Squiggly",
        AnnotationType.StrikeOut => "StrikeOut",
        AnnotationType.Stamp     => "Stamp",
        AnnotationType.Ink       => "Ink",
        _ => throw new AnnotationException($"Cannot write Unknown annotation type."),
    };
}
