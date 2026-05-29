# CffLoader

**Class** in `Chuvadi.Pdf.Fonts.Rendering` (Fonts)

Loads a Compact Font Format (CFF) / Type 1C font program and produces glyph outlines. Matches the public surface of `TrueTypeLoader` for interoperability.

```csharp
public sealed class CffLoader
```

## Remarks

CFF is the format embedded in PDFs via `/FontFile3` with subtype `Type1C` (simple CFF) or `CIDFontType0C` (CID-keyed CFF).  

 Glyph outlines are produced by interpreting Type 2 charstrings — a stack-based language with about 50 operators covering point movement, curve construction, and subroutine calls. The interpreter here implements the operators that appear in font files in practice; hint operators (`hstem`, `vstem`, `hintmask`, `cntrmask`) are parsed for stack consistency but produce no outline output.

## Constructors

### `CffLoader(byte[] fontData)`

Parses a CFF font program.

## Properties

### `UnitsPerEm`

```csharp
int UnitsPerEm => _unitsPerEm
```

Font units per em (typically 1000 for CFF).

### `NumGlyphs`

```csharp
int NumGlyphs => _charStrings.Count
```

Number of glyphs in the font.

### `IsCidFont`

```csharp
bool IsCidFont => _isCidFont
```

Whether the font is CID-keyed (CIDFontType0C). When `true`, `CidToGid` is populated from the charset; when `false`, `GlyphNameToGid` is populated instead.

### `CidToGid`

```csharp
IReadOnlyDictionary<int, int> CidToGid => _cidToGid
```

For CID-keyed fonts, maps each character identifier (CID) to its glyph index (GID). Empty for simple (Type1C) fonts.

### `GlyphNameToGid`

```csharp
IReadOnlyDictionary<string, int> GlyphNameToGid => _glyphNameToGid
```

For simple (Type1C) fonts, maps each glyph name to its glyph index (GID). Empty for CID-keyed fonts.

## Methods

### `GetGlyphIndex`

```csharp
int GetGlyphIndex(int codePoint)
```

Maps a Unicode code point to a glyph index. Returns 0 if not mapped.

### `GetGlyphOutline`

```csharp
GlyphOutline GetGlyphOutline(int glyphId)
```

Returns the rendered outline of the glyph with the given GID.

### `GetGlyphMetrics`

```csharp
GlyphMetrics GetGlyphMetrics(int glyphId) => GetGlyphOutline(glyphId).Metrics
```

Returns metrics-only data for the glyph with the given GID.

---

_Source: [`src/Chuvadi.Pdf.Fonts.Rendering/CffLoader.cs`](../../../src/Chuvadi.Pdf.Fonts.Rendering/CffLoader.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
