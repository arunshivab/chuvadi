# TiffEncoder

**Class** in `Chuvadi.Pdf.Images` (Images)

Encodes one or more `ImageFrame` objects to a baseline TIFF 6.0 byte stream.

```csharp
public static class TiffEncoder
```

## Remarks

Output format: - Little-endian. - 8 bits per sample, 3 samples per pixel (RGB photometric). - PackBits compression. - Single strip per page. Multi-frame inputs produce a multi-page TIFF.

## Methods

### `Encode`

__static__

```csharp
static byte[] Encode(ImageFrame frame)
```

Encodes a single image frame to a TIFF byte stream.

### `EncodeAll`

__static__

```csharp
static byte[] EncodeAll(IEnumerable<ImageFrame> frames)
```

Encodes a sequence of image frames to a multi-page TIFF byte stream.

---

_Source: [`src/Chuvadi.Pdf.Images/TiffEncoder.cs`](../../../src/Chuvadi.Pdf.Images/TiffEncoder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
