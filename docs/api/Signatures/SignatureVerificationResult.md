# SignatureVerificationResult

**Class** in `Chuvadi.Pdf.Signatures.Verification` (Signatures)

The result of verifying a PDF digital signature.

```csharp
public sealed class SignatureVerificationResult
```

## Properties

### `Status`

```csharp
SignatureVerificationStatus Status
```

The overall outcome.

### `Message`

```csharp
string Message
```

A human-readable explanation of the result.

### `SignerCertificate`

```csharp
X509Certificate? SignerCertificate
```

The signer's certificate, when located inside the CMS envelope.

### `IntegrityVerified`

```csharp
bool IntegrityVerified
```

True iff the cryptographic signature checks out AND the signed bytes' digest matches the messageDigest signed attribute. This is the strict cryptographic answer regardless of whether the signer is to be believed.

### `TrustValidated`

```csharp
bool TrustValidated
```

True iff `IntegrityVerified` is true AND the signer's certificate chain validates to a configured trust anchor per RFC 5280 §6.1. False when no trust store was supplied or path validation failed.

### `ValidatedPath`

```csharp
CertificatePath? ValidatedPath
```

The certificate path that validated against the trust store, when `TrustValidated` is true.

### `IsValid`

```csharp
bool IsValid => Status == SignatureVerificationStatus.Valid
```

Convenience shorthand for `Status == Valid`.

---

_Source: [`src/Chuvadi.Pdf.Signatures/Verification/SignatureVerificationResult.cs`](../../../src/Chuvadi.Pdf.Signatures/Verification/SignatureVerificationResult.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
