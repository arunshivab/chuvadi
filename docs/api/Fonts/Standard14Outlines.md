# Standard14Outlines

**Class** in `Chuvadi.Pdf.Fonts.Rendering` (Fonts)

Provides glyph outlines for the PDF Standard 14 fonts from an embedded resource, so they work even on hosts that lack the fonts (Blazor WASM, headless servers).

```csharp
public static class Standard14Outlines
```

## Remarks

The bundle is generated at build time by `tools/build_standard14_bundle.py` from Liberation Sans/Serif/Mono and URW StandardSymbolsPS/D050000L (commercially-redistributable, Apache-2.0-compatible licenses). If the developer hasn't run the build tool with the source TTFs in place, the bundle ships as a header-only placeholder and outline lookups return empty paths — width-only operation still works via `Standard14Widths`.

## Properties

### `BundleAvailable`

__static__

```csharp
static bool BundleAvailable => Loaded.Value.HasRealOutlines
```

True when the bundle was built from real font data.

### `KnownFonts`

__static__

```csharp
static IReadOnlyCollection<string> KnownFonts => Loaded.Value.Fonts.Keys
```

Returns the list of font names known to the bundle.

## Methods

### `GetGlyphPath`

__static__

```csharp
static GraphicsPath GetGlyphPath(string fontName, char ch)
```

Returns the outline path for `ch` in the given font.

**Returns:** An empty path when the font or character isn't in the bundle.

---

_Source: [`src/Chuvadi.Pdf.Fonts.Rendering/Standard14Outlines.cs`](../../../src/Chuvadi.Pdf.Fonts.Rendering/Standard14Outlines.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
