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

The signer's certificate, when it was found inside the CMS envelope. May be null when `Status` is `SignatureVerificationStatus.SignerCertificateNotFound`.

### `IntegrityVerified`

```csharp
bool IntegrityVerified
```

True when the cryptographic signature checks out and the message digest matches the bytes covered by /ByteRange.

### `IsValid`

```csharp
bool IsValid => Status == SignatureVerificationStatus.Valid
```

Convenience shorthand for `Status == Valid`.

---

_Source: [`src/Chuvadi.Pdf.Signatures/Verification/SignatureVerificationResult.cs`](../../../src/Chuvadi.Pdf.Signatures/Verification/SignatureVerificationResult.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
