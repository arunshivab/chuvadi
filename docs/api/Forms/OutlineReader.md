# OutlineReader

**Class** in `Chuvadi.Pdf.Forms` (Forms)

Reads the document outline (bookmark) tree from a PDF.

```csharp
public static class OutlineReader
```

## Remarks

Walks from `/Catalog/Outlines/First` through each item's `/Next` and `/First` pointers, building a tree of `OutlineItem` values. Destinations are resolved to zero-based page indices where possible. PDF 32000-1:2008 §12.3.3 — Document outline.

## Methods

### `GetOutlines`

__static__

```csharp
static IReadOnlyList<OutlineItem> GetOutlines(PdfDocument document)
```

Returns the top-level outline items. Empty when the document has no bookmarks.

---

_Source: [`src/Chuvadi.Pdf.Forms/OutlineReader.cs`](../../../src/Chuvadi.Pdf.Forms/OutlineReader.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
