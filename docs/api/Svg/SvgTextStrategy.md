# SvgTextStrategy

**Enum** in `Chuvadi.Pdf.Svg` (Svg)

How text is rendered to SVG.

```csharp
public enum SvgTextStrategy
```

## Values

| Name | Description |
|---|---|
| `Selectable` | Emit real `&lt;text&gt;` elements. Text is selectable, searchable, and accessible. Default. |
| `PerGlyph` | Emit one positioned glyph per character. Pixel-faithful but text is not selectable as a unit. |

---

_Source: [`src/Chuvadi.Pdf.Svg/SvgExportOptions.cs`](../../../src/Chuvadi.Pdf.Svg/SvgExportOptions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
