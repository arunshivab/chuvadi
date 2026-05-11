# FormFieldType

**Enum** in `Chuvadi.Pdf.Forms` (Forms)

Type of an AcroForm field. PDF 32000-1:2008 §12.7.4 — Field types.

```csharp
public enum FormFieldType
```

## Values

| Name | Description |
|---|---|
| `Unknown` | Unknown or non-terminal field (parent of other fields). |
| `Text` | Text input field (/FT /Tx). PDF 32000-1:2008 §12.7.4.3. |
| `Button` | Button field (/FT /Btn). Subtypes: pushbutton, checkbox, radio. §12.7.4.2. |
| `Choice` | Choice field (/FT /Ch). List box or combo box. §12.7.4.4. |
| `Signature` | Signature field (/FT /Sig). §12.7.4.5. |

---

_Source: [`src/Chuvadi.Pdf.Forms/FormFieldType.cs`](../../../src/Chuvadi.Pdf.Forms/FormFieldType.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
