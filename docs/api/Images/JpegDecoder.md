# JpegDecoder

**Class** in `Chuvadi.Pdf.Images` (Images)

Decodes a baseline sequential DCT JPEG (SOF0) into an `ImageFrame`.

```csharp
public static class JpegDecoder
```

## Remarks

Supports: 
 
- Baseline DCT (SOF0 marker) — covers 95%+ of JPEG in PDFs 
- YCbCr → RGB colour conversion 
- Grayscale (1 component) 
- 4:2:0, 4:2:2, 4:4:4 chroma subsampling 
- Up to 4 Huffman tables (DC + AC per component) 
- Up to 4 quantisation tables  Not supported: progressive JPEG (SOF2), arithmetic coding (SOF9), lossless JPEG, JFIF/EXIF metadata (ignored). ISO 10918-1:1994 — Digital compression and coding of continuous-tone images.

## Methods

### `Decode`

__static__

```csharp
static ImageFrame Decode(byte[] data)
```

Decodes a JPEG from a byte array.

### `Decode`

__static__

```csharp
static ImageFrame Decode(Stream input)
```

Decodes a JPEG from a stream.

---

_Source: [`src/Chuvadi.Pdf.Images/JpegDecoder.cs`](../../../src/Chuvadi.Pdf.Images/JpegDecoder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
