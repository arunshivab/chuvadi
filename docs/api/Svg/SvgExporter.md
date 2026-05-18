# SvgExporter

**Class** in `Chuvadi.Pdf.Svg` (Svg)

```csharp
public static class SvgExporter
```

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
