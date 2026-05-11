# CMapParser

**Class** in `Chuvadi.Pdf.Fonts` (Fonts)

Parses a PDF ToUnicode CMap stream and builds a character code to Unicode string mapping.

```csharp
public sealed class CMapParser
```

## Remarks

A ToUnicode CMap is a PostScript-like text stream that maps character codes (1 or 2 bytes) to Unicode codepoints or sequences. The two key sections are: 
 
-  `beginbfchar / endbfchar` — individual code→Unicode mappings.  
-  `beginbfrange / endbfrange` — range mappings where a contiguous block of codes maps to a contiguous block of Unicode values.   PDF 32000-1:2008 §9.10.3 — ToUnicode CMaps.

## Constructors

### `CMapParser(string content)`

Initialises a new `CMapParser` over the given CMap text.

**Parameters**

- `content` — The CMap stream content as a Latin-1 string.

### `CMapParser(byte[] bytes)`

Initialises a new `CMapParser` over raw CMap bytes.

## Methods

### `Parse`

```csharp
Dictionary<int, string> Parse()
```

Parses the CMap and returns a dictionary mapping character codes (as integers) to Unicode strings.

**Returns:** A dictionary where keys are character codes (0-65535 for 2-byte CMaps, 0-255 for 1-byte CMaps) and values are the corresponding Unicode strings.

---

_Source: [`src/Chuvadi.Pdf.Fonts/CMapParser.cs`](../../../src/Chuvadi.Pdf.Fonts/CMapParser.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
