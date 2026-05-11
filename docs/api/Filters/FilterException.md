# FilterException

**Class** in `Chuvadi.Pdf.Filters` (Filters)

Thrown when a PDF stream filter encounters data it cannot decode or encode.

```csharp
public sealed class FilterException : Exception
```

## Remarks

Covers malformed compressed data, invalid encoding sequences, checksum failures, and truncated streams.

## Constructors

### `FilterException()`

Initialises a new `FilterException` with no message.

### `FilterException(string message)`

Initialises a new `FilterException` with a message.

### `FilterException(string message, Exception innerException)`

Initialises a new `FilterException` with a message and an inner exception.

### `FilterException(string filterName, string message)`

Initialises a new `FilterException` with a message and the name of the filter that failed.

**Parameters**

- `filterName` — The PDF filter name, e.g. "FlateDecode".
- `message` — A description of the error.

### `FilterException(string filterName, string message, Exception innerException)`

Initialises a new `FilterException` with a filter name, message, and inner exception.

## Properties

### `FilterName`

```csharp
string? FilterName
```

Gets the name of the filter that failed, if known. Returns null when the filter name was not provided.

---

_Source: [`src/Chuvadi.Pdf.Filters/FilterException.cs`](../../../src/Chuvadi.Pdf.Filters/FilterException.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
