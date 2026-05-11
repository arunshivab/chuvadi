# PdfNull

**Class** in `Chuvadi.Pdf.Primitives` (Primitives)

Represents the PDF null object.

```csharp
public sealed class PdfNull : PdfPrimitive
```

## Remarks

There is exactly one null object in Chuvadi. Use `Value` rather than constructing new instances. PDF 32000-1:2008 §7.3.9 — Null object.

## Properties

### `PrimitiveType`

```csharp
override PdfPrimitiveType PrimitiveType => PdfPrimitiveType.Null
```

<inheritdoc/>

## Methods

### `new`

__static__

```csharp
static readonly PdfNull Value = new()
```

The singleton null object.

### `ToString`

```csharp
override string ToString() => "null"
```

Returns the PDF keyword `null`.

---

_Source: [`src/Chuvadi.Pdf.Primitives/PdfNull.cs`](../../../src/Chuvadi.Pdf.Primitives/PdfNull.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
