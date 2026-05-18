# PageDisplayList

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

A page's content as a neutral, ordered sequence of `RenderOp`s.

```csharp
public sealed class PageDisplayList : IReadOnlyList<RenderOp>
```

## Remarks

Built by `DisplayListBuilder.Build`; consumed by output adapters such as `SvgRenderer`, `WpfRenderer`, or future software rasterizers.  

 Pure value-like type: same page bytes, same display list. No rendering side effects.

## Properties

### `MediaWidth`

```csharp
double MediaWidth
```

Page width in points.

### `MediaHeight`

```csharp
double MediaHeight
```

Page height in points.

### `Rotation`

```csharp
int Rotation
```

Clockwise rotation in degrees (0, 90, 180, 270).

### `Count`

```csharp
int Count => _ops.Count
```

<inheritdoc />

### `index]`

```csharp
RenderOp this[int index] => _ops[index]
```

<inheritdoc />

## Methods

### `GetEnumerator`

```csharp
IEnumerator<RenderOp> GetEnumerator() => _ops.GetEnumerator()
```

<inheritdoc />

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/PageDisplayList.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/PageDisplayList.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
