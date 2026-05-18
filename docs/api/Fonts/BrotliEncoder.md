# BrotliEncoder

**Class** in `Chuvadi.Pdf.Fonts.Woff2` (Fonts)

Pure-C# Brotli encoder.

```csharp
public static class BrotliEncoder
```

## Remarks

Current scope: emits valid Brotli streams using uncompressed (stored) meta-blocks only. The bit-level meta-block header is implemented from RFC 7932 §9 and verified against `System.IO.Compression.BrotliStream` as a reference decoder. Each call emits one stored meta-block per slice of input (max 16 MiB per meta-block per the MNIBBLES=4 encoding) followed by a trailing empty meta-block with `ISLAST=1`.  

 Compressed meta-blocks (with Huffman coding and LZ77 back-references) are planned for subsequent Phase 2.2 stages; the LZ77 matcher in `BrotliCommandStream` already produces the command stream they will consume. The current public surface is stable across that transition.

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
