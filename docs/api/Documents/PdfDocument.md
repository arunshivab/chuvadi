# PdfDocument

**Class** in `Chuvadi.Pdf.Documents` (Documents)

Represents an opened PDF document.

```csharp
public sealed class PdfDocument : IDisposable
```

## Remarks

`PdfDocument` wraps a `PdfReader` and exposes the document-level object model: pages, metadata, and the document catalog. Open a document with `Open(Stream, bool)` or `Open(string)`. Dispose the document when finished — it owns the underlying reader and stream. PDF 32000-1:2008 §7.7.2 — Document Catalog. PDF 32000-1:2008 §14.3.3 — Document information dictionary.

## Properties

### `PageCount`

```csharp
int PageCount => Pages.Count
```

Gets the total number of pages.

### `Trailer`

```csharp
PdfDictionary Trailer => _reader.Trailer
```

Gets the raw trailer dictionary.

### `Info`

```csharp
PdfDictionary? Info => _reader.Info
```

Gets the raw document information dictionary, or null when absent.

### `Objects`

```csharp
PdfObjectStore Objects => _reader.Objects
```

Gets the underlying object store for direct object access.

## Methods

### `Open`

__static__

```csharp
static PdfDocument Open(Stream stream, bool leaveOpen = false)
```

Opens a PDF document from the given stream.

**Parameters**

- `stream` — A readable, seekable PDF stream.
- `leaveOpen` — True to leave the stream open when this document is disposed.

### `Open`

__static__

```csharp
static PdfDocument Open(string path)
```

Opens a PDF document from a file path.

**Parameters**

- `path` — The path to the PDF file.

### `GetInfoString`

```csharp
string? Title => GetInfoString(PdfName.Intern("Title"))
```

Gets the document Title, or null when not set. PDF 32000-1:2008 §14.3.3, Table 317 — Title.

### `GetInfoString`

```csharp
string? Author => GetInfoString(PdfName.Intern("Author"))
```

Gets the document Author, or null when not set.

### `GetInfoString`

```csharp
string? Subject => GetInfoString(PdfName.Intern("Subject"))
```

Gets the document Subject, or null when not set.

### `GetInfoString`

```csharp
string? Keywords => GetInfoString(PdfName.Intern("Keywords"))
```

Gets the document Keywords, or null when not set.

### `GetInfoString`

```csharp
string? Creator => GetInfoString(PdfName.Intern("Creator"))
```

Gets the name of the application that created the document.

### `GetInfoString`

```csharp
string? Producer => GetInfoString(PdfName.Intern("Producer"))
```

Gets the name of the PDF producer application.

### `Dispose`

```csharp
void Dispose()
```

<inheritdoc/>

---

_Source: [`src/Chuvadi.Pdf.Documents/PdfDocument.cs`](../../../src/Chuvadi.Pdf.Documents/PdfDocument.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
