# PageBuilder

**Class** in `Chuvadi.Pdf.Authoring` (Authoring)

Per-page drawing API. All coordinates use top-left origin (Y increases downward), units are PDF points (1 pt = 1/72 inch).

```csharp
public sealed class PageBuilder
```

## Properties

### `Width`

```csharp
double Width
```

Page width in points.

### `Height`

```csharp
double Height
```

Page height in points.

## Methods

### `DrawTable`

```csharp
TableBuilder DrawTable(double x, double y, double width)
```

Begins a fluent table at (x, y) with the given total width. Call `TableBuilder.Render` when done configuring.

---

_Source: [`src/Chuvadi.Pdf.Authoring/PageBuilder.cs`](../../../src/Chuvadi.Pdf.Authoring/PageBuilder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
