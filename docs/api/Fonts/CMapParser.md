# CMapParser

**Class** in `Chuvadi.Pdf.Fonts` (Fonts)

Parses a PDF ToUnicode CMap stream and builds a character code to Unicode string mapping.

```csharp
public sealed class CMapParser
```

## Remarks

A ToUnicode CMap is a PostScript-like text stream that maps character codes (1 to 4 bytes) to Unicode codepoints or sequences. The three key sections are: 
 
-  `begincodespacerange / endcodespacerange` — declares valid source code-byte windows.  
-  `beginbfchar / endbfchar` — individual code→Unicode mappings.  
-  `beginbfrange / endbfrange` — range mappings where a contiguous block of codes maps to a contiguous block of Unicode values.   PDF 32000-1:2008 §9.10.3 — ToUnicode CMaps. The bf-char and bf-range sections accept hex source codes of any byte width (1, 2, 3, 4 bytes); the codespacerange block tells the decoder how to slice the byte stream into codes of that width.

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

**Returns:** A dictionary where keys are character codes (1- to 4-byte codes packed big-endian into int) and values are the corresponding Unicode strings.

**Remarks:** Equivalent to `ParseFull`.`CMapParseResult.Mapping`. Retained for callers that only need the code→Unicode mapping and don't care about codespace declarations.

### `ParseFull`

```csharp
CMapParseResult ParseFull()
```

Parses the CMap and returns the full result, including the code→Unicode mapping and the declared codespace ranges.

**Returns:** A `CMapParseResult` with both pieces.

---

_Source: [`src/Chuvadi.Pdf.Fonts/CMapParser.cs`](../../../src/Chuvadi.Pdf.Fonts/CMapParser.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
