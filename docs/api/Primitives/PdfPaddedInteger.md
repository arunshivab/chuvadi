# PdfPaddedInteger

**Class** in `Chuvadi.Pdf.Primitives` (Primitives)

A PDF integer that serialises to exactly `PaddedWidth` ASCII characters, left-padded with leading zeros. Used by PDF signature emitters to reserve fixed-width slots in the `/ByteRange` array so the byte positions of subsequent dictionary entries (notably `/Contents`) do not shift when the placeholder is patched with the actual values.

```csharp
public sealed class PdfPaddedInteger : PdfPrimitive
```

## Remarks

PDF 32000-1:2008 §7.3.3 permits leading zeros in integer tokens; a value of `42` with `PaddedWidth` `10` serialises as `0000000042`, which parses back as `42` in any conforming reader. The width must be at least wide enough to hold the largest value the slot will ever carry.

## Constructors

### `PdfPaddedInteger(int value, int paddedWidth)`

Initialises a new padded integer with the given value and width.

**Parameters**

- `value` — The integer value; must fit in `paddedWidth` digits.
- `paddedWidth` — The total number of characters to emit, &gt; 0.

## Properties

### `Value`

```csharp
int Value
```

The integer value.

### `PaddedWidth`

```csharp
int PaddedWidth
```

The total width of the serialised form, in ASCII characters.

### `PrimitiveType`

```csharp
override PdfPrimitiveType PrimitiveType => PdfPrimitiveType.Integer
```

<inheritdoc/>

## Methods

### `ToString`

```csharp
override string ToString()
```

Returns the integer formatted with leading-zero padding.

---

_Source: [`src/Chuvadi.Pdf.Primitives/PdfPaddedInteger.cs`](../../../src/Chuvadi.Pdf.Primitives/PdfPaddedInteger.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
