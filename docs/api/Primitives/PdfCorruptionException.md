# PdfCorruptionException

**Class** in `Chuvadi.Pdf.Primitives` (Primitives)

Thrown when a PDF parses cleanly at the byte level but is semantically inconsistent: cyclic `/Kids` page tree references, missing required catalog entries, unresolvable indirect references, pages claiming a count that does not match their actual children, and other structural integrity failures.

```csharp
public sealed class PdfCorruptionException : PdfException
```

## Remarks

Distinguished from `PdfParseException` — that signals byte-level syntax errors. This signals a document where the bytes are fine but the document they describe is broken. In v2.0.0 this type replaces the v1.x `PdfDocumentException` and the semantic-integrity subset of `PdfObjectException`.

## Constructors

### `PdfCorruptionException()`

Initialises a new instance with no message.

### `PdfCorruptionException(string message) : base(message)`

Initialises a new instance with the given message.

**Parameters**

- `message` — A human-readable description of the failure.

### `PdfCorruptionException(string message, Exception innerException) : base(message, innerException)`

Initialises a new instance with the given message and an inner exception that caused the failure.

**Parameters**

- `message` — A human-readable description of the failure.
- `innerException` — The exception that triggered this one.

---

_Source: [`src/Chuvadi.Pdf.Primitives/PdfCorruptionException.cs`](../../../src/Chuvadi.Pdf.Primitives/PdfCorruptionException.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
