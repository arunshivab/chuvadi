# BrotliStoredEncoder

**Class** in `Chuvadi.Pdf.Fonts.Woff2` (Fonts)

Emits Brotli-compatible bitstreams for WOFF2 packaging.

```csharp
public static class BrotliStoredEncoder
```

## Remarks

Uses `System.IO.Compression.BrotliStream` under the hood with `CompressionLevel.NoCompression` to emit valid Brotli streams of stored blocks. This is pure-BCL (no external dependencies) and produces byte streams accepted by every conforming Brotli decoder.  

 Phase 2.2 will replace this with a hand-rolled compressor that does actual LZ77 matching and Huffman coding for better compression ratios. The API is stable so swapping the implementation is non-breaking.

## Methods

### `Encode`

__static__

```csharp
static byte[] Encode(byte[] data)
```

Encodes `data` as a valid Brotli stream.

---

_Source: [`src/Chuvadi.Pdf.Fonts.Woff2/BrotliStoredEncoder.cs`](../../../src/Chuvadi.Pdf.Fonts.Woff2/BrotliStoredEncoder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
