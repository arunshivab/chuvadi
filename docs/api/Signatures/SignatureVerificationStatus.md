# SignatureVerificationStatus

**Enum** in `Chuvadi.Pdf.Signatures.Verification` (Signatures)

The overall outcome of verifying a PDF signature.

```csharp
public enum SignatureVerificationStatus
```

## Values

| Name | Description |
|---|---|
| `Valid` | The signature is cryptographically valid: the message digest matches the signed bytes and the signature decrypts cleanly against the signing certificate's public key. |
| `Invalid` | The cryptographic signature does not match. |
| `DigestMismatch` | The signature's message digest does not match the hash of the signed bytes. |
| `SignerCertificateNotFound` | The signer certificate could not be located inside the CMS envelope. |
| `UnsupportedSubFilter` | The /SubFilter is not CMS-based; Chuvadi does not know how to verify it. |
| `UnsupportedAlgorithm` | The signature uses an algorithm Chuvadi does not implement. |
| `MalformedSignature` | The signature container could not be parsed. |

---

_Source: [`src/Chuvadi.Pdf.Signatures/Verification/SignatureVerificationStatus.cs`](../../../src/Chuvadi.Pdf.Signatures/Verification/SignatureVerificationStatus.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
