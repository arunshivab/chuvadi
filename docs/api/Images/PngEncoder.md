# PngEncoder

**Class** in `Chuvadi.Pdf.Images` (Images)

Encodes an `ImageFrame` to PNG format.

```csharp
public static class PngEncoder
```

## Remarks

Writes a valid PNG file using the existing `DeflateFilter` for IDAT compression. Produces 24-bit RGB PNG (no alpha) or 32-bit RGBA. Uses the Sub row filter for good compression on photographic content. PNG Specification 1.2 — http://www.libpng.org/pub/png/spec/1.2/ RFC 1950 — zlib format wrapping the DEFLATE stream in IDAT chunks.

## Methods

### `Encode`

__static__

```csharp
static void Encode(ImageFrame frame, Stream output, bool includeAlpha = false)
```

Encodes an `ImageFrame` to PNG and writes it to `output`.

**Parameters**

- `frame` — The image to encode.
- `output` — The stream to write the PNG to. Must be writable.
- `includeAlpha` — True to write 32-bit RGBA PNG (colour type 6); false to write 24-bit RGB PNG (colour type 2).

---

_Source: [`src/Chuvadi.Pdf.Images/PngEncoder.cs`](../../../src/Chuvadi.Pdf.Images/PngEncoder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
