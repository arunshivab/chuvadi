// Copyright 2025 Chuvadi Contributors
// SPDX-License-Identifier: Apache-2.0
// SPEC:  PDF 32000-1:2008 §12.7.2 — Interactive form dictionary
//        PDF 32000-1:2008 §12.7.3 — Field dictionaries
// PHASE: Phase 2 — Chuvadi.Pdf.Forms
// Reads AcroForm field tree from a PDF document.

using System.Collections.Generic;
using System.Text;
using Chuvadi.Pdf.Documents;
using Chuvadi.Pdf.Objects;
using Chuvadi.Pdf.Primitives;

namespace Chuvadi.Pdf.Forms;

/// <summary>
/// Reads AcroForm interactive form fields from a PDF document.
/// </summary>
/// <remarks>
/// Walks the form's <c>/Fields</c> array from <c>/Catalog/AcroForm</c>,
/// recursively resolving the field tree. Each leaf field carries its
/// fully-qualified name (ancestor partial names joined by periods),
/// type, current value, and indirect-object ID for later updating.
///
/// PDF 32000-1:2008 §12.7.2 — Interactive form dictionary.
/// </remarks>
public static class FormReader
{
    /// <summary>
    /// Returns all top-level form fields in the document. Empty when the
    /// document has no AcroForm.
    /// </summary>
    public static IReadOnlyList<FormField> GetFields(PdfDocument document)
    {
        if (document is null)
        {
            throw new System.ArgumentNullException(nameof(document));
        }

        PdfDictionary catalog = document.Catalog;
        PdfObjectStore store = document.Objects;

        if (!catalog.TryGetValue(PdfName.Intern("AcroForm"), out PdfPrimitive? acroFormRef))
        {
            return new List<FormField>();
        }

        PdfDictionary? acroForm = store.ResolveAs<PdfDictionary>(acroFormRef ?? PdfNull.Value);

        if (acroForm is null)
        {
            return new List<FormField>();
        }

        if (!acroForm.TryGetValue(PdfName.Intern("Fields"), out PdfPrimitive? fieldsPrim))
        {
            return new List<FormField>();
        }

        PdfArray? fieldsArray = store.ResolveAs<PdfArray>(fieldsPrim ?? PdfNull.Value);

        if (fieldsArray is null)
        {
            return new List<FormField>();
        }

        List<FormField> result = new List<FormField>();

        for (int i = 0; i < fieldsArray.Count; i++)
        {
            FormField? field = ReadField(fieldsArray[i], store, parentName: string.Empty);

            if (field is not null)
            {
                result.Add(field);
            }
        }

        return result;
    }

    /// <summary>
    /// Returns a flat list of every leaf field in the document, in tree order.
    /// Useful for callers that just want every fillable input.
    /// </summary>
    public static IReadOnlyList<FormField> GetLeafFields(PdfDocument document)
    {
        List<FormField> result = new List<FormField>();
        FlattenLeaves(GetFields(document), result);
        return result;
    }

    private static void FlattenLeaves(IReadOnlyList<FormField> fields, List<FormField> result)
    {
        foreach (FormField f in fields)
        {
            if (f.IsLeaf)
            {
                result.Add(f);
            }
            else
            {
                FlattenLeaves(f.Children, result);
            }
        }
    }

    // ── Recursive field reader ────────────────────────────────────────────

    private static FormField? ReadField(
        PdfPrimitive fieldRef, PdfObjectStore store, string parentName)
    {
        PdfObjectId objId = ExtractObjectId(fieldRef);
        PdfDictionary? fieldDict = store.ResolveAs<PdfDictionary>(fieldRef);

        if (fieldDict is null)
        {
            return null;
        }

        // Partial name
        string partialName = ExtractPartialName(fieldDict);
        string fullName = string.IsNullOrEmpty(parentName)
            ? partialName
            : (string.IsNullOrEmpty(partialName) ? parentName : parentName + "." + partialName);

        // Field type (may be inherited from parent — Phase 2 reads it locally)
        FormFieldType type = ExtractType(fieldDict);

        // Value
        string? value = ExtractValue(fieldDict, store);

        // Children
        List<FormField> children = new List<FormField>();

        if (fieldDict.TryGetValue(PdfName.Kids, out PdfPrimitive? kidsPrim))
        {
            PdfArray? kids = store.ResolveAs<PdfArray>(kidsPrim ?? PdfNull.Value);

            if (kids is not null)
            {
                for (int i = 0; i < kids.Count; i++)
                {
                    // A Kid may be a widget annotation rather than a child field.
                    // We treat any Kid with a /T entry as a child field; widgets
                    // (no /T) are absorbed into the parent and not enumerated.
                    PdfDictionary? kidDict = store.ResolveAs<PdfDictionary>(kids[i]);

                    if (kidDict is null)
                    {
                        continue;
                    }

                    if (kidDict.TryGetValue(PdfName.Intern("T"), out _))
                    {
                        FormField? child = ReadField(kids[i], store, fullName);

                        if (child is not null)
                        {
                            children.Add(child);
                        }
                    }
                }
            }
        }

        return new FormField(fullName, type, value, objId, children);
    }

    // ── Field property extraction ─────────────────────────────────────────

    private static string ExtractPartialName(PdfDictionary fieldDict)
    {
        if (!fieldDict.TryGetValue(PdfName.Intern("T"), out PdfPrimitive? tPrim))
        {
            return string.Empty;
        }

        if (tPrim is PdfString s)
        {
            return Encoding.Latin1.GetString(s.Bytes);
        }

        return string.Empty;
    }

    private static FormFieldType ExtractType(PdfDictionary fieldDict)
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

    private static string? ExtractValue(PdfDictionary fieldDict, PdfObjectStore store)
    {
        if (!fieldDict.TryGetValue(PdfName.Intern("V"), out PdfPrimitive? vPrim))
        {
            return null;
        }

        PdfPrimitive resolved = store.Resolve(vPrim ?? PdfNull.Value);

        if (resolved is PdfString s)
        {
            return Encoding.Latin1.GetString(s.Bytes);
        }

        if (resolved is PdfName n)
        {
            return n.Value;
        }

        if (resolved is PdfArray arr)
        {
            // Multi-select choice values
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < arr.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                PdfPrimitive item = store.Resolve(arr[i]);

                if (item is PdfString itemStr)
                {
                    sb.Append(Encoding.Latin1.GetString(itemStr.Bytes));
                }
                else if (item is PdfName itemName)
                {
                    sb.Append(itemName.Value);
                }
            }

            return sb.ToString();
        }

        return null;
    }

    private static PdfObjectId ExtractObjectId(PdfPrimitive primitive)
    {
        if (primitive is PdfReference reference)
        {
            return reference.ObjectId;
        }

        return new PdfObjectId(0, 0);
    }
}
