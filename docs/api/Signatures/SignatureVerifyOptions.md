# SignatureVerifyOptions

**Class** in `Chuvadi.Pdf.Signatures.Verification` (Signatures)

Options controlling signature verification.

```csharp
public sealed class SignatureVerifyOptions
```

## Properties

### `TrustStore`

```csharp
TrustStore? TrustStore
```

Trust anchors to validate the signer's certificate against. When null, trust evaluation is skipped and the result reports only cryptographic integrity.

### `ExtraIntermediates`

```csharp
IReadOnlyList<X509Certificate>? ExtraIntermediates
```

Extra intermediate-CA certificates available for path building, in addition to those embedded in the CMS envelope.

### `ValidationTime`

```csharp
DateTimeOffset? ValidationTime
```

The instant at which to evaluate certificate validity. Defaults to the signing time declared by the signature, or — failing that — the current UTC time.

### `ExtraCrls`

```csharp
IReadOnlyList<CertificateList>? ExtraCrls
```

CRLs to consult for revocation checks during path validation. May be null. CRLs embedded inside the CMS envelope are still consumed automatically (subject to `AutoExtractCmsCrls`); this property provides extras such as locally-cached CRLs.

### `AutoExtractCmsCrls`

```csharp
bool AutoExtractCmsCrls
```

When true (the default), CRLs embedded in the CMS SignedData envelope are decoded and added to the revocation set. Set to false to ignore embedded CRLs and rely only on `ExtraCrls`.

### `ExtraOcspResponses`

```csharp
IReadOnlyList<OcspResponse>? ExtraOcspResponses
```

OCSP responses to consult during revocation checking. CMS does not embed OCSP responses directly; supply locally-cached or out-of-band responses here.

## Methods

### `new`

__static__

```csharp
static readonly SignatureVerifyOptions Default = new()
```

The default options (no trust evaluation).

---

_Source: [`src/Chuvadi.Pdf.Signatures/Verification/SignatureVerifyOptions.cs`](../../../src/Chuvadi.Pdf.Signatures/Verification/SignatureVerifyOptions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
