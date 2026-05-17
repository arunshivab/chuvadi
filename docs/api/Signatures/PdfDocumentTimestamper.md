# PdfDocumentTimestamper

**Class** in `Chuvadi.Pdf.Signatures.Signing` (Signatures)

Adds a document-wide RFC 3161 timestamp (`/Type /DocTimeStamp`) to a PDF via an incremental update. The timestamp covers the entire document up to (but not including) the new signature's `/Contents` bytes, proving the document existed and was unchanged at the TSA's time.

```csharp
public static class PdfDocumentTimestamper
```

## Remarks

Unlike an embedded signature timestamp (RFC 3161 token inside a CMS SignerInfo's unsigned attributes), a document timestamp is a standalone signature dictionary whose `/Contents` is the TSA token itself and whose `/SubFilter` is `ETSI.RFC3161`. PDF 2.0 / PAdES / ETSI EN 319 142 LTV workflows lean on this: each archival period adds a fresh document timestamp, extending the trust horizon.  

 The implementation uses `PdfWriter.WriteIncrementalUpdate`, so any pre-existing signatures keep verifying.

## Methods

### `AddDocumentTimestamp`

__static__

```csharp
static byte[] AddDocumentTimestamp(byte[] signedPdfBytes, Options options)
```

Adds a document timestamp to `signedPdfBytes`, returning the augmented bytes.

---

_Source: [`src/Chuvadi.Pdf.Signatures/Signing/PdfDocumentTimestamper.cs`](../../../src/Chuvadi.Pdf.Signatures/Signing/PdfDocumentTimestamper.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
