# ImageFrame

**Class** in `Chuvadi.Pdf.Images` (Images)

A decoded image frame held in a `PixelBuffer`.

```csharp
public sealed class ImageFrame
```

## Remarks

`ImageFrame` is the output of all decoders (`JpegDecoder`, `PngDecoder`) and the input to all encoders (`PngEncoder`, `BmpEncoder`). The pixel data is always stored in the `Chuvadi.Pdf.Graphics.PixelBuffer` BGRA format regardless of the original image colour space. The `OriginalFormat` property records what the source image looked like before conversion.

## Constructors

### `ImageFrame(PixelBuffer pixels, ImageColorFormat originalFormat)`

Initialises an `ImageFrame` from an existing buffer.

## Properties

### `Pixels`

```csharp
PixelBuffer Pixels
```

Gets the pixel data in BGRA format.

### `Width`

```csharp
int Width => Pixels.Width
```

Gets the width in pixels.

### `Height`

```csharp
int Height => Pixels.Height
```

Gets the height in pixels.

### `OriginalFormat`

```csharp
ImageColorFormat OriginalFormat
```

Gets the colour format of the source image before conversion.

## Methods

### `Create`

__static__

```csharp
static ImageFrame Create(int width, int height, ImageColorFormat format)
```

Creates a new `ImageFrame` of the given dimensions, cleared to opaque white.

---

_Source: [`src/Chuvadi.Pdf.Images/ImageFrame.cs`](../../../src/Chuvadi.Pdf.Images/ImageFrame.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
