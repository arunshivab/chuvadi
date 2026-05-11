# AsciiHexFilter

**Class** in `Chuvadi.Pdf.Filters` (Filters)

Implements the PDF ASCIIHexDecode filter. Each byte is encoded as two uppercase hex characters. Whitespace is ignored on decode. EOD marker is `&gt;`. PDF 32000-1:2008 §7.4.2.

```csharp
public sealed class AsciiHexFilter : IStreamFilter
```

## Properties

### `FilterName`

```csharp
string FilterName => "ASCIIHexDecode"
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

_Source: [`src/Chuvadi.Pdf.Filters/AsciiHexFilter.cs`](../../../src/Chuvadi.Pdf.Filters/AsciiHexFilter.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
