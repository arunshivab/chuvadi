# EncryptionAlgorithm

**Enum** in `Chuvadi.Pdf.Encryption` (Encryption)

Identifies which encryption algorithm a PDF uses.

```csharp
public enum EncryptionAlgorithm
```

## Values

| Name | Description |
|---|---|
| `None` | Document is not encrypted. |
| `Rc4_40` | RC4 with 40-bit key (V=1, R=2). |
| `Rc4_128` | RC4 with 128-bit key (V=2 or V=4 with CFM=V2, R=3 or R=4). |
| `Aes_128` | AES with 128-bit key (V=4 with CFM=AESV2, R=4). |
| `Aes_256` | AES with 256-bit key, ISO 32000-2 key derivation (V=5, R=6). |

---

_Source: [`src/Chuvadi.Pdf.Encryption/EncryptionAlgorithm.cs`](../../../src/Chuvadi.Pdf.Encryption/EncryptionAlgorithm.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
