// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.7.2 — AcroForm dictionary (/NeedAppearances)
//        PDF 32000-1:2008 §12.7.3 — Field value (/V)
// PHASE: Phase 2 — Chuvadi.Pdf.Forms
// Updates AcroForm field values and writes a new PDF.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.IO;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Forms;

/// <summary>
/// Fills AcroForm field values in a PDF document and writes the result.
/// </summary>
/// <remarks>
/// For each fully-qualified field name in <c>values</c>, locates the field's
/// indirect object in the document, replaces its <c>/V</c> entry, and writes
/// a new PDF with the updated objects. Also sets
/// <c>/AcroForm/NeedAppearances=true</c> so that PDF viewers regenerate
/// the visible appearance streams from the new values.
///
/// PDF 32000-1:2008 §12.7.2 — Interactive form dictionary.
/// </remarks>
public static class FormFiller
{
    /// <summary>
    /// Fills the given field values and writes the resulting PDF to
    /// <paramref name="output"/>.
    /// </summary>
    /// <param name="output">The stream to write the filled PDF to.</param>
    /// <param name="document">The source document.</param>
    /// <param name="values">
    /// Map of fully-qualified field name → value. For text and choice fields the
    /// value is the string content. For checkboxes use "Yes" or "Off" (or the
    /// appearance state names defined by the field).
    /// </param>
    public static void Fill(
        Stream output,
        PdfDocument document,
        IReadOnlyDictionary<string, string> values)
    {
        if (output is null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (values is null)
        {
            throw new ArgumentNullException(nameof(values));
        }

        // Force-load all reachable objects so the document's store contains
        // the complete object graph before we iterate.
        PreloadAllObjects(document);

        IReadOnlyList<FormField> leafFields = FormReader.GetLeafFields(document);

        // Map field name → object ID for fast lookup
        Dictionary<string, PdfObjectId> fieldIds = new Dictionary<string, PdfObjectId>();

        foreach (FormField field in leafFields)
        {
            if (field.ObjectId.ObjectNumber != 0)
            {
                fieldIds[field.FullyQualifiedName] = field.ObjectId;
            }
        }

        // Track which field object IDs we're rewriting
        HashSet<int> rewrittenFieldNums = new HashSet<int>();
        List<PdfIndirectObject> updatedObjects = new List<PdfIndirectObject>();

        foreach (KeyValuePair<string, string> kv in values)
        {
            if (!fieldIds.TryGetValue(kv.Key, out PdfObjectId fieldId))
            {
                continue;
            }

            PdfIndirectObject? originalObj = FindObjectById(document, fieldId);

            if (originalObj is null || originalObj.Value is not PdfDictionary origDict)
            {
                continue;
            }

            PdfDictionary newDict = CopyDictionary(origDict);
            newDict.Set(PdfName.Intern("V"), MakeFieldValue(kv.Value));

            // For checkbox/radio buttons, also set /AS (appearance state) to match
            FormFieldType fieldType = ReadFieldType(origDict);

            if (fieldType == FormFieldType.Button)
            {
                newDict.Set(PdfName.Intern("AS"), PdfName.Intern(kv.Value));
            }

            updatedObjects.Add(new PdfIndirectObject(fieldId, newDict));
            rewrittenFieldNums.Add(fieldId.ObjectNumber);
        }

        // Update the AcroForm dictionary to set NeedAppearances = true
        UpdateAcroFormNeedAppearances(document, updatedObjects, rewrittenFieldNums);

        // Build full output object list
        List<PdfIndirectObject> allObjects = new List<PdfIndirectObject>();
        allObjects.AddRange(updatedObjects);

        foreach (PdfIndirectObject obj in document.Objects.Objects)
        {
            if (!rewrittenFieldNums.Contains(obj.Id.ObjectNumber))
            {
                allObjects.Add(obj);
            }
        }

        // Build trailer
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

        PdfWriter.Write(output, allObjects, trailer);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static void PreloadAllObjects(PdfDocument document)
    {
        HashSet<int> visited = new HashSet<int>();
        int pageCount = document.PageCount;

        for (int i = 0; i < pageCount; i++)
        {
            PdfPage page = document.Pages[i];
            Visit(document.Objects, page.Dictionary, visited);
        }

        // Also walk AcroForm tree if present
        PdfDictionary catalog = document.Catalog;

        if (catalog.TryGetValue(PdfName.Intern("AcroForm"), out PdfPrimitive? acroFormRef))
        {
            Visit(document.Objects, acroFormRef, visited);
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

    private static FormFieldType ReadFieldType(PdfDictionary fieldDict)
    {
        if (!fieldDict.TryGetValue(PdfName.Intern("FT"), out PdfPrimitive? ftPrim))
        {
            return FormFieldType.Unknown;
        }

        if (ftPrim is not PdfName ftName)
        {
            return FormFieldType.Unknown;
        }

        return ftName.Value switch
        {
            "Tx" => FormFieldType.Text,
            "Btn" => FormFieldType.Button,
            "Ch" => FormFieldType.Choice,
            "Sig" => FormFieldType.Signature,
            _ => FormFieldType.Unknown,
        };
    }

    private static PdfPrimitive MakeFieldValue(string value)
    {
        // For text/choice fields a literal string. For buttons we still write
        // a string here; AS gets written separately for button fields.
        return new PdfString(Encoding.Latin1.GetBytes(value));
    }

    private static PdfIndirectObject? FindObjectById(PdfDocument document, PdfObjectId id)
    {
        foreach (PdfIndirectObject obj in document.Objects.Objects)
        {
            if (obj.Id.ObjectNumber == id.ObjectNumber &&
                obj.Id.Generation == id.Generation)
            {
                return obj;
            }
        }

        return null;
    }

    private static void UpdateAcroFormNeedAppearances(
        PdfDocument document,
        List<PdfIndirectObject> updatedObjects,
        HashSet<int> rewrittenNums)
    {
        PdfDictionary catalog = document.Catalog;

        if (!catalog.TryGetValue(PdfName.Intern("AcroForm"), out PdfPrimitive? acroFormPrim))
        {
            return;
        }

        // AcroForm may be inline in catalog or an indirect reference.
        if (acroFormPrim is PdfReference acroRef)
        {
            PdfIndirectObject? acroObj = FindObjectById(document, acroRef.ObjectId);

            if (acroObj is null || acroObj.Value is not PdfDictionary origDict)
            {
                return;
            }

            PdfDictionary newDict = CopyDictionary(origDict);
            newDict.Set(PdfName.Intern("NeedAppearances"), true);
            updatedObjects.Add(new PdfIndirectObject(acroRef.ObjectId, newDict));
            rewrittenNums.Add(acroRef.ObjectId.ObjectNumber);
            return;
        }

        if (acroFormPrim is PdfDictionary inlineDict)
        {
            // AcroForm is inline in catalog — must update catalog itself
            PdfDictionary newAcro = CopyDictionary(inlineDict);
            newAcro.Set(PdfName.Intern("NeedAppearances"), true);

            // Find catalog object and update it
            foreach (PdfIndirectObject obj in document.Objects.Objects)
            {
                if (obj.Value is not PdfDictionary cdict)
                {
                    continue;
                }

                if (cdict.TryGetValue(PdfName.Type, out PdfPrimitive? t) &&
                    t is PdfName tn && tn.Value == "Catalog")
                {
                    PdfDictionary newCatalog = CopyDictionary(cdict);
                    newCatalog.Set(PdfName.Intern("AcroForm"), newAcro);
                    updatedObjects.Add(new PdfIndirectObject(obj.Id, newCatalog));
                    rewrittenNums.Add(obj.Id.ObjectNumber);
                    return;
                }
            }
        }
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
}
