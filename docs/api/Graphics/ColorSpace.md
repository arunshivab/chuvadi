# ColorSpace

**Enum** in `Chuvadi.Pdf.Graphics` (Graphics)

The colour space of a `ColorF` value. PDF 32000-1:2008 §8.6 — Colour spaces.

```csharp
public enum ColorSpace
```

## Values

| Name | Description |
|---|---|
| `Gray` | Single-channel grey (0 = black, 1 = white). PDF DeviceGray. PDF 32000-1:2008 §8.6.4.1. |
| `Rgb` | Red, Green, Blue (each 0–1). PDF DeviceRGB. PDF 32000-1:2008 §8.6.4.2. |
| `Cmyk` | Cyan, Magenta, Yellow, Key (Black) (each 0–1). PDF DeviceCMYK. PDF 32000-1:2008 §8.6.4.4. |

---

_Source: [`src/Chuvadi.Pdf.Graphics/ColorSpace.cs`](../../../src/Chuvadi.Pdf.Graphics/ColorSpace.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
