# TextRenderingMode

**Enum** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Rendering mode for a `TextOp` (PDF §9.3.6).

```csharp
public enum TextRenderingMode
```

## Values

| Name | Description |
|---|---|
| `Fill` | Fill glyphs. |
| `Stroke` | Stroke glyphs. |
| `FillThenStroke` | Fill then stroke glyphs. |
| `Invisible` | Invisible (just advances the text cursor). |
| `FillAndClip` | Fill and add to clip path. |
| `StrokeAndClip` | Stroke and add to clip path. |
| `FillStrokeAndClip` | Fill, stroke, and add to clip path. |
| `Clip` | Add to clip path only. |

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
