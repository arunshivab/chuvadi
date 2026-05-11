# RunLengthFilter

**Class** in `Chuvadi.Pdf.Filters` (Filters)

Implements the PDF RunLengthDecode filter (PackBits algorithm). Header 0-127: literal run. Header 129-255: repeat run. Header 128: EOD. PDF 32000-1:2008 §7.4.5.

```csharp
public sealed class RunLengthFilter : IStreamFilter
```

## Properties

### `FilterName`

```csharp
string FilterName => "RunLengthDecode"
```

<inheritdoc/>

## Methods

### `Decode`

```csharp
void Decode(Stream input, Stream output, FilterParameters? decodeParms = null)
```

<inheritdoc/>

### `Encode`

```csharp
void Encode(Stream input, Stream output, FilterParameters? encodeParms = null)
```

<inheritdoc/>

---

_Source: [`src/Chuvadi.Pdf.Filters/RunLengthFilter.cs`](../../../src/Chuvadi.Pdf.Filters/RunLengthFilter.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
