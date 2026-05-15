# OptionalContentReader

**Class** in `Chuvadi.Pdf.Documents` (Documents)

Reads optional content groups (layers) from a PDF document.

```csharp
public static class OptionalContentReader
```

## Methods

### `GetGroups`

__static__

```csharp
static IReadOnlyList<OptionalContentGroup> GetGroups(PdfDocument document)
```

Returns every Optional Content Group declared in the document's /OCProperties/OCGs array, with visibility resolved from the default configuration (/OCProperties/D).

**Parameters**

- `document` — The document to read.

**Returns:** Zero or more layers in declaration order.

### `GetDefaultConfigurationName`

__static__

```csharp
static string? GetDefaultConfigurationName(PdfDocument document)
```

Returns the human-readable name of the default OCG configuration (/OCProperties/D/Name), or null when none is set.

---

_Source: [`src/Chuvadi.Pdf.Documents/OptionalContentReader.cs`](../../../src/Chuvadi.Pdf.Documents/OptionalContentReader.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
