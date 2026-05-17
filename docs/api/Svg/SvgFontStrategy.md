# SvgFontStrategy

**Enum** in `Chuvadi.Pdf.Svg` (Svg)

How embedded fonts are handled.

```csharp
public enum SvgFontStrategy
```

## Values

| Name | Description |
|---|---|
| `EmbedAsWebFont` | Embed via `@font-face` blocks. TrueType, OpenType, CFF supported. Type 1 falls back to a CSS family. Default. |
| `CssFallbackOnly` | Skip embedding; rely on CSS family fallbacks. |

---

_Source: [`src/Chuvadi.Pdf.Svg/SvgExportOptions.cs`](../../../src/Chuvadi.Pdf.Svg/SvgExportOptions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
