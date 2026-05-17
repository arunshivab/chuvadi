# PdfSigner

**Class** in `Chuvadi.Pdf.Signatures.Signing` (Signatures)

Adds a CMS signature to a PDF document and returns the signed bytes.

```csharp
public static class PdfSigner
```

## Remarks

Implements the canonical PDF signing protocol:  
 
- Add a signature field + signature dictionary referencing fixed-width `/ByteRange` placeholder slots and a zero-byte `/Contents` reservation of `PdfSigningOptions.ContentsPlaceholderSize` bytes. 
- Write the full PDF to memory; scan the output to locate the `/ByteRange` placeholder slots and the `/Contents` value. 
- Patch the `/ByteRange` slots with the actual byte positions, using leading-zero padding to preserve their fixed widths so no downstream positions shift. 
- Sign the bytes covered by `/ByteRange` with the supplied `ISigner` via `CmsSignedDataBuilder.BuildDetached`. 
- Hex-encode the CMS and splice it into the `/Contents` placeholder; the remaining placeholder bytes stay zero.  

 This is a full-rewrite signing flow: the output is a freshly-written PDF carrying the new signature. Incremental update support (preserving the original byte stream and appending) is deferred to a future session.

## Methods

### `Sign`

__static__

```csharp
static byte[] Sign(PdfDocument document, ISigner signer, PdfSigningOptions options)
```

Signs a PDF document and returns the signed bytes.

**Parameters**

- `document` — The unsigned source document. Not modified.
- `signer` — The signer.
- `options` — Signing options (signature field name, signing time, reason, etc.).

**Returns:** The signed PDF bytes.

---

_Source: [`src/Chuvadi.Pdf.Signatures/Signing/PdfSigner.cs`](../../../src/Chuvadi.Pdf.Signatures/Signing/PdfSigner.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
