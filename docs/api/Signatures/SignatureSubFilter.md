# SignatureSubFilter

**Class** in `Chuvadi.Pdf.Signatures` (Signatures)

Constants and helpers for the /SubFilter entry of a PDF signature dictionary.

```csharp
public static class SignatureSubFilter
```

## Remarks

The /SubFilter value determines how the /Contents bytes are encoded and what kind of cryptographic structure they hold.

## Methods

### `IsCmsBased`

__static__

```csharp
static bool IsCmsBased(string subFilter)
```

Returns true when `subFilter` indicates the /Contents value carries a CMS / PKCS#7 SignedData container (covered by Chuvadi.Cryptography.Cms).

## Fields

### `AdbePkcs7Detached`

```csharp
const string AdbePkcs7Detached = "adbe.pkcs7.detached"
```

adbe.pkcs7.detached — most common modern PDF signature SubFilter.

### `AdbePkcs7Sha1`

```csharp
const string AdbePkcs7Sha1 = "adbe.pkcs7.sha1"
```

adbe.pkcs7.sha1 — legacy signature; the /Contents is a PKCS#7 wrapping a SHA-1 digest.

### `AdbeX509RsaSha1`

```csharp
const string AdbeX509RsaSha1 = "adbe.x509.rsa_sha1"
```

adbe.x509.rsa_sha1 — legacy raw signature; deprecated.

### `EtsiCAdESDetached`

```csharp
const string EtsiCAdESDetached = "ETSI.CAdES.detached"
```

ETSI.CAdES.detached — CAdES-based PDF signature SubFilter (eIDAS qualified signatures).

### `EtsiRfc3161`

```csharp
const string EtsiRfc3161 = "ETSI.RFC3161"
```

ETSI.RFC3161 — PDF document timestamp SubFilter.

---

_Source: [`src/Chuvadi.Pdf.Signatures/SignatureSubFilter.cs`](../../../src/Chuvadi.Pdf.Signatures/SignatureSubFilter.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
