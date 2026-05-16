# CmykTiffEncoder

**Class** in `Chuvadi.Pdf.Images` (Images)

Encodes `CmykImage` objects to a baseline TIFF 6.0 byte stream with CMYK photometric interpretation (5).

```csharp
public static class CmykTiffEncoder
```

## Methods

### `Encode`

__static__

```csharp
static byte[] Encode(CmykImage image)
```

Encodes a single CMYK image to a TIFF byte stream.

### `EncodeAll`

__static__

```csharp
static byte[] EncodeAll(IEnumerable<CmykImage> images)
```

Encodes a sequence of CMYK images to a multi-page TIFF.

---

_Source: [`src/Chuvadi.Pdf.Images/CmykTiffEncoder.cs`](../../../src/Chuvadi.Pdf.Images/CmykTiffEncoder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
