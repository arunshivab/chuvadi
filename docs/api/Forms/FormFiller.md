# FormFiller

**Class** in `Chuvadi.Pdf.Forms` (Forms)

Fills AcroForm field values in a PDF document and writes the result.

```csharp
public static class FormFiller
```

## Remarks

For each fully-qualified field name in `values`, locates the field's indirect object in the document, replaces its `/V` entry, and writes a new PDF with the updated objects. Also sets `/AcroForm/NeedAppearances=true` so that PDF viewers regenerate the visible appearance streams from the new values. PDF 32000-1:2008 §12.7.2 — Interactive form dictionary.

---

_Source: [`src/Chuvadi.Pdf.Forms/FormFiller.cs`](../../../src/Chuvadi.Pdf.Forms/FormFiller.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
