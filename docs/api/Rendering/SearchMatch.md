# SearchMatch

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

A search match against the logical text of a page.

```csharp
public sealed class SearchMatch
```

## Constructors

### `SearchMatch(int pageNumber, int characterOffset, int length, IReadOnlyList<Rect> boundingBoxes)`

Initialises a search match.

## Properties

### `PageNumber`

```csharp
int PageNumber
```

Zero-based page index.

### `CharacterOffset`

```csharp
int CharacterOffset
```

Character offset within the page's logical concatenated text.

### `Length`

```csharp
int Length
```

Match length in characters.

### `BoundingBoxes`

```csharp
IReadOnlyList<Rect> BoundingBoxes
```

Bounding boxes (multiple if the match spans more than one text run).

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/SearchTypes.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/SearchTypes.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
