# RedactionOptions

**Class** in `Chuvadi.Pdf.Redaction` (Redaction)

Top-level configuration for a redaction operation.

```csharp
public sealed class RedactionOptions
```

## Constructors

### `RedactionOptions()`

Initialises `RedactionOptions` with default values.

## Properties

### `Rectangles`

```csharp
IList<RedactionRect> Rectangles
```

Gets or initialises the list of rectangles to redact, by page.

### `OverlayColor`

```csharp
ColorF OverlayColor
```

Gets or initialises the colour painted over each redacted rectangle. Default: opaque black.

---

_Source: [`src/Chuvadi.Pdf.Redaction/RedactionOptions.cs`](../../../src/Chuvadi.Pdf.Redaction/RedactionOptions.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
