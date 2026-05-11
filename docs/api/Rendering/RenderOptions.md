# RenderOptions

**Class** in `Chuvadi.Pdf.Rendering` (Rendering)

Options that control how a PDF page is rasterized.

```csharp
public sealed class RenderOptions
```

## Constructors

### `RenderOptions()`

Initialises `RenderOptions` with default values.

## Properties

### `Default`

__static__

```csharp
static RenderOptions Default
```

Default options: 96 DPI, opaque white background.

### `Dpi`

```csharp
double Dpi
```

Gets or initialises the output resolution in dots per inch. Higher values produce larger, sharper images. Typical values: 72 (screen), 96 (Windows default), 150, 300 (print). Default: 96.

### `Background`

```csharp
ColorF Background
```

Gets or initialises the background colour painted before page content. Default: opaque white.

### `FlatnessTolerance`

```csharp
double FlatnessTolerance
```

Gets or initialises the flatness tolerance for Bezier curve flattening in device pixels. Smaller = smoother curves, more segments. Default: 0.25 pixels.

### `Scale`

```csharp
double Scale => Dpi / 72.0
```

Computes the scale factor from PDF points to device pixels for this DPI.

---

_Source: [`src/Chuvadi.Pdf.Rendering/RenderOptions.cs`](../../../src/Chuvadi.Pdf.Rendering/RenderOptions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
