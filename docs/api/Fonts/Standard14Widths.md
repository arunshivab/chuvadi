# Standard14Widths

**Class** in `Chuvadi.Pdf.Fonts.Rendering` (Fonts)

Provides advance-width metrics for the PDF Standard 14 fonts in 1/1000-em font design units.

```csharp
public static class Standard14Widths
```

## Remarks

Used by `RenderableFont` as the width source for Standard 14 fonts. Works even when `Standard14Outlines.BundleAvailable` is false — in that case glyphs cannot be drawn, but layout and selection still produce stable positions for the reader-app text layer.  

 Width fidelity in v2.0.0 R1 D3b:  
 
- <b>Exact</b> for all four Courier fonts (monospace, every glyph is 600/1000 em) and the space character in every family (well-known per-family AFM constants). 
- <b>Approximate</b> for variable-width fonts: a single per-font average is returned for any non-space code. Close enough that paragraph layout reads correctly; column alignment in monospaced-mimicking content may drift compared to an Adobe-exact reference renderer.  

 A follow-up will populate the full Adobe AFM tables via a codegen tool reading Liberation Fonts `hmtx` metrics, replacing the approximate path.  

 Units: 1/1000 of an em square. To convert the returned value to PDF user-space points for a given `pointSize`, multiply by `pointSize / 1000.0`.

## Methods

### `GetWidth`

__static__

```csharp
static int GetWidth(string fontName, int charCode)
```

Returns the advance width of `charCode` in `fontName` in 1/1000 em units.

**Parameters**

- `fontName` — A Standard 14 PostScript font name (e.g. "Helvetica").
- `charCode` — A WinAnsi character code (typically 0–255).

**Returns:** The advance width in 1/1000 em. For non-Standard 14 fonts returns the em-half default of 500. <exception cref="ArgumentNullException"> Thrown when `fontName` is null. </exception>

### `IsStandard14`

__static__

```csharp
static bool IsStandard14(string fontName)
```

Returns true when `fontName` matches one of the 14 Standard PostScript font names. <exception cref="ArgumentNullException"> Thrown when `fontName` is null. </exception>

## Fields

### `UnitsPerEm`

```csharp
const int UnitsPerEm = 1000
```

The units-per-em value used by all Standard 14 widths (1000).

---

_Source: [`src/Chuvadi.Pdf.Fonts.Rendering/Standard14Widths.cs`](../../../src/Chuvadi.Pdf.Fonts.Rendering/Standard14Widths.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
