# IStreamFilter

**Interface** in `Chuvadi.Pdf.Filters` (Filters)

Defines the contract for a PDF stream filter.

```csharp
public interface IStreamFilter
```

## Remarks

PDF streams may be compressed or encoded using one or more filters specified in the stream dictionary's `/Filter` entry. Each filter implementation handles one filter name. Filters are applied in sequence when decoding (Decode removes the filter) and in reverse sequence when encoding (Encode applies the filter). PDF 32000-1:2008 §7.4 — Filters.

---

_Source: [`src/Chuvadi.Pdf.Filters/IStreamFilter.cs`](../../../src/Chuvadi.Pdf.Filters/IStreamFilter.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
