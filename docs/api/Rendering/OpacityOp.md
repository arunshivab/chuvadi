# OpacityOp

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Pushes or pops an opacity group.

```csharp
public sealed class OpacityOp : RenderOp
```

## Properties

### `Kind`

```csharp
override RenderOpKind Kind => RenderOpKind.Opacity
```

<inheritdoc />

### `Push`

```csharp
required bool Push
```

True for push, false for pop.

### `Alpha`

```csharp
double Alpha
```

Constant alpha [0, 1] (only meaningful on push).

### `Isolated`

```csharp
bool Isolated
```

Whether the group is isolated (PDF transparency group).

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
