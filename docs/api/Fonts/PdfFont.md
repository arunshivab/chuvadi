# PdfFont

**Class** in `Chuvadi.Pdf.Fonts` (Fonts)

Represents a PDF font and provides character code to Unicode mapping for text extraction purposes.

```csharp
public sealed class PdfFont
```

## Remarks

Phase 1 supports text extraction only — no glyph rendering or metrics. The mapping strategy, in priority order: 
 
- ToUnicode CMap — present in most modern PDFs, most accurate. 
- Encoding + glyph name lookup — for simple fonts without ToUnicode. 
- Direct code-as-Unicode — last resort for unrecognised configurations.  PDF 32000-1:2008 §9.10.2 — Mapping character codes to Unicode values.

## Methods

### `FromDictionary`

__static__

```csharp
static PdfFont FromDictionary(PdfDictionary fontDict, IPdfObjectResolver resolver)
```

Builds a `PdfFont` from a font dictionary.

**Parameters**

- `fontDict` — The font dictionary from the page Resources.
- `resolver` — Used to resolve indirect objects (e.g. ToUnicode stream).

### `Default`

__static__

```csharp
static PdfFont Default()
```

Returns a default font that maps codes directly to their Latin-1 equivalents. Used when no font dictionary is available.

### `Decode`

```csharp
string Decode(byte[] bytes)
```

Converts a sequence of bytes from a PDF text string operator to Unicode text.

**Parameters**

- `bytes` — The raw bytes from a Tj, TJ, or similar operator.

**Returns:** The decoded Unicode string.

### `DecodeCode`

```csharp
string DecodeCode(int code)
```

Converts a single character code to a Unicode string. Returns an empty string when the code cannot be mapped.

---

_Source: [`src/Chuvadi.Pdf.Fonts/PdfFont.cs`](../../../src/Chuvadi.Pdf.Fonts/PdfFont.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
