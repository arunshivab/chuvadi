# Woff2Packer

**Class** in `Chuvadi.Pdf.Fonts.Woff2` (Fonts)

Packs a TrueType / OpenType font into the WOFF2 container format.

```csharp
public static class Woff2Packer
```

## Remarks

WOFF2 = WOFF header + table directory + Brotli-compressed concatenated table bodies. Phase 2.1 v1 uses `BrotliStoredEncoder` which emits stored (uncompressed) Brotli blocks — the resulting WOFF2 file is valid for every conforming WOFF2 decoder (including all modern browsers) but does not realize compression gains. Phase 2.2 will add a real Brotli compressor.  

 The transformed-glyf and transformed-loca optimizations are not applied in v1: tables are passed through verbatim. This is also spec-compliant (the transform is optional per the spec).

## Properties

### `ProducesCompressedOutput`

__static__

```csharp
static bool ProducesCompressedOutput => false
```

True if WOFF2 packing is fully effective. Phase 2.1 = false (stored Brotli).

## Methods

### `Pack`

__static__

```csharp
static byte[] Pack(byte[] sfntFont)
```

Packs a TrueType/OpenType font into a WOFF2 byte stream.

---

_Source: [`src/Chuvadi.Pdf.Fonts.Woff2/Woff2Packer.cs`](../../../src/Chuvadi.Pdf.Fonts.Woff2/Woff2Packer.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
