# ImageException

**Class** in `Chuvadi.Pdf.Images` (Images)

Thrown when an image cannot be decoded or encoded due to an invalid format, unsupported feature, or data corruption.

```csharp
public sealed class ImageException : Exception
```

## Constructors

### `ImageException()`

Initialises a new `ImageException` with no message.

### `ImageException(string message)`

Initialises a new `ImageException` with a message.

### `ImageException(string message, Exception innerException)`

Initialises a new `ImageException` with a message and an inner exception.

---

_Source: [`src/Chuvadi.Pdf.Images/ImageException.cs`](../../../src/Chuvadi.Pdf.Images/ImageException.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
