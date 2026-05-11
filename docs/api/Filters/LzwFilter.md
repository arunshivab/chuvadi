# LzwFilter

**Class** in `Chuvadi.Pdf.Filters` (Filters)

Implements the PDF LZWDecode filter. Variable-width codes (9-12 bits), MSB-first. EarlyChange=1 is PDF default. Code 256 = ClearTable. Code 257 = EOD. PDF 32000-1:2008 §7.4.6.

```csharp
public sealed class LzwFilter : IStreamFilter
```

## Properties

### `FilterName`

```csharp
string FilterName => "LZWDecode"
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

_Source: [`src/Chuvadi.Pdf.Filters/LzwFilter.cs`](../../../src/Chuvadi.Pdf.Filters/LzwFilter.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
