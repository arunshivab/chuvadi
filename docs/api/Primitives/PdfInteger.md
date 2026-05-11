# PdfInteger

**Class** in `Chuvadi.Pdf.Primitives` (Primitives)

Represents a PDF integer object. PDF 32000-1:2008 §7.3.3 — Numeric objects.

```csharp
public sealed class PdfInteger : PdfPrimitive
```

## Constructors

### `PdfInteger(int value)`

Initialises a new `PdfInteger` with the given value.

### `static implicit operator PdfInteger(int i) => new(i)`

Implicitly converts an `int` to a `PdfInteger`.

## Properties

### `Value`

```csharp
int Value
```

Gets the integer value.

### `PrimitiveType`

```csharp
override PdfPrimitiveType PrimitiveType => PdfPrimitiveType.Integer
```

<inheritdoc/>

## Methods

### `new`

__static__

```csharp
static readonly PdfInteger Zero = new(0)
```

The integer value zero, cached to avoid allocations.

### `new`

__static__

```csharp
static readonly PdfInteger One = new(1)
```

The integer value one, cached to avoid allocations.

### `ToString`

```csharp
override string ToString() => Value.ToString(CultureInfo.InvariantCulture)
```

Returns the integer formatted as a decimal string.

### `int`

__static__

```csharp
static implicit operator int(PdfInteger i)
```

Implicitly converts a `PdfInteger` to an `int`.

### `ToReal`

```csharp
PdfReal ToReal() => new(Value)
```

Converts this integer to a `PdfReal`.

---

_Source: [`src/Chuvadi.Pdf.Primitives/PdfInteger.cs`](../../../src/Chuvadi.Pdf.Primitives/PdfInteger.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
