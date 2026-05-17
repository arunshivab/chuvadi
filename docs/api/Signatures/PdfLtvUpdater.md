# PdfLtvUpdater

**Class** in `Chuvadi.Pdf.Signatures.Signing` (Signatures)

Adds (or augments) a Long-Term Validation `/DSS` dictionary on an already-signed PDF, optionally emitting `/VRI` entries keyed by SHA-1 of each signature's `/Contents`.

```csharp
public static class PdfLtvUpdater
```

## Remarks

The result is appended as an ISO 32000-1 §7.5.6 incremental update, so all existing signatures on the source document remain valid: the new `/DSS` bytes sit outside their byte ranges.  

 This is the natural counterpart to `PdfSigner`'s LTV embedding: where `PdfSigner` bakes `/DSS` in at sign time (without VRI, because the VRI key would land inside the signed range), `PdfLtvUpdater` adds `/DSS` + `/VRI` after signing. Common workflows:  
 
- Sign with the LTV material you have at sign time, then call `AddLtvMaterial` later (after fresh OCSP / CRL fetches) to refresh the validation data without invalidating the signature. 
- Sign without any LTV material, then add a full `/DSS` + `/VRI` pair afterward.

## Methods

### `AddLtvMaterial`

__static__

```csharp
static byte[] AddLtvMaterial(byte[] signedPdfBytes, LtvOptions material)
```

Appends an incremental update carrying a `/DSS` dictionary that merges `material` with any existing DSS in the source.

**Parameters**

- `signedPdfBytes` — The source PDF (must be signed; any existing /DSS is preserved and extended).
- `material` — The LTV material to embed. `IncludeVri` controls whether per-signature VRI entries are added.

---

_Source: [`src/Chuvadi.Pdf.Signatures/Signing/PdfLtvUpdater.cs`](../../../src/Chuvadi.Pdf.Signatures/Signing/PdfLtvUpdater.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
