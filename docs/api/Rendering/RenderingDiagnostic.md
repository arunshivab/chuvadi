# RenderingDiagnostic

**Record** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

A single diagnostic event recorded by `DisplayListBuilder` during page construction. Callers can inspect `PageDisplayList.Diagnostics` to detect graceful-degradation conditions that were previously silent.

```csharp
public sealed record RenderingDiagnostic(DiagnosticKind Kind, string Message)
```

## Parameters

- `Kind` — The category of diagnostic event.
- `Message` — Human-readable description of what went wrong, including context (e.g. the font key that could not be resolved). Not localised.

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/RenderingDiagnostic.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/RenderingDiagnostic.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
