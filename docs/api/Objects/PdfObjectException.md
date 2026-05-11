# PdfObjectException

**Class** in `Chuvadi.Pdf.Objects` (Objects)

Thrown when the PDF object model encounters an invalid structure, such as a malformed xref table or an unresolvable object reference.

```csharp
public sealed class PdfObjectException : Exception
```

## Constructors

### `PdfObjectException()`

Initialises a new `PdfObjectException` with no message.

### `PdfObjectException(string message)`

Initialises a new `PdfObjectException` with a message.

### `PdfObjectException(string message, Exception innerException)`

Initialises a new `PdfObjectException` with a message and an inner exception.

---

_Source: [`src/Chuvadi.Pdf.Objects/PdfObjectException.cs`](../../../src/Chuvadi.Pdf.Objects/PdfObjectException.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
