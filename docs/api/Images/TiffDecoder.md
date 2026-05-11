# TiffDecoder

**Class** in `Chuvadi.Pdf.Images` (Images)

Decodes TIFF images per TIFF 6.0 baseline.

```csharp
public static class TiffDecoder
```

## Remarks

Supports grayscale and RGB TIFFs at 1/4/8/16 bits per channel, with uncompressed, PackBits, or LZW compression. Multi-page TIFFs return a list of frames in document order.

## Methods

### `Decode`

__static__

```csharp
static List<ImageFrame> Decode(byte[] data)
```

Decodes all pages from a TIFF byte stream.

**Parameters**

- `data` — The TIFF file bytes.

**Returns:** One `ImageFrame` per page in document order.

---

_Source: [`src/Chuvadi.Pdf.Images/TiffDecoder.cs`](../../../src/Chuvadi.Pdf.Images/TiffDecoder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
