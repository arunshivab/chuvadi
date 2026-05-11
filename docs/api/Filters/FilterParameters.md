# FilterParameters

**Record** in `Chuvadi.Pdf.Filters` (Filters)

Parameters passed to a filter's Decode or Encode operation, derived from the `/DecodeParms` or `/EncodeParms` dictionary in the stream dictionary.

```csharp
public sealed record FilterParameters
```

## Remarks

Different filters use different parameters. This record carries the subset of parameters Chuvadi supports in Phase 1. PDF 32000-1:2008 §7.4.4.3 — FlateDecode parameters (Predictor etc.)

## Properties

### `Predictor`

```csharp
int Predictor
```

For FlateDecode: the predictor algorithm applied before compression. 1 = no predictor (default), 2 = TIFF predictor, 10-15 = PNG predictors (most common in modern PDFs). PDF 32000-1:2008 Table 8.

### `Colors`

```csharp
int Colors
```

For PNG predictors (Predictor 10-15): number of color components per pixel. Default is 1.

### `BitsPerComponent`

```csharp
int BitsPerComponent
```

For PNG predictors: number of bits per color component. Default is 8.

### `Columns`

```csharp
int Columns
```

For PNG predictors: number of pixels (columns) per row. Must be set when a PNG predictor is used.

### `EarlyChange`

```csharp
int EarlyChange
```

For LZW: early change flag. 0 = compatible with original LZW; 1 = early change (PDF default).

---

_Source: [`src/Chuvadi.Pdf.Filters/IStreamFilter.cs`](../../../src/Chuvadi.Pdf.Filters/IStreamFilter.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
