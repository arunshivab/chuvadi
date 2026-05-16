# PageRasterizer

**Class** in `Chuvadi.Pdf.Rendering` (Rendering)

Rasterizes a PDF page to a `PixelBuffer`.

```csharp
public sealed class PageRasterizer
```

## Remarks

`PageRasterizer` is the top-level public API for page rendering. It wires together all layers: 
 
- Decodes the page's content streams through their filter chains. 
- Tokenizes and interprets PDF graphics operators. 
- Fills paths using `ScanlineRasterizer`. 
- Strokes paths using `StrokeExpander`. 
- Renders text glyphs via `FontRenderer`. 
- Composites image XObjects from the page's Resources.  PDF operators supported: path construction (m l c v y h re), path painting (f F f* S s B B* b b* n), graphics state (q Q cm w J j M g G rg RG k K cs CS sc SC), text (BT ET Tf Td TD Tm T* Tj TJ ' ''), XObjects (Do). Unsupported operators are silently skipped. PDF 32000-1:2008 §8 — Graphics model.

## Constructors

### `PageRasterizer(PdfObjectStore objects, RenderOptions? options = null)`

Initialises a `PageRasterizer` for a document's object store.

**Parameters**

- `objects` — The document's object store.
- `options` — Rendering options. Uses `RenderOptions.Default` when null.

## Methods

### `Rasterize`

```csharp
PixelBuffer Rasterize(PdfPage page)
```

Rasterizes a PDF page to a `PixelBuffer`.

**Parameters**

- `page` — The page to rasterize.

**Returns:** A `PixelBuffer` in BGRA format containing the rendered page.

### `RasterizeToPng`

```csharp
byte[] RasterizeToPng(PdfPage page)
```

Rasterizes a page and encodes the result as PNG bytes.

---

_Source: [`src/Chuvadi.Pdf.Rendering/PageRasterizer.cs`](../../../src/Chuvadi.Pdf.Rendering/PageRasterizer.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
