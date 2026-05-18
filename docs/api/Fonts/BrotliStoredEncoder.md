# BrotliStoredEncoder

**Class** in `Chuvadi.Pdf.Fonts.Woff2` (Fonts)

Emits Brotli-compatible bitstreams for WOFF2 packaging.

```csharp
public static class BrotliStoredEncoder
```

## Remarks

Since Phase 2.2 this is a thin shim over `BrotliEncoder`, the pure-C# clean-room Brotli implementation. The shim is preserved so that callers from Phase 2.1 continue to work without source changes.  

 Behaviour: emits valid Brotli output using uncompressed (stored) meta-blocks. Output bytes are byte-identical to `BrotliEncoder.Encode`.

## Methods

### `Encode`

__static__

```csharp
static byte[] Encode(byte[] data) => BrotliEncoder.Encode(data)
```

Encodes `data` as a valid Brotli stream.

---

_Source: [`src/Chuvadi.Pdf.Fonts.Woff2/BrotliStoredEncoder.cs`](../../../src/Chuvadi.Pdf.Fonts.Woff2/BrotliStoredEncoder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
