# ChuvadiPdfReader

**Class** in `Chuvadi.Pdf.Reader` (Reader)

Production implementation of `IPdfReader` backed by the Chuvadi PDF library. Register as a singleton in DI; methods are stateless apart from the cached `SvgRenderer` instances (full-page and thumbnail), which are thread-safe to share.

```csharp
public sealed class ChuvadiPdfReader : IPdfReader
```

## Constructors

### `ChuvadiPdfReader()`

Initialises a new `ChuvadiPdfReader`.

---

_Source: [`src/Chuvadi.Pdf.Reader/ChuvadiPdfReader.cs`](../../../src/Chuvadi.Pdf.Reader/ChuvadiPdfReader.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
