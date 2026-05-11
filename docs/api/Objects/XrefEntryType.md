# XrefEntryType

**Enum** in `Chuvadi.Pdf.Objects` (Objects)

Identifies the type of a `XrefEntry`. PDF 32000-1:2008 §7.5.4, Table 17.

```csharp
public enum XrefEntryType
```

## Values

| Name | Description |
|---|---|
| `Free` | The object is free. The entry's value is the object number of the next free object in the free list, and its generation is the generation number to use if the object is reused. |
| `InUse` | The object is in use. The entry's value is the byte offset of the object definition in the PDF file. |
| `Compressed` | The object is compressed inside an object stream (PDF 1.5+). The entry's value is the object number of the containing object stream, and its index is the position within that stream. PDF 32000-1:2008 §7.5.8.2, Table 18, type 2. |

---

_Source: [`src/Chuvadi.Pdf.Objects/XrefEntry.cs`](../../../src/Chuvadi.Pdf.Objects/XrefEntry.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
