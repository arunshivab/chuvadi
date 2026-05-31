# SizeF

**Struct** in `Chuvadi.Pdf.Graphics` (Graphics)

An immutable size (width × height) in PDF points (1/72 inch).

```csharp
public readonly struct SizeF : IEquatable<SizeF>
```

## Constructors

### `SizeF(double width, double height)`

Initialises a new `SizeF`. <exception cref="ArgumentOutOfRangeException"> Thrown when `width` or `height` is negative. </exception>

## Properties

### `Width`

```csharp
double Width
```

Gets the width in PDF points.

### `Height`

```csharp
double Height
```

Gets the height in PDF points.

### `Zero`

__static__

```csharp
static SizeF Zero
```

Zero-size (0 × 0).

### `IsEmpty`

```csharp
bool IsEmpty => Width == 0 && Height == 0
```

Returns true when both dimensions are zero.

## Methods

### `==`

__static__

```csharp
static bool operator ==(SizeF left, SizeF right) => left.Equals(right)
```

Equality operator.

### `!=`

__static__

```csharp
static bool operator !=(SizeF left, SizeF right) => !left.Equals(right)
```

Inequality operator.

---

_Source: [`src/Chuvadi.Pdf.Graphics/SizeF.cs`](../../../src/Chuvadi.Pdf.Graphics/SizeF.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
