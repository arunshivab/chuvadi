# XrefStreamTable

**Class** in `Chuvadi.Pdf.Objects` (Objects)

Reads and writes PDF 1.5+ cross-reference streams.

```csharp
public sealed class XrefStreamTable
```

## Remarks

Cross-reference streams replace (or supplement) the classic xref table in PDF 1.5 and later. They are regular PDF stream objects whose content encodes xref entries as binary integers. The stream dictionary must contain: 
 
- `/Type /XRef` 
- `/Size` — highest object number + 1 
- `/W` — array of three field widths in bytes 
- `/Index` — optional array of subsection ranges  The `/W` array [w1, w2, w3] gives the byte widths of the three fields in each entry: 
 
- Field 1: entry type (0=free, 1=in-use, 2=compressed) 
- Field 2: offset/object-number/next-free 
- Field 3: generation/index-in-stream  A width of 0 means the field is absent and defaults to 0. PDF 32000-1:2008 §7.5.8 — Cross-reference streams.

## Constructors

### `XrefStreamTable()`

Creates an empty `XrefStreamTable`.

## Properties

### `Entries`

```csharp
IReadOnlyList<XrefEntry> Entries => _entries
```

Gets all entries in this xref stream.

### `Count`

```csharp
int Count => _entries.Count
```

Gets the number of entries.

## Methods

### `Add`

```csharp
void Add(XrefEntry entry)
```

Adds an entry to this xref stream.

### `Parse`

__static__

```csharp
static XrefStreamTable Parse(PdfDictionary dictionary, byte[] decodedBytes)
```

Parses a cross-reference stream from its decoded byte content and stream dictionary.

**Parameters**

- `dictionary` — The xref stream dictionary.
- `decodedBytes` — The decompressed stream content (after filter removal).

**Returns:** A populated `XrefStreamTable`. <exception cref="PdfObjectException"> Thrown when the stream is malformed. </exception>

### `Encode`

```csharp
byte[] Encode(int w1 = 1, int w2 = 4, int w3 = 2)
```

Encodes this xref stream's entries as binary bytes suitable for embedding in a PDF stream.

**Parameters**

- `w1` — Byte width of the type field (recommend 1).
- `w2` — Byte width of the offset field (recommend 4 or 8).
- `w3` — Byte width of the generation field (recommend 2).

**Returns:** The encoded binary content, ready for compression.

---

_Source: [`src/Chuvadi.Pdf.Objects/XrefStreamTable.cs`](../../../src/Chuvadi.Pdf.Objects/XrefStreamTable.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
