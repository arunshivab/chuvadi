# SvgRenderer

**Class** in `Chuvadi.Pdf.Svg` (Svg)

Renders a `PageDisplayList` to SVG.

```csharp
public sealed class SvgRenderer
```

## Remarks

Phase 2.1 architectural pivot: SVG output no longer walks the PDF content stream directly. Instead, `DisplayListBuilder` produces a neutral `PageDisplayList`, and this renderer turns it into SVG. The same display list also feeds the WPF renderer and any future output adapters.  

 Coordinate system: PDF uses bottom-left origin, SVG uses top-left. The output wraps content in a single `&lt;g transform="matrix(1 0 0 -1 0 H)"&gt;` outer group so PDF-native coordinates flow through directly. Text elements and images both receive a local counter-flip to read upright after the outer flip is applied.  

 v2.1.2/v2.1.3 text positioning: per-glyph X attributes on SVG `&lt;text&gt;` override the rendering font's natural advance widths. When an `@font-face` is registered for a run's BaseFont (see font embedding below), the embedded program's hmtx already encodes the correct glyph advances, and per-glyph X is suppressed so the font drives layout. When no embedded font is available, per-glyph X positions from PDF /Widths preserve inter-character spacing through the generic CSS fallback. The renderer also tracks per-line position across consecutive TextOps and shrinks excess gaps that appear before a space-starting run; with embedded fonts that shrink is suppressed, because the rendered run extent (hmtx-driven) can disagree with the gap calculation (/Widths-driven) and over-shrinking would visibly swallow the space. The DisplayList fold collapses Word's kern-before-space idiom into the surrounding run, so most TextOps no longer start with a bare space in the first place — making the suppressed shrink branch a rare path.  

 v2.1.2 path/border handling: filled rectangles with one dimension below `MinVisibleThickness` are expanded symmetrically about their midline so Word's 0.48-unit table borders remain visible at all zooms.  

 v2.1.2 clip application: each PDF graphics-state scope is wrapped in a `&lt;g&gt;`; ClipOps open additional `&lt;g clip-path&gt;` wrappers inside the scope that close when the matching Pop arrives. The SVG renderer naturally intersects nested clips.  

 v2.1.2 font embedding: at the start of rendering, every distinct font dictionary referenced on the page is offered to `FontEmbedder` which extracts the embedded font program (FontFile2/FontFile3), base64- encodes it, and emits a CSS `@font-face` rule. The renderer builds a mapping from each font's BaseFont name to the assigned CSS family and consults it during text emission. Without embedding, browsers substitute the PDF's subsetted fonts with system Times/Arial, whose glyph advance widths differ; the result is visible inter-character drift on multi-run lines (e.g. "Developed India's First..."). Embedding makes the PDF's own metrics authoritative.

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
string Render(PageDisplayList list) => Render(list, resolver: null)
```

Renders a pre-built `PageDisplayList` without embedding fonts. Used by the WPF rasterizer and other callers that don't need CSS @font-face support, and by tests that build display lists synthetically.

### `Render`

```csharp
string Render(PageDisplayList list, IPdfObjectResolver? resolver)
```

Renders a pre-built `PageDisplayList`, optionally embedding font programs as `@font-face` rules. Pass a non-null `resolver` to enable embedding; the resolver is used to walk into FontDescriptor → FontFile2/FontFile3 streams.

---

_Source: [`src/Chuvadi.Pdf.Svg/SvgRenderer.cs`](../../../src/Chuvadi.Pdf.Svg/SvgRenderer.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
