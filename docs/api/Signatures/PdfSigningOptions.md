# PdfSigningOptions

**Class** in `Chuvadi.Pdf.Signatures.Signing` (Signatures)

Options for `PdfSigner.Sign`.

```csharp
public sealed class PdfSigningOptions
```

## Properties

### `SignatureFieldName`

```csharp
string SignatureFieldName
```

The signature field's `/T` (partial field name). Defaults to `Signature1`.

### `Reason`

```csharp
string? Reason
```

Optional `/Reason` entry on the signature dictionary (e.g. `"I approve this document"`).

### `Location`

```csharp
string? Location
```

Optional `/Location` entry on the signature dictionary (e.g. the signer's city).

### `ContactInfo`

```csharp
string? ContactInfo
```

Optional `/ContactInfo` entry (e.g. an email address for follow-up).

### `SigningTime`

```csharp
DateTimeOffset? SigningTime
```

The signing time recorded both in the signature dictionary's `/M` entry and in the CMS `signingTime` signed attribute. Defaults to `DateTimeOffset.UtcNow` at sign time.

### `ContentsPlaceholderSize`

```csharp
int ContentsPlaceholderSize
```

The number of bytes reserved for the CMS signature inside the `/Contents` placeholder. Must be at least as large as the produced CMS. Defaults to 16384 (16 KiB), which comfortably accommodates an RSA-2048 signature with a small chain.

### `ExtraCertificates`

```csharp
IEnumerable<X509Certificate>? ExtraCertificates
```

Additional certificates to include in the CMS SignedData alongside the signer's own certificate (typically the issuing CA chain).

### `TsaClient`

```csharp
ITsaClient? TsaClient
```

When non-null, an RFC 3161 timestamp is fetched from this client over the SignerInfo's signature and embedded as an `id-aa-signatureTimeStampToken` unsigned attribute.

### `LtvOptions`

```csharp
LtvOptions? LtvOptions
```

When non-null, validation material is embedded in a `/DSS` dictionary (ISO 32000-2 §12.8.4.3) so that the signature can be validated offline at any time after signing — Long-Term Validation (LTV).

---

_Source: [`src/Chuvadi.Pdf.Signatures/Signing/PdfSigningOptions.cs`](../../../src/Chuvadi.Pdf.Signatures/Signing/PdfSigningOptions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
