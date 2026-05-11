# PointF

**Struct** in `Chuvadi.Pdf.Graphics` (Graphics)

An immutable point in 2D user space, measured in PDF points (1/72 inch). Origin is PDF convention: bottom-left, Y increases upward.

```csharp
public struct PointF : IEquatable<PointF>
```

## Constructors

### `PointF(double x, double y)`

Initialises a new `PointF`.

## Properties

### `X`

```csharp
double X
```

Gets the X coordinate.

### `Y`

```csharp
double Y
```

Gets the Y coordinate.

### `Zero`

__static__

```csharp
static PointF Zero
```

The origin point (0, 0).

## Methods

### `Translate`

```csharp
PointF Translate(double dx, double dy)
```

Returns a point offset by (`dx`, `dy`).

### `DistanceTo`

```csharp
double DistanceTo(PointF other)
```

Returns the distance to another point.

### `==`

__static__

```csharp
static bool operator ==(PointF left, PointF right) => left.Equals(right)
```

Equality operator.

### `!=`

__static__

```csharp
static bool operator !=(PointF left, PointF right) => !left.Equals(right)
```

Inequality operator.

---

_Source: [`src/Chuvadi.Pdf.Graphics/PointF.cs`](../../../src/Chuvadi.Pdf.Graphics/PointF.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
