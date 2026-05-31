# GlyphPosition

**Record** in `Chuvadi.Pdf.Rendering.DisplayList` (Rendering)

The position of a single glyph in a `TextRun`.

```csharp
public readonly record struct GlyphPosition(double X, double Y, double Advance, string Unicode)
```

---

_Source: [`src/Chuvadi.Pdf.Rendering.DisplayList/TextRun.cs`](../../../src/Chuvadi.Pdf.Rendering.DisplayList/TextRun.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
