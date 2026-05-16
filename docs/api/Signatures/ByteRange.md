# ByteRange

**Class** in `Chuvadi.Pdf.Signatures` (Signatures)

The /ByteRange of a PDF signature — two disjoint regions of the file that together form the bytes the signature actually covers.

```csharp
public sealed class ByteRange
```

## Remarks

PDF 32000-1 §12.8.1 defines /ByteRange as an array of four integers `[a b c d]` meaning: 
 
- `a` — the byte offset of the first range (almost always 0). 
- `b` — the length of the first range. 
- `c` — the byte offset of the second range. 
- `d` — the length of the second range.  The gap between the two ranges contains the hex-encoded /Contents value of the signature itself — the signature cannot cover its own bytes.

## Constructors

### `ByteRange(long firstOffset, long firstLength, long secondOffset, long secondLength)`

Initialises a new ByteRange. <exception cref="ArgumentOutOfRangeException">If any value is negative or the ranges overlap.</exception>

## Properties

### `FirstOffset`

```csharp
long FirstOffset
```

Offset of the first signed region (PDF spec: a).

### `FirstLength`

```csharp
long FirstLength
```

Length of the first signed region (PDF spec: b).

### `SecondOffset`

```csharp
long SecondOffset
```

Offset of the second signed region (PDF spec: c).

### `SecondLength`

```csharp
long SecondLength
```

Length of the second signed region (PDF spec: d).

### `TotalLength`

```csharp
long TotalLength => FirstLength + SecondLength
```

Total number of signed bytes (b + d).

### `GapOffset`

```csharp
long GapOffset => FirstOffset + FirstLength
```

Offset of the gap (end of first range).

### `GapLength`

```csharp
long GapLength => SecondOffset - GapOffset
```

Length of the gap between the two ranges.

## Methods

### `ToString`

```csharp
override string ToString()
```

<inheritdoc/>

---

_Source: [`src/Chuvadi.Pdf.Signatures/ByteRange.cs`](../../../src/Chuvadi.Pdf.Signatures/ByteRange.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
