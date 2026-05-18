# SearchOptions

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Options controlling a search.

```csharp
public sealed class SearchOptions
```

## Properties

### `CaseSensitive`

```csharp
bool CaseSensitive
```

Match case-sensitively. Default false.

### `WholeWord`

```csharp
bool WholeWord
```

Require whole-word matches. Default false.

### `PageRangeStart`

```csharp
int? PageRangeStart
```

Optional inclusive start page (0-based). Default 0.

### `PageRangeEnd`

```csharp
int? PageRangeEnd
```

Optional exclusive end page (0-based). Default = page count.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/SearchTypes.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/SearchTypes.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
