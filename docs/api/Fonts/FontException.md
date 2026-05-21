# FontException

**Class** in `Chuvadi.Pdf.Fonts` (Fonts)

Thrown when a font dictionary cannot be parsed or a character code cannot be mapped to a Unicode codepoint.

```csharp
public sealed class FontException : PdfException
```

## Constructors

### `FontException()`

Initialises a new `FontException` with no message.

### `FontException(string message)`

Initialises a new `FontException` with a message.

### `FontException(string message, Exception innerException)`

Initialises a new `FontException` with a message and an inner exception.

---

_Source: [`src/Chuvadi.Pdf.Fonts/FontException.cs`](../../../src/Chuvadi.Pdf.Fonts/FontException.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
