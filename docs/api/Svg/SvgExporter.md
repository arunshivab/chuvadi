# SvgExporter

**Class** in `Chuvadi.Pdf.Svg` (Svg)

Translates PDF page content streams to SVG.

```csharp
public static class SvgExporter
```

## Remarks

Mirrors the structure of `Chuvadi.Pdf.Rendering.PageRasterizer`: walks the content stream's operator tokens, maintaining a graphics state stack, but emits SVG elements via `SvgWriter` rather than rasterizing to a pixel buffer.  

 Coordinate system: SVG uses top-left origin (Y down); PDF uses bottom-left (Y up). The export wraps page content in a single `&lt;g transform="matrix(1 0 0 -1 0 H)"&gt;` outer group so PDF-native coordinates flow through directly. Text elements receive a local counter-flip so glyphs read upright.

## Methods

### `ExportPage`

__static__

```csharp
static string ExportPage(PdfDocument document, int pageIndex, SvgExportOptions? options = null)
```

Exports a page to an SVG string.

### `ExportPageBytes`

__static__

```csharp
static byte[] ExportPageBytes(PdfDocument document, int pageIndex, SvgExportOptions? options = null)
```

Exports a page to a byte array (UTF-8).

### `ExportPages`

__static__

```csharp
static IEnumerable<string> ExportPages(PdfDocument document, SvgExportOptions? options = null)
```

Enumerates SVG exports for all pages.

---

_Source: [`src/Chuvadi.Pdf.Svg/SvgExporter.cs`](../../../src/Chuvadi.Pdf.Svg/SvgExporter.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
