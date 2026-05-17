# LtvOptions

**Class** in `Chuvadi.Pdf.Signatures.Signing` (Signatures)

Long-term validation material to embed in a PDF at sign time.

```csharp
public sealed class LtvOptions
```

## Remarks

When supplied via `PdfSigningOptions.LtvOptions`, Chuvadi emits a `/DSS` dictionary (ISO 32000-2 §12.8.4.3) into the document during signing. The DSS carries certificates (typically the signer's chain), CRLs covering each chain link, and OCSP responses for any link relying on OCSP for revocation. With this material baked into the document, a verifier can check the signature offline at any point in the future without re-contacting the issuing CAs.  

 When `IncludeVri` is true, the same material is additionally emitted as a per-signature `/VRI` sub-dictionary entry keyed by SHA-1 of the CMS `/Contents` bytes. This is optional — the document-level material is usually enough — but is the convention used by Adobe products and what the Phase 1.1.4 verifier picks up when present.

## Properties

### `Certificates`

```csharp
IReadOnlyList<X509Certificate>? Certificates
```

Certificates to embed (typically the signer's CA chain). The signer's own certificate is already embedded in the CMS; adding it here is harmless but redundant.

### `Crls`

```csharp
IReadOnlyList<CertificateList>? Crls
```

CRLs to embed, covering links in the signer's chain.

### `OcspResponses`

```csharp
IReadOnlyList<OcspResponse>? OcspResponses
```

OCSP responses to embed, covering links in the signer's chain.

### `IncludeVri`

```csharp
bool IncludeVri
```

When true, additionally emit a `/VRI` sub-dictionary entry keyed by SHA-1 of the signature's `/Contents` bytes, carrying the same material.

---

_Source: [`src/Chuvadi.Pdf.Signatures/Signing/LtvOptions.cs`](../../../src/Chuvadi.Pdf.Signatures/Signing/LtvOptions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
