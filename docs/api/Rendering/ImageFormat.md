# ImageFormat

**Enum** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Raster format of image pixel data.

```csharp
public enum ImageFormat
```

## Values

| Name | Description |
|---|---|
| `Raw` | Raw raw byte buffer in the declared color space. |
| `Jpeg` | JPEG-encoded (DCT). Pass through unchanged where possible. |
| `Png` | PNG-encoded (FlateDecode-based). |

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/RenderOp.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
