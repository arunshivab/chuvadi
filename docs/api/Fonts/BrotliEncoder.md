# BrotliEncoder

**Class** in `Chuvadi.Pdf.Fonts.Woff2` (Fonts)

Pure-C# Brotli encoder.

```csharp
public static class BrotliEncoder
```

## Remarks

Produces valid Brotli streams using LZ77-based compressed meta-blocks. For each call, the encoder runs the LZ77 matcher in `BrotliCommandStream` to produce an insert-and-copy command stream, then `BrotliCompressedEmitter` emits one or more compressed meta-blocks with per-block Huffman trees over the literal, insert-and-copy, and distance alphabets.  

 The encoder also speculatively emits a stored-meta-block variant and returns whichever is smaller. This avoids size regressions on inputs where the compression overhead (prefix-code declarations, frequency overhead for tiny alphabets) exceeds the savings — typically very short or highly-uniform inputs.  

 Output is validated to round-trip through any conformant Brotli decoder including `System.IO.Compression.BrotliStream`.

## Methods

### `Encode`

__static__

```csharp
static byte[] Encode(byte[] data)
```

Encodes `data` as a valid Brotli stream.

---

_Source: [`src/Chuvadi.Pdf.Fonts.Woff2/BrotliEncoder.cs`](../../../src/Chuvadi.Pdf.Fonts.Woff2/BrotliEncoder.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
