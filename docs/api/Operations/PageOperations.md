# PageOperations

**Class** in `Chuvadi.Pdf.Operations` (Operations)

Provides static methods for high-level PDF page operations: merge, split, delete, rotate, and reorder.

```csharp
public static class PageOperations
```

## Remarks

All operations work at the PDF object-graph level — they copy and reassemble page dictionaries without modifying content streams. Each method writes a new PDF to the supplied output stream using `PdfWriter`. The input documents are not modified. PDF 32000-1:2008 §7.7.3 — Page tree nodes and page objects.

## Methods

### `Merge`

__static__

```csharp
static void Merge(Stream output, params PdfDocument[] documents)
```

Merges two or more PDF documents into a single output stream. Pages appear in the order of the input documents.

**Parameters**

- `output` — The stream to write the merged PDF to.
- `documents` — The documents to merge, in order. <exception cref="ArgumentNullException"> Thrown when `output` or `documents` is null. </exception> <exception cref="OperationsException"> Thrown when any document has no pages or an invalid structure. </exception>

### `SplitPages`

__static__

```csharp
static List<MemoryStream> SplitPages(PdfDocument document)
```

Splits a document into individual single-page PDFs.

**Parameters**

- `document` — The document to split.

**Returns:** A list of `MemoryStream` objects, one per page, each containing a valid single-page PDF.

---

_Source: [`src/Chuvadi.Pdf.Operations/PageOperations.cs`](../../../src/Chuvadi.Pdf.Operations/PageOperations.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
