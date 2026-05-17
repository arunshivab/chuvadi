# CmykImage

**Class** in `Chuvadi.Pdf.Images` (Images)

A planar CMYK 8-bit-per-channel image.

```csharp
public sealed class CmykImage
```

## Remarks

Stores four bytes per pixel in C, M, Y, K order. The conversion from BGRA is the standard subtractive formula; it does NOT apply ICC colour management. For print-accurate output, layer an ICC transform on top of this buffer (e.g., via Little CMS or your raster image processor).

## Constructors

### `CmykImage(int width, int height)`

Initialises a new CMYK image with all channels zeroed (white in subtractive).

## Properties

### `Width`

```csharp
int Width
```

Width in pixels.

### `Height`

```csharp
int Height
```

Height in pixels.

### `ByteCount`

```csharp
int ByteCount => _pixels.Length
```

Total byte count (Width × Height × 4).

### `Stride`

```csharp
int Stride => Width * 4
```

Row stride in bytes (Width × 4).

### `Pixels`

```csharp
ReadOnlySpan<byte> Pixels => _pixels
```

Raw CMYK pixel bytes (C, M, Y, K interleaved per pixel).

## Methods

### `SetPixel`

```csharp
void SetPixel(int x, int y, byte c, byte m, byte yel, byte k)
```

Sets a single pixel's C, M, Y, K components (0..255 each).

### `FromBgra`

__static__

```csharp
static CmykImage FromBgra(PixelBuffer source)
```

Creates a `CmykImage` from a BGRA `PixelBuffer` using the standard subtractive RGB→CMYK conversion.

---

_Source: [`src/Chuvadi.Pdf.Images/CmykImage.cs`](../../../src/Chuvadi.Pdf.Images/CmykImage.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
