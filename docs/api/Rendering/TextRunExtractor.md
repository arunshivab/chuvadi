# TextRunExtractor

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Walks a `PageDisplayList` and produces a sequence of `TextRun`s in reading order.

```csharp
public static class TextRunExtractor
```

## Remarks

Reading-order detection in v1: cluster runs into baseline-grouped lines, sort lines top-to-bottom, sort runs within a line by x-position. Adequate for single-column layouts; multi-column flows are a Phase 2.2 concern.

## Methods

### `Extract`

__static__

```csharp
static IReadOnlyList<TextRun> Extract(PageDisplayList list)
```

Extracts text runs from a page's display list.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/TextRunExtractor.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/TextRunExtractor.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
