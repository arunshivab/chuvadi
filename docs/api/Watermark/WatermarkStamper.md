# WatermarkStamper

**Class** in `Chuvadi.Pdf.Watermark` (Watermark)

Stamps text or image watermarks onto PDF pages by appending new content streams, preserving the original page content.

```csharp
public static class WatermarkStamper
```

## Remarks

Watermarks are applied as additional content streams appended to each targeted page. The original content is untouched. Opacity is implemented via PDF ExtGState (/ca fill opacity, PDF 32000-1:2008 §11.6.4.4). Standard PDF font names (no embedding required): Helvetica, Helvetica-Bold, Helvetica-Oblique, Helvetica-BoldOblique, Times-Roman, Times-Bold, Times-Italic, Times-BoldItalic, Courier, Courier-Bold, Courier-Oblique, Courier-BoldOblique, Symbol, ZapfDingbats.

---

_Source: [`src/Chuvadi.Pdf.Watermark/WatermarkStamper.cs`](../../../src/Chuvadi.Pdf.Watermark/WatermarkStamper.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
