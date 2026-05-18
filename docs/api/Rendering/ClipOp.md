# ClipOp

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Pushes a clipping region.

```csharp
public sealed class ClipOp : RenderOp
```

## Properties

### `Kind`

```csharp
override RenderOpKind Kind => RenderOpKind.Clip
```

<inheritdoc />

### `Geometry`

```csharp
required PathGeometry Geometry
```

The clipping path.

### `FillRule`

```csharp
FillRule FillRule
```

Fill rule for the clip region.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
