# PdfReader

**Class** in `Chuvadi.Pdf.IO` (IO)

Opens an existing PDF file and provides access to its object graph. PDF 32000-1:2008 §7.5 — File structure.

```csharp
public sealed class PdfReader : IDisposable
```

## Properties

### `Trailer`

```csharp
PdfDictionary Trailer
```

Gets the PDF trailer dictionary.

### `Objects`

```csharp
PdfObjectStore Objects
```

Gets the lazy object store.

## Methods

### `Open`

__static__

```csharp
static PdfReader Open(Stream stream, bool leaveOpen = false)
```

Opens a PDF file from the given readable, seekable stream.

### `Dispose`

```csharp
void Dispose()
```

<inheritdoc/>

---

_Source: [`src/Chuvadi.Pdf.IO/PdfReader.cs`](../../../src/Chuvadi.Pdf.IO/PdfReader.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
