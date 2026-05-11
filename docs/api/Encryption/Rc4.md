# Rc4

**Class** in `Chuvadi.Pdf.Encryption` (Encryption)

RC4 stream cipher. Symmetric: the same operation encrypts and decrypts.

```csharp
public static class Rc4
```

## Remarks

RC4 has known cryptographic weaknesses and is included only for reading legacy PDFs. New PDFs created by Chuvadi never use RC4.

## Methods

### `Process`

__static__

```csharp
static byte[] Process(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data)
```

Runs RC4 over `data` with the given key. Returns a new byte array; `data` is not modified.

---

_Source: [`src/Chuvadi.Pdf.Encryption/Rc4.cs`](../../../src/Chuvadi.Pdf.Encryption/Rc4.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
