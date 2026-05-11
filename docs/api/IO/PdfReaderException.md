# PdfReaderException

**Class** in `Chuvadi.Pdf.IO` (IO)

Thrown when `PdfReader` encounters a PDF file structure it cannot parse or recover from.

```csharp
public sealed class PdfReaderException : Exception
```

## Constructors

### `PdfReaderException()`

Initialises a new `PdfReaderException` with no message.

### `PdfReaderException(string message)`

Initialises a new `PdfReaderException` with a message.

### `PdfReaderException(string message, Exception innerException)`

Initialises a new `PdfReaderException` with a message and an inner exception.

---

_Source: [`src/Chuvadi.Pdf.IO/PdfReaderException.cs`](../../../src/Chuvadi.Pdf.IO/PdfReaderException.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
