# ImageWatermarkOptions

**Class** in `Chuvadi.Pdf.Watermark` (Watermark)

Options for stamping an image watermark onto PDF pages.

```csharp
public sealed class ImageWatermarkOptions
```

## Constructors

### `ImageWatermarkOptions()`

Initialises an `ImageWatermarkOptions` with default values. The image is centred, at 30% opacity, at 50% of page width.

## Properties

### `Opacity`

```csharp
float Opacity
```

Gets or initialises the opacity from 0 (transparent) to 1 (opaque). Default: 0.3.

### `ScaleFraction`

```csharp
double ScaleFraction
```

Gets or initialises the image width as a fraction of the page width. 0.5 = 50% of the page width. Default: 0.5.

### `RotationDegrees`

```csharp
double RotationDegrees
```

Gets or initialises the rotation in degrees. Default: 0.

### `PageIndices`

```csharp
int[]? PageIndices
```

Gets or initialises which pages to watermark. Null means all pages. Otherwise a zero-based page index set.

---

_Source: [`src/Chuvadi.Pdf.Watermark/ImageWatermarkOptions.cs`](../../../src/Chuvadi.Pdf.Watermark/ImageWatermarkOptions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
