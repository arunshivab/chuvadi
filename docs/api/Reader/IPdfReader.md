# IPdfReader

**Interface** in `Chuvadi.Pdf.Reader` (Reader)

High-level facade over the Chuvadi library for interactive PDF readers. Designed for Blazor WebAssembly apps and any other consumer that wants a small, mockable surface area instead of wiring the lower-level modules (Documents, Rendering, Svg, Text, etc.) directly.

```csharp
public interface IPdfReader
```

## Remarks

All methods are asynchronous. Some operations (rendering, outline traversal) are CPU-bound and complete synchronously internally; they are still surfaced as Task-returning methods so that callers can use a uniform `await`-everywhere idiom and so that the facade can become genuinely asynchronous in future without breaking callers.

---

_Source: [`src/Chuvadi.Pdf.Reader/IPdfReader.cs`](../../../src/Chuvadi.Pdf.Reader/IPdfReader.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
