# PathGeometry

**Class** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

An ordered sequence of path segments.

```csharp
public sealed class PathGeometry
```

## Constructors

### `PathGeometry()`

Initialises an empty path.

### `PathGeometry(IEnumerable<PathSegment> segments)`

Initialises from a sequence of segments.

## Properties

### `Segments`

```csharp
IReadOnlyList<PathSegment> Segments => _segments
```

The ordered segments of this path.

### `IsEmpty`

```csharp
bool IsEmpty => _segments.Count == 0
```

Whether this path has any segments.

## Methods

### `MoveTo`

```csharp
PathGeometry MoveTo(double x, double y)
```

Adds a move-to command.

### `LineTo`

```csharp
PathGeometry LineTo(double x, double y)
```

Adds a line-to command.

### `CubicTo`

```csharp
PathGeometry CubicTo(double x1, double y1, double x2, double y2, double x3, double y3)
```

Adds a cubic-bezier-to command with two control points.

### `Close`

```csharp
PathGeometry Close()
```

Adds a close-path command.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/PathGeometry.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/PathGeometry.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
