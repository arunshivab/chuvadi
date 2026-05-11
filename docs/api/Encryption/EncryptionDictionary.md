# EncryptionDictionary

**Class** in `Chuvadi.Pdf.Encryption` (Encryption)

Parsed view of a PDF's /Encrypt trailer entry. Identifies the algorithm, key length, password verification values, and permission flags.

```csharp
public sealed class EncryptionDictionary
```

## Properties

### `Algorithm`

```csharp
EncryptionAlgorithm Algorithm
```

Encryption algorithm in use.

### `V`

```csharp
int V
```

/V entry (algorithm version 1..5).

### `R`

```csharp
int R
```

/R entry (revision 2..6).

### `KeyBits`

```csharp
int KeyBits
```

/Length entry: key length in bits.

### `KeyBytes`

```csharp
int KeyBytes => KeyBits / 8
```

Key length in bytes (KeyBits / 8).

### `Permissions`

```csharp
int Permissions
```

/P entry: permission flags.

### `O`

```csharp
byte[] O
```

/O entry: owner-password verification bytes.

### `U`

```csharp
byte[] U
```

/U entry: user-password verification bytes.

### `OE`

```csharp
byte[] OE
```

/OE entry (R=6 only): encrypted file key from owner password.

### `UE`

```csharp
byte[] UE
```

/UE entry (R=6 only): encrypted file key from user password.

### `Perms`

```csharp
byte[] Perms
```

/Perms entry (R=6 only): encrypted permissions check.

### `EncryptMetadata`

```csharp
bool EncryptMetadata
```

/EncryptMetadata entry (default true).

## Methods

### `Parse`

__static__

```csharp
static EncryptionDictionary? Parse(PdfDictionary? dict)
```

Parses an /Encrypt dictionary. Returns null when the dictionary is missing or uses an unsupported security handler.

---

_Source: [`src/Chuvadi.Pdf.Encryption/EncryptionDictionary.cs`](../../../src/Chuvadi.Pdf.Encryption/EncryptionDictionary.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
