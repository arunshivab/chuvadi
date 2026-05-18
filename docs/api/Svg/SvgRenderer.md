# SvgRenderer

**Class** in `Chuvadi.Pdf.Svg` (Svg)

Renders a `PageDisplayList` to SVG.

```csharp
public sealed class SvgRenderer
```

## Remarks

This is the Phase 2.1 architectural pivot: SVG output no longer walks the PDF content stream directly. Instead, `DisplayListBuilder` produces a neutral `PageDisplayList`, and this renderer turns it into SVG. The same display list also feeds the WPF renderer (Phase 2.1 Stage 11) and any future output adapters (software rasterizer, etc.).  

 Coordinate system: PDF uses bottom-left origin, SVG uses top-left. The output wraps content in a single `&lt;g transform="matrix(1 0 0 -1 0 H)"&gt;` outer group so PDF-native coordinates flow through directly. Text elements receive a local counter-flip to read upright.

## Constructors

### `SvgRenderer() : this(new SvgExportOptions())`

Initialises a renderer with default options.

### `SvgRenderer(SvgExportOptions options)`

Initialises a renderer with the given options.

## Methods

### `RenderPage`

```csharp
string RenderPage(PdfDocument document, int pageIndex)
```

Renders one page of `document` to an SVG string.

### `RenderPageBytes`

```csharp
byte[] RenderPageBytes(PdfDocument document, int pageIndex)
```

Renders one page of `document` to UTF-8 bytes.

### `RenderPage`

```csharp
void RenderPage(PdfDocument document, int pageIndex, Stream output)
```

Renders one page of `document` to `output`.

### `RenderPages`

```csharp
IEnumerable<string> RenderPages(PdfDocument document)
```

Enumerates SVG renders for all pages of `document`.

### `Render`

```csharp
string Render(PageDisplayList list)
```

Renders a pre-built `PageDisplayList` to an SVG string.

---

_Source: [`src/Chuvadi.Pdf.Svg/SvgRenderer.cs`](../../../src/Chuvadi.Pdf.Svg/SvgRenderer.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
