# PdfReference

**Class** in `Chuvadi.Pdf.Primitives` (Primitives)

Represents a PDF indirect object reference, e.g. `12 0 R`. PDF 32000-1:2008 §7.3.10 — Indirect objects.

```csharp
public sealed class PdfReference : PdfPrimitive, IEquatable<PdfReference>
```

## Constructors

### `PdfReference(PdfObjectId objectId)`

Initialises a new `PdfReference`.

### `PdfReference(int objectNumber, int generation = 0)`

Initialises a new `PdfReference` from object number and generation.

## Properties

### `ObjectId`

```csharp
PdfObjectId ObjectId
```

Gets the identity of the referenced object.

### `ObjectNumber`

```csharp
int ObjectNumber => ObjectId.ObjectNumber
```

Gets the object number.

### `Generation`

```csharp
int Generation => ObjectId.Generation
```

Gets the generation number.

### `PrimitiveType`

```csharp
override PdfPrimitiveType PrimitiveType => PdfPrimitiveType.Reference
```

<inheritdoc/>

## Methods

### `Equals`

```csharp
override bool Equals(object? obj) => Equals(obj as PdfReference)
```

<inheritdoc/>

### `GetHashCode`

```csharp
override int GetHashCode() => ObjectId.GetHashCode()
```

<inheritdoc/>

### `ToString`

```csharp
override string ToString() => ObjectId.ToString()
```

Returns the PDF indirect reference syntax, e.g. `12 0 R`.

---

_Source: [`src/Chuvadi.Pdf.Primitives/PdfReference.cs`](../../../src/Chuvadi.Pdf.Primitives/PdfReference.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
