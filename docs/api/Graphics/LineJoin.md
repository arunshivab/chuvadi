# LineJoin

**Enum** in `Chuvadi.Pdf.Graphics` (Graphics)

Specifies the shape of corners where two path segments meet when stroked. PDF 32000-1:2008 §8.4.3.4 — Line join style, Table 55.

```csharp
public enum LineJoin
```

## Values

| Name | Description |
|---|---|
| `Miter` | Miter join. The outer edges of the strokes are extended to meet at a point. Clipped to a bevel when the miter length exceeds the miter limit. PDF value 0. |
| `Round` | Round join. A circular arc is drawn at the corner. PDF value 1. |
| `Bevel` | Bevel join. The corner is finished with a straight line segment. PDF value 2. |

---

_Source: [`src/Chuvadi.Pdf.Graphics/LineJoin.cs`](../../../src/Chuvadi.Pdf.Graphics/LineJoin.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
