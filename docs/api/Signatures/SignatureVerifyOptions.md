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

### `AutoExtractDss`

```csharp
bool AutoExtractDss
```

When true (the default), the document's `/DSS` dictionary (ISO 32000-2 §12.8.4.3) is read and its certificates, CRLs, and OCSP responses are added to the verification material. Adobe-style PDF signatures with long-term validation (LTV) typically embed their revocation info this way rather than in CAdES unsigned attributes.

### `TsaTrustStore`

```csharp
TrustStore? TsaTrustStore
```

Trust anchors for evaluating the TSA's certificate chain when a signature timestamp is present. When non-null, the verifier path-validates the TSA cert at the timestamp's `genTime` using every certificate, CRL, and OCSP response it has on hand, and reports the outcome via `SignatureVerificationResult.TimestampTrustValidated` and `SignatureVerificationResult.TimestampValidatedPath`. When null (the default), only the cryptographic verification of the timestamp is performed — its trust is not evaluated. Most callers will use a TSA-specific trust store distinct from the signing-cert trust store, since the two trust regimes are independent.

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
