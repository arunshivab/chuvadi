# CmykConverter

**Class** in `Chuvadi.Pdf.Images` (Images)

Converts `PixelBuffer` BGRA data to packed CMYK 8 bits per channel.

```csharp
public static class CmykConverter
```

## Remarks

This is a naive, ICC-profile-free conversion suitable for previews and non-colour-critical output. For colour-managed print workflows, run the output through an ICC-aware converter (e.g. Little CMS) afterwards. Formula (per Adobe Photoshop): for normalised RGB in [0,1] K = 1 - max(R, G, B) C = (1 - R - K) / (1 - K), 0 if K = 1 M = (1 - G - K) / (1 - K), 0 if K = 1 Y = (1 - B - K) / (1 - K), 0 if K = 1 Then scale each channel to [0, 255].

## Methods

### `ToCmyk`

__static__

```csharp
static byte[] ToCmyk(PixelBuffer source)
```

Converts a BGRA pixel buffer to packed CMYK bytes (4 bytes per pixel, row-major, top-down).

**Parameters**

- `source` — Source BGRA pixel buffer.

**Returns:** Packed CMYK output, exactly `Width × Height × 4` bytes.

### `ToCmykFrame`

__static__

```csharp
static ImageFrame ToCmykFrame(PixelBuffer source)
```

Returns the four CMYK channels as a single `ImageFrame` tagged as `ImageColorFormat.Cmyk32`. The pixel buffer itself stays BGRA (a re-interpretation: B=C, G=M, R=Y, A=K) so downstream encoders can detect the format via `OriginalFormat` and emit the right photometric.

---

_Source: [`src/Chuvadi.Pdf.Images/CmykConverter.cs`](../../../src/Chuvadi.Pdf.Images/CmykConverter.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
