# DiagnosticKind

**Enum** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

Classifies a `RenderingDiagnostic`. New values should be added at the end so existing serialised or persisted enums survive.

```csharp
public enum DiagnosticKind
```

## Values

| Name | Description |
|---|---|
| `DecodeFallback` | The builder could not fully resolve a font and fell back to Latin-1 byte-passthrough decoding. The resulting text characters equal the raw byte codes from the content stream rather than their proper Unicode mapping; downstream output (e.g. SVG `&lt;text&gt;`) will visibly degrade. Most commonly caused by the font dictionary not being resolvable (missing entry, unresolvable indirect reference, malformed ToUnicode CMap, etc.). |

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/RenderingDiagnostic.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/RenderingDiagnostic.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
