# StrokeExpander

**Class** in `Chuvadi.Pdf.Rendering` (Rendering)

Converts a stroked path into a filled path by expanding each segment by half the stroke width on each side.

```csharp
public sealed class StrokeExpander
```

## Remarks

Phase 2 implements butt caps and bevel/miter joins. Round caps and round joins are approximated with bevels. The output is a list of sub-paths suitable for `ScanlineRasterizer` with the non-zero winding fill rule. PDF 32000-1:2008 §8.5.3.2 — Stroking.

## Methods

### `Expand`

```csharp
List<List<PointF>> Expand(List<List<PointF>> subPaths, StrokeStyle style)
```

Expands the flattened sub-paths of a stroked path into a filled outline.

**Parameters**

- `subPaths` — Flattened sub-paths from `PathFlattener`.
- `style` — The stroke style (width, cap, join, miter limit).

**Returns:** A list of filled sub-paths forming the stroke outline. Use `FillRule.NonZeroWinding` when rasterizing.

---

_Source: [`src/Chuvadi.Pdf.Rendering/StrokeExpander.cs`](../../../src/Chuvadi.Pdf.Rendering/StrokeExpander.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
