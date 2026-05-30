# PdfObjectId

**Record** in `Chuvadi.Pdf.Primitives` (Primitives)

Uniquely identifies an indirect object in a PDF file.

```csharp
public record PdfObjectId(int ObjectNumber, int Generation)
```

## Remarks

Every indirect object in a PDF is identified by a pair of non-negative integers: an object number and a generation number. The generation number is almost always zero in modern PDFs; it increments only when an object is deleted and a new object reuses the same object number (a rare operation in incremental updates). PDF 32000-1:2008 §7.3.10 — Indirect objects.

## Parameters

- `ObjectNumber` — The object number. Must be greater than zero for real objects. Object number 0 is reserved by the PDF specification.
- `Generation` — The generation number. Zero in the vast majority of PDFs.

## Properties

### `IsValid`

```csharp
bool IsValid => ObjectNumber > 0
```

Returns true if this ID refers to a real indirect object (object number greater than zero).

## Methods

### `new`

__static__

```csharp
static readonly PdfObjectId Invalid = new(0, 0)
```

The invalid / sentinel object ID. Represents "no object". Object number 0 is reserved and never used for real objects.

### `ThrowIfInvalid`

```csharp
void ThrowIfInvalid()
```

Validates that the object ID is well-formed according to the PDF specification. <exception cref="ArgumentOutOfRangeException"> Thrown when `ObjectNumber` is negative, or when `Generation` is negative. </exception>

### `CompareTo`

```csharp
int CompareTo(PdfObjectId other)
```

Compares two object IDs. Ordered first by object number, then by generation.

### `ToString`

```csharp
override string ToString() => $"
```

Returns the PDF indirect reference syntax, e.g. `12 0 R`.

### `Parse`

__static__

```csharp
static PdfObjectId Parse(ReadOnlySpan<char> value)
```

Parses a PDF indirect reference from its canonical string form, e.g. `"12 0 R"`.

**Parameters**

- `value` — The string to parse.

**Returns:** The parsed `PdfObjectId`. <exception cref="FormatException"> Thrown when the string is not a valid indirect reference. </exception>

### `TryParse`

__static__

```csharp
static bool TryParse(ReadOnlySpan<char> value, out PdfObjectId result)
```

Attempts to parse a PDF indirect reference from its canonical string form.

**Parameters**

- `value` — The string to parse.
- `result` — When successful, the parsed `PdfObjectId`; otherwise `Invalid`.

**Returns:** True if parsing succeeded; false otherwise.

---

_Source: [`src/Chuvadi.Pdf.Primitives/PdfObjectId.cs`](../../../src/Chuvadi.Pdf.Primitives/PdfObjectId.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
