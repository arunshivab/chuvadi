# PdfPermissionException

**Class** in `Chuvadi.Pdf.Primitives` (Primitives)

Thrown when an operation is blocked because the document's permission flags forbid it: extracting text from a copy-restricted document, modifying a write-protected document, assembling a no-assembly document.

```csharp
public sealed class PdfPermissionException : PdfException
```

## Remarks

The `Required` property tells the caller which permission was missing, so they can either prompt for the owner password (which bypasses permission checks) or surface a meaningful error to the end user. Distinguished from `PdfEncryptionException`: that signals an inability to decrypt at all; this signals a successful decrypt followed by a permission-denied operation. This exception type is new in v2.0.0 — v1.x had no equivalent and silently performed restricted operations.

## Constructors

### `PdfPermissionException()`

Initialises a new instance with no message and no required permission.

### `PdfPermissionException(string message) : base(message)`

Initialises a new instance with the given message and no required permission.

**Parameters**

- `message` — A human-readable description of the failure.

### `PdfPermissionException(string message, Exception innerException) : base(message, innerException)`

Initialises a new instance with the given message and an inner exception.

**Parameters**

- `message` — A human-readable description of the failure.
- `innerException` — The exception that triggered this one.

### `PdfPermissionException(string message, PdfPermissions required) : base(message)`

Initialises a new instance with the given message and the permission that was required but not granted.

**Parameters**

- `message` — A human-readable description of the failure.
- `required` — The permission flag the caller lacked.

## Properties

### `Required`

```csharp
PdfPermissions Required
```

The permission flag that was required but not granted by the document. May be `PdfPermissions.None` if no specific permission was identified at throw time.

---

_Source: [`src/Chuvadi.Pdf.Primitives/PdfPermissionException.cs`](../../../src/Chuvadi.Pdf.Primitives/PdfPermissionException.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
