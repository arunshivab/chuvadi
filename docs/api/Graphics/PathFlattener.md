# PathFlattener

**Class** in `Chuvadi.Pdf.Graphics` (Graphics)

Flattens a `Path` containing cubic Bezier curves into a sequence of straight line segments suitable for scanline rasterization.

```csharp
public sealed class PathFlattener
```

## Remarks

Uses adaptive subdivision: a curve segment is split in two when its midpoint deviates from the chord by more than the flatness tolerance. This produces fewer segments for nearly-straight curves while maintaining accuracy where curvature is high. The output is a list of sub-paths, each being an ordered list of `PointF` vertices. Closed sub-paths include the closing edge implicitly (the rasterizer connects the last point back to the first). PDF 32000-1:2008 §8.5.2.3 — Bezier curves.

## Constructors

### `PathFlattener(double flatness = 0.25)`

Initialises a `PathFlattener` with the given flatness tolerance.

**Parameters**

- `flatness` — Maximum permitted deviation of a flattened segment from the true curve, in the same units as the path coordinates (PDF points). Smaller values = more segments = higher accuracy. Typical values: 0.1 (high quality) to 1.0 (fast).

## Properties

### `Flatness`

```csharp
double Flatness => _flatness
```

Gets the flatness tolerance in path coordinate units.

## Methods

### `Flatten`

```csharp
List<List<PointF>> Flatten(Path path)
```

Flattens a path into a list of sub-paths. Each sub-path is a list of vertex points.

**Parameters**

- `path` — The source path to flatten.

**Returns:** A list of sub-paths. Each sub-path is a non-empty list of points. The caller is responsible for applying the fill rule across sub-paths.

---

_Source: [`src/Chuvadi.Pdf.Graphics/PathFlattener.cs`](../../../src/Chuvadi.Pdf.Graphics/PathFlattener.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
