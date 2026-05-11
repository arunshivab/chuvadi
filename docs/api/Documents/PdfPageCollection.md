# PdfPageCollection

**Class** in `Chuvadi.Pdf.Documents` (Documents)

Provides lazy, random-access to the pages of a PDF document.

```csharp
public sealed class PdfPageCollection : IReadOnlyList<PdfPage>
```

## Remarks

The PDF page tree is a balanced tree of /Pages nodes with /Page leaves. `PdfPageCollection` traverses this tree on demand, caching resolved pages after the first access. `Count` is read directly from the root /Pages node's /Count entry — it does not require traversing the tree. PDF 32000-1:2008 §7.7.3 — Page tree.

## Properties

### `Count`

```csharp
int Count => _cache.Length
```

Gets the total number of pages in the document.

## Methods

### `GetEnumerator`

```csharp
IEnumerator<PdfPage> GetEnumerator()
```

<inheritdoc/>

---

_Source: [`src/Chuvadi.Pdf.Documents/PdfPageCollection.cs`](../../../src/Chuvadi.Pdf.Documents/PdfPageCollection.cs)_
_Generated from XML doc comments. Do not edit; regenerate with `python tools/gen_api_docs.py`._
