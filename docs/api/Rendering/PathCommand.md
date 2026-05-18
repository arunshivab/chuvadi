# PathCommand

**Enum** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Type of a path command.

```csharp
public enum PathCommand
```

## Values

| Name | Description |
|---|---|
| `MoveTo` | Begin a new subpath at the given point. |
| `LineTo` | Draw a straight line to the given point. |
| `CubicTo` | Draw a cubic Bezier with two control points. |
| `Close` | Close the current subpath. |

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/PathGeometry.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/PathGeometry.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
