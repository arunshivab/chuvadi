# GlyphOutline

**Class** in `Chuvadi.Pdf.Fonts.Rendering` (Fonts)

The outline of a single glyph as a `Path` of contours, together with its `GlyphMetrics`.

```csharp
public sealed class GlyphOutline
```

## Remarks

The `Outline` path is in font design units with Y increasing upward (TrueType convention). The rasterizer scales and flips Y when drawing to a `Chuvadi.Pdf.Graphics.PixelBuffer`. An empty `Outline` (no segments) is valid for whitespace glyphs such as space and non-breaking space. OpenType spec §glyf — Glyph Data table.

## Constructors

### `GlyphOutline(Path outline, GlyphMetrics metrics)`

Initialises a `GlyphOutline`.

## Properties

### `Outline`

```csharp
Path Outline
```

Gets the glyph contours as a `Path`.

### `Metrics`

```csharp
GlyphMetrics Metrics
```

Gets the typographic metrics for this glyph.

### `IsEmpty`

```csharp
bool IsEmpty => Outline.IsEmpty
```

Returns true when the glyph has no visible outline (for example, a space character).

## Methods

### `Scale`

```csharp
GlyphOutline Scale(double pointSize)
```

Returns a new `GlyphOutline` scaled to the given point size, suitable for rendering.

**Parameters**

- `pointSize` — The target size in PDF points (1/72 inch).

---

_Source: [`src/Chuvadi.Pdf.Fonts.Rendering/GlyphOutline.cs`](../../../src/Chuvadi.Pdf.Fonts.Rendering/GlyphOutline.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
