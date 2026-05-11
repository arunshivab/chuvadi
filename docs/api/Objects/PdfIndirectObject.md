# PdfIndirectObject

**Class** in `Chuvadi.Pdf.Objects` (Objects)

Represents an indirect object — a `PdfPrimitive` paired with the `PdfObjectId` that identifies it in the PDF file.

```csharp
public sealed class PdfIndirectObject
```

## Remarks

Every named object in a PDF file is an indirect object. Direct objects (values inside dictionaries and arrays) are not indirect objects. An indirect object definition in PDF syntax looks like: 
```
 12 0 obj &lt;&lt; /Type /Page ... &gt;&gt; endobj 
```
 The object number (12) and generation number (0) together form the `PdfObjectId`. The primitive (the dictionary) is the value. PDF 32000-1:2008 §7.3.10 — Indirect objects.

## Constructors

### `PdfIndirectObject(PdfObjectId id, PdfPrimitive value)`

Initialises a new `PdfIndirectObject`.

**Parameters**

- `id` — The object identity. Must be valid (ObjectNumber > 0).
- `value` — The primitive value of this object. Must not be null. Use `PdfNull.Value` when the object has a null value.

## Properties

### `Id`

```csharp
PdfObjectId Id
```

Gets the identity of this indirect object.

### `Value`

```csharp
PdfPrimitive Value
```

Gets the primitive value of this indirect object.

## Methods

### `GetAs<T>`

```csharp
T? GetAs<T>() where T : PdfPrimitive => Value as T
```

Gets the value cast to <typeparamref name="T"/>, or null if the value is not of the expected type.

### `Cast<T>`

```csharp
T Cast<T>() where T : PdfPrimitive => Value.Cast<T>()
```

Gets the value cast to <typeparamref name="T"/>. <exception cref="InvalidCastException"> Thrown when the value is not of type <typeparamref name="T"/>. </exception>

---

_Source: [`src/Chuvadi.Pdf.Objects/PdfIndirectObject.cs`](../../../src/Chuvadi.Pdf.Objects/PdfIndirectObject.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
