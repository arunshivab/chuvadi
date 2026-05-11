# PdfReal

**Class** in `Chuvadi.Pdf.Primitives` (Primitives)

Represents a PDF real (floating-point) object. PDF 32000-1:2008 §7.3.3 — Numeric objects.

```csharp
public sealed class PdfReal : PdfPrimitive
```

## Constructors

### `PdfReal(double value)`

Initialises a new `PdfReal` with the given value.

### `static implicit operator PdfReal(double d) => new(d)`

Implicitly converts a `double` to a `PdfReal`.

### `static implicit operator PdfReal(float f) => new(f)`

Implicitly converts a `float` to a `PdfReal`.

## Properties

### `Value`

```csharp
double Value
```

Gets the real value.

### `PrimitiveType`

```csharp
override PdfPrimitiveType PrimitiveType => PdfPrimitiveType.Real
```

<inheritdoc/>

## Methods

### `new`

__static__

```csharp
static readonly PdfReal Zero = new(0.0)
```

The real value zero, cached to avoid allocations.

### `ToString`

```csharp
override string ToString()
```

Returns the value formatted as a decimal string using invariant culture. Uses G6: up to 6 significant digits, no trailing zeros.

### `double`

__static__

```csharp
static implicit operator double(PdfReal r)
```

Implicitly converts a `PdfReal` to a `double`.

### `ToDouble`

__static__

```csharp
static double ToDouble(PdfPrimitive primitive)
```

Returns the numeric value as a double, whether the primitive is a `PdfInteger` or `PdfReal`. <exception cref="InvalidCastException"> Thrown when `primitive` is neither integer nor real. </exception>

---

_Source: [`src/Chuvadi.Pdf.Primitives/PdfReal.cs`](../../../src/Chuvadi.Pdf.Primitives/PdfReal.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
