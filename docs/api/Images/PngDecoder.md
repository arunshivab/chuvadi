# PngDecoder

**Class** in `Chuvadi.Pdf.Images` (Images)

Decodes a PNG image into an `ImageFrame`.

```csharp
public static class PngDecoder
```

## Remarks

Supports: 
 
- Colour types: Grayscale (0), RGB (2), Indexed (3), Grayscale+Alpha (4), RGBA (6) 
- Bit depths: 1, 2, 4, 8 (16-bit downsampled to 8-bit) 
- Row filters: None, Sub, Up, Average, Paeth 
- Interlacing: None (Adam7 interlace not supported)  PNG Specification 1.2 — http://www.libpng.org/pub/png/spec/1.2/

## Methods

### `Decode`

__static__

```csharp
static ImageFrame Decode(byte[] data)
```

Decodes a PNG from a byte array.

**Parameters**

- `data` — The raw PNG bytes.

**Returns:** A decoded `ImageFrame`. <exception cref="ImageException">Thrown on invalid PNG data.</exception>

### `Decode`

__static__

```csharp
static ImageFrame Decode(Stream input)
```

Decodes a PNG from a stream.

---

_Source: [`src/Chuvadi.Pdf.Images/PngDecoder.cs`](../../../src/Chuvadi.Pdf.Images/PngDecoder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
