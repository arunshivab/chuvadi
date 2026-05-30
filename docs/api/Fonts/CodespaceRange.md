# CodespaceRange

**Record** in `Chuvadi.Pdf.Fonts` (Fonts)

A declared codespace range from a CMap's `begincodespacerange ... endcodespacerange` block.

```csharp
public record CodespaceRange(int Lo, int Hi, int ByteCount)
```

## Remarks

PDF 32000-1:2008 §9.7.6.2 — codespace ranges declare how the input byte stream is partitioned into character codes. A CMap can mix 1-byte and multi-byte ranges; the decoder uses the longest declared byte count as the upper bound when matching codes.

## Parameters

- `Lo` — Inclusive low code point (bytes packed big-endian into an integer).
- `Hi` — Inclusive high code point (bytes packed big-endian into an integer).
- `ByteCount` — Number of source bytes consumed by codes in this range.

---

_Source: [`src/Chuvadi.Pdf.Fonts/CMapParser.cs`](../../../src/Chuvadi.Pdf.Fonts/CMapParser.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
