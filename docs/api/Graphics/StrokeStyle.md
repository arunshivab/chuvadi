# StrokeStyle

**Class** in `Chuvadi.Pdf.Graphics` (Graphics)

Encapsulates all stroke properties: width, cap, join, dash pattern, and miter limit. PDF 32000-1:2008 §8.4.3 — Graphics state parameters for stroking.

```csharp
public sealed class StrokeStyle
```

## Constructors

### `StrokeStyle()`

Initialises a `StrokeStyle` with default values.

## Properties

### `Default`

__static__

```csharp
static StrokeStyle Default
```

The default stroke style: 1pt solid black, butt cap, miter join.

### `Width`

```csharp
double Width
```

Gets or initialises the stroke width in user space units. PDF 32000-1:2008 §8.4.3.2 — Line width.

### `Cap`

```csharp
LineCap Cap
```

Gets or initialises the line cap style. PDF 32000-1:2008 §8.4.3.3.

### `Join`

```csharp
LineJoin Join
```

Gets or initialises the line join style. PDF 32000-1:2008 §8.4.3.4.

### `MiterLimit`

```csharp
double MiterLimit
```

Gets or initialises the miter limit. When the miter length exceeds Width × MiterLimit, a bevel join is used. PDF 32000-1:2008 §8.4.3.5. Default 10.

### `DashPattern`

```csharp
double[] DashPattern
```

Gets or initialises the dash pattern. Empty array means solid stroke. PDF 32000-1:2008 §8.4.3.6 — Line dash pattern.

### `DashOffset`

```csharp
double DashOffset
```

Gets or initialises the dash phase offset. PDF 32000-1:2008 §8.4.3.6.

### `Color`

```csharp
ColorF Color
```

Gets or initialises the stroke colour.

### `IsSolid`

```csharp
bool IsSolid => DashPattern.Length == 0
```

Returns true when the stroke is a solid line (no dash pattern).

## Methods

### `WithWidth`

```csharp
StrokeStyle WithWidth(double width)
```

Returns a copy with the given width.

### `WithColor`

```csharp
StrokeStyle WithColor(ColorF color)
```

Returns a copy with the given colour.

---

_Source: [`src/Chuvadi.Pdf.Graphics/StrokeStyle.cs`](../../../src/Chuvadi.Pdf.Graphics/StrokeStyle.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
