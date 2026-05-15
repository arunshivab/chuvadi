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

Opens a PDF file from the given readable, seekable stream. <exception cref="PdfReaderException"> Thrown when the file is encrypted. Use the password overload to open encrypted PDFs. </exception>

### `Open`

__static__

```csharp
static PdfReader Open(Stream stream, string password, bool leaveOpen = false)
```

Opens a PDF file with the given password. For unencrypted PDFs the password is ignored.

**Parameters**

- `stream` — Readable, seekable stream containing the PDF.
- `password` — User or owner password. Empty string for default.
- `leaveOpen` — Whether to leave the stream open on dispose. <exception cref="PdfReaderException">Thrown when the password is incorrect.</exception>

### `Dispose`

```csharp
void Dispose()
```

<inheritdoc/>

---

_Source: [`src/Chuvadi.Pdf.IO/PdfReader.cs`](../../../src/Chuvadi.Pdf.IO/PdfReader.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
