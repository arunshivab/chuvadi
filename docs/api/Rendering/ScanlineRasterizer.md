# ScanlineRasterizer

**Class** in `Chuvadi.Pdf.Rendering` (Rendering)

Fills vector paths into a `PixelBuffer` using a scanline edge-crossing algorithm.

```csharp
public sealed class ScanlineRasterizer
```

## Remarks

Supports both PDF fill rules: 
 
- Non-zero winding number — PDF operators f, F, B, b 
- Even-odd — PDF operators f*, B*, b*  Input is a list of sub-paths from `PathFlattener`, each being a closed list of `PointF` vertices in device space. PDF 32000-1:2008 §8.5.3.3 — Filling.

---

_Source: [`src/Chuvadi.Pdf.Rendering/ScanlineRasterizer.cs`](../../../src/Chuvadi.Pdf.Rendering/ScanlineRasterizer.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
