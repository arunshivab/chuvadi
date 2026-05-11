# RenderingException

**Class** in `Chuvadi.Pdf.Rendering` (Rendering)

Thrown when a PDF page cannot be rasterized due to an unsupported feature, invalid data, or internal rasterizer error.

```csharp
public sealed class RenderingException : Exception
```

## Constructors

### `RenderingException()`

Initialises a new `RenderingException` with no message.

### `RenderingException(string message)`

Initialises a new `RenderingException` with a message.

### `RenderingException(string message, Exception innerException)`

Initialises a new `RenderingException` with a message and an inner exception.

---

_Source: [`src/Chuvadi.Pdf.Rendering/RenderingException.cs`](../../../src/Chuvadi.Pdf.Rendering/RenderingException.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
