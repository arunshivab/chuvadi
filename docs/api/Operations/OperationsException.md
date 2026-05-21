# OperationsException

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Thrown when a PDF page operation (merge, split, delete, rotate, reorder) cannot be completed due to an invalid argument or document structure.

```csharp
public sealed class OperationsException : PdfException
```

## Constructors

### `OperationsException()`

Initialises a new `OperationsException` with no message.

### `OperationsException(string message)`

Initialises a new `OperationsException` with a message.

### `OperationsException(string message, Exception innerException)`

Initialises a new `OperationsException` with a message and an inner exception.

---

_Source: [`src/Chuvadi.Pdf.Operations/OperationsException.cs`](../../../src/Chuvadi.Pdf.Operations/OperationsException.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
