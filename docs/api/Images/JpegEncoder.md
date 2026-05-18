# JpegEncoder

**Class** in `Chuvadi.Pdf.Images.Jpeg` (Images)

Pure-C# baseline DCT JPEG encoder. Produces JFIF-compliant JPEG byte streams from raw RGB pixel data.

```csharp
public static class JpegEncoder
```

## Remarks

Implements ITU-T T.81 baseline sequential DCT-based coding with Huffman entropy coding. Uses the IJG standard quantization tables scaled by a quality factor (1-100) and the standard Huffman tables.  

 Supports 24-bit RGB and 8-bit grayscale input. The output is a complete JFIF container (SOI ... EOI) suitable for direct embedding as a `data:image/jpeg;base64,...` URL or storage as a `.jpg` file.

## Methods

### `EncodeRgb`

__static__

```csharp
static byte[] EncodeRgb(byte[] rgb, int width, int height, int quality = 85)
```

Encodes RGB pixel data to JPEG bytes.

**Parameters**

- `rgb` — Interleaved 24-bit RGB pixel data.
- `width` — Pixel width.
- `height` — Pixel height.
- `quality` — Quality factor 1-100 (default 85).

### `EncodeGrayscale`

__static__

```csharp
static byte[] EncodeGrayscale(byte[] gray, int width, int height, int quality = 85)
```

Encodes grayscale pixel data to JPEG bytes.

---

_Source: [`src/Chuvadi.Pdf.Images.Jpeg/JpegEncoder.cs`](../../../src/Chuvadi.Pdf.Images.Jpeg/JpegEncoder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
