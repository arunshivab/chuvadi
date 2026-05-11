# BmpEncoder

**Class** in `Chuvadi.Pdf.Images` (Images)

Encodes an `ImageFrame` to Windows BMP format.

```csharp
public static class BmpEncoder
```

## Remarks

Writes a 24-bit BGR BMP (no alpha) by default, or 32-bit BGRA when includeAlpha is true. The BMP pixel rows are stored top-down. Row padding is applied to 4-byte alignment. BMP v3 (BITMAPINFOHEADER) — no compression, no colour table.

## Methods

### `Encode`

__static__

```csharp
static void Encode(ImageFrame frame, Stream output, bool includeAlpha = false)
```

Encodes an `ImageFrame` to BMP and writes it to `output`.

**Parameters**

- `frame` — The image to encode.
- `output` — The stream to write the BMP to. Must be writable.
- `includeAlpha` — True to write 32-bit BGRA; false to write 24-bit BGR.

---

_Source: [`src/Chuvadi.Pdf.Images/BmpEncoder.cs`](../../../src/Chuvadi.Pdf.Images/BmpEncoder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
