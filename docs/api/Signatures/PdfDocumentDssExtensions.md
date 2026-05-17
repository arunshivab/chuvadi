# PdfDocumentDssExtensions

**Class** in `Chuvadi.Pdf.Signatures.Dss` (Signatures)

Extension methods on `PdfDocument` for accessing its Document Security Store.

```csharp
public static class PdfDocumentDssExtensions
```

## Methods

### `GetDocumentSecurityStore`

__static__

```csharp
static DocumentSecurityStore? GetDocumentSecurityStore(this PdfDocument document)
```

Reads the document's `/DSS` dictionary and decodes its arrays. Returns null when the document has no DSS.

---

_Source: [`src/Chuvadi.Pdf.Signatures/Dss/PdfDocumentDssExtensions.cs`](../../../src/Chuvadi.Pdf.Signatures/Dss/PdfDocumentDssExtensions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
