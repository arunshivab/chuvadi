# FillRule

**Enum** in `Chuvadi.Pdf.Graphics` (Graphics)

Determines how the interior of a path is defined when the path self-intersects or has nested sub-paths. PDF 32000-1:2008 §8.5.3.3 — Filling.

```csharp
public enum FillRule
```

## Values

| Name | Description |
|---|---|
| `NonZeroWinding` | Non-zero winding number rule. A point is inside if a ray from that point crosses the path in a way that the winding number is non-zero. Default rule for PDF operator 'f' and 'F'. |
| `EvenOdd` | Even-odd rule. A point is inside if a ray from that point crosses the path boundary an odd number of times. Used by PDF operator 'f*'. |

---

_Source: [`src/Chuvadi.Pdf.Graphics/FillRule.cs`](../../../src/Chuvadi.Pdf.Graphics/FillRule.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
