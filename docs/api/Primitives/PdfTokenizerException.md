# PdfTokenizerException

**Class** in `Chuvadi.Pdf.Primitives` (Primitives)

Thrown when the `PdfTokenizer` encounters bytes that cannot form a valid PDF token.

```csharp
public sealed class PdfTokenizerException : Exception
```

## Constructors

### `PdfTokenizerException()`

Initialises a new `PdfTokenizerException` with no message.

### `PdfTokenizerException(string message)`

Initialises a new `PdfTokenizerException` with a message.

### `PdfTokenizerException(string message, Exception innerException)`

Initialises a new `PdfTokenizerException` with a message and an inner exception.

### `PdfTokenizerException(string message, long byteOffset)`

Initialises a new `PdfTokenizerException` with a message and the byte offset at which the error was detected.

**Parameters**

- `message` — A description of the error.
- `byteOffset` — The byte offset in the stream where the error was detected.

### `PdfTokenizerException(string message, long byteOffset, Exception innerException)`

Initialises a new `PdfTokenizerException` with a message, byte offset, and an inner exception.

## Properties

### `ByteOffset`

```csharp
long ByteOffset
```

Gets the byte offset in the stream where the error was detected. Returns -1 when the offset is not available.

---

_Source: [`src/Chuvadi.Pdf.Primitives/PdfTokenizerException.cs`](../../../src/Chuvadi.Pdf.Primitives/PdfTokenizerException.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
