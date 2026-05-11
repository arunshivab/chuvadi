# PdfDocumentException

**Class** in `Chuvadi.Pdf.Documents` (Documents)

Thrown when the PDF document model encounters an invalid or unsupported structure, such as a malformed page tree or a missing required entry.

```csharp
public sealed class PdfDocumentException : Exception
```

## Constructors

### `PdfDocumentException()`

Initialises a new `PdfDocumentException` with no message.

### `PdfDocumentException(string message)`

Initialises a new `PdfDocumentException` with a message.

### `PdfDocumentException(string message, Exception innerException)`

Initialises a new `PdfDocumentException` with a message and an inner exception.

---

_Source: [`src/Chuvadi.Pdf.Documents/PdfDocumentException.cs`](../../../src/Chuvadi.Pdf.Documents/PdfDocumentException.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
