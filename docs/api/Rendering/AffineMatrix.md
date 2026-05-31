# AffineMatrix

**Record** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

2D affine transformation matrix in PDF convention. 
```
 | A  B  0 | | C  D  0 | | E  F  1 | 
```
 Applied to a point (x, y): (A*x + C*y + E, B*x + D*y + F).

```csharp
public readonly record struct AffineMatrix(double A, double B, double C, double D, double E, double F)
```

## Properties

### `Identity`

__static__

```csharp
static AffineMatrix Identity
```

Identity transform.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/AffineMatrix.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/AffineMatrix.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
