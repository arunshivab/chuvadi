# TransformOp

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Pushes or pops a graphics-state transformation matrix.

```csharp
public sealed class TransformOp : RenderOp
```

## Properties

### `Kind`

```csharp
override RenderOpKind Kind => RenderOpKind.Transform
```

<inheritdoc />

### `Push`

```csharp
required bool Push
```

True for push (q + cm), false for pop (Q).

### `Ctm`

```csharp
AffineMatrix Ctm
```

The cumulative CTM after this op (for renderers that don't track state).

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
