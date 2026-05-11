# Ascii85Filter

**Class** in `Chuvadi.Pdf.Filters` (Filters)

Implements the PDF ASCII85Decode filter. 4 binary bytes → 5 ASCII characters. EOD marker is `~&gt;`. Zero group of 4 bytes is encoded as single `z`. PDF 32000-1:2008 §7.4.3.

```csharp
public sealed class Ascii85Filter : IStreamFilter
```

## Properties

### `FilterName`

```csharp
string FilterName => "ASCII85Decode"
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

_Source: [`src/Chuvadi.Pdf.Filters/Ascii85Filter.cs`](../../../src/Chuvadi.Pdf.Filters/Ascii85Filter.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
