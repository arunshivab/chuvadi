# PdfPermissions

**Enum** in `Chuvadi.Pdf.Primitives` (Primitives)

```csharp
public enum PdfPermissions
```

## Values

| Name | Description |
|---|---|
| `None` | No permissions granted. All restricted operations denied. |
| `Print` | Permission to print the document (low-resolution if `PrintHighQuality` is not also set). |
| `ModifyContents` | Permission to modify the document's contents other than by adding annotations or filling form fields. |
| `CopyContents` | Permission to copy or otherwise extract text and graphics from the document. Required for text selection and clipboard copy. |
| `ModifyAnnotations` | Permission to add, modify, or delete annotations and form fields (including signature fields). |
| `FillForms` | Permission to fill existing interactive form fields, including signature fields, without altering the form's structure. |
| `ExtractAccessibility` | Permission to extract text and graphics for accessibility purposes (screen readers, content reflow). |
| `Assemble` | Permission to assemble the document (insert, rotate, or delete pages and create bookmarks or thumbnails), even when `ModifyContents` is denied. |
| `PrintHighQuality` | Permission to print the document at high resolution. Without this flag but with `Print`, the document may be printed only at a degraded resolution. |
| `All` | All permissions granted. Used as the default when authoring a new encrypted document without explicit restrictions. |

---

_Source: [`src/Chuvadi.Pdf.Primitives/PdfPermissions.cs`](../../../src/Chuvadi.Pdf.Primitives/PdfPermissions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
