# IPdfObjectResolver

**Interface** in `Chuvadi.Pdf.Objects` (Objects)

Resolves PDF indirect object references to their primitive values.

```csharp
public interface IPdfObjectResolver
```

## Remarks

This interface decouples the object model layer from the IO layer. The `PdfObjectStore` implements it for in-memory graphs. The `PdfReader` in `Chuvadi.Pdf.IO` implements it for file-backed lazy resolution. Callers that receive a `PdfPrimitive` and want to follow any indirect references should call `Resolve` to unwrap `PdfReference` instances. PDF 32000-1:2008 §7.3.10 — Indirect objects.

---

_Source: [`src/Chuvadi.Pdf.Objects/IPdfObjectResolver.cs`](../../../src/Chuvadi.Pdf.Objects/IPdfObjectResolver.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
