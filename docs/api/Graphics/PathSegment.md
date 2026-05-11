# PathSegment

**Struct** in `Chuvadi.Pdf.Graphics` (Graphics)

A single segment in a vector graphics path. Stores up to three points (for cubic Bezier curves). PDF 32000-1:2008 §8.5.2.

```csharp
public struct PathSegment
```

## Properties

### `Kind`

```csharp
PathSegmentKind Kind
```

Gets the kind of this segment.

### `P0`

```csharp
PointF P0
```

The endpoint for MoveTo and LineTo; the first control point for CubicBezierTo.

### `P1`

```csharp
PointF P1
```

The second control point for CubicBezierTo; unused otherwise.

### `P2`

```csharp
PointF P2
```

The endpoint for CubicBezierTo; unused otherwise.

## Methods

### `MoveTo`

__static__

```csharp
static PathSegment MoveTo(PointF point)
```

Creates a MoveTo segment. PDF operator 'm'.

### `MoveTo`

__static__

```csharp
static PathSegment MoveTo(double x, double y)
```

Creates a MoveTo segment from coordinates.

### `LineTo`

__static__

```csharp
static PathSegment LineTo(PointF point)
```

Creates a LineTo segment. PDF operator 'l'.

### `LineTo`

__static__

```csharp
static PathSegment LineTo(double x, double y)
```

Creates a LineTo segment from coordinates.

### `CubicBezierTo`

__static__

```csharp
static PathSegment CubicBezierTo(PointF cp1, PointF cp2, PointF endpoint)
```

Creates a cubic Bezier curve segment. PDF operator 'c': current point → cp1 → cp2 → endpoint.

### `ClosePath`

__static__

```csharp
static PathSegment ClosePath()
```

Creates a ClosePath segment. PDF operator 'h'.

### `ToString`

```csharp
override string ToString()
```

<inheritdoc/>

---

_Source: [`src/Chuvadi.Pdf.Graphics/PathSegment.cs`](../../../src/Chuvadi.Pdf.Graphics/PathSegment.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
