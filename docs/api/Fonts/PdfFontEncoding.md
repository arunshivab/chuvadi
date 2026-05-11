# PdfFontEncoding

**Class** in `Chuvadi.Pdf.Fonts` (Fonts)

Maps 1-byte character codes (0-255) to Unicode codepoints for simple fonts.

```csharp
public sealed class PdfFontEncoding
```

## Remarks

PDF simple fonts use a single-byte encoding. The encoding may be: 
 
- A named standard encoding (WinAnsiEncoding, MacRomanEncoding, etc.) 
- A font's built-in encoding 
- A custom encoding with a /Differences array that overrides specific codes  This class builds the code→Unicode map from an encoding dictionary, falling back to WinAnsiEncoding when no encoding is specified. PDF 32000-1:2008 §9.6.5, §9.6.6.

## Methods

### `Build`

__static__

```csharp
static PdfFontEncoding Build(PdfPrimitive? encoding)
```

Builds an encoding from an /Encoding entry in a font dictionary.

**Parameters**

- `encoding` — The /Encoding value — either a PdfName (named encoding) or a PdfDictionary (custom encoding with optional Differences). May be null, in which case WinAnsiEncoding is used.

### `FromNamedEncoding`

__static__

```csharp
static PdfFontEncoding FromNamedEncoding(string name)
```

Returns a `PdfFontEncoding` for a standard named encoding.

### `GetCharacter`

```csharp
char GetCharacter(byte code) => _map[code]
```

Maps a 1-byte character code to a Unicode character. Returns '\0' when the code is not mapped.

### `IsMapped`

```csharp
bool IsMapped(byte code) => _map[code] != '\0'
```

Returns true when the character code has a Unicode mapping.

### `GlyphNameToUnicode`

__static__

```csharp
static char GlyphNameToUnicode(string name)
```

Maps a PDF glyph name to its Unicode codepoint. Returns '\0' for unknown names. Covers the Adobe Glyph List subset most commonly seen in PDFs. PDF 32000-1:2008 §9.6.6 — use of glyph names.

---

_Source: [`src/Chuvadi.Pdf.Fonts/PdfFontEncoding.cs`](../../../src/Chuvadi.Pdf.Fonts/PdfFontEncoding.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
