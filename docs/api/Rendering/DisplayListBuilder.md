# DisplayListBuilder

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Builds a `PageDisplayList` by walking a page's content stream and translating each PDF operator to a `RenderOp`.

```csharp
public static class DisplayListBuilder
```

## Methods

### `Build`

__static__

```csharp
static PageDisplayList Build(PdfDocument document, int pageIndex)
```

Builds a display list for the given page.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/DisplayListBuilder.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/DisplayListBuilder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
