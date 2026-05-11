# AesCrypto

**Class** in `Chuvadi.Pdf.Encryption` (Encryption)

AES-CBC encryption/decryption with PDF's IV-prefix wire format.

```csharp
public static class AesCrypto
```

## Remarks

PDF 32000 mandates CBC mode with PKCS#7 padding. The 16-byte IV is stored as a prefix on the ciphertext; ciphertext length is therefore always a multiple of 16 plus 16 bytes for the IV. AES-128 uses a 128-bit key; AES-256 uses a 256-bit key. The mode is identical.

## Methods

### `Decrypt`

__static__

```csharp
static byte[] Decrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> ivAndCipher)
```

Decrypts data that begins with a 16-byte IV followed by AES-CBC ciphertext.

### `Encrypt`

__static__

```csharp
static byte[] Encrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> plain)
```

Encrypts data with AES-CBC and prefixes a 16-byte IV. The IV is generated from a cryptographically strong random source.

---

_Source: [`src/Chuvadi.Pdf.Encryption/AesCrypto.cs`](../../../src/Chuvadi.Pdf.Encryption/AesCrypto.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
