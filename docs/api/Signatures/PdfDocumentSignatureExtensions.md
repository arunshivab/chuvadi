# PdfDocumentSignatureExtensions

**Class** in `Chuvadi.Pdf.Signatures` (Signatures)

Signature-related extensions on `PdfDocument`.

```csharp
public static class PdfDocumentSignatureExtensions
```

## Methods

### `Signatures`

__static__

```csharp
static IReadOnlyList<PdfSignature> Signatures(this PdfDocument document)
```

Returns the digital signatures found in `document`'s AcroForm tree, in field order.

**Returns:** An empty list when the document has no signatures.

### `GetSignedBytes`

__static__

```csharp
static byte[] GetSignedBytes(this PdfDocument document, PdfSignature signature)
```

Reads the bytes covered by `signature`'s /ByteRange from the underlying file.

**Remarks:** These are the bytes whose hash the signature actually covers. Pass them (or a hash of them) to the verification step alongside the decoded CMS SignedData.

---

_Source: [`src/Chuvadi.Pdf.Signatures/PdfDocumentSignatureExtensions.cs`](../../../src/Chuvadi.Pdf.Signatures/PdfDocumentSignatureExtensions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
