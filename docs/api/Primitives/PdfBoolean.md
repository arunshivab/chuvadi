# PdfBoolean

**Class** in `Chuvadi.Pdf.Primitives` (Primitives)

Represents a PDF boolean value (`true` or `false`). Use `True` and `False` singletons rather than constructing new instances. PDF 32000-1:2008 §7.3.2 — Boolean objects.

```csharp
public sealed class PdfBoolean : PdfPrimitive
```

## Constructors

### `static implicit operator PdfBoolean(bool b) => FromBool(b)`

Implicitly converts a `bool` to a `PdfBoolean`.

## Properties

### `Value`

```csharp
bool Value
```

Gets the boolean value.

### `PrimitiveType`

```csharp
override PdfPrimitiveType PrimitiveType => PdfPrimitiveType.Boolean
```

<inheritdoc/>

## Methods

### `new`

__static__

```csharp
static readonly PdfBoolean True = new(true)
```

The PDF boolean `true`.

### `new`

__static__

```csharp
static readonly PdfBoolean False = new(false)
```

The PDF boolean `false`.

### `FromBool`

__static__

```csharp
static PdfBoolean FromBool(bool value) => value ? True : False
```

Returns the singleton corresponding to the given boolean value.

### `ToString`

```csharp
override string ToString() => Value ? "true" : "false"
```

Returns `true` or `false` as PDF keywords.

### `bool`

__static__

```csharp
static implicit operator bool(PdfBoolean b)
```

Implicitly converts a `PdfBoolean` to a `bool`.

---

_Source: [`src/Chuvadi.Pdf.Primitives/PdfBoolean.cs`](../../../src/Chuvadi.Pdf.Primitives/PdfBoolean.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
