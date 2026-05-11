# Encryptor

**Class** in `Chuvadi.Pdf.Encryption` (Encryption)

Encrypts individual strings and streams for writing an encrypted PDF.

```csharp
public sealed class Encryptor
```

## Constructors

### `Encryptor(byte[] fileKey, EncryptionAlgorithm algorithm)`

Constructs an encryptor for the given file key and algorithm.

## Methods

### `Encrypt`

```csharp
byte[] Encrypt(byte[] data, int objectNumber, int generation)
```

Encrypts data belonging to a specific indirect object.

### `GenerateFileKeyAes256`

__static__

```csharp
static byte[] GenerateFileKeyAes256()
```

Generates a random 32-byte file key suitable for AES-256 encryption.

### `GenerateFileKeyAes128`

__static__

```csharp
static byte[] GenerateFileKeyAes128()
```

Generates a random 16-byte file key suitable for AES-128 encryption.

---

_Source: [`src/Chuvadi.Pdf.Encryption/Encryptor.cs`](../../../src/Chuvadi.Pdf.Encryption/Encryptor.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
