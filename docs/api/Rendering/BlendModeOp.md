# BlendModeOp

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Pushes or pops a blend mode.

```csharp
public sealed class BlendModeOp : RenderOp
```

## Properties

### `Kind`

```csharp
override RenderOpKind Kind => RenderOpKind.BlendMode
```

<inheritdoc />

### `Push`

```csharp
required bool Push
```

True for push, false for pop.

### `Mode`

```csharp
PdfBlendMode Mode
```

Blend mode (only meaningful on push).

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
