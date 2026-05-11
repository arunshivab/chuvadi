# PdfPrimitiveType

**Enum** in `Chuvadi.Pdf.Primitives` (Primitives)

Identifies the concrete type of a `PdfPrimitive`.

```csharp
public enum PdfPrimitiveType
```

## Values

| Name | Description |
|---|---|
| `Null` | The null object. PDF 32000-1:2008 §7.3.9. |
| `Boolean` | A boolean value. PDF 32000-1:2008 §7.3.2. |
| `Integer` | A signed integer. PDF 32000-1:2008 §7.3.3. |
| `Real` | A floating-point real number. PDF 32000-1:2008 §7.3.3. |
| `Name` | A symbolic name. PDF 32000-1:2008 §7.3.5. |
| `String` | A byte string. PDF 32000-1:2008 §7.3.4. |
| `Array` | An ordered array of primitives. PDF 32000-1:2008 §7.3.6. |
| `Dictionary` | A keyed dictionary of primitives. PDF 32000-1:2008 §7.3.7. |
| `Stream` | A dictionary with an attached byte payload. PDF 32000-1:2008 §7.3.8. |
| `Reference` | An indirect reference to another object. PDF 32000-1:2008 §7.3.10. |

---

_Source: [`src/Chuvadi.Pdf.Primitives/PdfPrimitive.cs`](../../../src/Chuvadi.Pdf.Primitives/PdfPrimitive.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
