# FormReader

**Class** in `Chuvadi.Pdf.Forms` (Forms)

Reads AcroForm interactive form fields from a PDF document.

```csharp
public static class FormReader
```

## Remarks

Walks the form's `/Fields` array from `/Catalog/AcroForm`, recursively resolving the field tree. Each leaf field carries its fully-qualified name (ancestor partial names joined by periods), type, current value, and indirect-object ID for later updating. PDF 32000-1:2008 §12.7.2 — Interactive form dictionary.

## Methods

### `GetFields`

__static__

```csharp
static IReadOnlyList<FormField> GetFields(PdfDocument document)
```

Returns all top-level form fields in the document. Empty when the document has no AcroForm.

### `GetLeafFields`

__static__

```csharp
static IReadOnlyList<FormField> GetLeafFields(PdfDocument document)
```

Returns a flat list of every leaf field in the document, in tree order. Useful for callers that just want every fillable input.

---

_Source: [`src/Chuvadi.Pdf.Forms/FormReader.cs`](../../../src/Chuvadi.Pdf.Forms/FormReader.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
