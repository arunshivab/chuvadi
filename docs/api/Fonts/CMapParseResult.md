# CMapParseResult

**Class** in `Chuvadi.Pdf.Fonts` (Fonts)

The full result of parsing a ToUnicode CMap: the bf-char/bf-range mappings and the declared codespace ranges.

```csharp
public sealed class CMapParseResult
```

## Properties

### `Mapping`

```csharp
required IReadOnlyDictionary<int, string> Mapping
```

Code → Unicode string mapping from bfchar/bfrange sections.

### `CodespaceRanges`

```csharp
required IReadOnlyList<CodespaceRange> CodespaceRanges
```

Declared codespace ranges from begincodespacerange sections.

---

_Source: [`src/Chuvadi.Pdf.Fonts/CMapParser.cs`](../../../src/Chuvadi.Pdf.Fonts/CMapParser.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
