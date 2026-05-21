# PdfException

**Class** in `Chuvadi.Pdf.Primitives` (Primitives)

Abstract base class for every exception raised by the Chuvadi library.

```csharp
public abstract class PdfException : Exception
```

## Remarks

Callers that want to catch "any Chuvadi error" without caring about the specific kind catch this type. Callers that want to react to a specific failure mode (a parse error, a permission denial, an encryption fault) catch one of the sealed subtypes: `PdfParseException`, `PdfCorruptionException`, `PdfEncryptionException`, `PdfPermissionException`. Module-specific exceptions (e.g. `AnnotationException`, `RenderingException`) also derive from this type so a single `catch (PdfException)` covers them too. This class is abstract on purpose: every throw site must categorise its failure as one of the concrete subtypes. There is no general-purpose "something went wrong with the PDF" exception — that signal is too weak to act on.

---

_Source: [`src/Chuvadi.Pdf.Primitives/PdfException.cs`](../../../src/Chuvadi.Pdf.Primitives/PdfException.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
