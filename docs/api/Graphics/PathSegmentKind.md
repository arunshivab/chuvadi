# PathSegmentKind

**Enum** in `Chuvadi.Pdf.Graphics` (Graphics)

The kind of a `PathSegment`. PDF 32000-1:2008 §8.5.2 — Path construction operators.

```csharp
public enum PathSegmentKind
```

## Values

| Name | Description |
|---|---|
| `MoveTo` | Move to a new point without drawing. Starts a new sub-path. PDF operator 'm'. |
| `LineTo` | Draw a straight line from the current point to the endpoint. PDF operator 'l'. |
| `CubicBezierTo` | Draw a cubic Bezier curve using two control points. PDF operator 'c'. |
| `ClosePath` | Close the current sub-path with a straight line to the start point. PDF operator 'h'. |

---

_Source: [`src/Chuvadi.Pdf.Graphics/PathSegment.cs`](../../../src/Chuvadi.Pdf.Graphics/PathSegment.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
