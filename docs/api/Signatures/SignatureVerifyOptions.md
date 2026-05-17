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

### `AutoExtractCadesValues`

```csharp
bool AutoExtractCadesValues
```

When true (the default), CAdES unsigned attributes `id-aa-ets-certValues` and `id-aa-ets-revocationValues` are extracted from the SignerInfo's unsigned attributes and used as additional intermediates and revocation info. Set to false to disable.

### `AutoVerifySignatureTimestamp`

```csharp
bool AutoVerifySignatureTimestamp
```

When true (the default), the `id-aa-signatureTimeStampToken` unsigned attribute is located, decoded, and cryptographically verified. If a timestamp validates and the caller did not supply an explicit `ValidationTime`, the timestamp's genTime is used as the validation time for certificate-chain evaluation. This is the CAdES-T pattern: the timestamp records WHEN the signature existed, so the chain is evaluated at that time even if certificates have since expired.

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
