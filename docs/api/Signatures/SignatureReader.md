# SignatureReader

**Class** in `Chuvadi.Pdf.Signatures` (Signatures)

Reads digital-signature fields out of a PDF document's AcroForm tree.

```csharp
public static class SignatureReader
```

## Methods

### `Read`

__static__

```csharp
static IReadOnlyList<PdfSignature> Read(PdfDictionary catalog, IPdfObjectResolver resolver)
```

Walks the AcroForm tree under `catalog` and returns one `PdfSignature` per signature field that has a value.

**Parameters**

- `catalog` — The document catalog dictionary.
- `resolver` — Object resolver for indirect references.

**Returns:** The signatures in field order; empty when the document has none.

### `ExtractSignedBytes`

__static__

```csharp
static byte[] ExtractSignedBytes(byte[] fileBytes, ByteRange byteRange)
```

Builds the contiguous byte sequence covered by `byteRange` from `fileBytes`.

### `WriteSignedBytes`

__static__

```csharp
static void WriteSignedBytes(System.IO.Stream source, ByteRange byteRange, System.IO.Stream destination)
```

Streams the bytes covered by `byteRange` from `source` into `destination`.

**Remarks:** Use this overload for files larger than 2 GiB or when the caller wants to feed a hash function incrementally rather than materialising the signed bytes as a single array.

---

_Source: [`src/Chuvadi.Pdf.Signatures/SignatureReader.cs`](../../../src/Chuvadi.Pdf.Signatures/SignatureReader.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
