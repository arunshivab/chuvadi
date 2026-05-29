# OpenTypeFontBuilder

**Class** in `Chuvadi.Pdf.Fonts.Rendering` (Fonts)

Wraps a raw CFF font program in an OpenType (OTTO) SFNT envelope with the synthesised tables a browser requires (`CFF `, `cmap`, `head`, `hhea`, `hmtx`, `maxp`, `name`, `OS/2`, `post`), so the font can be embedded in an SVG `@font-face` rule and located by semantic Unicode code point.

```csharp
public static class OpenTypeFontBuilder
```

## Remarks

The raw CFF program is passed through unchanged as the `CFF ` table. Glyph metrics for `hmtx`, `head`, `hhea`, and `OS/2` are read from `CffLoader`. Created/modified timestamps are fixed at zero so the output is deterministic.

---

_Source: [`src/Chuvadi.Pdf.Fonts.Rendering/OpenTypeFontBuilder.cs`](../../../src/Chuvadi.Pdf.Fonts.Rendering/OpenTypeFontBuilder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
