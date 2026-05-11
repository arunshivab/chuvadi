# PdfWriter

**Class** in `Chuvadi.Pdf.IO` (IO)

Writes a complete PDF file to an output stream.

```csharp
public static class PdfWriter
```

## Remarks

`PdfWriter` performs a full rewrite — it serialises all provided indirect objects, builds a fresh cross-reference table, and writes a valid PDF trailer. Streams are written with their existing raw bytes unchanged. The `/Length` entry is updated to reflect the actual byte count. PDF version written: `%PDF-1.7`. xref format: classic cross-reference table (not a cross-reference stream). PDF 32000-1:2008 §7.5 — File structure.

---

_Source: [`src/Chuvadi.Pdf.IO/PdfWriter.cs`](../../../src/Chuvadi.Pdf.IO/PdfWriter.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
