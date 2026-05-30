# Rect

**Record** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

An axis-aligned bounding rectangle in PDF user-space coords.

```csharp
public record Rect(double X, double Y, double Width, double Height)
```

## Properties

### `Right`

```csharp
double Right => X + Width
```

The right edge (X + Width).

### `Top`

```csharp
double Top => Y + Height
```

The top edge (Y + Height).

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/TextRun.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/TextRun.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
