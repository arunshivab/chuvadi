# PdfEncryptionException

**Class** in `Chuvadi.Pdf.Primitives` (Primitives)

Thrown when an encryption or decryption operation fails: wrong password, unsupported security handler revision, malformed encryption dictionary, missing required encryption metadata, or a cryptographic primitive that could not produce the expected output.

```csharp
public sealed class PdfEncryptionException : PdfException
```

## Remarks

In v2.0.0 this type replaces the v1.x `EncryptionException` from `Chuvadi.Pdf.Encryption`. Distinguished from `PdfPermissionException`: this signals an inability to decrypt or encrypt at all; that signals a successful decrypt followed by a permission-denied operation (e.g. content extraction blocked by the document's permission flags).

## Constructors

### `PdfEncryptionException()`

Initialises a new instance with no message.

### `PdfEncryptionException(string message) : base(message)`

Initialises a new instance with the given message.

**Parameters**

- `message` — A human-readable description of the failure.

### `PdfEncryptionException(string message, Exception innerException) : base(message, innerException)`

Initialises a new instance with the given message and an inner exception that caused the failure.

**Parameters**

- `message` — A human-readable description of the failure.
- `innerException` — The exception that triggered this one.

---

_Source: [`src/Chuvadi.Pdf.Primitives/PdfEncryptionException.cs`](../../../src/Chuvadi.Pdf.Primitives/PdfEncryptionException.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
