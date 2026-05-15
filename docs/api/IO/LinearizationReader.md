# LinearizationReader

**Class** in `Chuvadi.Pdf.IO` (IO)

Detects linearization and parses the parameter dictionary.

```csharp
public static class LinearizationReader
```

## Methods

### `TryRead`

__static__

```csharp
static LinearizationInfo? TryRead(PdfObjectStore store)
```

Attempts to read the linearization parameter dictionary from an object store.

**Parameters**

- `store` — The object store to scan.

**Returns:** The parsed `LinearizationInfo`, or null when the document is not linearized.

### `TryRead`

__static__

```csharp
static LinearizationInfo? TryRead(PdfObjectStore store, int maxObjectNumberToScan)
```

Attempts to read the linearization parameter dictionary, scanning object numbers up to the given limit. Use this overload when the document may have many objects ahead of the parameter dict.

---

_Source: [`src/Chuvadi.Pdf.IO/LinearizationReader.cs`](../../../src/Chuvadi.Pdf.IO/LinearizationReader.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
